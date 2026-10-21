namespace TimeGolf.Game.Geometry;

public sealed record Wall(Vec2 A, Vec2 B)
{
    // Guards the projection against a degenerate zero-length segment.
    private const double MinLengthSquared = 1e-12;

    public Vec2 ClosestPoint(Vec2 p)
    {
        var ab = B - A;
        var t = Math.Clamp(Vec2.Dot(p - A, ab) / Math.Max(ab.LengthSquared, MinLengthSquared), 0, 1);
        return A + (ab * t);
    }

    public double DistanceTo(Vec2 p) => (p - ClosestPoint(p)).Length;
}