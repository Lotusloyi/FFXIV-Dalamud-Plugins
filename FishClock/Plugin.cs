using System.Runtime.InteropServices;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FishClock.Windows;
using Lumina.Excel.Sheets;

namespace FishClock;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "FishClock";

    private const string CommandName = "/fishclock";

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    // 构造函数注入的 Dalamud 服务
    public IDalamudPluginInterface PluginInterface { get; }
    public ICommandManager CommandManager { get; }
    public IClientState ClientState { get; }
    public IDataManager DataManager { get; }
    public IPluginLog Log { get; }
    public IChatGui ChatGui { get; }

    public Configuration Configuration { get; }
    public FishDataSet Data { get; }

    private readonly WindowSystem windowSystem = new("FishClock");
    private readonly FishClockWindow clockWindow;

    /// <summary>钓点 ID → 天气表（累计概率）。</summary>
    private readonly Dictionary<uint, List<(int WeatherId, int Cumulative)>?> weatherRateCache = [];

    private readonly Dictionary<uint, string> itemNameCache = [];
    private readonly Dictionary<uint, string> placeNameCache = [];
    private readonly Dictionary<int, string> weatherNameCache = [];

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IClientState clientState,
        IDataManager dataManager,
        IChatGui chatGui,
        IPluginLog log)
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        ClientState = clientState;
        DataManager = dataManager;
        ChatGui = chatGui;
        Log = log;

        Configuration = Configuration.Load(pluginInterface);
        Data = FishDataSet.Load();

        clockWindow = new FishClockWindow(this);
        windowSystem.AddWindow(clockWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开 / 关闭钓鱼时钟窗口。",
            ShowInHelp = true,
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi += ToggleWindow;

        Log.Information($"FishClock 已加载：{Data.Fish.Count} 条鱼类数据，输入 /fishclock 打开窗口。");
    }

    private void OnCommand(string command, string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            ToggleWindow();
            return;
        }

        switch (arguments.Trim())
        {
            case "on":
                clockWindow.IsOpen = true;
                break;
            case "off":
                clockWindow.IsOpen = false;
                break;
            default:
                clockWindow.IsOpen = true;
                clockWindow.SetSearch(arguments.Trim());
                break;
        }
    }

    public void ToggleWindow()
        => clockWindow.Toggle();

    // ---------- 名称解析 ----------

    public string GetItemName(uint itemId)
    {
        if (itemNameCache.TryGetValue(itemId, out var cached))
            return cached;
        string name;
        try
        {
            var item = DataManager.GetExcelSheet<Item>().GetRow(itemId);
            name = item.Name.ExtractText();
        }
        catch
        {
            name = $"#{itemId}";
        }
        itemNameCache[itemId] = name;
        return name;
    }

    public string GetPlaceName(uint placeNameId)
    {
        if (placeNameCache.TryGetValue(placeNameId, out var cached))
            return cached;
        string name;
        try
        {
            var row = DataManager.GetExcelSheet<PlaceName>().GetRow(placeNameId);
            name = row.Name.ExtractText();
        }
        catch
        {
            name = $"#{placeNameId}";
        }
        placeNameCache[placeNameId] = name;
        return name;
    }

    public string GetWeatherName(int weatherId)
    {
        if (weatherNameCache.TryGetValue(weatherId, out var cached))
            return cached;
        string name;
        try
        {
            var row = DataManager.GetExcelSheet<Weather>().GetRow((uint)weatherId);
            name = row.Name.ExtractText();
        }
        catch
        {
            name = $"#{weatherId}";
        }
        weatherNameCache[weatherId] = name;
        return name;
    }

    // ---------- 天气表 ----------

    /// <summary>取某钓点的天气累计概率表；null 表示该钓点无天气数据。</summary>
    public List<(int WeatherId, int Cumulative)>? GetWeatherRates(uint spotId)
    {
        if (!Data.SpotById.TryGetValue(spotId, out var spot))
            return null;
        if (weatherRateCache.TryGetValue(spotId, out var cached))
            return cached;

        List<(int WeatherId, int Cumulative)>? rates = null;
        try
        {
            var territory = DataManager.GetExcelSheet<TerritoryType>().GetRow(spot.TerritoryTypeId);
            if (territory.WeatherRate.IsValid)
            {
                var rateRow = territory.WeatherRate.Value;
                rates = [];
                var cumulative = 0;
                var weatherIds = rateRow.Weather;
                var values = rateRow.Rate;
                for (var i = 0; i < values.Count && i < weatherIds.Count; i++)
                {
                    var value = (int)values[i];
                    if (value <= 0)
                        continue;
                    cumulative += value;
                    rates.Add(((int)weatherIds[i].RowId, cumulative));
                }
                if (rates.Count == 0)
                    rates = null;
            }
        }
        catch
        {
            rates = null;
        }

        weatherRateCache[spotId] = rates;
        return rates;
    }

    /// <summary>当前地图的天气表。</summary>
    public List<(int WeatherId, int Cumulative)>? GetCurrentTerritoryRates()
    {
        if (ClientState.TerritoryType == 0)
            return null;
        try
        {
            var territory = DataManager.GetExcelSheet<TerritoryType>().GetRow(ClientState.TerritoryType);
            if (!territory.WeatherRate.IsValid)
                return null;
            var rateRow = territory.WeatherRate.Value;
            var rates = new List<(int WeatherId, int Cumulative)>();
            var cumulative = 0;
            var weatherIds = rateRow.Weather;
            var values = rateRow.Rate;
            for (var i = 0; i < values.Count && i < weatherIds.Count; i++)
            {
                var value = (int)values[i];
                if (value <= 0)
                    continue;
                cumulative += value;
                rates.Add(((int)weatherIds[i].RowId, cumulative));
            }
            return rates.Count > 0 ? rates : null;
        }
        catch
        {
            return null;
        }
    }

    public static void PlayAlertSound()
        => MessageBeep(0x00000040);

    public void Dispose()
    {
        PluginInterface.UiBuilder.OpenMainUi -= ToggleWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        CommandManager.RemoveHandler(CommandName);
        windowSystem.RemoveAllWindows();
    }
}
