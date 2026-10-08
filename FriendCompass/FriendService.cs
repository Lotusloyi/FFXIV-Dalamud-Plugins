using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace FriendCompass;

/// <summary>一条好友信息的快照（从 InfoProxyFriendList 读取）。</summary>
public sealed class FriendSnapshot
{
    public required ulong ContentId;
    public required string Name;
    public required ushort HomeWorld;
    public required ushort CurrentWorld;
    /// <summary>TerritoryType 行 ID（好友所在区域），0 表示未知 / 离线。</summary>
    public required ushort Location;
    public required byte Job;
    public required bool Online;

    /// <summary>是否和指定区域在同一图。</summary>
    public bool InZone(uint territoryId) => Online && Location == territoryId && territoryId != 0;
}

/// <summary>从客户端好友列表代理读取好友数据。</summary>
public sealed class FriendService
{
    public unsafe List<FriendSnapshot> GetFriends()
    {
        var list = new List<FriendSnapshot>();
        try
        {
            var proxy = InfoProxyFriendList.Instance();
            if (proxy == null)
                return list;

            var common = &proxy->InfoProxyCommonList;
            var span = common->CharDataSpan;
            foreach (ref readonly var entry in span)
            {
                var name = entry.NameString;
                if (string.IsNullOrEmpty(name))
                    continue;

                var online = entry.State != InfoProxyCommonList.CharacterData.OnlineStatus.Offline;
                list.Add(new FriendSnapshot
                {
                    ContentId = entry.ContentId,
                    Name = name,
                    HomeWorld = entry.HomeWorld,
                    CurrentWorld = entry.CurrentWorld,
                    Location = entry.Location,
                    Job = entry.Job,
                    Online = online,
                });
            }
        }
        catch
        {
            // 读取失败（如游戏未完全登录）返回空列表即可
        }
        return list;
    }
}
