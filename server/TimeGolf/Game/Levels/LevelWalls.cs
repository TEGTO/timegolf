using TimeGolf.Game.Geometry;

namespace TimeGolf.Game.Levels;

internal static class LevelWalls
{
    public static List<Wall> Build(int width, int height, Surface[] cells, int[] heights)
    {
        Surface At(int x, int z) => x < 0 || z < 0 || x >= width || z >= height ? Surface.Void : cells[(z * width) + x];
        int TierAt(int x, int z) => x < 0 || z < 0 || x >= width || z >= height ? 0 : heights[(z * width) + x];

        List<Wall> walls = [];

        for (var z = 0; z <= height; z++)
        {
            int? runStart = null;
            for (var x = 0; x <= width; x++)
            {
                var edge = x < width && HasEdge(At(x, z - 1), TierAt(x, z - 1), At(x, z), TierAt(x, z));
                if (edge && runStart == null) runStart = x;
                if (!edge && runStart != null)
                {
                    walls.Add(new Wall(new Vec2(runStart.Value, z), new Vec2(x, z)));
                    runStart = null;
                }
            }
        }

        for (var x = 0; x <= width; x++)
        {
            int? runStart = null;
            for (var z = 0; z <= height; z++)
            {
                var edge = z < height && HasEdge(At(x - 1, z), TierAt(x - 1, z), At(x, z), TierAt(x, z));
                if (edge && runStart == null) runStart = z;
                if (!edge && runStart != null)
                {
                    walls.Add(new Wall(new Vec2(x, runStart.Value), new Vec2(x, z)));
                    runStart = null;
                }
            }
        }

        return walls;
    }

    private static bool HasEdge(Surface a, int tierA, Surface b, int tierB) =>
        (a, b) switch
        {
            (Surface.Void, Surface.Void) => false,
            (Surface.Void, _) or (_, Surface.Void) => true,
            // Tier changes are walkable only through slope cells; elsewhere they are cliffs.
            _ => tierA != tierB && !a.IsSlope() && !b.IsSlope()
        };
}