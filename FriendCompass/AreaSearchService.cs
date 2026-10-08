using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;

namespace FriendCompass;

public sealed unsafe class AreaSearchService : IDisposable
{
    private delegate void EndRequestDelegate(InfoProxySearch* proxy);
    private readonly Plugin plugin;
    private readonly Hook<EndRequestDelegate>? endRequestHook;
    private Response? response;
    private SearchFilter? previousFilter;
    private TrackingContext requestContext;
    private long lastRequest;
    private long lastAttempt;
    private long requestStarted;
    private uint supportedTerritory;
    private ushort placeName;
    private ushort requestedPlaceName;
    private TrackingContext lastContext;
    private bool disposed;
    private bool failed;

    public AreaRoster Roster { get; } = new();
    public string Status { get; private set; } = "等待区域搜索";

    public AreaSearchService(Plugin plugin, IGameInteropProvider interop)
    {
        this.plugin = plugin;
        try
        {
            var proxy = InfoProxySearch.Instance();
            if (proxy == null) throw new InvalidOperationException("PlayerSearch proxy unavailable");
            // EndRequest is virtual function 10 in the client struct definition.
            endRequestHook = interop.HookFromAddress<EndRequestDelegate>(((nint*)*(nint*)proxy)[10], OnEndRequest);
            endRequestHook.Enable();
        }
        catch (Exception exception)
        {
            endRequestHook?.Dispose();
            endRequestHook = null;
            plugin.Log.Warning(exception, "区域搜索监听初始化失败");
            Status = "区域搜索不可用，详见日志";
        }
    }

    private void OnEndRequest(InfoProxySearch* proxy)
    {
        endRequestHook!.Original(proxy);
        if (disposed || previousFilter == null || proxy != InfoProxySearch.Instance()) return;
        try
        {
            var common = &proxy->InfoProxyCommonList;
            if (proxy->EntryCount > 200 || (proxy->EntryCount != 0 && common->CharData == null)) return;
            var players = common->CharDataSpan.ToArray().Select(entry =>
                new AreaPlayer(entry.ContentId, entry.NameString, entry.HomeWorld, entry.CurrentWorld, entry.Location, entry.Job)).ToArray();
            Interlocked.Exchange(ref response, new Response(players, proxy->LocationCount == 1 ? proxy->LocationIDs[0] : (ushort)0,
                proxy->NameString, proxy->EntryCount >= 200, requestContext));
        }
        catch (Exception exception)
        {
            plugin.Log.Warning(exception, "读取区域搜索结果失败");
        }
    }

    public void Tick()
    {
        if (endRequestHook == null || failed) return;
        try
        {
            TickCore();
        }
        catch (Exception exception)
        {
            failed = true;
            Roster.Clear();
            RestoreFilter();
            Status = "区域搜索已暂停，详见日志";
            plugin.Log.Warning(exception, "区域搜索失败，已暂停后台查询");
        }
    }

    private void TickCore()
    {
        var context = plugin.CurrentTrackingContext();
        var now = Environment.TickCount64;
        if (!plugin.ClientState.IsLoggedIn || plugin.ObjectTable.LocalPlayer == null || plugin.DisabledByDuty ||
            plugin.Condition[ConditionFlag.BetweenAreas] || plugin.Condition[ConditionFlag.BetweenAreas51])
        {
            Roster.Clear();
            Interlocked.Exchange(ref response, null);
            RestoreFilter();
            return;
        }
        if (!AreaRoster.SameArea(lastContext, context))
        {
            Roster.Clear();
            lastRequest = 0;
            lastContext = context;
        }
        if (supportedTerritory != context.Territory)
        {
            var territory = plugin.DataManager.GetExcelSheet<TerritoryType>().GetRow(context.Territory);
            var zone = territory.PlaceNameZone.RowId;
            placeName = plugin.DataManager.GetExcelSheet<PlayerSearchSubLocation>().Any(row => row.PlaceName.RowId == zone && zone != 519)
                ? (ushort)zone : (ushort)0;
            supportedTerritory = context.Territory;
        }

        var completed = Interlocked.Exchange(ref response, null);
        if (completed != null)
        {
            if (completed.PlaceName == placeName && placeName != 0 && completed.Name.Length == 0 &&
                AreaRoster.SameArea(completed.Context, context) &&
                previousFilter != null && AreaRoster.SameArea(requestContext, context))
            {
                var players = completed.Players.Where(player => player.Job > 0 && player.Territory == context.Territory &&
                    (player.CurrentWorld == 0 || player.CurrentWorld == context.World)).ToArray();
                Roster.Replace(players, context, now, completed.Truncated);
                lastRequest = now;
                Status = $"区域名单 {players.Length} 人";
                plugin.Log.Information($"区域搜索完成：地区={context.Territory} 当前分流={context.Instance} 人数={players.Length} 截断={completed.Truncated}；搜索名单不提供坐标或好友分流");
            }
            RestoreFilter();
        }
        if (previousFilter != null && now - requestStarted >= 15000)
        {
            RestoreFilter();
            Status = "区域搜索未完成，等待下次刷新";
        }
        if (!plugin.Configuration.UseAreaSearch || plugin.TrackedFriend is not { Online: true } target ||
            target.Location != context.Territory || target.CurrentWorld != context.World || placeName == 0)
            return;
        if (previousFilter != null || now - lastRequest < 60000 || now - lastAttempt < 15000 || IsSearchOpen()) return;

        var proxy = InfoProxySearch.Instance();
        if (proxy == null) return;
        previousFilter = SearchFilter.Capture(proxy);
        requestContext = context;
        requestedPlaceName = placeName;
        requestStarted = lastRequest = lastAttempt = now;
        proxy->JobMask = ulong.MaxValue;
        proxy->LevelMin = 1;
        proxy->LevelMax = 255;
        proxy->GrandCompanyMask = byte.MaxValue;
        proxy->LanguageMask = byte.MaxValue;
        proxy->OnlineStatusMask = 1ul << 47;
        proxy->LocationCount = 1;
        proxy->LocationIDs[0] = placeName;
        proxy->Name.Clear();
        if (proxy->RequestData()) Status = "正在刷新区域名单";
        else { RestoreFilter(); Status = "区域搜索请求未发送，等待下次刷新"; }
    }

    public void RequestRefresh() => lastRequest = 0;

    private static bool IsSearchOpen()
    {
        var module = AgentModule.Instance();
        var agent = module == null ? null : module->GetAgentByInternalId(AgentId.Search);
        return agent != null && agent->IsAgentActive();
    }

    private void RestoreFilter()
    {
        var proxy = InfoProxySearch.Instance();
        if (previousFilter != null && proxy != null && !IsSearchOpen() &&
            proxy->LocationCount == 1 && proxy->LocationIDs[0] == requestedPlaceName && proxy->NameString.Length == 0 &&
            proxy->JobMask == ulong.MaxValue && proxy->LevelMin == 1 && proxy->LevelMax == 255 &&
            proxy->GrandCompanyMask == byte.MaxValue && proxy->LanguageMask == byte.MaxValue && proxy->OnlineStatusMask == 1ul << 47)
            previousFilter.Restore(proxy);
        previousFilter = null;
    }

    public void Dispose()
    {
        disposed = true;
        endRequestHook?.Dispose();
        RestoreFilter();
    }

    private sealed record Response(AreaPlayer[] Players, ushort PlaceName, string Name, bool Truncated, TrackingContext Context);

    private sealed record SearchFilter(ulong Jobs, ushort Min, ushort Max, byte Company, byte Language,
        ulong Online, byte LocationCount, ushort[] Locations, byte[] Name)
    {
        public static SearchFilter Capture(InfoProxySearch* proxy) => new(proxy->JobMask, proxy->LevelMin, proxy->LevelMax,
            proxy->GrandCompanyMask, proxy->LanguageMask, proxy->OnlineStatusMask, proxy->LocationCount,
            proxy->LocationIDs.ToArray(), proxy->Name.ToArray());

        public void Restore(InfoProxySearch* proxy)
        {
            proxy->JobMask = Jobs;
            proxy->LevelMin = Min;
            proxy->LevelMax = Max;
            proxy->GrandCompanyMask = Company;
            proxy->LanguageMask = Language;
            proxy->OnlineStatusMask = Online;
            proxy->LocationCount = LocationCount;
            Locations.CopyTo(proxy->LocationIDs);
            Name.CopyTo(proxy->Name);
        }
    }
}
