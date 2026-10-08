namespace FriendCompass;

public sealed record AreaPlayer(ulong ContentId, string Name, ushort HomeWorld, ushort CurrentWorld, ushort Territory, byte Job);

public sealed class AreaRoster
{
    public IReadOnlyList<AreaPlayer> Players { get; private set; } = [];
    public TrackingContext Context { get; private set; }
    public long ObservedAt { get; private set; }
    public bool HasData { get; private set; }
    public bool MayBeTruncated { get; private set; }

    public void Replace(IReadOnlyList<AreaPlayer> players, TrackingContext context, long now, bool truncated)
    {
        Players = players;
        Context = context;
        ObservedAt = now;
        HasData = true;
        MayBeTruncated = truncated;
    }

    public bool IsCurrent(TrackingContext context, long now) =>
        HasData && SameArea(Context, context) && now >= ObservedAt && now - ObservedAt <= 120000;

    public static bool SameArea(TrackingContext first, TrackingContext second) =>
        first.Territory == second.Territory && first.World == second.World && first.Instance == second.Instance;

    public bool Contains(FriendSnapshot friend) => Players.Any(player =>
        (player.ContentId != 0 && player.ContentId == friend.ContentId) ||
        (player.Name == friend.Name && player.HomeWorld == friend.HomeWorld));

    public void Clear()
    {
        Players = [];
        HasData = false;
        MayBeTruncated = false;
    }
}
