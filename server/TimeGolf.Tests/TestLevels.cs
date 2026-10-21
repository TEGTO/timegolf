using TimeGolf.Game.Geometry;
using TimeGolf.Game.Levels;

namespace TimeGolf.Tests;

internal static class TestLevels
{
    public static Level Corridor()
    {
        const int width = 8;
        const int height = 20;
        var cells = new Surface[width * height];
        for (var z = 1; z < height - 1; z++)
        {
            for (var x = 1; x < width - 1; x++)
                cells[(z * width) + x] = Surface.Grass;
        }

        var start = new Rect(1, 13, 6, 6);
        List<Vec2> spawns = [];
        for (var z = 14.0; z <= 18; z += 0.6)
        {
            for (var x = 1.6; x <= 6.4; x += 0.6)
                spawns.Add(new Vec2(x, z));
        }

        return Level.Create(0, 1, width, height, cells, new int[width * height], [], [], [], start, spawns, new Vec2(4, 3), 0);
    }
}