using Dalamud.Configuration;
using Dalamud.Plugin;

namespace FriendCompass;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>是否显示悬浮罗盘（箭头 + 距离）。</summary>
    public bool ShowOverlay { get; set; } = true;

    /// <summary>是否在世界中（好友头顶）绘制标记。</summary>
    public bool ShowWorldMarker { get; set; } = true;

    /// <summary>是否在大地图（AreaMap）上标记好友位置。</summary>
    public bool ShowMapMarker { get; set; } = true;

    /// <summary>副本（任务）内自动禁用指向与标记。</summary>
    public bool DisableInDuty { get; set; } = true;

    /// <summary>点击追踪时自动传送到好友所在服务器 / 地图。</summary>
    public bool TeleportOnTrack { get; set; } = true;

    /// <summary>自动追踪同图（同区域）的在线好友。</summary>
    public bool AutoTrackSameZone { get; set; }

    /// <summary>追踪的好友跨图 / 来到同图时聊天栏提醒。</summary>
    public bool AlertOnZoneChange { get; set; } = true;

    /// <summary>提醒时播放提示音。</summary>
    public bool PlaySound { get; set; } = true;

    /// <summary>当前追踪的好友（ContentId），0 表示未追踪。</summary>
    public ulong TrackedContentId { get; set; }

    /// <summary>追踪好友的名字缓存（用于在对象表中查找）。</summary>
    public string TrackedName { get; set; } = string.Empty;

    public static Configuration Load(IDalamudPluginInterface pluginInterface)
        => pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

    public void Save(IDalamudPluginInterface pluginInterface)
        => pluginInterface.SavePluginConfig(this);
}
