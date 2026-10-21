namespace TimeGolf.Game.Geometry;

public readonly record struct Vec2(double X, double Z)
{
    // Below this a length is treated as zero, so a direction cannot be derived from it.
    public const double Epsilon = 1e-9;

    public static readonly Vec2 Zero = new(0, 0);

    public double Length => Math.Sqrt((X * X) + (Z * Z));
    public double LengthSquared => (X * X) + (Z * Z);
    public Vec2 Normalized => Length > Epsilon ? this / Length : Zero;

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Z + b.Z);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Z - b.Z);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Z);
    public static Vec2 operator *(Vec2 a, double k) => new(a.X * k, a.Z * k);
    public static Vec2 operator /(Vec2 a, double k) => new(a.X / k, a.Z / k);

    public static double Dot(Vec2 a, Vec2 b) => (a.X * b.X) + (a.Z * b.Z);

    // The generated record ToString would print Normalized, which is a Vec2 itself, recursing forever.
    public override string ToString() => $"({X}, {Z})";
}