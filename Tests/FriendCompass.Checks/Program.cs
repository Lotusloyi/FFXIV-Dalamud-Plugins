using System.Numerics;
using FriendCompass;

var checks = 0;

void Check(bool passed, string name)
{
    if (!passed)
        throw new InvalidOperationException(name);
    checks++;
}

void Direction(Vector2 cameraRight, Vector3 target, Vector2 expected, string name)
{
    Check(TrackingMath.TryCompassDirection(target, cameraRight, out var actual) &&
        Vector2.Distance(actual, expected) < 0.0001f, name);
}

// Independently specified north/east/south/west camera and target fixtures.
Direction(new(1, 0), new(0, 0, -300), new(0, -1), "North camera: forward");
Direction(new(1, 0), new(300, 0, 0), new(1, 0), "North camera: right");
Direction(new(1, 0), new(0, 0, 300), new(0, 1), "North camera: behind");
Direction(new(1, 0), new(-300, 0, 0), new(-1, 0), "North camera: left");
Direction(new(0, 1), new(500, 0, 0), new(0, -1), "East camera: forward");
Direction(new(0, 1), new(0, 0, 500), new(1, 0), "East camera: right");
Direction(new(0, 1), new(-500, 0, 0), new(0, 1), "East camera: behind");
Direction(new(0, 1), new(0, 0, -500), new(-1, 0), "East camera: left");
Direction(new(-1, 0), new(0, 0, 900), new(0, -1), "South camera: forward");
Direction(new(0, -1), new(-900, 0, 0), new(0, -1), "West camera: forward");
var diagonal = MathF.Sqrt(0.5f);
Direction(new(diagonal, diagonal), new(250, 0, -250), new(0, -1), "Diagonal camera: forward");
Direction(new(1, 0), new(300, 800, 0), new(1, 0), "Altitude does not rotate bearing");
Check(!TrackingMath.TryCompassDirection(Vector3.Zero, Vector2.UnitX, out _), "Coincident positions");
Check(!TrackingMath.TryCompassDirection(new(0, 4, 0), Vector2.UnitX, out _), "Vertical-only target");
Check(!TrackingMath.TryCompassDirection(new(float.NaN, 0, 0), Vector2.UnitX, out _), "Invalid coordinates");
Check(!TrackingMath.TryCompassDirection(Vector3.One, Vector2.Zero, out _), "Invalid camera");

Check(TrackingMath.TryEdgeMarker(Vector2.UnitX, new(100, 200), new(1920, 1080), 24, out var rightEdge) &&
    rightEdge == new Vector2(1996, 740), "Offscreen right marker stays inside offset viewport");
Check(TrackingMath.TryEdgeMarker(-Vector2.UnitY, Vector2.Zero, new(1920, 1080), 24, out var topEdge) &&
    topEdge == new Vector2(960, 24), "Offscreen top marker");
Check(TrackingMath.TryEdgeMarker(new(-10, 10), Vector2.Zero, new(1920, 1080), 24, out var diagonalEdge) &&
    diagonalEdge == new Vector2(444, 1056), "Offscreen diagonal marker preserves bearing");
Check(TrackingMath.TryEdgeMarker(Vector2.UnitX, Vector2.Zero, new(320, 240), 1000, out var smallEdge) &&
    smallEdge.X >= 0 && smallEdge.X <= 320, "Large UI scale does not place marker outside small viewport");
Check(!TrackingMath.TryEdgeMarker(Vector2.Zero, Vector2.Zero, new(1920, 1080), 24, out _), "Invalid edge direction");
Check(!TrackingMath.TryEdgeMarker(new(float.NaN, 1), Vector2.Zero, new(1920, 1080), 24, out _), "Nonfinite edge direction");

Check(TrackingMath.WorldToTexture(Vector3.Zero, 100, 0, 0) == new Vector2(1024, 1024), "Map center");
Check(TrackingMath.WorldToTexture(new(-1024, 0, -1024), 100, 0, 0) == Vector2.Zero, "Map northwest");
Check(TrackingMath.WorldToTexture(new(1024, 0, 1024), 100, 0, 0) == new Vector2(2048), "Map southeast");
Check(TrackingMath.WorldToTexture(new(-200, 0, 300), 200, 200, -300) == new Vector2(1024), "Map offsets");
Check(TrackingMath.WorldToTexture(new(100, 999, -100), 200, 0, 0) == new Vector2(1224, 824), "Map scale and X/Z");
Check(TrackingMath.TextureToNode(new(1224, 824), new(1024, 1024)) == new Vector2(612, 412), "Texture node half-size");
var nodePoint = TrackingMath.TransformNodePoint(new(10, 20), new(40, 50), Vector2.Zero, new(2), 0);
Check(nodePoint == new Vector2(60, 90), "Map node zoom and pan");
Check(TrackingMath.TransformNodePoint(nodePoint, new(100, 200), Vector2.Zero, new(1.5f), 0) == new Vector2(190, 335),
    "Parent/UI scale applied once");
Check(TrackingMath.TransformNodePoint(new(15), new(3), new(10), new(2), 0) == new Vector2(23), "Node scale origin");
Check(Vector2.Distance(TrackingMath.TransformNodePoint(new(10, 0), Vector2.Zero, Vector2.Zero, Vector2.One, MathF.PI / 2),
    new(0, 10)) < 0.0001f, "Node rotation");

var context = new TrackingContext(100, 21, 1, 4);
var cached = new TrackedPosition(42, new(100, 0, 200), context, 1000, PositionSource.Character);
Check(cached.IsRecent(context, 42, 121000, 120), "Cache retained to deadline");
Check(!cached.IsRecent(context, 42, 121001, 120), "Cache expires");
Check(!cached.IsRecent(context, 43, 2000, 120), "Cache rejects different friend");
Check(!cached.IsRecent(context with { Territory = 101 }, 42, 2000, 120), "Cache rejects different territory");
Check(!cached.IsRecent(context with { World = 22 }, 42, 2000, 120), "Cache rejects different world");
Check(!cached.IsRecent(context with { Instance = 2 }, 42, 2000, 120), "Cache rejects different instance");
Check(!cached.IsRecent(context with { Map = 5 }, 42, 2000, 120), "Cache rejects different map layer");
Check(!cached.IsRecent(context, 42, 999, 120), "Cache rejects backwards clock");
var lastSeen = cached with { Source = PositionSource.LastSeen };
Check(lastSeen.ObservedAt == cached.ObservedAt && lastSeen.Describe(3000).Contains("2 秒前"), "Cache preserves age and labels it");

FriendSnapshot Friend(ulong id, bool online) => new()
{
    ContentId = id, Name = $"Friend {id}", HomeWorld = 21, CurrentWorld = 21,
    Location = 100, Job = 19, Online = online,
};

var tracked = Friend(42, true);
var distant = new NearbyPlayer((nint)1, 42, "Friend 42", 21, new(388, 0, 0), 2, true);
Check(NearbyPlayerMatcher.Find([distant], tracked) == distant, "DR native player at 388m can be matched");
var farAcrossMap = distant with { Position = new(1500, 0, 0) };
Check(NearbyPlayerMatcher.Find([farAcrossMap], tracked) == farAcrossMap, "No artificial 400m cutoff");
var nameFallback = distant with { ContentId = 99 };
Check(NearbyPlayerMatcher.Find([nameFallback], tracked) == nameFallback, "Name and home world recover differing ID");
Check(NearbyPlayerMatcher.Find([nameFallback with { ContentId = 0 }], tracked) != null, "Name fallback recovers missing ID");
Check(NearbyPlayerMatcher.Find([nameFallback with { HomeWorld = 22 }], tracked) == null, "Same name on another home world rejected");
Check(NearbyPlayerMatcher.Find([nameFallback with { Name = "Friend 420" }], tracked) == null, "Partial name rejected");
Check(NearbyPlayerMatcher.Find([nameFallback, distant], tracked) == distant, "ContentId match outranks earlier name fallback");
Check(NearbyPlayerMatcher.Find([distant], Friend(42, false)) == distant, "Live player not hidden by stale offline status");
Check(NearbyPlayerMatcher.Find([], tracked) == null, "Missing native and object-table player has no fabricated coordinates");

var monitor = new FriendPresenceMonitor();
Check(monitor.Update([Friend(1, true), Friend(2, false)]).Count == 0, "Initial baseline has no alerts");
Check(monitor.Update([]).Count == 0, "Empty proxy has no alerts");
Check(monitor.Update([Friend(1, false), Friend(2, true)]).Count == 0, "Transitions require confirmation");
var changes = monitor.Update([Friend(1, false), Friend(2, true)]);
Check(changes.Count == 2 && changes.Contains(("Friend 1", false)) && changes.Contains(("Friend 2", true)),
    "Both login/logout transitions without a tracked target");
Check(monitor.Update([Friend(1, false), Friend(2, true)]).Count == 0, "No duplicate alerts");
Check(monitor.Update([Friend(1, true), Friend(2, true)]).Count == 0, "Transient status held");
Check(monitor.Update([Friend(1, false), Friend(2, true)]).Count == 0, "Transient status rejected");
Check(monitor.Update([Friend(3, true)]).Count == 0, "New friend establishes baseline");
monitor.Clear();
Check(monitor.Update([Friend(1, true), Friend(2, false)]).Count == 0, "Login/character switch resets baseline");

Check(InstanceMenu.IsTravelEntry("切换副本区"), "Chinese travel submenu");
Check(InstanceMenu.IsTravelEntry("Travel to Instanced Area"), "English travel submenu");
Check(!InstanceMenu.IsTravelEntry("都市传送网"), "Do not select aethernet travel");
Check(InstanceMenu.Number("黑衣森林中央林区\uE0B5（空闲）") == 5, "Private-use instance glyph 5");
Check(InstanceMenu.Number("副本区 3") == 3, "Text instance number");
Check(InstanceMenu.Number("Instance 9 (Full)") == 9, "Availability does not hide instance number");
Check(InstanceMenu.Number("传送到住宅区第5区") == 0, "Unrelated menu digits rejected");
Check(InstanceMenu.Number("分流 10") == 0, "Out-of-range instance rejected");
Check(InstanceMenu.Next(3, [1, 2, 3, 4, 5], new HashSet<int> { 3 }) == 4, "Try next known instance");
Check(InstanceMenu.Next(4, [1, 2, 3, 4, 5], new HashSet<int> { 3, 4 }) == 5, "Advance from 4 to friend instance 5");
Check(InstanceMenu.Next(5, [1, 2, 3, 4, 5], new HashSet<int> { 3, 4, 5 }) == 1, "Wrap within menu choices");
Check(InstanceMenu.Next(5, [1, 2, 3, 4, 5], new HashSet<int> { 1, 2, 3, 4, 5 }) == 0, "Never repeat completed cycle");
Check(InstanceMenu.Next(3, [], new HashSet<int> { 3 }) == 4, "Unobserved menu discovery attempt");

var roster = new AreaRoster();
Check(!roster.IsCurrent(context, 1000), "No area data before a response");
roster.Replace([new AreaPlayer(1, "Friend 1", 21, 21, 100, 19)], context, 1000, false);
Check(roster.IsCurrent(context, 2000) && roster.Contains(Friend(1, true)), "Area roster identifies a distant friend by ID");
Check(!roster.Contains(Friend(2, true)), "Area roster excludes unlisted friend");
Check(!roster.IsCurrent(context with { Instance = 5 }, 2000), "Old area response not carried across instance changes");
Check(!roster.IsCurrent(context with { Territory = 101 }, 2000), "Old area response not carried across territories");
Check(!roster.IsCurrent(context with { World = 22 }, 2000), "Old area response not carried across worlds");
Check(roster.IsCurrent(context with { Map = 5 }, 2000), "Area names apply to all floors in the same territory");
Check(!roster.IsCurrent(context, 121001) && !roster.IsCurrent(context, 999), "Area roster expires and rejects backwards clock");
roster.Replace([new AreaPlayer(0, "Friend 1", 21, 21, 100, 19)], context, 1000, true);
Check(roster.Contains(Friend(1, true)) && roster.MayBeTruncated, "Name and home world fallback with truncated result");
roster.Replace([new AreaPlayer(0, "Friend 1", 22, 21, 100, 19)], context, 1000, false);
Check(!roster.Contains(Friend(1, true)), "Same-name player on another home world is not the friend");
roster.Clear();
Check(!roster.HasData && roster.Players.Count == 0, "Area data cleared on zoning/logout");

Console.WriteLine($"PASS: {checks} FriendCompass regression checks");
