using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace FriendCompass.Windows;

public class MainWindow : Window
{
    private readonly Plugin plugin;

    private long lastRefresh;
    private string search = string.Empty;
    private int selectedInstance = 1;
    private ulong selectedFriendId;

    public MainWindow(Plugin plugin)
        : base("FriendCompass - 好友罗盘", ImGuiWindowFlags.None)
    {
        this.plugin = plugin;
        Size = new Vector2(740, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var now = Environment.TickCount64;
        if (now - lastRefresh > 2000)
        {
            lastRefresh = now;
            plugin.DetectLifestream();
        }

        if (!plugin.LifestreamDetected)
        {
            ImGui.TextWrapped("未检测到 Lifestream：跨服自动传送不可用");
            ImGui.Separator();
        }

        DrawTrackedCard();

        if (plugin.DisabledByDuty)
        {
            ImGui.TextColored(new Vector4(0.9f, 0.6f, 0.2f, 1f), "副本中：指向与标记已禁用");
        }

        ImGui.Separator();
        DrawSettings();
        ImGui.Separator();
        DrawAreaRoster();
        DrawFriendTable();
    }

    // ---------- 当前追踪状态 ----------

    private void DrawTrackedCard()
    {
        var tracked = plugin.TrackedFriend;
        if (tracked == null)
        {
            ImGui.TextDisabled("未追踪好友：在下方列表点「追踪」");
            return;
        }

        if (selectedFriendId != tracked.ContentId)
        {
            selectedFriendId = tracked.ContentId;
            selectedInstance = (int)plugin.InstanceSwitcher.CurrentInstance() % 9 + 1;
        }

        // 传送进行中优先显示进度
        var statusText = plugin.Teleporter.StatusText;
        if (plugin.Teleporter.Busy)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.3f, 1f), $"➤ {statusText}");
            ImGui.SameLine();
            if (ImGui.SmallButton("取消传送"))
                plugin.Teleporter.Cancel();
            ImGui.SameLine();
            ImGui.TextDisabled($"目标：{tracked.Name}");
            return;
        }

        // 分流切换进行中显示进度
        if (plugin.InstanceSwitcher.Busy)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.3f, 1f), $"➤ {plugin.InstanceSwitcher.StatusText}");
            ImGui.SameLine();
            if (ImGui.SmallButton("取消"))
                plugin.InstanceSwitcher.Cancel();
            ImGui.SameLine();
            ImGui.TextDisabled($"目标：{tracked.Name}");
            return;
        }

        var statusCol = tracked.Online ? new Vector4(0.35f, 0.85f, 0.45f, 1f) : new Vector4(0.6f, 0.6f, 0.6f, 1f);
        ImGui.TextColored(statusCol, tracked.Online ? "●" : "○");
        ImGui.SameLine();
        ImGui.Text($"{tracked.Name}");
        ImGui.SameLine();
        ImGui.TextDisabled($"[{plugin.GetWorldName(tracked.CurrentWorld)} · {plugin.GetJobName(tracked.Job)}]");

        if (plugin.Position is { } position && plugin.ObjectTable.LocalPlayer != null)
        {
            var dist = Vector3.Distance(plugin.ObjectTable.LocalPlayer.Position, position.Position);
            ImGui.TextColored(position.Source == PositionSource.LastSeen ?
                    new Vector4(1f, 0.8f, 0.1f, 1f) : new Vector4(0.1f, 1f, 0.9f, 1f),
                $"{position.Describe(Environment.TickCount64)} · 距离 {dist:F0} 米");
        }
        else if (!tracked.Online)
        {
            ImGui.TextDisabled("离线");
        }
        else if (tracked.Location != 0 && tracked.Location == plugin.ClientState.TerritoryType &&
                 tracked.CurrentWorld == plugin.ObjectTable.LocalPlayer?.CurrentWorld.RowId)
        {
            ImGui.TextColored(new Vector4(0.9f, 0.85f, 0.3f, 1f), plugin.TrackedInAreaRoster
                ? "区域名单已查到好友 · 坐标未同步 · 分流未确认" : "好友列表显示同图 · 暂无坐标 · 分流未确认");
            ImGui.TextDisabled($"你的当前分流：{plugin.InstanceSwitcher.CurrentInstance()}");
            ImGui.SetNextItemWidth(110);
            ImGui.InputInt("目标分流", ref selectedInstance);
            selectedInstance = Math.Clamp(selectedInstance, 1, 9);
            ImGui.SameLine();
            if (ImGui.SmallButton("前往分流")) plugin.InstanceSwitcher.Start(selectedInstance);
            if (plugin.InstanceSwitcher.AvailableInstances.Count > 0)
                ImGui.TextDisabled($"最近菜单编号：{string.Join("、", plugin.InstanceSwitcher.AvailableInstances)}");
        }
        else
        {
            ImGui.Text($"位于：{plugin.GetTerritoryName(tracked.Location)}");
        }

        if (plugin.InstanceSwitcher.LastError.Length > 0)
            ImGui.TextWrapped(plugin.InstanceSwitcher.LastError);

        if (ImGui.SmallButton("传送"))
            plugin.Teleporter.Start(tracked);
        ImGui.SameLine();
        if (ImGui.SmallButton("停止追踪"))
            plugin.Untrack();
    }

    // ---------- 设置 ----------

    private void DrawSettings()
    {
        var config = plugin.Configuration;
        var changed = false;

        var v = config.ShowOverlay;
        if (ImGui.Checkbox("悬浮距离窗", ref v)) { config.ShowOverlay = v; changed = true; }
        ImGui.SameLine();
        v = config.ShowWorldMarker;
        if (ImGui.Checkbox("头顶标记", ref v)) { config.ShowWorldMarker = v; changed = true; }
        ImGui.SameLine();
        v = config.ShowMapMarker;
        if (ImGui.Checkbox("地图标记", ref v)) { config.ShowMapMarker = v; changed = true; }
        ImGui.SameLine();
        v = config.DisableInDuty;
        if (ImGui.Checkbox("副本内禁用", ref v)) { config.DisableInDuty = v; changed = true; }

        v = config.TeleportOnTrack;
        if (ImGui.Checkbox("追踪时自动传送", ref v)) { config.TeleportOnTrack = v; changed = true; }
        ImGui.SameLine();
        v = config.AutoSwitchInstance;
        if (ImGui.Checkbox("同图未找到时自动切换分流", ref v)) { config.AutoSwitchInstance = v; changed = true; }
        ImGui.SameLine();
        v = config.AlertOnZoneChange;
        if (ImGui.Checkbox("跨图提醒", ref v)) { config.AlertOnZoneChange = v; changed = true; }

        v = config.AlertOnOnlineChange;
        if (ImGui.Checkbox("全部好友上下线提醒", ref v)) { config.AlertOnOnlineChange = v; changed = true; }
        ImGui.SameLine();
        v = config.UsePartyPositions;
        if (ImGui.Checkbox("使用同图小队坐标", ref v)) { config.UsePartyPositions = v; changed = true; }
        ImGui.SameLine();
        v = config.KeepLastKnownPosition;
        if (ImGui.Checkbox("保留最近位置", ref v)) { config.KeepLastKnownPosition = v; changed = true; }
        v = config.UseAreaSearch;
        if (ImGui.Checkbox("查询区域玩家名单", ref v)) { config.UseAreaSearch = v; changed = true; }

        if (ImGui.CollapsingHeader("大小与刷新间隔"))
        {
            var compassSize = config.CompassSize;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("罗盘直径", ref compassSize, 96f, 280f, "%.0f px"))
            { config.CompassSize = compassSize; changed = true; }
            var worldScale = config.WorldMarkerScale;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("头顶标记大小", ref worldScale, 0.75f, 3f, "%.2f"))
            { config.WorldMarkerScale = worldScale; changed = true; }
            var mapScale = config.MapMarkerScale;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("地图标记大小", ref mapScale, 0.75f, 3f, "%.2f"))
            { config.MapMarkerScale = mapScale; changed = true; }
            var retention = config.LastKnownPositionSeconds;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderInt("最近位置保留时间", ref retention, 10, 600, "%d 秒"))
            { config.LastKnownPositionSeconds = retention; changed = true; }
            var refresh = config.FriendRefreshSeconds;
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderInt("好友列表刷新间隔", ref refresh, 15, 120, "%d 秒"))
            { config.FriendRefreshSeconds = refresh; changed = true; }
        }

        if (changed)
            config.Save(plugin.PluginInterface);
    }

    // ---------- 好友列表 ----------

    private void DrawAreaRoster()
    {
        if (!plugin.Configuration.UseAreaSearch) return;
        var roster = plugin.AreaSearch.Roster;
        var now = Environment.TickCount64;
        var valid = roster.IsCurrent(plugin.CurrentTrackingContext(), now);
        ImGui.TextDisabled($"已同步角色：{plugin.SynchronizedPlayerCount} · {plugin.AreaSearch.Status}");
        if (!valid) return;
        if (!ImGui.TreeNode($"区域玩家：{roster.Players.Count}{(roster.MayBeTruncated ? "+" : "")}##area_roster")) return;
        ImGui.TextDisabled($"{(now - roster.ObservedAt) / 1000} 秒前更新 · 分流未确认");
        using (var child = ImRaii.Child("area_roster_rows", new Vector2(0, 130), false))
        {
            if (child)
                foreach (var player in roster.Players.OrderBy(player => player.Name))
                {
                    var isFriend = plugin.FriendList.Any(friend => friend.ContentId == player.ContentId ||
                        (friend.Name == player.Name && friend.HomeWorld == player.HomeWorld));
                    ImGui.TextColored(isFriend ? new Vector4(0.1f, 1f, 0.9f, 1f) : Vector4.One,
                        $"{player.Name} [{plugin.GetWorldName(player.HomeWorld)}] · {plugin.GetJobName(player.Job)}");
                }
        }
        ImGui.TreePop();
    }

    private void DrawFriendTable()
    {
        ImGui.SetNextItemWidth(200);
        var s = search;
        if (ImGui.InputTextWithHint("##search", "搜索好友…", ref s, 64))
            search = s;

        // 主动向服务器请求刷新好友列表（等效打开游戏好友列表点刷新）
        ImGui.SameLine();
        if (ImGui.SmallButton("刷新"))
        {
            plugin.Friends.RequestRefresh();
            plugin.AreaSearch.RequestRefresh();
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"共 {plugin.FriendList.Count} 位好友");

        using var table = ImRaii.Table("friend_list", 6,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY);
        if (!table) return;

        ImGui.TableSetupColumn("状态", ImGuiTableColumnFlags.WidthStretch, 0.5f);
        ImGui.TableSetupColumn("名字", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("服务器", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        ImGui.TableSetupColumn("职业", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("所在地区", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableHeadersRow();

        var currentTerritory = plugin.ClientState.TerritoryType;
        foreach (var friend in plugin.FriendList)
        {
            if (search.Length > 0 &&
                !friend.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;

            var sameZone = friend.InZone(currentTerritory) &&
                           friend.CurrentWorld == plugin.ObjectTable.LocalPlayer?.CurrentWorld.RowId;
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextColored(
                sameZone ? new Vector4(0.35f, 0.85f, 0.45f, 1f)
                : friend.Online ? new Vector4(0.9f, 0.85f, 0.3f, 1f)
                : new Vector4(0.5f, 0.5f, 0.5f, 1f),
                !friend.Online ? "离线" : sameZone ? "同图" : "在线");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(friend.Name);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plugin.GetWorldName(friend.CurrentWorld));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plugin.GetJobName(friend.Job));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(friend.Online ? plugin.GetTerritoryName(friend.Location) : "-");

            ImGui.TableNextColumn();
            var tracked = plugin.Configuration.TrackedContentId == friend.ContentId;
            if (tracked)
            {
                ImGui.TextDisabled("追踪中");
            }
            else if (ImGui.SmallButton($"追踪##{friend.ContentId}"))
            {
                plugin.Track(friend);
            }
        }
    }
}
