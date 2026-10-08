using Dalamud.Configuration;
using Dalamud.Plugin;

namespace FateTracker;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>新 FATE 出现时是否发送聊天栏提醒。</summary>
    public bool AlertOnNewFate { get; set; } = true;

    /// <summary>是否同时播放提示音。</summary>
    public bool PlaySound { get; set; } = true;

    /// <summary>是否在列表中显示与玩家的距离。</summary>
    public bool ShowDistance { get; set; } = true;

    /// <summary>是否隐藏已结束的 FATE（进度 100%）。</summary>
    public bool HideCompleted { get; set; } = true;

    public void Save(IDalamudPluginInterface pluginInterface)
        => pluginInterface.SavePluginConfig(this);

    public static Configuration Load(IDalamudPluginInterface pluginInterface)
        => pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
}
