using TimeGolf.Game.Geometry;

namespace TimeGolf.Game.Levels;

public static class SurfaceExtensions
{
    public static bool IsSlope(this Surface s) => s >= Surface.SlopeEast;

    public static Vec2 SlopeDirection(this Surface s) => s switch
    {
        Surface.SlopeEast => new Vec2(1, 0),
        Surface.SlopeWest => new Vec2(-1, 0),
        Surface.SlopeSouth => new Vec2(0, 1),
        Surface.SlopeNorth => new Vec2(0, -1),
        _ => Vec2.Zero
    };

    public static char ToChar(this Surface s) => s switch
    {
        Surface.Grass => 'g',
        Surface.Sand => 's',
        Surface.SlopeEast => '>',
        Surface.SlopeWest => '<',
        Surface.SlopeSouth => 'v',
        Surface.SlopeNorth => '^',
        _ => '.'
    };
}