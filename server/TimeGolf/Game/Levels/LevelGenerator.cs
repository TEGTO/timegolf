using TimeGolf.Game.Geometry;

namespace TimeGolf.Game.Levels;

public static class LevelGenerator
{
    private const int GridW = 7;
    private const int GridH = 5;
    private const int ModuleSize = 6;
    // Void ring around the module grid so every floor cell has a neighbour to wall against.
    private const int Margin = 1;
    private const int BigW = (GridW * ModuleSize) + (2 * Margin);
    private const int BigH = (GridH * ModuleSize) + (2 * Margin);
    private const int Sides = 4;
    private const int MaxDifficultyRound = 5;
    private const int BasePathLength = 4;
    private const int PathLengthJitter = 3;
    private const int MinPathLength = 5;
    private const int MaxPathLength = 10;
    private const double KeepDirectionChance = 0.6;
    private const int MinOpening = 2;
    private const int MaxOpening = 4;
    private const int WideBand = 4;
    private const int WindmillBand = 3;
    private const int NarrowBand = 2;
    private const int MinRampWidth = 3;
    private const int RampSlopeFrom = 2;
    private const int RampSlopeTo = 4;
    private const int PaletteCount = 5;
    private const double HoleClearance = 1.5;
    private const double HoleSampleStep = 0.5;
    private const int HoleSamples = (int)(ModuleSize / HoleSampleStep) - 1;
    private const double SpawnWallClearance = 0.4;
    private const double BumperRadius = 0.35;
    private const double CellCenter = 0.5;
    // Matches the 3-decimal wire rounding so the client sees the exact phase the server simulates.
    private const int PhaseDecimals = 3;
    private const double FloorSlack = 1e-9;

    private static readonly int[] Dx = [1, 0, -1, 0];
    private static readonly int[] Dz = [0, 1, 0, -1];
    private static readonly Surface[] SlopeOf = [Surface.SlopeEast, Surface.SlopeSouth, Surface.SlopeWest, Surface.SlopeNorth];
    private static readonly Piece[] Features = [Piece.Ramp, Piece.Windmill, Piece.Bumpers, Piece.Bunker, Piece.Chicane, Piece.Plaza];

    // Pillar boxes as (a1, a2, b1, b2): a = distance from the entry side, b = distance from a lateral side.
    // All are symmetric about the module center, so they suit bends too; the last one needs a straight path axis.
    private static readonly (double A1, double A2, double B1, double B2)[][] PlazaPatterns =
    [
        [(2, 4, 2, 4)],
        [(1, 2, 1, 2), (1, 2, 4, 5), (4, 5, 1, 2), (4, 5, 4, 5)],
        [(1.1, 1.9, 2.6, 3.4), (2.6, 3.4, 1.1, 1.9), (4.1, 4.9, 2.6, 3.4), (2.6, 3.4, 4.1, 4.9)],
        [(2, 4, 1, 2), (2, 4, 4, 5)]
    ];

    // Same (a1, a2, b1, b2) boxes; the second stub hangs from the opposite lateral side.
    private static readonly (double A1, double A2, double B1, double B2)[] ChicaneStubs = [(1.25, 1.75, 1, 3), (4.25, 4.75, 1, 3)];

    // Bumper centers as (a, b) like the plaza boxes: a triangle and a line across the path.
    private static readonly (double A, double B)[][] BumperPatterns = [[(2, 3), (3.8, 2), (3.8, 4)], [(3, 1.5), (3, 3), (3, 4.5)]];

    // Six-cell bunkers as (a, b) cell offsets: off-axis along one side wall on straights, tucked into the outer corner on bends.
    private static readonly (int A, int B)[] StraightBunker = [(1, 1), (2, 1), (2, 2), (3, 1), (3, 2), (4, 1)];
    private static readonly (int A, int B)[] BendBunker = [(1, 1), (1, 2), (1, 3), (2, 1), (2, 2), (3, 1)];

    private enum Piece { Start, Hole, HoleEnd, Open, Corridor, Chicane, Plaza, Bridge, Bunker, Ramp, Bumpers, Windmill }

    private sealed record Module(int X, int Z, int Entry, int Exit, Piece Kind, int Width)
    {
        public int Ox => (X * ModuleSize) + Margin;
        public int Oz => (Z * ModuleSize) + Margin;
        public int Across => Rotate(Entry, 1);
        public bool Straight => Exit == Opposite(Entry);
        public List<Pillar> Pillars { get; } = [];
        public List<Bumper> Bumpers { get; } = [];
        public List<Windmill> Windmills { get; } = [];
        public bool IsBand => Kind is Piece.HoleEnd or Piece.Corridor or Piece.Chicane or Piece.Bridge or Piece.Ramp or Piece.Windmill;

        // World point at distance a from side sa and b from the perpendicular side sb.
        public Vec2 At(int sa, double a, int sb, double b)
        {
            static double Coord(int side, double d) => side < 2 ? ModuleSize - d : d;
            return sa % 2 == 0 ? new Vec2(Ox + Coord(sa, a), Oz + Coord(sb, b)) : new Vec2(Ox + Coord(sb, b), Oz + Coord(sa, a));
        }

        public Pillar Box(int sa, double a1, double a2, int sb, double b1, double b2)
        {
            var (p, q) = (At(sa, a1, sb, b1), At(sa, a2, sb, b2));
            return new Pillar(Math.Min(p.X, q.X), Math.Min(p.Z, q.Z), Math.Abs(p.X - q.X), Math.Abs(p.Z - q.Z));
        }
    }

    public static Level Generate(int seed, int round, GameOptions options) =>
        TryGenerate(seed, round, options) ?? Generate(unchecked(seed + 1), round, options);

    private static Level? TryGenerate(int seed, int round, GameOptions options)
    {
        var clearance = options.BallRadius + LevelGenValidator.ClearanceMargin;
        var rng = new Random(seed);
        var r = Math.Clamp(round, 1, MaxDifficultyRound);
        var path = Walk(rng, Math.Clamp(BasePathLength + r + rng.Next(PathLengthJitter), MinPathLength, MaxPathLength));
        var entries = path.Select((p, i) => i > 0 ? SideTo(p, path[i - 1]) : -1).ToArray();
        var exits = path.Select((p, i) => i < path.Count - 1 ? SideTo(p, path[i + 1]) : -1).ToArray();
        var kinds = PickKinds(rng, r, [.. entries.Zip(exits, (e, x) => x == Opposite(e))]);
        var modules = path.Select((p, i) => new Module(p.X, p.Z, entries[i], exits[i], kinds[i], WidthOf(kinds[i], rng))).ToArray();

        var cells = new Surface[BigW * BigH];
        var tiers = new int[BigW * BigH];
        var tier = 0;
        foreach (var m in modules) tier = Rasterize(m, cells, tiers, tier, rng);
        Carve(modules, path, cells);

        var (x0, z0, w, h) = Bounds(cells);
        var trimmed = new Surface[w * h];
        var heights = new int[w * h];
        for (var z = 0; z < h; z++)
        {
            for (var x = 0; x < w; x++)
            {
                trimmed[(z * w) + x] = cells[((z + z0) * BigW) + x + x0];
                heights[(z * w) + x] = tiers[((z + z0) * BigW) + x + x0];
            }
        }

        var palette = rng.Next(PaletteCount);
        var start = new Rect(modules[0].Ox - x0, modules[0].Oz - z0, ModuleSize, ModuleSize);
        Level Build(Vec2 hole, IReadOnlyList<Vec2> spawns) => Level.Create(
            seed, round, w, h, trimmed, heights,
            [.. modules.SelectMany(m => m.Pillars).Select(p => p with { X = p.X - x0, Z = p.Z - z0 })],
            [.. modules.SelectMany(m => m.Bumpers).Select(b => b with { X = b.X - x0, Z = b.Z - z0 })],
            [.. modules.SelectMany(m => m.Windmills).Select(v => v with { X = v.X - x0, Z = v.Z - z0 })],
            start, spawns, hole, palette, clearance);

        var draft = Build(Vec2.Zero, []);
        var last = modules[^1];
        Vec2[] holes =
        [
            .. from i in Enumerable.Range(1, HoleSamples)
            from j in Enumerable.Range(1, HoleSamples)
            let p = new Vec2(last.Ox - x0 + (i * HoleSampleStep), last.Oz - z0 + (j * HoleSampleStep))
            where draft.SurfaceAt(p) == Surface.Grass && LevelGenValidator.HasClearance(draft, p, HoleClearance)
            select p
        ];
        if (holes.Length == 0) return null;

        var level = Build(holes[rng.Next(holes.Length)], Spawns(draft, start, options.SpawnSpacing, rng));
        foreach (var m in modules.Where(m => m.Pillars.Count + m.Bumpers.Count + m.Windmills.Count > 0))
        {
            if (LevelGenValidator.IsBeatable(level, clearance)) return level;
            m.Pillars.Clear();
            m.Bumpers.Clear();
            m.Windmills.Clear();
            level = Build(level.Hole, level.Spawns);
        }

        return LevelGenValidator.IsBeatable(level, clearance) ? level : null;
    }

    private static List<(int X, int Z)> Walk(Random rng, int length)
    {
        var edge = Enumerable.Range(0, GridW * GridH)
            .Select(i => (X: i % GridW, Z: i / GridW))
            .Where(c => c.X == 0 || c.Z == 0 || c.X == GridW - 1 || c.Z == GridH - 1)
            .ToArray();
        List<(int X, int Z)> path = [edge[rng.Next(edge.Length)]];
        // Exhaustive backtracking always finds a self-avoiding path of length <= MaxPathLength on a 7x5 grid.
        Extend(rng, path, -1, length);
        return path;
    }

    private static bool Extend(Random rng, List<(int X, int Z)> path, int dir, int length)
    {
        if (path.Count == length) return true;
        var (x, z) = path[^1];
        foreach (var d in Directions(rng, dir))
        {
            var next = (X: x + Dx[d], Z: z + Dz[d]);
            if (next.X < 0 || next.Z < 0 || next.X >= GridW || next.Z >= GridH || path.Contains(next)) continue;
            path.Add(next);
            if (Extend(rng, path, d, length)) return true;
            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    private static int[] Directions(Random rng, int dir)
    {
        if (dir < 0)
        {
            int[] all = [.. Enumerable.Range(0, Sides)];
            rng.Shuffle(all);
            return all;
        }

        int[] turns = rng.Next(2) == 0 ? [Rotate(dir, 1), Rotate(dir, 3)] : [Rotate(dir, 3), Rotate(dir, 1)];
        return rng.NextDouble() < KeepDirectionChance ? [dir, .. turns] : [.. turns, dir];
    }

    private static int SideTo((int X, int Z) from, (int X, int Z) to) =>
        Enumerable.Range(0, Sides).First(d => Dx[d] == to.X - from.X && Dz[d] == to.Z - from.Z);

    private static int Rotate(int side, int quarterTurns) => (side + quarterTurns) % Sides;

    private static int Opposite(int side) => Rotate(side, 2);

    private static bool Fits(Piece kind, bool straight) =>
        straight || kind is not (Piece.Chicane or Piece.Bridge or Piece.Ramp or Piece.Windmill);

    private static Piece[] PickKinds(Random rng, int round, bool[] straight)
    {
        var count = straight.Length;
        var kinds = new Piece[count];
        kinds[0] = Piece.Start;
        kinds[^1] = rng.Next(2) == 0 ? Piece.Hole : Piece.HoleEnd;
        var bunkers = Math.Max(1, count / 3);
        for (var i = 1; i < count - 1; i++)
        {
            (Piece Kind, int Weight)[] table =
            [
                (Piece.Open, Math.Max(1, 5 - round)), (Piece.Corridor, 3), (Piece.Plaza, 2 + (round / 2)), (Piece.Bunker, bunkers > 0 ? 2 : 0),
                (Piece.Bumpers, 1 + (round / 2)), (Piece.Chicane, 1 + (round / 2)), (Piece.Bridge, round - 1), (Piece.Ramp, 2 + (round / 2)),
                (Piece.Windmill, 1 + round)
            ];
            var allowed = table.Where(t => t.Kind != kinds[i - 1] && Fits(t.Kind, straight[i])).ToArray();
            var roll = rng.Next(allowed.Sum(t => t.Weight));
            var k = 0;
            while (roll >= allowed[k].Weight) roll -= allowed[k++].Weight;
            kinds[i] = allowed[k].Kind;
            if (kinds[i] == Piece.Bunker) bunkers--;
        }

        if (!kinds.Any(Features.Contains))
        {
            var j = 1 + rng.Next(count - 2);
            var options = Features.Where(k => Fits(k, straight[j]) && k != kinds[j - 1] && k != kinds[j + 1]).ToArray();
            kinds[j] = options[rng.Next(options.Length)];
        }

        return kinds;
    }

    private static int WidthOf(Piece kind, Random rng) => kind switch
    {
        Piece.Chicane or Piece.HoleEnd => WideBand,
        Piece.Windmill => WindmillBand,
        // Narrow openings keep the bunker clear of them.
        Piece.Bridge or Piece.Bunker => NarrowBand,
        Piece.Ramp => rng.Next(MinRampWidth, MaxOpening + 1),
        _ => rng.Next(MinOpening, MaxOpening + 1)
    };

    private static int Lo(int width) => (ModuleSize - width) / 2;

    // Maps (distance from the given side's border, coordinate across it) to module-local cell.
    private static (int X, int Z) Local(int side, int a, int c) => side switch
    {
        0 => (ModuleSize - 1 - a, c),
        1 => (c, ModuleSize - 1 - a),
        2 => (a, c),
        _ => (c, a)
    };

    private static int Rasterize(Module m, Surface[] cells, int[] tiers, int tier, Random rng)
    {
        void Set(int x, int z, Surface s, int t)
        {
            cells[((m.Oz + z) * BigW) + m.Ox + x] = s;
            tiers[((m.Oz + z) * BigW) + m.Ox + x] = t;
        }

        var lo = Lo(m.Width);
        void Strip(int side)
        {
            for (var a = 0; a < lo + m.Width; a++)
            {
                for (var c = lo; c < lo + m.Width; c++)
                {
                    var (x, z) = Local(side, a, c);
                    Set(x, z, Surface.Grass, tier);
                }
            }
        }

        if (m.IsBand)
        {
            Strip(m.Entry);
            if (m.Kind != Piece.HoleEnd) Strip(m.Exit);
        }
        else
        {
            for (var z = 0; z < ModuleSize; z++)
            {
                for (var x = 0; x < ModuleSize; x++)
                    Set(x, z, Surface.Grass, tier);
            }
        }

        switch (m.Kind)
        {
            case Piece.Ramp:
                // Slope cells keep the upper tier; two grass rows after them let the ball settle.
                for (var a = RampSlopeFrom; a < ModuleSize; a++)
                {
                    for (var c = lo; c < lo + m.Width; c++)
                    {
                        var (x, z) = Local(m.Entry, a, c);
                        Set(x, z, a < RampSlopeTo ? SlopeOf[m.Exit] : Surface.Grass, a < RampSlopeTo ? tier : tier + 1);
                    }
                }

                return tier + 1;
            case Piece.Chicane:
                var near = Rotate(m.Across, 2 * rng.Next(2));
                var (first, second) = (ChicaneStubs[0], ChicaneStubs[1]);
                m.Pillars.Add(m.Box(m.Entry, first.A1, first.A2, near, first.B1, first.B2));
                m.Pillars.Add(m.Box(m.Entry, second.A1, second.A2, Opposite(near), second.B1, second.B2));
                break;
            case Piece.Plaza:
                var pattern = PlazaPatterns[rng.Next(m.Straight ? PlazaPatterns.Length : PlazaPatterns.Length - 1)];
                m.Pillars.AddRange(pattern.Select(p => m.Box(m.Entry, p.A1, p.A2, m.Across, p.B1, p.B2)));
                break;
            case Piece.Bumpers:
                var spots = BumperPatterns[rng.Next(BumperPatterns.Length)];
                m.Bumpers.AddRange(spots.Select(s => m.At(m.Entry, s.A, m.Across, s.B)).Select(p => new Bumper(p.X, p.Z, BumperRadius)));
                break;
            case Piece.Bunker:
                var shape = m.Straight ? StraightBunker : BendBunker;
                var lateral = Rotate(m.Across, 2 * rng.Next(2));
                var (sa, sb) = m.Straight ? (m.Entry, lateral) : (Opposite(m.Entry), Opposite(m.Exit));
                foreach (var p in shape.Select(c => m.At(sa, c.A + CellCenter, sb, c.B + CellCenter)))
                    Set((int)p.X - m.Ox, (int)p.Z - m.Oz, Surface.Sand, tier);
                break;
            case Piece.Windmill:
                var (wx, wz) = m.Entry % 2 == 0 ? (ModuleSize / 2.0, lo + (m.Width / 2.0)) : (lo + (m.Width / 2.0), ModuleSize / 2.0);
                m.Windmills.Add(new Windmill(m.Ox + wx, m.Oz + wz, m.Entry % 2, Math.Round(rng.NextDouble() * 2 * Math.PI, PhaseDecimals)));
                break;
        }

        return tier;
    }

    private static void Carve(Module[] modules, List<(int X, int Z)> path, Surface[] cells)
    {
        for (var i = 0; i < modules.Length; i++)
        {
            var m = modules[i];
            for (var side = 0; side < Sides; side++)
            {
                var width = OpeningWidth(modules, path, i, side);
                if (width < 0) continue;
                for (var c = 0; c < ModuleSize; c++)
                {
                    if (width > 0 && c >= Lo(width) && c < Lo(width) + width) continue;
                    var (x, z) = Local(side, 0, c);
                    cells[((m.Oz + z) * BigW) + m.Ox + x] = Surface.Void;
                }
            }
        }
    }

    // -1 = leave border as is, 0 = close it fully, n = keep a centered opening of n cells.
    private static int OpeningWidth(Module[] modules, List<(int X, int Z)> path, int i, int side)
    {
        var m = modules[i];
        if (side == m.Entry) return Math.Min(m.Width, modules[i - 1].Width);
        if (side == m.Exit) return Math.Min(m.Width, modules[i + 1].Width);
        // Unrelated touching modules get a void gap so the path can't be short-circuited.
        var neighbor = path.IndexOf((m.X + Dx[side], m.Z + Dz[side]));
        return neighbor >= 0 && neighbor < i ? 0 : -1;
    }

    private static (int X0, int Z0, int W, int H) Bounds(Surface[] cells)
    {
        int minX = BigW, minZ = BigH, maxX = 0, maxZ = 0;
        for (var z = 0; z < BigH; z++)
        {
            for (var x = 0; x < BigW; x++)
            {
                if (cells[(z * BigW) + x] == Surface.Void) continue;
                (minX, minZ, maxX, maxZ) = (Math.Min(minX, x), Math.Min(minZ, z), Math.Max(maxX, x), Math.Max(maxZ, z));
            }
        }

        return (minX - Margin, minZ - Margin, maxX - minX + 1 + (2 * Margin), maxZ - minZ + 1 + (2 * Margin));
    }

    private static Vec2[] Spawns(Level draft, Rect start, double spacing, Random rng)
    {
        var n = (int)Math.Floor(((ModuleSize - (2 * SpawnWallClearance)) / spacing) + FloorSlack) + 1;
        var offset = (ModuleSize - ((n - 1) * spacing)) / 2;
        Vec2[] spawns =
        [
            .. from i in Enumerable.Range(0, n)
            from j in Enumerable.Range(0, n)
            let p = new Vec2(start.X + offset + (i * spacing), start.Z + offset + (j * spacing))
            where draft.SurfaceAt(p) == Surface.Grass && LevelGenValidator.HasClearance(draft, p, SpawnWallClearance)
            select p
        ];
        rng.Shuffle(spawns);
        return spawns;
    }
}