using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FriendCompass;

public enum TeleportStage
{
    Idle,
    RefreshInfo,       // 好友位置未知：请求服务器刷新好友列表
    OpenWorldTravel,   // 打开跨界传送界面
    PickWorld,         // 在 WorldTravelSelect 中选择目标服务器
    ConfirmTravel,     // 确认 YesNo
    WaitArrival,       // 等待跨界传送完成
    ZoneTeleport,      // 以太水晶传送到好友所在地图
    Done,
}

/// <summary>点击追踪后的自动传送：跨服（同大区）→ 跨图（以太水晶）。</summary>
public sealed unsafe class TeleportService
{
    private readonly Plugin plugin;

    public TeleportStage Stage { get; private set; } = TeleportStage.Idle;

    private FriendSnapshot? target;
    private long stageStartTick;
    private long lastTick;

    // 各阶段超时（毫秒）
    private const int StageTimeoutMs = 25000;
    private const int ArrivalTimeoutMs = 150000;

    public TeleportService(Plugin plugin) => this.plugin = plugin;

    public bool Busy => Stage is not (TeleportStage.Idle or TeleportStage.Done);

    public string StatusText => Stage switch
    {
        TeleportStage.RefreshInfo => "正在获取好友位置…",
        TeleportStage.OpenWorldTravel => "正在打开跨界传送…",
        TeleportStage.PickWorld => "正在选择目标服务器…",
        TeleportStage.ConfirmTravel => "等待确认跨界传送…",
        TeleportStage.WaitArrival => "跨界传送中…",
        TeleportStage.ZoneTeleport => "正在传送至好友所在地图…",
        _ => string.Empty,
    };

    public void Start(FriendSnapshot friend)
    {
        if (Busy)
        {
            plugin.ChatGui.Print("[FriendCompass] 正在传送中，请稍候。");
            return;
        }
        if (!friend.Online)
        {
            plugin.ChatGui.Print($"[FriendCompass] 好友 {friend.Name} 不在线，无法传送。");
            return;
        }
        if (plugin.DisabledByDuty)
        {
            plugin.ChatGui.Print("[FriendCompass] 副本内无法传送。");
            return;
        }

        target = friend;
        Stage = friend.Location == 0 ? TeleportStage.RefreshInfo : PickStartStage(friend);
        stageStartTick = Environment.TickCount64;

        if (Stage == TeleportStage.RefreshInfo)
            plugin.Friends.RequestRefresh();

        plugin.ChatGui.Print($"[FriendCompass] 开始前往好友 {friend.Name} 所在位置（{plugin.GetWorldName(friend.CurrentWorld)}）…");
    }

    /// <summary>根据好友与本地玩家的世界关系决定起点阶段。</summary>
    private TeleportStage PickStartStage(FriendSnapshot friend)
    {
        var localWorld = LocalWorld();
        if (localWorld != 0 && localWorld != friend.CurrentWorld)
            return TeleportStage.OpenWorldTravel;
        return TeleportStage.ZoneTeleport;
    }

    private ushort LocalWorld()
    {
        if (plugin.ObjectTable.LocalPlayer is { } lp)
            return (ushort)lp.CurrentWorld.RowId;
        return 0;
    }

    /// <summary>重新从好友列表读取目标好友的最新数据（主要是位置字段）。</summary>
    private bool RefreshTargetInfo()
    {
        if (target == null)
            return false;
        var fresh = plugin.Friends.GetFriends().FirstOrDefault(f => f.ContentId == target.ContentId);
        if (fresh == null || !fresh.Online)
            return false;
        target.Location = fresh.Location;
        return true;
    }

    public void Cancel()
    {
        if (Busy)
            plugin.ChatGui.Print("[FriendCompass] 传送已取消。");
        Stage = TeleportStage.Idle;
        target = null;
    }

    public void Tick()
    {
        var now = Environment.TickCount64;
        if (now - lastTick < 500)
            return;
        lastTick = now;
        if (!Busy)
            return;

        var timeout = Stage == TeleportStage.WaitArrival ? ArrivalTimeoutMs : StageTimeoutMs;
        if (now - stageStartTick > timeout)
        {
            Abort("传送超时，已中止（可能条件不满足或网络延迟）。");
            return;
        }

        try
        {
            switch (Stage)
            {
                case TeleportStage.RefreshInfo: TickRefreshInfo(); break;
                case TeleportStage.OpenWorldTravel: TickOpenWorldTravel(); break;
                case TeleportStage.PickWorld: TickPickWorld(); break;
                case TeleportStage.ConfirmTravel: TickConfirmTravel(); break;
                case TeleportStage.WaitArrival: TickWaitArrival(); break;
                case TeleportStage.ZoneTeleport: TickZoneTeleport(); break;
            }
        }
        catch (Exception e)
        {
            plugin.Log.Warning($"传送状态机异常：{e.Message}");
            Abort("传送出错，已中止。");
        }
    }

    private void EnterStage(TeleportStage stage)
    {
        Stage = stage;
        stageStartTick = Environment.TickCount64;
    }

    private void Abort(string message)
    {
        plugin.ChatGui.Print($"[FriendCompass] {message}");
        Stage = TeleportStage.Idle;
        target = null;
    }

    // ---------- 阶段实现 ----------

    private void TickRefreshInfo()
    {
        if (target == null) { Abort("目标丢失。"); return; }

        RefreshTargetInfo();
        if (target.Location != 0)
        {
            EnterStage(PickStartStage(target));
            return;
        }

        // 位置一直未知：好友可能设置了隐藏位置信息。不同世界仍可先跨界，到步后再取位置。
        var localWorld = LocalWorld();
        if (localWorld != 0 && localWorld != target.CurrentWorld)
        {
            EnterStage(TeleportStage.OpenWorldTravel);
        }
        else
        {
            Abort($"好友 {target.Name} 隐藏了位置信息，无法确定所在地图（可手动传送后使用悬浮窗）。");
        }
    }

    private void TickOpenWorldTravel()
    {
        if (target == null) { Abort("目标丢失。"); return; }

        var localPlayer = plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return; // 等登录

        if (plugin.PartyList.Length > 1)
        {
            Abort("跨界传送需要退出小队，请先 /leave 退队。");
            return;
        }
        if (BusyOccupied())
            return; // 等待空闲

        var agent = AgentWorldTravel.Instance();
        if (agent == null) { Abort("无法访问跨界传送代理。"); return; }

        agent->SetupWorldTravelInfo((ushort)localPlayer.CurrentWorld.RowId, target.CurrentWorld);
        agent->ShowAddon();
        EnterStage(TeleportStage.PickWorld);
    }

    private void TickPickWorld()
    {
        if (target == null) { Abort("目标丢失。"); return; }

        var addonPtr = plugin.GameGui.GetAddonByName("WorldTravelSelect").Address;
        if (addonPtr == nint.Zero)
            return; // 等待界面

        var addon = (AtkUnitBase*)addonPtr;
        if (!addon->IsVisible)
            return;

        var worldName = plugin.GetWorldName(target.CurrentWorld);
        var index = FindWorldIndex(worldName);
        if (index < 0)
        {
            Abort($"在跨界传送列表中找不到服务器「{worldName}」（可能与好友不在同一大区）。");
            return;
        }

        Fire(addon, 0, index + 2);
        EnterStage(TeleportStage.ConfirmTravel);
    }

    private void TickConfirmTravel()
    {
        var addonPtr = plugin.GameGui.GetAddonByName("SelectYesno").Address;
        if (addonPtr == nint.Zero)
            return;
        var addon = (AtkUnitBase*)addonPtr;
        if (!addon->IsVisible)
            return;

        Fire(addon, 0); // 确认「是」
        EnterStage(TeleportStage.WaitArrival);
    }

    private void TickWaitArrival()
    {
        if (target == null) { Abort("目标丢失。"); return; }

        var localPlayer = plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return;

        // 跨界完成后服务器会把当前服务器切换为目标服务器
        if (localPlayer.CurrentWorld.RowId == target.CurrentWorld)
            EnterStage(TeleportStage.ZoneTeleport);
    }

    private void TickZoneTeleport()
    {
        if (target == null) { Abort("目标丢失。"); return; }

        var localPlayer = plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null || BusyOccupied())
            return;

        // 跨界后好友位置可能才有数据；仍未知则中止
        if (target.Location == 0)
            RefreshTargetInfo();
        if (target.Location == 0)
        {
            Abort($"好友 {target.Name} 隐藏了位置信息，无法确定所在地图（已到达好友所在服务器）。");
            return;
        }

        if (plugin.ClientState.TerritoryType == target.Location)
        {
            Stage = TeleportStage.Done;
            plugin.ChatGui.Print($"[FriendCompass] 已到达好友 {target.Name} 所在地图。");
            target = null;
            return;
        }

        var telepo = Telepo.Instance();
        if (telepo == null) { Abort("无法访问传送模块。"); return; }

        telepo->UpdateAetheryteList();
        var list = telepo->TeleportList;

        TeleportInfo? best = null;
        for (var i = 0; i < list.Count; i++)
        {
            var info = list[i];
            if (info.TerritoryId != target.Location)
                continue;
            // 优先普通以太水晶（非房屋），再选花费最低的
            if (best == null ||
                (best.Value.EstateType != 0 && info.EstateType == 0) ||
                (best.Value.EstateType == info.EstateType && info.GilCost < best.Value.GilCost))
                best = info;
        }

        if (best == null)
        {
            Abort($"「{plugin.GetTerritoryName(target.Location)}」没有已调谐的以太水晶，请手动前往。");
            return;
        }

        telepo->Teleport(best.Value.AetheryteId, best.Value.SubIndex);
        Stage = TeleportStage.Done;
        plugin.ChatGui.Print($"[FriendCompass] 正在传送至「{plugin.GetTerritoryName(target.Location)}」…");
        target = null;
    }

    // ---------- 工具 ----------

    private bool BusyOccupied()
    {
        var c = plugin.Condition;
        return c[ConditionFlag.Occupied] || c[ConditionFlag.InCombat] ||
               c[ConditionFlag.Casting] || c[ConditionFlag.BetweenAreas] ||
               c[ConditionFlag.Mounted] || c[ConditionFlag.Jumping];
    }

    /// <summary>从 WorldTravelSelect 的字符串数组中找目标服务器序号（0 起），找不到返回 -1。</summary>
    private static int FindWorldIndex(string worldName)
    {
        var atkModule = RaptureAtkModule.Instance();
        if (atkModule == null)
            return -1;
        var arr = atkModule->AtkArrayDataHolder.StringArrays[(int)StringArrayType.WorldTranslate];
        if (arr == null)
            return -1;

        var index = 0;
        for (var i = 3; i <= 10; i++)
        {
            var p = arr->StringArray[i];
            if (!p.HasValue)
                break;
            var name = Marshal.PtrToStringUTF8((nint)p.Value)?.Trim();
            if (string.IsNullOrEmpty(name))
                break;
            if (name == worldName)
                return index;
            index++;
        }
        return -1;
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
