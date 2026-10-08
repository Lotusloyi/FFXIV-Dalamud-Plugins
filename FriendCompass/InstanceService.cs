using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FriendCompass;

public enum InstanceStage
{
    Idle,
    TargetAetheryte,
    Interact,
    WaitMenu,
    PickTravel,
    PickInstance,
    WaitArrival,
    Done,
}

public sealed unsafe class InstanceService
{
    private const float AetheryteMaxDistance = 11f;
    private const int StageTimeoutMs = 15000;
    private readonly Plugin plugin;
    private IGameObject? aetheryte;
    private int targetInstance;
    private uint startTerritory;
    private uint choicesTerritory;
    private ushort choicesWorld;
    private long stageStartTick;
    private long lastTick;
    private string lastMenu = string.Empty;
    private string previousMenu = string.Empty;
    private bool sawLoading;
    private int menuSamples;
    private int[] availableInstances = [];

    public InstanceService(Plugin plugin) => this.plugin = plugin;
    public InstanceStage Stage { get; private set; }
    public bool Busy => Stage is not (InstanceStage.Idle or InstanceStage.Done);
    public bool LastAttemptFailed { get; private set; }
    public string LastError { get; private set; } = string.Empty;
    public IReadOnlyList<int> AvailableInstances =>
        choicesTerritory == plugin.ClientState.TerritoryType &&
        choicesWorld == plugin.ObjectTable.LocalPlayer?.CurrentWorld.RowId ? availableInstances : [];
    public string StatusText => Stage switch
    {
        InstanceStage.TargetAetheryte => "正在选中以太水晶…",
        InstanceStage.Interact => "正在打开以太水晶菜单…",
        InstanceStage.WaitMenu or InstanceStage.PickTravel => "正在打开切换副本区菜单…",
        InstanceStage.PickInstance => $"正在选择分流 {targetInstance}…",
        InstanceStage.WaitArrival => $"等待到达分流 {targetInstance}…",
        _ => string.Empty,
    };

    public uint CurrentInstance()
    {
        var state = UIState.Instance();
        return state == null ? plugin.ClientState.Instance : state->PublicInstance.InstanceId;
    }

    public static bool IsInstancedArea()
    {
        var state = UIState.Instance();
        return state != null && state->PublicInstance.IsInstancedArea();
    }

    public bool Start(int instance)
    {
        if (Busy) return false;
        targetInstance = instance;
        if (instance is < 1 or > 9) return Reject("分流编号需在 1-9 之间。");
        if (plugin.DisabledByDuty) return Reject("副本内无法切换分流。");
        if (plugin.ObjectTable.LocalPlayer == null || !IsInstancedArea())
            return Reject("当前地图没有可切换的分流。");
        if (CurrentInstance() == instance) return Reject($"已经在分流 {instance}。");
        if (IsBlocked()) return Reject("当前状态无法切换分流（请落地、下坐骑、脱战，并关闭其他对话）。");
        aetheryte = FindAetheryte();
        if (aetheryte == null) return Reject("切换分流需要靠近以太水晶（约 11 米内）。");

        LastAttemptFailed = false;
        LastError = string.Empty;
        targetInstance = instance;
        startTerritory = plugin.ClientState.TerritoryType;
        sawLoading = false;
        previousMenu = string.Empty;
        plugin.Log.Information($"分流切换开始：地区={startTerritory} 当前={CurrentInstance()} 目标={instance} 水晶={aetheryte.Name.TextValue}");
        EnterStage(InstanceStage.TargetAetheryte);
        return true;
    }

    public void Cancel()
    {
        Stage = InstanceStage.Idle;
        aetheryte = null;
    }

    public void ClearFailure()
    {
        LastAttemptFailed = false;
        LastError = string.Empty;
    }

    public bool CanStart => !Busy && !plugin.DisabledByDuty && !IsBlocked();

    private bool IsBlocked()
    {
        var c = plugin.Condition;
        return c[ConditionFlag.InCombat] || c[ConditionFlag.Casting] ||
            c[ConditionFlag.BetweenAreas] || c[ConditionFlag.BetweenAreas51] ||
            c[ConditionFlag.Occupied] || c[ConditionFlag.OccupiedInQuestEvent] ||
            c[ConditionFlag.Mounted] || c[ConditionFlag.InFlight];
    }

    public IGameObject? FindAetheryte()
    {
        var local = plugin.ObjectTable.LocalPlayer;
        if (local == null) return null;
        return plugin.ObjectTable.Where(obj => obj.ObjectKind == ObjectKind.Aetheryte && obj.IsTargetable)
            .Where(obj => Vector3.Distance(obj.Position, local.Position) < AetheryteMaxDistance)
            .OrderBy(obj => Vector3.DistanceSquared(obj.Position, local.Position)).FirstOrDefault();
    }

    public void Tick()
    {
        var now = Environment.TickCount64;
        if (!Busy || now - lastTick < 500) return;
        lastTick = now;
        if (plugin.ClientState.TerritoryType != startTerritory)
        {
            Reject("地区已改变，分流切换已中止。");
            return;
        }
        if (now - stageStartTick > (Stage == InstanceStage.WaitArrival ? 45000 : StageTimeoutMs))
        {
            Reject($"切换分流超时：{StatusText}（当前 {CurrentInstance()}，目标 {targetInstance}）。");
            return;
        }

        try
        {
            switch (Stage)
            {
                case InstanceStage.TargetAetheryte:
                    if (aetheryte == null || !aetheryte.IsTargetable)
                    { Reject("以太水晶不可用了。"); break; }
                    plugin.Targets.Target = aetheryte;
                    EnterStage(InstanceStage.Interact);
                    break;
                case InstanceStage.Interact:
                    if (aetheryte == null) { Reject("以太水晶不可用了。"); break; }
                    TargetSystem.Instance()->InteractWithObject((GameObject*)aetheryte.Address, false);
                    EnterStage(InstanceStage.WaitMenu);
                    break;
                case InstanceStage.WaitMenu:
                    if (TryMenu(out _, out _)) EnterStage(InstanceStage.PickTravel);
                    break;
                case InstanceStage.PickTravel:
                    PickTravel();
                    break;
                case InstanceStage.PickInstance:
                    PickInstance();
                    break;
                case InstanceStage.WaitArrival:
                    var loading = plugin.Condition[ConditionFlag.BetweenAreas] || plugin.Condition[ConditionFlag.BetweenAreas51];
                    sawLoading |= loading;
                    if (!loading && plugin.ObjectTable.LocalPlayer != null && CurrentInstance() == targetInstance)
                    {
                        Stage = InstanceStage.Done;
                        aetheryte = null;
                        plugin.Log.Information($"分流切换完成：当前={CurrentInstance()} 目标={targetInstance} 经历加载={sawLoading}");
                        plugin.ChatGui.Print($"[FriendCompass] 已到达分流 {targetInstance}。");
                        plugin.Friends.RequestRefresh();
                    }
                    break;
            }
        }
        catch (Exception exception)
        {
            plugin.Log.Warning(exception, "分流切换异常");
            Reject("切换分流异常，已中止，详情已记录到卫月日志。");
        }
    }

    private void PickTravel()
    {
        if (!TryMenu(out var addon, out var entries)) return;
        var index = Array.FindIndex(entries, InstanceMenu.IsTravelEntry);
        if (index < 0)
        {
            if (entries.Any(text => InstanceMenu.Number(text) != 0))
                EnterStage(InstanceStage.PickInstance);
            else Reject("以太水晶菜单中没有“切换副本区”，菜单内容已记录到日志。");
            return;
        }
        previousMenu = string.Join(" | ", entries);
        Fire(addon, index);
        EnterStage(InstanceStage.PickInstance);
    }

    private void PickInstance()
    {
        if (!TryMenu(out var addon, out var entries) || string.Join(" | ", entries) == previousMenu) return;
        availableInstances = entries.Select(InstanceMenu.Number).Where(number => number > 0).Distinct().Order().ToArray();
        if (availableInstances.Length == 0) return;
        choicesTerritory = plugin.ClientState.TerritoryType;
        choicesWorld = (ushort)(plugin.ObjectTable.LocalPlayer?.CurrentWorld.RowId ?? 0);
        var index = Array.FindIndex(entries, text => InstanceMenu.Number(text) == targetInstance);
        if (index < 0)
        {
            Reject($"分流菜单没有 {targetInstance}；本次菜单编号：{string.Join("、", availableInstances)}。自动尝试已暂停。");
            return;
        }
        Fire(addon, index);
        plugin.Log.Information($"分流菜单已点击：索引={index} 目标={targetInstance}，等待客户端确认");
        EnterStage(InstanceStage.WaitArrival);
    }

    private bool TryMenu(out AtkUnitBase* addon, out string[] entries)
    {
        addon = (AtkUnitBase*)plugin.GameGui.GetAddonByName("SelectString").Address;
        entries = [];
        if (addon == null || !addon->IsVisible) return false;
        var popup = &((AddonSelectString*)addon)->PopupMenu.PopupMenu;
        if (popup->EntryNames == null || popup->EntryCount is < 1 or > 32) return false;
        entries = new string[popup->EntryCount];
        for (var i = 0; i < entries.Length; i++)
        {
            var ptr = popup->EntryNames[i].Value;
            if (ptr == null) return false;
            entries[i] = SeString.Parse(MemoryHelper.ReadRawNullTerminated((nint)ptr)).TextValue;
        }
        var signature = string.Join(" | ", entries);
        if (signature != lastMenu)
        {
            lastMenu = signature;
            menuSamples = 1;
            plugin.Log.Information($"分流菜单快照：阶段={Stage} 条目={signature}");
            return false;
        }
        return ++menuSamples >= 2;
    }

    private bool Reject(string message)
    {
        LastAttemptFailed = true;
        LastError = message;
        plugin.Log.Warning($"分流切换失败：阶段={Stage} 当前={CurrentInstance()} 目标={targetInstance}；{message}");
        plugin.ChatGui.Print($"[FriendCompass] {message}");
        Cancel();
        return false;
    }

    private void EnterStage(InstanceStage stage)
    {
        Stage = stage;
        stageStartTick = Environment.TickCount64;
        lastMenu = string.Empty;
        menuSamples = 0;
    }

    private static void Fire(AtkUnitBase* addon, int index)
    {
        var value = new AtkValue { Type = AtkValueType.Int, Int = index };
        addon->FireCallback(1, &value, true);
    }
}
