using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace FriendCompass;

/// <summary>从客户端好友列表代理读取好友数据。</summary>
public sealed class FriendService
{
    public unsafe List<FriendSnapshot> GetFriends()
        => TryGetFriends(out var friends) ? friends : [];

    public unsafe bool TryGetFriends(out List<FriendSnapshot> list)
    {
        list = [];
        try
        {
            var proxy = InfoProxyFriendList.Instance();
            if (proxy == null)
                return false;

            var common = &proxy->InfoProxyCommonList;
            if (proxy->EntryCount > 200 || (proxy->EntryCount != 0 && common->CharData == null))
                return false;
            var span = common->CharDataSpan;
            foreach (ref readonly var entry in span)
            {
                var name = entry.NameString;
                if (string.IsNullOrEmpty(name) || entry.ContentId == 0 || entry.WaitingForFriendListApproval)
                    continue;

                const InfoProxyCommonList.CharacterData.OnlineStatus offlineStates =
                    InfoProxyCommonList.CharacterData.OnlineStatus.OfflineExd |
                    InfoProxyCommonList.CharacterData.OnlineStatus.NotFound |
                    InfoProxyCommonList.CharacterData.OnlineStatus.WaitingForFriendListApproval;
                var online = entry.State != 0 && (entry.State & offlineStates) == 0;
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
            list.Clear();
            return false;
        }
        return true;
    }

    private long lastRequestTick;

    /// <summary>向服务器请求刷新好友列表（填充所在地区等字段）。</summary>
    public unsafe bool RequestRefresh()
    {
        var now = Environment.TickCount64;
        if (now - lastRequestTick < 5_000)
            return false;
        lastRequestTick = now;
        try
        {
            var proxy = InfoProxyFriendList.Instance();
            if (proxy != null)
                return proxy->RequestData();
        }
        catch
        {
            // 忽略
        }
        return false;
    }
}
