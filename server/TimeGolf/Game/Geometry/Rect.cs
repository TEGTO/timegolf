namespace TimeGolf.Game.Geometry;

public sealed record Rect(double X, double Z, double W, double D)
{
    public Vec2 Center => new(X + (W / 2), Z + (D / 2));

    public bool Contains(Vec2 p) => p.X >= X && p.X <= X + W && p.Z >= Z && p.Z <= Z + D;
}