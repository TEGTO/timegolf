using TimeGolf.Game.Geometry;

namespace TimeGolf.Game.Levels;

public sealed class Level
{
    // Default ballRadius + LevelGenValidator.ClearanceMargin; the generator passes the configured value.
    public const double DefaultPathRadius = 0.16;

    private double[]? distanceField;

    public required int Seed { get; init; }
    public required int Round { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required Surface[] Cells { get; init; }
    public required int[] Heights { get; init; }
    public required IReadOnlyList<Pillar> Pillars { get; init; }
    public required IReadOnlyList<Bumper> Bumpers { get; init; }
    public required IReadOnlyList<Windmill> Windmills { get; init; }
    public required Rect Start { get; init; }
    public required IReadOnlyList<Vec2> Spawns { get; init; }
    public required Vec2 Hole { get; init; }
    public required int Palette { get; init; }

    // Cell boundary walls only; physics also needs PillarWalls (pillar + windmill base edges) and GateWalls.
    public IReadOnlyList<Wall> Walls { get; private init; } = [];
    public IReadOnlyList<Wall> PillarWalls { get; private init; } = [];
    public IReadOnlyList<Wall[]> GateWalls { get; private init; } = [];
    public double PathRadius { get; private init; } = DefaultPathRadius;

    public IEnumerable<Pillar> Solids => Pillars.Concat(Windmills.SelectMany(w => w.Blocks()));

    public static Level Create(
        int seed,
        int round,
        int width,
        int height,
        Surface[] cells,
        int[] heights,
        IReadOnlyList<Pillar> pillars,
        IReadOnlyList<Bumper> bumpers,
        IReadOnlyList<Windmill> windmills,
        Rect start,
        IReadOnlyList<Vec2> spawns,
        Vec2 hole,
        int palette,
        double pathRadius = DefaultPathRadius) =>
        new()
        {
            Seed = seed,
            Round = round,
            Width = width,
            Height = height,
            Cells = cells,
            Heights = heights,
            Pillars = pillars,
            Bumpers = bumpers,
            Windmills = windmills,
            Start = start,
            Spawns = spawns,
            Hole = hole,
            Palette = palette,
            PathRadius = pathRadius,
            Walls = LevelWalls.Build(width, height, cells, heights),
            PillarWalls = [.. pillars.Concat(windmills.SelectMany(w => w.Blocks())).SelectMany(p => p.Edges())],
            GateWalls = [.. windmills.Select(w => w.Gates())]
        };

    public double DistanceTo(Vec2 p) =>
        LevelGenValidator.Distance(this, distanceField ??= LevelGenValidator.DistanceField(this, PathRadius), p);

    public Surface CellAt(int cx, int cz) =>
        cx < 0 || cz < 0 || cx >= Width || cz >= Height ? Surface.Void : Cells[(cz * Width) + cx];

    public Surface SurfaceAt(Vec2 p) => CellAt((int)Math.Floor(p.X), (int)Math.Floor(p.Z));

    public bool IsFloor(Vec2 p) => SurfaceAt(p) != Surface.Void;
}