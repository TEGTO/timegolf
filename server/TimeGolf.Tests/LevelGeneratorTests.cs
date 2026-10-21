using System.Text;
using TimeGolf.Game;
using TimeGolf.Game.Geometry;
using TimeGolf.Game.Levels;

namespace TimeGolf.Tests;

public sealed class LevelGeneratorTests
{
    private static readonly GameOptions Options = new();

    private static IEnumerable<Level> AllLevels() =>
        from round in Enumerable.Range(1, 5)
        from seed in Enumerable.Range(0, 1000)
        select LevelGenerator.Generate(seed, round, Options);

    [Test]
    public void Generate_SameSeed_ProducesIdenticalLevel()
    {
        // Act
        var a = LevelGenerator.Generate(1234, 3, Options);
        var b = LevelGenerator.Generate(1234, 3, Options);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(b.Cells, Is.EqualTo(a.Cells));
            Assert.That(b.Heights, Is.EqualTo(a.Heights));
            Assert.That(b.Pillars, Is.EqualTo(a.Pillars));
            Assert.That(b.Bumpers, Is.EqualTo(a.Bumpers));
            Assert.That(b.Windmills, Is.EqualTo(a.Windmills));
            Assert.That(b.Spawns, Is.EqualTo(a.Spawns));
            Assert.That(b.Hole, Is.EqualTo(a.Hole));
            Assert.That(b.Palette, Is.EqualTo(a.Palette));
        }
    }

    [Test]
    public void Generate_ThousandSeedsPerRound_AllBeatableAndWellFormed()
    {
        // Arrange
        var minGap = (2 * Options.BallRadius) + Options.SpawnMargin;
        List<string> failures = [];

        // Act
        foreach (var level in AllLevels())
        {
            var spawnsOk = level.Spawns.Count >= 16
                && level.Spawns.All(p => level.Start.Contains(p) && level.SurfaceAt(p) == Surface.Grass)
                && level.Spawns.All(p => level.Spawns.All(q => p == q || (p - q).Length >= minGap));
            var holeOk = level.SurfaceAt(level.Hole) == Surface.Grass && LevelGenValidator.HasClearance(level, level.Hole, 1.5);
            var boundsOk = level.Width is > 2 and <= 44 && level.Height is > 2 and <= 32
                && level.Cells.Length == level.Width * level.Height && level.Heights.Length == level.Cells.Length
                && Enumerable.Range(0, level.Width).All(x => level.CellAt(x, 0) == Surface.Void && level.CellAt(x, level.Height - 1) == Surface.Void)
                && Enumerable.Range(0, level.Height).All(z => level.CellAt(0, z) == Surface.Void && level.CellAt(level.Width - 1, z) == Surface.Void)
                && level.Palette is >= 0 and <= 4;
            var beatable = LevelGenValidator.IsBeatable(level, Options.BallRadius + 0.02);
            if (!(spawnsOk && holeOk && boundsOk && beatable))
                failures.Add($"seed {level.Seed} round {level.Round}: spawns {spawnsOk} hole {holeOk} bounds {boundsOk} beatable {beatable}");
        }

        // Assert
        Assert.That(failures, Is.Empty);
    }

    [Test]
    public void Generate_SandOnlyAsWholeBunkers()
    {
        // Arrange
        List<string> failures = [];

        // Act
        foreach (var level in AllLevels())
        {
            var bunkers = SandComponents(level);
            if (bunkers.Count > 3 || bunkers.Exists(b => b.Count < 5 || b.Any(c => Neighbours(c).All(n => !b.Contains(n)))))
                failures.Add($"seed {level.Seed} round {level.Round}: bunkers {string.Join(",", bunkers.Select(b => b.Count))}");
        }

        // Assert
        Assert.That(failures, Is.Empty);
    }

    [Test]
    public void Generate_WindmillsSitInsideFloorCorridors()
    {
        // Arrange
        List<string> failures = [];

        // Act
        foreach (var level in AllLevels().Where(l => l.Windmills.Count > 0))
        {
            var onFloor = level.Windmills.All(w => level.IsFloor(new Vec2(w.X, w.Z))
                && w.Blocks().All(b => level.IsFloor(new Vec2(b.X + (b.W / 2), b.Z + (b.D / 2)))));
            var clearOfHole = level.Windmills.All(w => (new Vec2(w.X, w.Z) - level.Hole).Length > 3);
            if (!(onFloor && clearOfHole))
                failures.Add($"seed {level.Seed} round {level.Round}: floor {onFloor} hole {clearOfHole}");
        }

        // Assert
        Assert.That(failures, Is.Empty);
    }

    [Test]
    public void Generate_WindmillShareRisesWithRound()
    {
        // Act
        var share = new[] { 1, 5 }.Select(round => Enumerable.Range(0, 1000).Count(seed => LevelGenerator.Generate(seed, round, Options).Windmills.Count > 0) / 1000.0).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(share[0], Is.InRange(0.15, 0.5));
            Assert.That(share[1], Is.GreaterThan(share[0]));
        }
    }

    [Test]
    public void DistanceField_FiniteAtSpawnsZeroAtHoleAndAtLeastStraightLine()
    {
        // Arrange
        var levels = from round in Enumerable.Range(1, 5) from seed in Enumerable.Range(0, 100) select LevelGenerator.Generate(seed, round, Options);
        List<string> failures = [];

        // Act
        foreach (var level in levels)
        {
            var spawnsOk = level.Spawns.All(p => level.DistanceTo(p) is var d && double.IsFinite(d) && d >= (p - level.Hole).Length - 1e-9);
            var holeOk = level.DistanceTo(level.Hole) < 1e-9;
            if (!(spawnsOk && holeOk))
                failures.Add($"seed {level.Seed} round {level.Round}: spawns {spawnsOk} hole {holeOk}");
        }

        // Assert
        Assert.That(failures, Is.Empty);
    }

    [Test]
    public void DistanceTo_StraightCorridor_IsCloseToStraightLine()
    {
        // Arrange
        var level = TestLevels.Corridor();

        // Act
        var distance = level.DistanceTo(new Vec2(4, 10));

        // Assert
        Assert.That(distance, Is.EqualTo(7).Within(0.3));
    }

    [Test]
    public void DistanceTo_AroundObstacle_IsLongerThanStraightLine()
    {
        // Arrange
        var c = TestLevels.Corridor();
        var level = Level.Create(c.Seed, c.Round, c.Width, c.Height, c.Cells, c.Heights, [new Pillar(1, 8, 5, 1)], [], [], c.Start, c.Spawns, c.Hole, c.Palette);

        // Act
        var distance = level.DistanceTo(new Vec2(2, 12));

        // Assert
        Assert.That(distance, Is.GreaterThan((new Vec2(2, 12) - level.Hole).Length + 2));
    }

    [Test]
    public void Generate_PrintsSamples()
    {
        // Act
        var levels = new[] { (8, 1), (4, 3), (5, 5) }.Select(s => LevelGenerator.Generate(s.Item1, s.Item2, Options)).ToList();

        // Assert
        foreach (var level in levels) TestContext.Out.WriteLine(Render(level));
        Assert.That(levels, Has.Count.EqualTo(3));
    }

    private static string Render(Level level)
    {
        var sb = new StringBuilder($"seed {level.Seed} round {level.Round} {level.Width}x{level.Height} palette {level.Palette}\n");
        for (var z = 0; z < level.Height; z++)
        {
            for (var x = 0; x < level.Width; x++)
            {
                var center = new Vec2(x + 0.5, z + 0.5);
                sb.Append(CellChar(level, x, z, center));
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static char CellChar(Level level, int x, int z, Vec2 center)
    {
        if ((int)Math.Floor(level.Hole.X) == x && (int)Math.Floor(level.Hole.Z) == z) return 'O';
        if (level.Windmills.Any(w => w.Blocks().Any(b => Inside(b, center)) || (Math.Abs(w.X - center.X) < 0.7 && Math.Abs(w.Z - center.Z) < 0.7))) return 'W';
        if (level.Pillars.Any(p => Inside(p, center))) return '#';
        if (level.Bumpers.Any(b => (new Vec2(b.X, b.Z) - center).Length < 0.5)) return '*';
        if (level.Start.Contains(center)) return level.CellAt(x, z) == Surface.Void ? '.' : 'S';
        var c = level.CellAt(x, z).ToChar();
        return c == 'g' ? (char)('0' + level.Heights[(z * level.Width) + x]) : c;
    }

    private static bool Inside(Pillar p, Vec2 c) => c.X > p.X && c.X < p.X + p.W && c.Z > p.Z && c.Z < p.Z + p.D;

    private static IEnumerable<(int X, int Z)> Neighbours((int X, int Z) c) => [(c.X + 1, c.Z), (c.X - 1, c.Z), (c.X, c.Z + 1), (c.X, c.Z - 1)];

    private static List<HashSet<(int X, int Z)>> SandComponents(Level level)
    {
        bool IsSand((int X, int Z) c) => level.CellAt(c.X, c.Z) == Surface.Sand;
        List<HashSet<(int X, int Z)>> components = [];
        var cells = from z in Enumerable.Range(0, level.Height) from x in Enumerable.Range(0, level.Width) select (X: x, Z: z);
        foreach (var cell in cells.Where(c => IsSand(c) && !components.Exists(b => b.Contains(c))))
        {
            HashSet<(int X, int Z)> component = [cell];
            var queue = new Queue<(int X, int Z)>([cell]);
            while (queue.TryDequeue(out var c))
            {
                foreach (var n in Neighbours(c).Where(n => IsSand(n) && component.Add(n))) queue.Enqueue(n);
            }

            components.Add(component);
        }

        return components;
    }
}