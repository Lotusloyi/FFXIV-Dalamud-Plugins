using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;

namespace FishClock.Windows;

/// <summary>一行展示数据：某鱼在某钓点的最近开放窗口。</summary>
public sealed class FishRow
{
    public required FishEntry Fish;
    public required SpotEntry Spot;
    public long StartEarthMs;
    public long EndEarthMs;

    public bool IsActive(long now) => StartEarthMs <= now && now < EndEarthMs;
    public string Key => $"{Fish.ItemId}:{Spot.Id}";
}

public class FishClockWindow : Window
{
    private readonly Plugin plugin;

    private string search = string.Empty;
    private readonly List<FishRow> rows = [];
    private long lastTick;
    private readonly HashSet<string> activeKeys = [];
    private bool firstTick = true;

    public FishClockWindow(Plugin plugin)
        : base("FishClock - 钓鱼时钟", ImGuiWindowFlags.None)
    {
        this.plugin = plugin;
        Size = new Vector2(640, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void SetSearch(string text)
    {
        search = text;
        lastTick = 0; // 强制刷新
    }

    public override void Draw()
    {
        var now = EorzeaWeather.NowEarthMs;
        if (now - lastTick > 1000)
        {
            lastTick = now;
            Tick(now);
        }

        DrawWeatherBar(now);
        ImGui.Separator();
        DrawFilters();
        ImGui.Separator();
        DrawTable(now);
    }

    // ---------- 顶部：艾欧泽亚时间 + 当前地图天气预报 ----------

    private void DrawWeatherBar(long now)
    {
        ImGui.Text($"艾欧泽亚时间：{EorzeaWeather.FormatEtTime(now)}");

        var rates = plugin.GetCurrentTerritoryRates();
        var territoryName = GetCurrentTerritoryName();
        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        ImGui.Text($"{territoryName} 天气：");

        if (rates == null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("该地区无天气变化");
            return;
        }

        var forecast = EorzeaWeather.GetNextWeathers(rates, 5, now);
        var first = true;
        foreach (var (startMs, weatherId) in forecast)
        {
            var label = plugin.GetWeatherName(weatherId);
            var time = DateTimeOffset.FromUnixTimeMilliseconds(startMs).LocalDateTime;
            var text = first
                ? $"{label}（至 {time:HH:mm}）"
                : $"{time:HH:mm} {label}";
            ImGui.SameLine();
            if (first)
                ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f), text);
            else
                ImGui.Text(text);
            first = false;
        }
    }

    private string GetCurrentTerritoryName()
    {
        try
        {
            var territory = plugin.DataManager.GetExcelSheet<TerritoryType>().GetRow(plugin.ClientState.TerritoryType);
            var placeName = territory.PlaceName.Value;
            return placeName.Name.ExtractText();
        }
        catch
        {
            return "未知地区";
        }
    }

    // ---------- 筛选 ----------

    private void DrawFilters()
    {
        var config = plugin.Configuration;

        ImGui.SetNextItemWidth(180);
        var s = search;
        if (ImGui.InputTextWithHint("##search", "搜索鱼名…", ref s, 64))
        {
            search = s;
            lastTick = 0;
        }

        ImGui.SameLine();
        var onlyMap = config.OnlyCurrentMap;
        if (ImGui.Checkbox("仅当前地图", ref onlyMap))
        {
            config.OnlyCurrentMap = onlyMap;
            config.Save(plugin.PluginInterface);
            lastTick = 0;
        }

        ImGui.SameLine();
        var hideAlways = config.HideAlwaysAvailable;
        if (ImGui.Checkbox("隐藏常驻鱼", ref hideAlways))
        {
            config.HideAlwaysAvailable = hideAlways;
            config.Save(plugin.PluginInterface);
            lastTick = 0;
        }

        ImGui.SameLine();
        var alert = config.AlertOnWindowOpen;
        if (ImGui.Checkbox("窗口提醒", ref alert))
        {
            config.AlertOnWindowOpen = alert;
            config.Save(plugin.PluginInterface);
        }

        if (alert)
        {
            ImGui.SameLine();
            var sound = config.PlaySound;
            if (ImGui.Checkbox("提示音", ref sound))
            {
                config.PlaySound = sound;
                config.Save(plugin.PluginInterface);
            }
        }
    }

    // ---------- 时钟表 ----------

    private void DrawTable(long now)
    {
        using var table = ImRaii.Table("fish_clock", 5,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY);
        if (!table) return;

        ImGui.TableSetupColumn("状态", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("鱼类", ImGuiTableColumnFlags.WidthStretch, 2.2f);
        ImGui.TableSetupColumn("钓点", ImGuiTableColumnFlags.WidthStretch, 1.8f);
        ImGui.TableSetupColumn("开放条件", ImGuiTableColumnFlags.WidthStretch, 2.4f);
        ImGui.TableSetupColumn("倒计时", ImGuiTableColumnFlags.WidthStretch, 1.3f);
        ImGui.TableHeadersRow();

        if (rows.Count == 0)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(new Vector4(0.7f), "没有符合条件的鱼类");
            return;
        }

        foreach (var row in rows)
        {
            var active = row.IsActive(now);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            if (active)
                ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f), "进行中");
            else
                ImGui.Text("等待");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plugin.GetItemName(row.Fish.ItemId));
            if (ImGui.IsItemHovered() && row.Fish.PredatorItemIds.Count > 0)
            {
                ImGui.BeginTooltip();
                ImGui.Text("同模鱼 / 前置鱼饵需求：");
                foreach (var pid in row.Fish.PredatorItemIds)
                    ImGui.Text(" - " + plugin.GetItemName(pid));
                ImGui.EndTooltip();
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plugin.GetPlaceName(row.Spot.PlaceNameId));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(DescribeConditions(row.Fish));

            ImGui.TableNextColumn();
            if (active)
            {
                var remain = row.EndEarthMs - now;
                ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f),
                    "剩 " + TimeSpan.FromMilliseconds(remain).ToString(@"hh\:mm\:ss"));
            }
            else
            {
                ImGui.Text(EorzeaWeather.FormatCountdown(row.StartEarthMs, now));
            }
        }
    }

    private string DescribeConditions(FishEntry fish)
    {
        var parts = new List<string>();
        if (fish.AlwaysAvailable)
        {
            parts.Add("全天");
        }
        else
        {
            var endText = fish.EndHour >= 24 ? "24:00" : EorzeaWeather.FormatEtHour(fish.EndHour);
            parts.Add($"ET {EorzeaWeather.FormatEtHour(fish.StartHour)} ~ {endText}");
        }

        if (fish.WeatherIds.Count > 0)
            parts.Add("天气: " + string.Join("/", fish.WeatherIds.Select(w => plugin.GetWeatherName(w))));
        if (fish.PreviousWeatherIds.Count > 0)
            parts.Add("前置: " + string.Join("/", fish.PreviousWeatherIds.Select(w => plugin.GetWeatherName(w))));
        if (fish.HasFolklore)
            parts.Add("传承录");
        return string.Join("  ", parts);
    }

    // ---------- 每秒刷新：计算窗口 + 提醒 ----------

    private void Tick(long now)
    {
        var config = plugin.Configuration;
        var currentTerritory = plugin.ClientState.TerritoryType;

        rows.Clear();
        var newActive = new HashSet<string>();

        foreach (var fish in plugin.Data.Fish)
        {
            if (config.HideAlwaysAvailable && fish.AlwaysAvailable)
                continue;

            var fishName = plugin.GetItemName(fish.ItemId);
            if (search.Length > 0 && !fishName.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;

            FishRow? best = null;
            foreach (var spotId in fish.SpotIds)
            {
                if (!plugin.Data.SpotById.TryGetValue(spotId, out var spot))
                    continue;
                if (config.OnlyCurrentMap && spot.TerritoryTypeId != currentTerritory)
                    continue;

                var rates = plugin.GetWeatherRates(spot.Id);
                var window = EorzeaWeather.FindNextWindow(fish, rates);
                if (window == null)
                    continue;

                if (best == null || window.Value.Start < best.StartEarthMs)
                {
                    best = new FishRow
                    {
                        Fish = fish,
                        Spot = spot,
                        StartEarthMs = window.Value.Start,
                        EndEarthMs = window.Value.End,
                    };
                }
            }

            if (best == null)
                continue;

            if (best.IsActive(now))
            {
                newActive.Add(best.Key);
                if (!activeKeys.Contains(best.Key) && !firstTick && config.AlertOnWindowOpen)
                    Alert(best);
            }

            rows.Add(best);
        }

        firstTick = false;
        activeKeys.Clear();
        activeKeys.UnionWith(newActive);

        rows.Sort((a, b) => a.StartEarthMs.CompareTo(b.StartEarthMs));
        if (rows.Count > 200)
            rows.RemoveRange(200, rows.Count - 200);
    }

    private void Alert(FishRow row)
    {
        var fishName = plugin.GetItemName(row.Fish.ItemId);
        var spotName = plugin.GetPlaceName(row.Spot.PlaceNameId);
        var msg = $"[FishClock] {fishName} 的钓鱼窗口已开启！钓点：{spotName}";
        if (plugin.Configuration.PlaySound)
            Plugin.PlayAlertSound();
        plugin.ChatGui.Print(msg);
    }
}
