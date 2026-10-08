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

    /// <summary>追踪的好友同图但未找到（疑似分流不同）时自动切换分流查找。</summary>
    public bool AutoSwitchInstance { get; set; } = true;

    /// <summary>追踪的好友跨图 / 来到同图时聊天栏提醒。</summary>
    public bool AlertOnZoneChange { get; set; } = true;

    public bool AlertOnOnlineChange { get; set; } = true;

    public float CompassSize { get; set; } = 160f;

    public float WorldMarkerScale { get; set; } = 1.5f;

    public float MapMarkerScale { get; set; } = 1.5f;

    public bool UsePartyPositions { get; set; } = true;

    /// <summary>当前地图的服务器玩家名单，没有 XYZ 或分流。</summary>
    public bool UseAreaSearch { get; set; } = true;

    public bool KeepLastKnownPosition { get; set; } = true;

    public int LastKnownPositionSeconds { get; set; } = 120;

    public int FriendRefreshSeconds { get; set; } = 30;

    /// <summary>当前追踪的好友（ContentId），0 表示未追踪。</summary>
    public ulong TrackedContentId { get; set; }

    /// <summary>追踪好友的名字缓存（用于在对象表中查找）。</summary>
    public string TrackedName { get; set; } = string.Empty;

    public static Configuration Load(IDalamudPluginInterface pluginInterface)
        => pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

    public void Save(IDalamudPluginInterface pluginInterface)
        => pluginInterface.SavePluginConfig(this);
}
