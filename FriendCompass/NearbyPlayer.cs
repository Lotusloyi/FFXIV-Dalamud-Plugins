using System.Numerics;

namespace FriendCompass;

internal sealed record NearbyPlayer(nint Address, ulong ContentId, string Name, ushort HomeWorld,
    Vector3 Position, float Height, bool FromCharacterManager);

internal static class NearbyPlayerMatcher
{
    public static NearbyPlayer? Find(IReadOnlyList<NearbyPlayer> players, FriendSnapshot friend)
    {
        // A live ContentId wins over names; name + home world also identifies a character uniquely.
        var byId = players.FirstOrDefault(player => friend.ContentId != 0 && player.ContentId == friend.ContentId);
        if (byId != null)
            return byId;
        if (friend.HomeWorld == 0 || friend.HomeWorld == ushort.MaxValue || string.IsNullOrWhiteSpace(friend.Name))
            return null;
        return players.FirstOrDefault(player => player.HomeWorld == friend.HomeWorld &&
            string.Equals(player.Name, friend.Name, StringComparison.Ordinal));
    }
}
