using System.Numerics;

namespace FriendCompass;

internal static class TrackingMath
{
    public static bool TryCompassDirection(Vector3 delta, Vector2 cameraRight, out Vector2 direction)
    {
        direction = default;
        if (!IsFinite(delta) || !float.IsFinite(cameraRight.X) || !float.IsFinite(cameraRight.Y) ||
            cameraRight.LengthSquared() < 0.0001f)
            return false;

        var right = Vector2.Normalize(cameraRight);
        var forward = new Vector2(right.Y, -right.X);
        var groundDelta = new Vector2(delta.X, delta.Z);
        direction = new(Vector2.Dot(groundDelta, right), -Vector2.Dot(groundDelta, forward));
        if (direction.LengthSquared() < 0.01f)
            return false;

        direction = Vector2.Normalize(direction);
        return true;
    }

    public static Vector2 WorldToTexture(Vector3 position, float sizeFactor, float offsetX, float offsetY)
        => new(1024f + (position.X + offsetX) * sizeFactor / 100f,
               1024f + (position.Z + offsetY) * sizeFactor / 100f);

    public static Vector2 TextureToNode(Vector2 texturePosition, Vector2 nodeSize)
        => texturePosition * nodeSize / 2048f;

    public static Vector2 TransformNodePoint(Vector2 point, Vector2 position, Vector2 origin, Vector2 scale, float rotation)
    {
        var transformed = (point - origin) * scale;
        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);
        return new Vector2(transformed.X * cos - transformed.Y * sin,
            transformed.X * sin + transformed.Y * cos) + origin + position;
    }

    public static bool IsFinite(Vector3 position)
        => float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z);

    public static bool TryEdgeMarker(Vector2 direction, Vector2 viewportPosition, Vector2 viewportSize,
        float inset, out Vector2 marker)
    {
        marker = default;
        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || direction.LengthSquared() < 0.0001f ||
            !float.IsFinite(viewportSize.X) || !float.IsFinite(viewportSize.Y) || viewportSize.X <= 0 || viewportSize.Y <= 0)
            return false;
        var halfSize = Vector2.Max(viewportSize / 2f - new Vector2(Math.Max(0, inset)), Vector2.One);
        var x = MathF.Abs(direction.X) > float.Epsilon ? halfSize.X / MathF.Abs(direction.X) : float.MaxValue;
        var y = MathF.Abs(direction.Y) > float.Epsilon ? halfSize.Y / MathF.Abs(direction.Y) : float.MaxValue;
        marker = viewportPosition + viewportSize / 2f + direction * MathF.Min(x, y);
        return float.IsFinite(marker.X) && float.IsFinite(marker.Y);
    }
}
