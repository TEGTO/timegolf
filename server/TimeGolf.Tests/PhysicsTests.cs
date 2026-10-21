using TimeGolf.Game;
using TimeGolf.Game.Geometry;
using TimeGolf.Game.Levels;
using TimeGolf.Game.Simulation;

namespace TimeGolf.Tests;

public sealed class PhysicsTests
{
    private static readonly GameOptions Options = new();
    private static readonly double Dt = 1.0 / (Options.TickRate * Options.Substeps);

    private static IEnumerable<TestCaseData> WallDirections()
    {
        yield return new TestCaseData(new Vec2(-1, 0)).SetArgDisplayNames("left");
        yield return new TestCaseData(new Vec2(1, 0)).SetArgDisplayNames("right");
        yield return new TestCaseData(new Vec2(0, -1)).SetArgDisplayNames("up");
        yield return new TestCaseData(new Vec2(0, 1)).SetArgDisplayNames("down");
        yield return new TestCaseData(new Vec2(-1, 1)).SetArgDisplayNames("diagonal");
    }

    [TestCaseSource(nameof(WallDirections))]
    public void BallAtMaxSpeed_NeverTunnelsThroughWalls(Vec2 dir)
    {
        // Arrange
        var level = TestLevels.Corridor();
        var ball = Moving(new Vec2(4, 10), dir.Normalized * Options.MaxHitSpeed);
        var physics = new Physics(Options);
        var r = Options.BallRadius - 1e-9;
        var escaped = false;

        // Act
        for (var i = 0; i < 600; i++)
        {
            physics.Step(level, [ball], Dt, 0, [], []);
            var p = ball.Position;
            escaped |= p.X < 1 + r || p.X > 7 - r || p.Z < 1 + r || p.Z > 19 - r;
        }

        // Assert
        Assert.That(escaped, Is.False);
    }

    [Test]
    public void Ball_EventuallyRestsOnFlat()
    {
        // Arrange
        var level = TestLevels.Corridor();
        var ball = Moving(new Vec2(4, 10), new Vec2(3, 2));
        var physics = new Physics(Options);
        List<Ball> stopped = [];

        // Act
        for (var i = 0; i < 1200 && ball.Moving; i++)
        {
            physics.Step(level, [ball], Dt, 0, stopped, []);
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ball.Moving, Is.False);
            Assert.That(ball.Velocity, Is.EqualTo(Vec2.Zero));
            Assert.That(stopped, Is.EqualTo(new[] { ball }));
        }
    }

    [Test]
    public void Slope_KeepsBallMoving()
    {
        // Arrange
        var level = Strip(Surface.SlopeEast);
        var ball = Moving(new Vec2(2, 1.5), Vec2.Zero);
        var physics = new Physics(Options);

        // Act
        for (var i = 0; i < 240; i++)
        {
            physics.Step(level, [ball], Dt, 0, [], []);
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ball.Moving, Is.True);
            Assert.That(ball.Velocity.X, Is.GreaterThan(0));
            Assert.That(ball.Position.X, Is.GreaterThan(2));
        }
    }

    [Test]
    public void BallCollision_ConservesMomentumAndDoesNotGainEnergy()
    {
        // Arrange
        var options = Options with { RollingDecel = 0 };
        var level = TestLevels.Corridor();
        var a = Moving(new Vec2(3, 10), new Vec2(5, 0));
        var b = new Ball("b", new Vec2(3.27, 10.05));
        var physics = new Physics(options);
        var energyBefore = a.Velocity.LengthSquared;

        // Act
        physics.Step(level, [a, b], Dt, 0, [], []);

        // Assert
        var momentum = a.Velocity + b.Velocity;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(b.Moving, Is.True);
            Assert.That(momentum.X, Is.EqualTo(5).Within(1e-9));
            Assert.That(momentum.Z, Is.EqualTo(0).Within(1e-9));
            Assert.That(a.Velocity.LengthSquared + b.Velocity.LengthSquared, Is.LessThanOrEqualTo(energyBefore));
        }
    }

    [Test]
    public void Sand_StopsBallSoonerThanGrass()
    {
        // Arrange
        var grass = Moving(new Vec2(2, 1.5), new Vec2(5, 0));
        var sand = Moving(new Vec2(2, 1.5), new Vec2(5, 0));
        var physics = new Physics(Options);

        // Act
        Settle(physics, Strip(Surface.Grass), grass);
        Settle(physics, Strip(Surface.Sand), sand);

        // Assert
        Assert.That(sand.Position.X, Is.LessThan(grass.Position.X));
    }

    [Test]
    public void SlowBall_IsCapturedByHole()
    {
        // Arrange
        var level = TestLevels.Corridor();
        var ball = Moving(level.Hole - new Vec2(0.3, 0), new Vec2(1, 0));
        var physics = new Physics(Options);
        List<Ball> holed = [];

        // Act
        for (var i = 0; i < 120 && !ball.Holed; i++)
        {
            physics.Step(level, [ball], Dt, 0, [], holed);
        }

        // Assert
        Assert.That(holed, Is.EqualTo(new[] { ball }));
    }

    [Test]
    public void FastBall_SkimsOverHole()
    {
        // Arrange
        var level = TestLevels.Corridor();
        var ball = Moving(level.Hole - new Vec2(0.3, 0), new Vec2(10, 0));
        var physics = new Physics(Options);

        // Act
        for (var i = 0; i < 10; i++)
        {
            physics.Step(level, [ball], Dt, 0, [], []);
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ball.Holed, Is.False);
            Assert.That(ball.Position.X, Is.GreaterThan(level.Hole.X + Options.HoleRadius));
        }
    }

    [TestCase(0.0, true)]
    [TestCase(0.3, true)]
    [TestCase(-0.3, true)]
    [TestCase(0.4, false)]
    [TestCase(Math.PI / 4, false)]
    [TestCase(Math.PI / 2, true)]
    [TestCase(Math.PI + 0.2, true)]
    public void Windmill_IsClosed_OnlyWhileABladePointsDown(double angle, bool closed)
    {
        // Arrange
        var windmill = new Windmill(0, 0, 0, angle);

        // Act
        var result = windmill.IsClosed(0, Options.WindmillOmega, Options.WindmillBladeHalfAngle);

        // Assert
        Assert.That(result, Is.EqualTo(closed));
    }

    [Test]
    public void WindmillGate_BladeDown_BouncesBallBack()
    {
        // Arrange
        var level = WindmillCorridor(0);
        var ball = Moving(new Vec2(9, 2.5), new Vec2(8, 0));

        // Act
        Run(level, ball, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ball.Position.X, Is.LessThan(9.4));
            Assert.That(ball.Velocity.X, Is.LessThanOrEqualTo(0));
        }
    }

    [Test]
    public void WindmillGate_BladesClear_LetsBallThroughTunnel()
    {
        // Arrange
        var level = WindmillCorridor(Math.PI / 4);
        var ball = Moving(new Vec2(9, 2.5), new Vec2(8, 0));

        // Act
        Run(level, ball, 1);

        // Assert
        Assert.That(ball.Position.X, Is.GreaterThan(10.6));
    }

    [Test]
    public void WindmillBase_BlocksBallBesideTunnel()
    {
        // Arrange
        var level = WindmillCorridor(Math.PI / 4);
        var ball = Moving(new Vec2(9, 1.5), new Vec2(8, 0));

        // Act
        Run(level, ball, 1);

        // Assert
        Assert.That(ball.Position.X, Is.LessThan(9.4));
    }

    internal static Level WindmillCorridor(double phase)
    {
        const int width = 20;
        const int height = 5;
        var cells = new Surface[width * height];
        for (var z = 1; z < height - 1; z++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                cells[(z * width) + x] = Surface.Grass;
            }
        }

        return Level.Create(0, 1, width, height, cells, new int[width * height], [], [], [new Windmill(10, 2.5, 0, phase)], new Rect(1, 1, 3, 3), [], new Vec2(17, 2.5), 0);
    }

    private static void Run(Level level, Ball ball, double seconds)
    {
        var physics = new Physics(Options);
        for (var i = 0; i < seconds / Dt; i++)
        {
            physics.Step(level, [ball], Dt, i * Dt, [], []);
        }
    }

    private static Ball Moving(Vec2 position, Vec2 velocity)
    {
        var ball = new Ball("a", position);
        ball.Launch(velocity);
        return ball;
    }

    private static void Settle(Physics physics, Level level, Ball ball)
    {
        for (var i = 0; i < 2400 && ball.Moving; i++)
        {
            physics.Step(level, [ball], Dt, 0, [], []);
        }
    }

    private static Level Strip(Surface surface)
    {
        const int width = 40;
        const int height = 3;
        var cells = new Surface[width * height];
        for (var x = 1; x < width - 1; x++)
        {
            cells[width + x] = surface;
        }

        return Level.Create(0, 1, width, height, cells, new int[width * height], [], [], [], new Rect(1, 1, 2, 1), [], new Vec2(38, 1.5), 0);
    }

    [Test]
    public void WallHit_RecordsBounce()
    {
        // Arrange
        var level = TestLevels.Corridor();
        var ball = Moving(new Vec2(4, 10), new Vec2(-5, 0));
        var physics = new Physics(Options);

        // Act
        for (var i = 0; i < 120; i++)
        {
            physics.Step(level, [ball], Dt, 0, [], []);
        }

        // Assert
        Assert.That(physics.Bounces, Has.Some.Matches<Bounce>(b => b.Kind == BounceKind.Wall && b.Speed > 1));
    }
}