using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace FateTracker.Windows;

public class FateTrackerWindow : Window
{
    private readonly Plugin plugin;

    public FateTrackerWindow(Plugin plugin)
        : base("FateTracker - FATE 追踪器", ImGuiWindowFlags.NoScrollbar)
    {
        this.plugin = plugin;
        Size = new Vector2(420, 320);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        DrawConfigSection();
        ImGui.Separator();
        DrawFateTable();
    }

    private void DrawConfigSection()
    {
        var config = plugin.Configuration;

        var alert = config.AlertOnNewFate;
        if (ImGui.Checkbox("新 FATE 出现时提醒", ref alert))
        {
            config.AlertOnNewFate = alert;
            config.Save(plugin.PluginInterface);
        }

        if (config.AlertOnNewFate)
        {
            using var indent = ImRaii.PushIndent();
            var sound = config.PlaySound;
            if (ImGui.Checkbox("同时播放提示音", ref sound))
            {
                config.PlaySound = sound;
                config.Save(plugin.PluginInterface);
            }
        }

        var distance = config.ShowDistance;
        if (ImGui.Checkbox("显示与玩家的距离", ref distance))
        {
            config.ShowDistance = distance;
            config.Save(plugin.PluginInterface);
        }

        var hideCompleted = config.HideCompleted;
        if (ImGui.Checkbox("隐藏已完成的 FATE", ref hideCompleted))
        {
            config.HideCompleted = hideCompleted;
            config.Save(plugin.PluginInterface);
        }
    }

    private void DrawFateTable()
    {
        var player = plugin.ObjectTable.LocalPlayer;
        var fateTable = plugin.FateTable;

        using var table = ImRaii.Table("fate_table", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);
        if (!table) return;

        ImGui.TableSetupColumn("FATE", ImGuiTableColumnFlags.WidthStretch, 2.5f);
        ImGui.TableSetupColumn("等级", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("进度", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("剩余 / 距离", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableHeadersRow();

        if (fateTable.Length == 0)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(new Vector4(0.7f), "当前地图没有进行中的 FATE");
            return;
        }

        foreach (var fate in fateTable)
        {
            if (plugin.Configuration.HideCompleted && fate.Progress >= 100)
                continue;

            ImGui.TableNextRow();

            // 名称
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(fate.Name.TextValue);

            // 等级
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{fate.Level}");

            // 进度
            ImGui.TableNextColumn();
            var progress = fate.Progress / 100f;
            ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{fate.Progress}%");

            // 剩余时间 / 距离
            ImGui.TableNextColumn();
            var remaining = fate.TimeRemaining > 0 ? TimeSpan.FromSeconds(fate.TimeRemaining) : TimeSpan.Zero;
            var text = remaining > TimeSpan.Zero
                ? $"{remaining:mm\\:ss}"
                : "--:--";

            if (plugin.Configuration.ShowDistance && player != null)
            {
                var dist = Vector3.Distance(player.Position, fate.Position);
                text += $" / {dist:F0}m";
            }

            ImGui.TextUnformatted(text);
        }
    }
}
