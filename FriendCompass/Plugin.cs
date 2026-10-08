using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FriendCompass.Windows;
using Lumina.Excel.Sheets;

namespace FriendCompass;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "FriendCompass";

    private const string CommandName = "/fcompass";

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    // 构造函数注入的 Dalamud 服务
    public IDalamudPluginInterface PluginInterface { get; }
    public ICommandManager CommandManager { get; }
    public IClientState ClientState { get; }
    public IObjectTable ObjectTable { get; }
    public ICondition Condition { get; }
    public IGameGui GameGui { get; }
    public IFramework Framework { get; }
    public IDataManager DataManager { get; }
    public IChatGui ChatGui { get; }
    public IPartyList PartyList { get; }
    public ITargetManager Targets { get; }
    public IPluginLog Log { get; }

    public Configuration Configuration { get; }
    public FriendService Friends { get; } = new();
    public TeleportService Teleporter { get; }
    public InstanceService InstanceSwitcher { get; }

    private readonly WindowSystem windowSystem = new("FriendCompass");
    public readonly MainWindow MainWindow;
    public readonly OverlayWindow Overlay;

    // ---------- 名称 / 表格缓存 ----------
    private readonly Dictionary<uint, string> territoryNameCache = [];
    private readonly Dictionary<ushort, string> worldNameCache = [];
    private readonly Dictionary<byte, string> jobNameCache = [];
    private Dictionary<uint, Map>? territoryMapCache;

    // ---------- 追踪状态 ----------
    /// <summary>当前生效的追踪目标（手动或自动），由 Framework 定时刷新。</summary>
    public FriendSnapshot? TrackedFriend { get; private set; }

    /// <summary>追踪目标是否真实在同图（对象表中找到了角色）。</summary>
    public IPlayerCharacter? TrackedCharacter { get; private set; }

    private ushort lastTrackedLocation;
    private bool lastTrackedOnline = true;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IClientState clientState,
        IObjectTable objectTable,
        ICondition condition,
        IGameGui gameGui,
        IFramework framework,
        IDataManager dataManager,
        IChatGui chatGui,
        IPartyList partyList,
        ITargetManager targets,
        IPluginLog log)
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        ClientState = clientState;
        ObjectTable = objectTable;
        Condition = condition;
        GameGui = gameGui;
        Framework = framework;
        DataManager = dataManager;
        ChatGui = chatGui;
        PartyList = partyList;
        Targets = targets;
        Log = log;

        Configuration = Configuration.Load(pluginInterface);
        Teleporter = new TeleportService(this);
        InstanceSwitcher = new InstanceService(this);

        MainWindow = new MainWindow(this);
        Overlay = new OverlayWindow(this);
        windowSystem.AddWindow(MainWindow);
        windowSystem.AddWindow(Overlay);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开好友罗盘。/fcompass <名字> 直接追踪该好友。",
            ShowInHelp = true,
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainWindow;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        Framework.Update += OnFrameworkUpdate;

        DetectLifestream();
        Log.Information($"FriendCompass 已加载，输入 /fcompass 打开窗口。Lifestream（跨服传送依赖）：{(LifestreamDetected ? "已检测到" : "未检测到")}");
    }

    private void OnCommand(string command, string arguments)
    {
        var arg = arguments.Trim();
        if (arg.Length == 0)
        {
            ToggleMainWindow();
            return;
        }

        var match = Friends.GetFriends()
            .Where(f => f.Name.Contains(arg, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Online)
            .FirstOrDefault();
        if (match == null)
        {
            ChatGui.Print($"[FriendCompass] 未找到名字包含「{arg}」的好友。");
            return;
        }
        Track(match);
        MainWindow.IsOpen = true;
    }

    public void Track(FriendSnapshot friend)
    {
        Configuration.TrackedContentId = friend.ContentId;
        Configuration.TrackedName = friend.Name;
        Configuration.Save(PluginInterface);
        RefreshTracked(force: true);

        // 点击追踪 = 追踪 + 自动传送到好友所在服务器 / 地图
        if (Configuration.TeleportOnTrack)
            Teleporter.Start(friend);
    }

    public void Untrack()
    {
        Configuration.TrackedContentId = 0;
        Configuration.TrackedName = string.Empty;
        Configuration.Save(PluginInterface);
        TrackedFriend = null;
        TrackedCharacter = null;
    }

    public void ToggleMainWindow() => MainWindow.Toggle();

    // ---------- Lifestream 依赖检测（跨服传送用） ----------

    public bool LifestreamDetected { get; private set; }

    public void DetectLifestream()
    {
        try
        {
            LifestreamDetected = PluginInterface.InstalledPlugins.Any(p =>
                p.InternalName.Equals("Lifestream", StringComparison.OrdinalIgnoreCase) && p.IsLoaded);
        }
        catch
        {
            LifestreamDetected = false;
        }
    }

    /// <summary>跨服传送前检测 Lifestream 是否可用（已安装且加载、IPC 可调用）。</summary>
    public bool IsLifestreamAvailable()
    {
        DetectLifestream();
        return LifestreamDetected;
    }

    // ---------- 副本禁用 ----------

    public bool DisabledByDuty =>
        Configuration.DisableInDuty &&
        (Condition[ConditionFlag.BoundByDuty] ||
         Condition[ConditionFlag.BoundByDuty56] ||
         Condition[ConditionFlag.BoundByDuty95]);

    // ---------- 定时刷新（2 秒） ----------

    private long lastRefreshTick;

    private void OnFrameworkUpdate(IFramework framework)
    {
        // 传送状态机（内部自带 500ms 节流）
        Teleporter.Tick();
        InstanceSwitcher.Tick();

        // 限制刷新频率，避免每帧扫描好友列表与对象表
        var now = Environment.TickCount64;
        if (now - lastRefreshTick < 1000)
            return;
        lastRefreshTick = now;
        RefreshTracked();
        TickAutoInstanceSwitch();
    }

    // ---------- 自动切换分流 ----------

    private long lastAutoSwitchTick;
    private int autoSwitchCount;
    private ulong autoSwitchTargetId;
    private bool autoSwitchExhausted;

    // 到达新分流后等待对象表与好友数据刷新的 settle 时间
    private const int AutoSwitchSettleMs = 8000;
    private const int AutoSwitchMaxAttempts = 9;

    private void TickAutoInstanceSwitch()
    {
        if (!Configuration.AutoSwitchInstance)
            return;
        if (DisabledByDuty || Teleporter.Busy || InstanceSwitcher.Busy)
            return;

        var target = TrackedFriend;
        if (target is not { Online: true })
        {
            ResetAutoSwitch();
            return;
        }

        if (target.ContentId != autoSwitchTargetId)
        {
            // 换了追踪目标，重新开始计数
            autoSwitchTargetId = target.ContentId;
            autoSwitchCount = 0;
            autoSwitchExhausted = false;
            lastAutoSwitchTick = Environment.TickCount64;
        }

        if (TrackedCharacter != null)
        {
            // 找到好友了：重置尝试计数（保留目标，避免同图好友短暂超出范围后从头计）
            autoSwitchCount = 0;
            autoSwitchExhausted = false;
            return;
        }

        var territory = ClientState.TerritoryType;
        if (territory == 0 || target.Location != territory || ObjectTable.LocalPlayer == null)
        {
            ResetAutoSwitch();
            return;
        }

        if (!InstanceService.IsInstancedArea())
            return; // 非分流地图，好友只是距离较远，切分流无意义

        var now = Environment.TickCount64;
        if (now - lastAutoSwitchTick < AutoSwitchSettleMs)
            return;

        if (autoSwitchExhausted || autoSwitchCount >= AutoSwitchMaxAttempts)
        {
            if (!autoSwitchExhausted)
            {
                autoSwitchExhausted = true;
                ChatGui.Print($"[FriendCompass] 已自动尝试 {AutoSwitchMaxAttempts} 个分流仍未找到 {target.Name}，停止自动切换（可手动点「切换分流」继续尝试）。");
            }
            return;
        }

        if (InstanceSwitcher.FindAetheryte() == null)
        {
            ChatGui.Print("[FriendCompass] 自动切换分流需要靠近任意以太水晶（约 11 米内）。");
            lastAutoSwitchTick = now; // 8 秒内不重复提示
            return;
        }

        var cur = (int)InstanceSwitcher.CurrentInstance();
        InstanceSwitcher.Start(cur is >= 1 and <= 8 ? cur + 1 : 1);
        lastAutoSwitchTick = now;
        autoSwitchCount++;
    }

    private void ResetAutoSwitch()
    {
        autoSwitchTargetId = 0;
        autoSwitchCount = 0;
        autoSwitchExhausted = false;
    }

    public void RefreshTracked(bool force = false)
    {
        try
        {
            var friends = Friends.GetFriends();

            // 手动追踪
            FriendSnapshot? target = null;
            if (Configuration.TrackedContentId != 0)
                target = friends.FirstOrDefault(f => f.ContentId == Configuration.TrackedContentId);

            // 自动追踪同图好友
            if (target == null && Configuration.AutoTrackSameZone && ClientState.TerritoryType != 0)
                target = friends.FirstOrDefault(f => f.InZone(ClientState.TerritoryType));

            TrackedFriend = target;

            // 同图时在对象表中按名字找角色
            TrackedCharacter = null;
            if (target is { Online: true } && ObjectTable.LocalPlayer != null &&
                target.Location == ClientState.TerritoryType)
            {
                foreach (var obj in ObjectTable)
                {
                    if (obj is IPlayerCharacter pc && pc.Name.TextValue == target.Name)
                    {
                        TrackedCharacter = pc;
                        break;
                    }
                }
            }

            // 跨图 / 上下线提醒
            if (target != null &&
                (target.Location != lastTrackedLocation || target.Online != lastTrackedOnline || force))
            {
                if (Configuration.AlertOnZoneChange && !force)
                    AlertZoneChange(target);
                lastTrackedLocation = target.Location;
                lastTrackedOnline = target.Online;
            }

            // 悬浮窗激活：WindowSystem 只绘制 IsOpen 的窗口，必须在这里管理
            Overlay.IsOpen = !DisabledByDuty &&
                             target is { Online: true } &&
                             TrackedCharacter != null;
        }
        catch
        {
            // 忽略单帧异常
        }
    }

    private void AlertZoneChange(FriendSnapshot friend)
    {
        if (!friend.Online)
        {
            ChatGui.Print($"[FriendCompass] 好友 {friend.Name} 已下线。");
            return;
        }

        if (ClientState.TerritoryType != 0 && friend.InZone(ClientState.TerritoryType))
        {
            ChatGui.Print($"[FriendCompass] 好友 {friend.Name} 来到了你所在的地图！");
            if (Configuration.PlaySound)
                MessageBeep(0x00000040);
        }
        else
        {
            var where = GetTerritoryName(friend.Location);
            var world = GetWorldName(friend.CurrentWorld);
            ChatGui.Print($"[FriendCompass] 好友 {friend.Name} 现在位于：{where}（{world}）");
        }
    }

    // ---------- 名称解析 ----------

    public string GetTerritoryName(ushort territoryId)
    {
        if (territoryId == 0)
            return "未知地区";
        if (territoryNameCache.TryGetValue(territoryId, out var cached))
            return cached;
        string name;
        try
        {
            var row = DataManager.GetExcelSheet<TerritoryType>().GetRow(territoryId);
            name = row.PlaceName.Value.Name.ExtractText();
            if (string.IsNullOrEmpty(name))
                name = row.Name.ExtractText();
        }
        catch
        {
            name = $"#{territoryId}";
        }
        territoryNameCache[territoryId] = name;
        return name;
    }

    public string GetWorldName(ushort worldId)
    {
        if (worldId == 0)
            return "未知服务器";
        if (worldNameCache.TryGetValue(worldId, out var cached))
            return cached;
        string name;
        try
        {
            name = DataManager.GetExcelSheet<World>().GetRow(worldId).Name.ExtractText();
        }
        catch
        {
            name = $"#{worldId}";
        }
        worldNameCache[worldId] = name;
        return name;
    }

    public string GetJobName(byte jobId)
    {
        if (jobId == 0)
            return "-";
        if (jobNameCache.TryGetValue(jobId, out var cached))
            return cached;
        string name;
        try
        {
            name = DataManager.GetExcelSheet<ClassJob>().GetRow(jobId).Name.ExtractText();
        }
        catch
        {
            name = $"#{jobId}";
        }
        jobNameCache[jobId] = name;
        return name;
    }

    /// <summary>取区域对应的地图行；多个图层时取第一个。</summary>
    public Map? GetMapForTerritory(ushort territoryId)
    {
        if (territoryId == 0)
            return null;
        territoryMapCache ??= BuildTerritoryMapCache();
        return territoryMapCache.TryGetValue(territoryId, out var map) ? map : null;
    }

    private Dictionary<uint, Map> BuildTerritoryMapCache()
    {
        var dict = new Dictionary<uint, Map>();
        try
        {
            foreach (var map in DataManager.GetExcelSheet<Map>())
            {
                var terr = map.TerritoryType.RowId;
                if (terr != 0 && !dict.ContainsKey(terr))
                    dict[terr] = map;
            }
        }
        catch
        {
            // 留空字典，地图标记功能不可用
        }
        return dict;
    }

    public static void PlayAlertSound() => MessageBeep(0x00000040);

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainWindow;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        CommandManager.RemoveHandler(CommandName);
        windowSystem.RemoveAllWindows();
    }
}
