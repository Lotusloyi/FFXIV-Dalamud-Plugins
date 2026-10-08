using Dalamud.Configuration;
using Dalamud.Plugin;

namespace FishClock;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>窗口开启时发送聊天栏提醒。</summary>
    public bool AlertOnWindowOpen { get; set; } = true;

    /// <summary>提醒时播放提示音。</summary>
    public bool PlaySound { get; set; } = true;

    /// <summary>默认只显示当前地图的鱼类。</summary>
    public bool OnlyCurrentMap { get; set; }

    /// <summary>隐藏无时间/天气限制的常驻鱼。</summary>
    public bool HideAlwaysAvailable { get; set; } = true;

    public void Save(IDalamudPluginInterface pluginInterface)
        => pluginInterface.SavePluginConfig(this);

    public static Configuration Load(IDalamudPluginInterface pluginInterface)
        => pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
}
