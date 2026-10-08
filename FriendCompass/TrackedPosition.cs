using System.Numerics;

namespace FriendCompass;

public enum PositionSource
{
    Character,
    Party,
    LastSeen,
}

public readonly record struct TrackingContext(uint Territory, ushort World, uint Instance, uint Map);

public sealed record TrackedPosition(
    ulong ContentId, Vector3 Position, TrackingContext Context, long ObservedAt,
    PositionSource Source, float Height = 2f)
{
    public bool IsRecent(TrackingContext context, ulong contentId, long now, int seconds)
        => ContentId == contentId && Context == context && now >= ObservedAt &&
           now - ObservedAt <= Math.Clamp(seconds, 10, 600) * 1000L;

    public string Describe(long now) => Source switch
    {
        PositionSource.Party => "小队坐标",
        PositionSource.LastSeen => $"最近位置 · {Math.Max(0, (now - ObservedAt) / 1000)} 秒前",
        _ => "实时坐标",
    };
}
