using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
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
    TargetAetheryte,   // 选中以太水晶
    Interact,          // 与水晶交互，打开菜单
    WaitMenu,          // 等待 SelectString 菜单出现
    PickInstance,      // 在菜单中选择目标分流
    Done,
}

/// <summary>
/// 一键切换分流：与 Lifestream 相同的实现思路——选中附近的以太水晶，
/// 在「Travel to Instanced Area」菜单中点选目标分流。
/// </summary>
public sealed unsafe class InstanceService
{
    private readonly Plugin plugin;

    // 分流数字对应的特殊字符（与 Lifestream 一致：- 代表分流 1-9）
    private static readonly char[] InstanceNumbers = "\0".ToCharArray();

    private const float AetheryteMaxDistance = 11f;
    private const int StageTimeoutMs = 15000;

    public InstanceStage Stage { get; private set; } = InstanceStage.Idle;

    private int targetInstance;
    private Dalamud.Game.ClientState.Objects.Types.IGameObject? aetheryte;
    private long stageStartTick;
    private long lastTick;

    public InstanceService(Plugin plugin) => this.plugin = plugin;

    public bool Busy => Stage is not (InstanceStage.Idle or InstanceStage.Done);

    public string StatusText => Stage switch
    {
        InstanceStage.TargetAetheryte => "正在选中以太水晶…",
        InstanceStage.Interact => "正在打开以太水晶菜单…",
        InstanceStage.WaitMenu => "等待分流菜单…",
        InstanceStage.PickInstance => $"正在切换到分流 {targetInstance}…",
        _ => string.Empty,
    };

    /// <summary>当前地图的分流编号（0 = 非分流地图）。</summary>
    public uint CurrentInstance() => plugin.ClientState.Instance;

    /// <summary>当前地图是否为分流地图。</summary>
    public static bool IsInstancedArea()
    {
        try
        {
            return UIState.Instance()->PublicInstance.IsInstancedArea();
        }
        catch
        {
            return false;
        }
    }

    public void Start(int instance)
    {
        if (Busy)
        {
            plugin.ChatGui.Print("[FriendCompass] 正在切换分流，请稍候。");
            return;
        }
        if (instance < 1 || instance > 9)
        {
            plugin.ChatGui.Print("[FriendCompass] 分流编号需在 1-9 之间。");
            return;
        }
        if (plugin.DisabledByDuty)
        {
            plugin.ChatGui.Print("[FriendCompass] 副本内无法切换分流。");
            return;
        }

        var c = plugin.Condition;
        if (c[ConditionFlag.InCombat] || c[ConditionFlag.Casting] || c[ConditionFlag.BetweenAreas] ||
            c[ConditionFlag.Occupied] || c[ConditionFlag.OccupiedInQuestEvent] || c[ConditionFlag.Mounted] ||
            c[ConditionFlag.InFlight])
        {
            plugin.ChatGui.Print("[FriendCompass] 当前状态无法切换分流（请在落地、脱战、非坐骑状态下重试）。");
            return;
        }

        try
        {
            if (!UIState.Instance()->PublicInstance.IsInstancedArea())
            {
                plugin.ChatGui.Print("[FriendCompass] 当前地图没有分流。");
                return;
            }
        }
        catch (Exception e)
        {
            plugin.Log.Warning($"无法读取分流信息：{e.Message}");
        }

        aetheryte = FindAetheryte();
        if (aetheryte == null)
        {
            plugin.ChatGui.Print("[FriendCompass] 切换分流需要靠近任意以太水晶（约 11 米内），请走近后重试。");
            return;
        }

        targetInstance = instance;
        EnterStage(InstanceStage.TargetAetheryte);
    }

    public void Cancel()
    {
        Stage = InstanceStage.Idle;
        aetheryte = null;
    }

    public Dalamud.Game.ClientState.Objects.Types.IGameObject? FindAetheryte()
    {
        var localPlayer = plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return null;

        Dalamud.Game.ClientState.Objects.Types.IGameObject? best = null;
        var bestDist = float.MaxValue;
        foreach (var obj in plugin.ObjectTable)
        {
            if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Aetheryte || !obj.IsTargetable)
                continue;
            var dist = Vector3.Distance(obj.Position, localPlayer.Position);
            if (dist < AetheryteMaxDistance && dist < bestDist)
            {
                best = obj;
                bestDist = dist;
            }
        }
        return best;
    }

    public void Tick()
    {
        var now = Environment.TickCount64;
        if (now - lastTick < 500)
            return;
        lastTick = now;
        if (!Busy)
            return;

        if (now - stageStartTick > StageTimeoutMs)
        {
            plugin.ChatGui.Print("[FriendCompass] 切换分流超时（请确认站在以太水晶旁且菜单未被占用）。");
            Cancel();
            return;
        }

        try
        {
            switch (Stage)
            {
                case InstanceStage.TargetAetheryte: TickTargetAetheryte(); break;
                case InstanceStage.Interact: TickInteract(); break;
                case InstanceStage.WaitMenu: TickWaitMenu(); break;
                case InstanceStage.PickInstance: TickPickInstance(); break;
            }
        }
        catch (Exception e)
        {
            plugin.Log.Warning($"切换分流异常：{e.Message}");
            plugin.ChatGui.Print("[FriendCompass] 切换分流出错，已中止。");
            Cancel();
        }
    }

    private void EnterStage(InstanceStage stage)
    {
        Stage = stage;
        stageStartTick = Environment.TickCount64;
    }

    private void TickTargetAetheryte()
    {
        if (aetheryte == null || !aetheryte.IsTargetable)
        {
            plugin.ChatGui.Print("[FriendCompass] 以太水晶不可用了，请重试。");
            Cancel();
            return;
        }

        if (plugin.Targets.Target?.Address != aetheryte.Address)
        {
            plugin.Targets.Target = aetheryte;
            return;
        }

        EnterStage(InstanceStage.Interact);
    }

    private void TickInteract()
    {
        if (aetheryte == null) { Cancel(); return; }
        if (plugin.Condition[ConditionFlag.OccupiedInQuestEvent])
        {
            EnterStage(InstanceStage.WaitMenu);
            return;
        }

        // 目标已选中：发起交互打开菜单
        if (plugin.Targets.Target?.Address != aetheryte.Address)
        {
            plugin.Targets.Target = aetheryte;
            return;
        }
        TargetSystem.Instance()->InteractWithObject((GameObject*)aetheryte.Address, false);
        EnterStage(InstanceStage.WaitMenu);
    }

    private void TickWaitMenu()
    {
        var addon = plugin.GameGui.GetAddonByName("SelectString");
        if (addon.Address == nint.Zero)
            return;
        var unit = (AtkUnitBase*)addon.Address;
        if (!unit->IsVisible)
            return;
        EnterStage(InstanceStage.PickInstance);
    }

    private void TickPickInstance()
    {
        var addon = plugin.GameGui.GetAddonByName("SelectString");
        if (addon.Address == nint.Zero)
        {
            // 菜单被关闭：重新交互
            EnterStage(InstanceStage.Interact);
            return;
        }
        var selectString = (AddonSelectString*)addon.Address;
        var popup = &selectString->PopupMenu.PopupMenu;

        var index = -1;
        var count = Math.Min(popup->EntryCount, 32);
        for (var i = 0; i < count; i++)
        {
            var text = EntryText(popup, i);
            if (text != null && text.Contains(InstanceNumbers[targetInstance]))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            plugin.ChatGui.Print($"[FriendCompass] 菜单中没有分流 {targetInstance}（该分流可能已满或未开放）。");
            // 关闭菜单
            Fire((AtkUnitBase*)addon.Address, -1);
            Cancel();
            return;
        }

        Fire((AtkUnitBase*)addon.Address, index);
        Stage = InstanceStage.Done;
        plugin.ChatGui.Print($"[FriendCompass] 正在前往分流 {targetInstance}…");
        plugin.Friends.RequestRefresh(); // 切完刷新好友位置
        aetheryte = null;
    }

    private static string? EntryText(PopupMenu* popup, int index)
    {
        try
        {
            var ptr = popup->EntryNames[index].Value;
            if (ptr == null)
                return null;

            // 条目是 SeString（分流数字以图标负载存储），解析后还原为可读文本
            var seString = SeString.Parse(MemoryHelper.ReadRawNullTerminated((nint)ptr));
            var sb = new StringBuilder();
            foreach (var payload in seString.Payloads)
            {
                if (payload is TextPayload text)
                {
                    sb.Append(text.Text);
                }
                else if (payload is IconPayload icon)
                {
                    var v = (uint)icon.Icon;
                    sb.Append((char)(v >= 0xE000 ? v : 0xE000 + v));
                }
            }
            return sb.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>向 Addon 发送回调（等效 ECommons Callback.Fire 的 int 参数版本）。</summary>
    private static void Fire(AtkUnitBase* addon, params int[] values)
    {
        var atkValues = stackalloc AtkValue[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            atkValues[i].Type = AtkValueType.Int;
            atkValues[i].Int = values[i];
        }
        addon->FireCallback((uint)values.Length, atkValues, true);
    }
}
