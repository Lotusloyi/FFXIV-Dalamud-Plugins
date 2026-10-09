using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FriendCompass.Windows;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace FriendCompass;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "FriendCompass";

    private const string CommandName = "/fcompass";

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
    public AreaSearchService AreaSearch { get; }

    private readonly WindowSystem windowSystem = new("FriendCompass");
    public readonly MainWindow MainWindow;
    public readonly OverlayWindow Overlay;

    // ---------- 名称 / 表格缓存 ----------
    private readonly Dictionary<uint, string> territoryNameCache = [];
    private readonly Dictionary<ushort, string> worldNameCache = [];
    private readonly Dictionary<byte, string> jobNameCache = [];

    // ---------- 追踪状态 ----------
    /// <summary>当前生效的追踪目标（手动或自动），由 Framework 定时刷新。</summary>
    public FriendSnapshot? TrackedFriend { get; private set; }

    /// <summary>追踪目标是否真实在同图（对象表中找到了角色）。</summary>
    public IPlayerCharacter? TrackedCharacter { get; private set; }

    public TrackedPosition? Position { get; private set; }
    public IReadOnlyList<FriendSnapshot> FriendList { get; private set; } = [];
    public int SynchronizedPlayerCount { get; private set; }
    public bool TrackedInAreaRoster => Configuration.UseAreaSearch && TrackedFriend is { } friend &&
        AreaSearch.Roster.IsCurrent(CurrentTrackingContext(), Environment.TickCount64) && AreaSearch.Roster.Contains(friend);

    private FriendSnapshot? previousTrackedFriend;
    private readonly FriendPresenceMonitor presenceMonitor = new();
    private readonly NearbyPlayerScanner nearbyPlayers = new();
    private TrackedPosition? lastKnownPosition;
    private long lastFriendReadTick;
    private long lastServerRefreshTick;
    private bool wasLoggedIn;

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
        IPluginLog log,
        IGameInteropProvider interop)
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
        AreaSearch = new AreaSearchService(this, interop);

        MainWindow = new MainWindow(this);
        Overlay = new OverlayWindow(this);
        windowSystem.AddWindow(MainWindow);
        windowSystem.AddWindow(Overlay);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开好友罗盘。/fcompass <名字> 追踪；/fcompass instance 5 指定分流；/fcompass debug 记录诊断。",
            ShowInHelp = true,
        });

        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainWindow;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        Framework.Update += OnFrameworkUpdate;

        DetectLifestream();
        Log.Information($"FriendCompass 1.3.5.0 已加载，输入 /fcompass 打开窗口。Lifestream（跨服传送依赖）：{(LifestreamDetected ? "已检测到" : "未检测到")}");
    }

    private void OnCommand(string command, string arguments)
    {
        var arg = arguments.Trim();
        if (arg.Length == 0)
        {
            ToggleMainWindow();
            return;
        }

        if (arg.Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            LogTrackingDiagnostics();
            ChatGui.Print("[FriendCompass] 当前坐标来源、同步玩家与区域名单诊断已写入卫月日志。");
            return;
        }
        if (arg.StartsWith("instance ", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(arg[9..].Trim(), out var instance)) InstanceSwitcher.Start(instance);
            else ChatGui.Print("[FriendCompass] 用法：/fcompass instance 5");
            return;
        }

        RefreshFriendList();
        var match = FriendList
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
        Teleporter.Cancel();
        InstanceSwitcher.Cancel();
        InstanceSwitcher.ClearFailure();
        ResetAutoSwitch();
        lastKnownPosition = null;
        Position = null;
        TrackedCharacter = null;
        TrackedFriend = friend;
        previousTrackedFriend = friend;
        Configuration.TrackedContentId = friend.ContentId;
        Configuration.TrackedName = friend.Name;
        Configuration.Save(PluginInterface);
        Friends.RequestRefresh();
        AreaSearch.RequestRefresh();
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
        previousTrackedFriend = null;
        lastKnownPosition = null;
        Position = null;
        Overlay.IsOpen = false;
        Teleporter.Cancel();
        InstanceSwitcher.Cancel();
        ResetAutoSwitch();
    }

    private void Draw()
    {
        windowSystem.Draw();
        // Map/world markers do not depend on the compass window being open.
        Overlay.DrawMarkers();
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

    // ---------- 后台好友刷新与位置采样 ----------

    private long lastRefreshTick;

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!ClientState.IsLoggedIn)
        {
            AreaSearch.Tick();
            if (wasLoggedIn)
            {
                presenceMonitor.Clear();
                FriendList = [];
                TrackedFriend = null;
                previousTrackedFriend = null;
                Teleporter.Cancel();
                InstanceSwitcher.Cancel();
                ResetAutoSwitch();
            }
            wasLoggedIn = false;
            nearbyPlayers.Clear();
            SynchronizedPlayerCount = 0;
            TrackedCharacter = null;
            lastKnownPosition = null;
            Position = null;
            Overlay.IsOpen = false;
            return;
        }

        var now = Environment.TickCount64;
        if (!wasLoggedIn)
        {
            wasLoggedIn = true;
            lastServerRefreshTick = 0;
            lastFriendReadTick = 0;
        }

        if (!Condition[ConditionFlag.BetweenAreas] && !Condition[ConditionFlag.BetweenAreas51])
        {
            if (now - lastServerRefreshTick >= Math.Clamp(Configuration.FriendRefreshSeconds, 15, 120) * 1000L)
            {
                Friends.RequestRefresh();
                lastServerRefreshTick = now;
            }
            if (now - lastFriendReadTick >= 1000)
            {
                lastFriendReadTick = now;
                RefreshFriendList();
            }
        }

        // 传送状态机（内部自带 500ms 节流）
        Teleporter.Tick();
        InstanceSwitcher.Tick();

        // 限制刷新频率，避免每帧扫描好友列表与对象表
        if (now - lastRefreshTick < 100)
            return;
        lastRefreshTick = now;
        RefreshTracked();
        AreaSearch.Tick();
        TickAutoInstanceSwitch();
    }

    private void RefreshFriendList()
    {
        if (!ClientState.IsLoggedIn || !Friends.TryGetFriends(out var friends) || friends.Count == 0)
            return;

        FriendList = friends;
        foreach (var (name, online) in presenceMonitor.Update(friends))
        {
            if (Configuration.AlertOnOnlineChange)
                ChatGui.Print($"[FriendCompass] 好友 {name} 已{(online ? "上线" : "下线")}。");
        }
    }

    // ---------- 自动切换分流 ----------

    private long lastAutoSwitchTick;
    private int autoSwitchCount;
    private ulong autoSwitchTargetId;
    private bool autoSwitchExhausted;
    private readonly HashSet<int> visitedInstances = [];
    private TrackingContext autoSwitchContext;
    private uint lastAutoObservedInstance;

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

        var context = CurrentTrackingContext();
        if (target.ContentId != autoSwitchTargetId || context.Territory != autoSwitchContext.Territory || context.World != autoSwitchContext.World)
        {
            // 换了追踪目标，重新开始计数
            autoSwitchTargetId = target.ContentId;
            autoSwitchCount = 0;
            autoSwitchExhausted = false;
            visitedInstances.Clear();
            autoSwitchContext = context;
            lastAutoObservedInstance = context.Instance;
            visitedInstances.Add((int)context.Instance);
            lastAutoSwitchTick = Environment.TickCount64;
        }

        if (InstanceSwitcher.LastAttemptFailed)
        {
            autoSwitchExhausted = true;
            return;
        }
        if (lastAutoObservedInstance != context.Instance)
        {
            lastAutoObservedInstance = context.Instance;
            lastAutoSwitchTick = Environment.TickCount64;
            visitedInstances.Add((int)context.Instance);
        }

        if (Position is { Source: not PositionSource.LastSeen })
        {
            // Received coordinates confirm the current instance; an area roster does not.
            autoSwitchCount = 0;
            autoSwitchExhausted = true;
            return;
        }

        var territory = ClientState.TerritoryType;
        if (territory == 0 || target.Location != territory || ObjectTable.LocalPlayer == null)
        {
            ResetAutoSwitch();
            return;
        }

        if (target.CurrentWorld != ObjectTable.LocalPlayer.CurrentWorld.RowId || Position != null ||
            Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51])
            return;

        if (!InstanceService.IsInstancedArea())
            return; // 非分流地图，好友只是距离较远，切分流无意义

        var now = Environment.TickCount64;
        if (now - lastAutoSwitchTick < AutoSwitchSettleMs)
            return;

        var next = InstanceMenu.Next((int)context.Instance, InstanceSwitcher.AvailableInstances, visitedInstances);
        if (autoSwitchExhausted || autoSwitchCount >= AutoSwitchMaxAttempts || next == 0)
        {
            if (!autoSwitchExhausted)
            {
                autoSwitchExhausted = true;
                ChatGui.Print($"[FriendCompass] 已尝试可用分流，仍没有 {target.Name} 的实时坐标，自动切换暂停。可手动指定分流。");
            }
            return;
        }

        if (!InstanceSwitcher.CanStart || InstanceSwitcher.FindAetheryte() == null) return;
        if (InstanceSwitcher.Start(next))
        {
            visitedInstances.Add(next);
            lastAutoSwitchTick = now;
            autoSwitchCount++;
        }
    }

    private void ResetAutoSwitch()
    {
        autoSwitchTargetId = 0;
        autoSwitchCount = 0;
        autoSwitchExhausted = false;
        visitedInstances.Clear();
    }

    public void RefreshTracked(bool force = false)
    {
        try
        {
            var target = Configuration.TrackedContentId == 0 ? null :
                FriendList.FirstOrDefault(f => f.ContentId == Configuration.TrackedContentId) ?? TrackedFriend;

            TrackedFriend = target;

            RefreshPosition(target);

            // 跨图 / 上下线提醒
            if (target != null)
            {
                if (Configuration.AlertOnZoneChange && !force && target.Online &&
                    previousTrackedFriend is { Online: true } previous &&
                    previous.ContentId == target.ContentId &&
                    (target.Location != previous.Location || target.CurrentWorld != previous.CurrentWorld))
                    AlertZoneChange(target);
                previousTrackedFriend = target;
            }

            // 悬浮窗激活：WindowSystem 只绘制 IsOpen 的窗口，必须在这里管理
            Overlay.IsOpen = !DisabledByDuty &&
                             Configuration.ShowOverlay && Position != null;
        }
        catch (Exception exception)
        {
            TrackedCharacter = null;
            Position = null;
            Overlay.IsOpen = false;
            Log.Debug($"好友位置采样失败：{exception.Message}");
        }
    }

    private unsafe void RefreshPosition(FriendSnapshot? target)
    {
        TrackedCharacter = null;
        Position = null;
        var local = ObjectTable.LocalPlayer;
        if (local == null || DisabledByDuty ||
            Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51])
        {
            nearbyPlayers.Clear();
            SynchronizedPlayerCount = 0;
            lastKnownPosition = null;
            return;
        }

        nearbyPlayers.Scan(ObjectTable, local.Address);
        SynchronizedPlayerCount = nearbyPlayers.Players.Count;
        if (target == null)
        {
            lastKnownPosition = null;
            return;
        }

        var context = CurrentTrackingContext();
        var now = Environment.TickCount64;
        var player = NearbyPlayerMatcher.Find(nearbyPlayers.Players, target);
        if (player != null)
        {
            TrackedCharacter = ObjectTable.CreateObjectReference(player.Address) as IPlayerCharacter;
            Position = new(target.ContentId, player.Position, context, now, PositionSource.Character, player.Height);
        }

        // A live character also takes precedence over a stale offline friend-list response.
        if (Position == null && !target.Online)
        {
            lastKnownPosition = null;
            return;
        }

        // A loaded ContentId is fresher than the periodically refreshed friend location.
        if (Position == null &&
            ((target.CurrentWorld != 0 && target.CurrentWorld != context.World) ||
             (target.Location != 0 && target.Location != context.Territory)))
        {
            lastKnownPosition = null;
            return;
        }

        if (Position == null && Configuration.UsePartyPositions)
        {
            foreach (var member in PartyList)
            {
                if (member.ContentId != target.ContentId || member.Territory.RowId != context.Territory ||
                    member.Address == nint.Zero)
                    continue;
                var native = (PartyMember*)member.Address;
                // The party packet has its own coordinate-valid flag, separate from loaded characters.
                if ((native->Flags & 0x04) == 0 || !TrackingMath.IsFinite(member.Position))
                    continue;
                Position = new(target.ContentId, member.Position, context, now, PositionSource.Party);
                break;
            }
        }

        if (Position != null)
            lastKnownPosition = Position;
        else if (Configuration.KeepLastKnownPosition && lastKnownPosition is { } known &&
                 known.IsRecent(context, target.ContentId, now, Configuration.LastKnownPositionSeconds))
            Position = known with { Source = PositionSource.LastSeen };
        else
            lastKnownPosition = null;
    }

    private void AlertZoneChange(FriendSnapshot friend)
    {
        if (ClientState.TerritoryType != 0 && friend.InZone(ClientState.TerritoryType) &&
            ObjectTable.LocalPlayer?.CurrentWorld.RowId == friend.CurrentWorld)
        {
            ChatGui.Print($"[FriendCompass] 好友 {friend.Name} 来到了你所在的地图！");
        }
        else
        {
            var where = GetTerritoryName(friend.Location);
            var world = GetWorldName(friend.CurrentWorld);
            ChatGui.Print($"[FriendCompass] 好友 {friend.Name} 现在位于：{where}（{world}）");
        }
    }

    public unsafe TrackingContext CurrentTrackingContext()
    {
        var map = AgentMap.Instance();
        return new(ClientState.TerritoryType, (ushort)(ObjectTable.LocalPlayer?.CurrentWorld.RowId ?? 0),
            InstanceSwitcher.CurrentInstance(), map == null ? 0 : map->CurrentMapId);
    }

    private unsafe void LogTrackingDiagnostics()
    {
        var target = TrackedFriend;
        var context = CurrentTrackingContext();
        var roster = AreaSearch.Roster;
        var local = ObjectTable.LocalPlayer;
        var farthest = local == null || nearbyPlayers.Players.Count == 0 ? 0 :
            nearbyPlayers.Players.Max(player => Vector3.Distance(local.Position, player.Position));
        Log.Information($"追踪诊断：版本=1.3.6.0 环境={context} 卫月缓存分流={ClientState.Instance} 好友={target?.Name} 好友地区={target?.Location} 好友服务器={target?.CurrentWorld} 同步玩家={SynchronizedPlayerCount} 原生角色={nearbyPlayers.NativeCount} 对象表玩家={nearbyPlayers.ObjectTableCount} 最远玩家={farthest:F0}米 坐标={Position?.Describe(Environment.TickCount64) ?? "无"} 区域名单有效={roster.IsCurrent(context, Environment.TickCount64)} 区域命中={TrackedInAreaRoster} 搜索状态={AreaSearch.Status} 分流阶段={InstanceSwitcher.Stage}");
        if (target == null) return;
        foreach (var player in nearbyPlayers.Players.Where(player => player.Name == target.Name))
        {
            var distance = local == null ? 0 : Vector3.Distance(local.Position, player.Position);
            Log.Information($"同名同步对象：姓名={player.Name} 出生服={player.HomeWorld} 目标出生服={target.HomeWorld} ContentId匹配={player.ContentId == target.ContentId} ContentId为空={player.ContentId == 0} 原生角色={player.FromCharacterManager} 距离={distance:F0}米 坐标={player.Position}");
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

    public Map? GetMap(uint mapId)
    {
        if (mapId == 0)
            return null;
        try
        {
            return DataManager.GetExcelSheet<Map>().GetRow(mapId);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainWindow;
        PluginInterface.UiBuilder.Draw -= Draw;
        Teleporter.Cancel();
        InstanceSwitcher.Cancel();
        AreaSearch.Dispose();
        CommandManager.RemoveHandler(CommandName);
        windowSystem.RemoveAllWindows();
    }
}
