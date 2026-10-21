using TimeGolf.Game.Geometry;

namespace TimeGolf.Game.Levels;

public sealed record Pillar(double X, double Z, double W, double D)
{
    public IEnumerable<Wall> Edges()
    {
        Vec2 a = new(X, Z), b = new(X + W, Z), c = new(X + W, Z + D), d = new(X, Z + D);
        return [new Wall(a, b), new Wall(b, c), new Wall(c, d), new Wall(d, a)];
    }
}