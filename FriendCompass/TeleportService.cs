using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace FriendCompass;

public enum TeleportStage
{
    Idle,
    RefreshInfo,       // 好友位置未知：请求服务器刷新好友列表
    WorldTravelLifestream, // 通过 Lifestream IPC 执行跨界传送
    WaitArrival,       // 等待跨界传送完成
    ZoneTeleport,      // 以太水晶传送到好友所在地图
    Done,
}

/// <summary>点击追踪后的自动传送：跨服（依赖 Lifestream 插件）→ 跨图（以太水晶）。</summary>
public sealed class TeleportService
{
    private readonly Plugin plugin;

    public TeleportStage Stage { get; private set; } = TeleportStage.Idle;

    private FriendSnapshot? target;
    private long stageStartTick;
    private long lastTick;

    // 各阶段超时（毫秒）
    private const int StageTimeoutMs = 30000;
    private const int ArrivalTimeoutMs = 180000;

    public TeleportService(Plugin plugin) => this.plugin = plugin;

    public bool Busy => Stage is not (TeleportStage.Idle or TeleportStage.Done);

    public string StatusText => Stage switch
    {
        TeleportStage.RefreshInfo => "正在获取好友位置…",
        TeleportStage.WorldTravelLifestream => "正在通过 Lifestream 跨界传送…",
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
            return TeleportStage.WorldTravelLifestream;
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
                case TeleportStage.WorldTravelLifestream: TickWorldTravelLifestream(); break;
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
            EnterStage(TeleportStage.WorldTravelLifestream);
        }
        else
        {
            Abort($"好友 {target.Name} 隐藏了位置信息，无法确定所在地图（可手动传送后使用悬浮窗）。");
        }
    }

    /// <summary>通过 Lifestream IPC 执行跨界传送（Lifestream 会自动处理找水晶、对话、确认等流程）。</summary>
    private void TickWorldTravelLifestream()
    {
        if (target == null) { Abort("目标丢失。"); return; }

        var worldName = plugin.GetWorldName(target.CurrentWorld);

        if (!plugin.IsLifestreamAvailable())
        {
            Abort($"跨服传送需要安装 Lifestream 插件（好友在 {worldName}，与你不属同一服务器）。请在卫月插件列表安装 Lifestream 后重试。");
            return;
        }

        try
        {
            var canVisit = plugin.PluginInterface
                .GetIpcSubscriber<string, bool>("Lifestream.CanVisitSameDC")
                .InvokeFunc(worldName);
            if (!canVisit)
            {
                Abort($"Lifestream 报告无法前往「{worldName}」（需要与好友在同一大区，且不在小队 / 副本中）。");
                return;
            }

            var started = plugin.PluginInterface
                .GetIpcSubscriber<string, bool>("Lifestream.ChangeWorld")
                .InvokeFunc(worldName);
            if (!started)
            {
                Abort($"Lifestream 未能开始跨界传送（可能正在忙或条件不满足），请查看 Lifestream 设置。");
                return;
            }

            EnterStage(TeleportStage.WaitArrival);
        }
        catch (Exception e)
        {
            plugin.Log.Warning($"Lifestream IPC 调用失败：{e.Message}");
            Abort("Lifestream IPC 调用失败，请确认 Lifestream 已安装并启用。");
        }
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

    private unsafe void TickZoneTeleport()
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
}
