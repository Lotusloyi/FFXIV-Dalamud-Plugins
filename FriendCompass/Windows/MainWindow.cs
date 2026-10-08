using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace FriendCompass.Windows;

public class MainWindow : Window
{
    private readonly Plugin plugin;

    private List<FriendSnapshot> friends = [];
    private long lastRefresh;
    private string search = string.Empty;

    public MainWindow(Plugin plugin)
        : base("FriendCompass - 好友罗盘", ImGuiWindowFlags.None)
    {
        this.plugin = plugin;
        Size = new Vector2(560, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var now = Environment.TickCount64;
        if (now - lastRefresh > 2000)
        {
            lastRefresh = now;
            plugin.DetectLifestream();
            friends = plugin.Friends.GetFriends();
            plugin.RefreshTracked();
        }

        if (!plugin.LifestreamDetected)
        {
            ImGui.TextColored(new Vector4(0.9f, 0.75f, 0.3f, 1f),
                "⚠ 未检测到 Lifestream：跨服自动传送不可用（请在卫月插件列表安装 Lifestream）");
            ImGui.Separator();
        }

        DrawTrackedCard();

        if (plugin.DisabledByDuty)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.9f, 0.6f, 0.2f, 1f), "副本中：指向与标记已禁用");
        }

        ImGui.Separator();
        DrawSettings();
        ImGui.Separator();
        DrawFriendTable();
    }

    // ---------- 当前追踪状态 ----------

    private void DrawTrackedCard()
    {
        var tracked = plugin.TrackedFriend;
        if (tracked == null)
        {
            ImGui.TextDisabled("未追踪好友：在下方列表点「追踪」，或开启自动追踪同图好友");
            return;
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

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();

        if (!tracked.Online)
        {
            ImGui.TextDisabled("离线");
        }
        else if (plugin.TrackedCharacter != null && plugin.ObjectTable.LocalPlayer != null)
        {
            var dist = Vector3.Distance(plugin.ObjectTable.LocalPlayer.Position, plugin.TrackedCharacter.Position);
            ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f),
                $"同图 · 距离 {dist:F0} 米");
        }
        else if (tracked.Location != 0 && tracked.Location == plugin.ClientState.TerritoryType)
        {
            // 同图但好友在约 100 米同步范围外，游戏不提供精确位置——常见原因是分流不同
            ImGui.TextColored(new Vector4(0.9f, 0.85f, 0.3f, 1f), "同图 · 超出同步范围（约 100 米）");
            ImGui.SameLine();
            ImGui.TextDisabled($"当前分流 {plugin.InstanceSwitcher.CurrentInstance()}");
            ImGui.SameLine();
            if (ImGui.SmallButton("切换分流"))
            {
                // 一键切到下一个分流，重复点击可逐个尝试直到找到好友
                var cur = (int)plugin.InstanceSwitcher.CurrentInstance();
                plugin.InstanceSwitcher.Start(cur is >= 1 and <= 8 ? cur + 1 : 1);
            }
        }
        else
        {
            ImGui.Text($"位于：{plugin.GetTerritoryName(tracked.Location)}");
        }

        ImGui.SameLine();
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

        v = config.AutoTrackSameZone;
        if (ImGui.Checkbox("自动追踪同图好友", ref v)) { config.AutoTrackSameZone = v; changed = true; }
        ImGui.SameLine();
        v = config.TeleportOnTrack;
        if (ImGui.Checkbox("追踪时自动传送", ref v)) { config.TeleportOnTrack = v; changed = true; }
        ImGui.SameLine();
        v = config.AlertOnZoneChange;
        if (ImGui.Checkbox("跨图提醒", ref v)) { config.AlertOnZoneChange = v; changed = true; }
        if (config.AlertOnZoneChange)
        {
            ImGui.SameLine();
            v = config.PlaySound;
            if (ImGui.Checkbox("提示音", ref v)) { config.PlaySound = v; changed = true; }
        }

        if (changed)
            config.Save(plugin.PluginInterface);
    }

    // ---------- 好友列表 ----------

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
            lastRefresh = 0; // 稍后自动重新拉取
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"共 {friends.Count} 位好友");

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
        foreach (var friend in friends)
        {
            if (search.Length > 0 &&
                !friend.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;

            var sameZone = friend.InZone(currentTerritory);
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
