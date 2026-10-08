using System.Runtime.InteropServices;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FateTracker.Windows;

namespace FateTracker;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "FateTracker";

    private const string CommandName = "/fate";

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    // 构造函数注入的 Dalamud 服务（v15 要求，静态 [PluginService] 不再自动注入）
    public IDalamudPluginInterface PluginInterface { get; }
    public ICommandManager CommandManager { get; }
    public IClientState ClientState { get; }
    public IObjectTable ObjectTable { get; }
    public IFateTable FateTable { get; }
    public IChatGui ChatGui { get; }
    public IPluginLog Log { get; }

    public Configuration Configuration { get; }
    private readonly WindowSystem windowSystem = new("FateTracker");
    private readonly FateTrackerWindow fateWindow;

    private readonly HashSet<ushort> knownFateIds = [];
    private bool initialScanDone;
    private readonly List<ushort> currentFrameIds = [];

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IClientState clientState,
        IObjectTable objectTable,
        IFateTable fateTable,
        IChatGui chatGui,
        IPluginLog log)
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        ClientState = clientState;
        ObjectTable = objectTable;
        FateTable = fateTable;
        ChatGui = chatGui;
        Log = log;

        Configuration = Configuration.Load(pluginInterface);

        fateWindow = new FateTrackerWindow(this);
        windowSystem.AddWindow(fateWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开 / 关闭 FATE 追踪器窗口。",
            ShowInHelp = true,
        });

        PluginInterface.UiBuilder.Draw += ScanFates;
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi += ToggleWindow;

        ClientState.TerritoryChanged += OnTerritoryChanged;

        Log.Information("FateTracker 已加载，输入 /fate 打开窗口。");
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        knownFateIds.Clear();
        initialScanDone = false;
    }

    /// <summary>每帧扫描当前地图的 FATE：建立 / 更新基线，并对新出现的 FATE 发送提醒。窗口关闭时也会运行。</summary>
    private void ScanFates()
    {
        currentFrameIds.Clear();

        foreach (var fate in FateTable)
        {
            currentFrameIds.Add(fate.FateId);

            if (!initialScanDone)
            {
                knownFateIds.Add(fate.FateId);
                continue;
            }

            if (!Configuration.AlertOnNewFate)
            {
                knownFateIds.Add(fate.FateId);
                continue;
            }

            if (knownFateIds.Add(fate.FateId))
            {
                ChatGui.Print($"[FateTracker] 新的 FATE 出现：{fate.Name.TextValue}");
                if (Configuration.PlaySound)
                    PlayAlertSound();
            }
        }

        if (!initialScanDone)
        {
            initialScanDone = true;
            return;
        }

        knownFateIds.RemoveWhere(id => !currentFrameIds.Contains(id));
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
                fateWindow.IsOpen = true;
                break;
            case "off":
                fateWindow.IsOpen = false;
                break;
            default:
                ChatGui.PrintError("[FateTracker] 未知参数。用法：/fate [on|off]");
                break;
        }
    }

    public void ToggleWindow()
        => fateWindow.Toggle();

    private static void PlayAlertSound()
        => MessageBeep(0x00000040); // MB_ICONASTERISK

    public void Dispose()
    {
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.Draw -= ScanFates;
        CommandManager.RemoveHandler(CommandName);
        windowSystem.RemoveAllWindows();
    }
}
