using TimeGolf.Game.Geometry;

namespace TimeGolf.Game.Levels;

internal static class LevelGenValidator
{
    // Added to the ball radius so a path that only just fits is not counted as passable.
    public const double ClearanceMargin = 0.02;

    private const int PerCell = 4;
    private const double Step = 1.0 / PerCell;
    private const double NodeCenter = 0.5;
    // The hole keeps HoleClearance from every wall, so within this radius the straight line is the walking path.
    private const double StraightLineRadius = 1;
    private static readonly double DiagonalStep = Step * Math.Sqrt(2);

    public static bool IsBeatable(Level level, double radius)
    {
        var (blocked, nx, nz) = Blocked(level, radius);
        var from = Index(level, level.Start.Center);
        var target = Index(level, level.Hole);
        if (blocked[from] || blocked[target]) return false;

        var queue = new Queue<int>([from]);
        blocked[from] = true;
        while (queue.TryDequeue(out var n))
        {
            if (n == target) return true;
            int i = n % nx, j = n / nx;
            foreach (var (di, dj) in (ReadOnlySpan<(int, int)>)[(1, 0), (-1, 0), (0, 1), (0, -1)])
            {
                int ni = i + di, nj = j + dj;
                if (ni < 0 || nj < 0 || ni >= nx || nj >= nz || blocked[(nj * nx) + ni]) continue;
                blocked[(nj * nx) + ni] = true;
                queue.Enqueue((nj * nx) + ni);
            }
        }

        return false;
    }

    public static double[] DistanceField(Level level, double radius)
    {
        var (blocked, nx, nz) = Blocked(level, radius);
        var dist = new double[nx * nz];
        Array.Fill(dist, double.PositiveInfinity);
        var queue = new PriorityQueue<int, double>();
        var hole = Index(level, level.Hole);
        foreach (var n in Around(hole % nx, hole / nx, nx, nz))
        {
            if (blocked[n]) continue;
            dist[n] = (Node(n % nx, n / nx) - level.Hole).Length;
            queue.Enqueue(n, dist[n]);
        }

        while (queue.TryDequeue(out var n, out var d))
        {
            if (d > dist[n]) continue;
            int i = n % nx, j = n / nx;
            foreach (var m in Around(i, j, nx, nz))
            {
                int mi = m % nx, mj = m / nx;
                var diagonal = mi != i && mj != j;
                // No corner cutting: a diagonal move needs both orthogonal neighbours free.
                if (blocked[m] || (diagonal && (blocked[(j * nx) + mi] || blocked[(mj * nx) + i]))) continue;
                var next = d + (diagonal ? DiagonalStep : Step);
                if (next >= dist[m]) continue;
                dist[m] = next;
                queue.Enqueue(m, next);
            }
        }

        return dist;
    }

    public static double Distance(Level level, double[] field, Vec2 p)
    {
        var direct = (p - level.Hole).Length;
        if (direct < StraightLineRadius) return direct;

        int nx = level.Width * PerCell, nz = level.Height * PerCell;
        int ci = Math.Clamp((int)(p.X / Step), 0, nx - 1), cj = Math.Clamp((int)(p.Z / Step), 0, nz - 1);
        var best = double.PositiveInfinity;
        int? foundAt = null;
        for (var ring = 0; ring < Math.Max(nx, nz) && (foundAt == null || ring <= foundAt + 1); ring++)
        {
            foreach (var n in Ring(ci, cj, ring, nx, nz))
            {
                if (double.IsInfinity(field[n])) continue;
                best = Math.Min(best, field[n] + (Node(n % nx, n / nx) - p).Length);
                foundAt ??= ring;
            }
        }

        return foundAt == null ? direct : best;
    }

    public static bool HasClearance(Level level, Vec2 p, double distance) =>
        HasClearance(level.Walls, p, distance) && HasClearance(level.PillarWalls, p, distance);

    private static bool HasClearance(IReadOnlyList<Wall> walls, Vec2 p, double distance)
    {
        for (var k = 0; k < walls.Count; k++)
        {
            var (a, b) = (walls[k].A, walls[k].B);
            if (p.X > Math.Min(a.X, b.X) - distance && p.X < Math.Max(a.X, b.X) + distance
                && p.Z > Math.Min(a.Z, b.Z) - distance && p.Z < Math.Max(a.Z, b.Z) + distance
                && walls[k].DistanceTo(p) < distance)
            {
                return false;
            }
        }

        return true;
    }

    private static (bool[] Blocked, int Nx, int Nz) Blocked(Level level, double radius)
    {
        int nx = level.Width * PerCell, nz = level.Height * PerCell;
        var blocked = new bool[nx * nz];
        for (var j = 0; j < nz; j++)
        {
            for (var i = 0; i < nx; i++)
                blocked[(j * nx) + i] = level.CellAt(i / PerCell, j / PerCell) == Surface.Void;
        }

        void Mark(double minX, double minZ, double maxX, double maxZ, Func<Vec2, bool> hit)
        {
            for (var j = Math.Max(0, (int)(minZ / Step)); j <= Math.Min(nz - 1, (int)(maxZ / Step)); j++)
            {
                for (var i = Math.Max(0, (int)(minX / Step)); i <= Math.Min(nx - 1, (int)(maxX / Step)); i++)
                {
                    if (hit(Node(i, j))) blocked[(j * nx) + i] = true;
                }
            }
        }

        foreach (var w in level.Walls.Concat(level.PillarWalls))
        {
            Mark(Math.Min(w.A.X, w.B.X) - radius, Math.Min(w.A.Z, w.B.Z) - radius, Math.Max(w.A.X, w.B.X) + radius,
                Math.Max(w.A.Z, w.B.Z) + radius, p => w.DistanceTo(p) < radius);
        }

        // Windmill gates are ignored: they open periodically, so they never make a level unbeatable.
        foreach (var p in level.Solids)
            Mark(p.X, p.Z, p.X + p.W, p.Z + p.D, new Rect(p.X, p.Z, p.W, p.D).Contains);

        foreach (var b in level.Bumpers)
        {
            var reach = b.R + radius;
            var c = new Vec2(b.X, b.Z);
            Mark(b.X - reach, b.Z - reach, b.X + reach, b.Z + reach, p => (p - c).Length < reach);
        }

        return (blocked, nx, nz);
    }

    private static int Index(Level level, Vec2 p)
    {
        int nx = level.Width * PerCell, nz = level.Height * PerCell;
        return (Math.Clamp((int)(p.Z / Step), 0, nz - 1) * nx) + Math.Clamp((int)(p.X / Step), 0, nx - 1);
    }

    private static IEnumerable<int> Around(int ci, int cj, int nx, int nz)
    {
        for (var j = Math.Max(0, cj - 1); j <= Math.Min(nz - 1, cj + 1); j++)
        {
            for (var i = Math.Max(0, ci - 1); i <= Math.Min(nx - 1, ci + 1); i++)
                yield return (j * nx) + i;
        }
    }

    private static IEnumerable<int> Ring(int ci, int cj, int r, int nx, int nz)
    {
        for (var j = cj - r; j <= cj + r; j++)
        {
            for (var i = ci - r; i <= ci + r; i++)
            {
                if ((Math.Abs(i - ci) == r || Math.Abs(j - cj) == r) && i >= 0 && j >= 0 && i < nx && j < nz) yield return (j * nx) + i;
            }
        }
    }

    private static Vec2 Node(int i, int j) => new((i + NodeCenter) * Step, (j + NodeCenter) * Step);
}