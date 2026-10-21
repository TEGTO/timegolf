using TimeGolf.Game;
using TimeGolf.Game.Geometry;
using TimeGolf.Game.Simulation;

namespace TimeGolf.Tests;

public sealed class TimelineTests
{
    private const double HolePower = 0.345;
    private static readonly GameOptions Options = new();
    private static readonly Vec2 Up = new(0, -1);
    private static readonly Vec2 Down = new(0, 1);

    [Test]
    public void PresentHit_AppendsRecordAndMovesOnlyHitBall()
    {
        // Arrange
        var timeline = Create(new Vec2(2, 16), new Vec2(4, 10));

        // Act
        var result = timeline.Append(new HitRecord("a", new Vec2(1, 0), 0.2));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(timeline.Records, Has.Count.EqualTo(1));
            Assert.That(result.Tracks.Select(t => t.PlayerId), Is.EqualTo(new[] { "a" }));
            Assert.That(result.End[0].Position, Is.Not.EqualTo(result.Start[0].Position));
            Assert.That(result.End[1], Is.EqualTo(result.Start[1]));
            Assert.That(result.DurationMs, Is.GreaterThan(0));
            Assert.That(timeline.Present, Is.EqualTo(result.End));
        }
    }

    [Test]
    public void Replace_ResimulatesLaterRecordsWithSameDirAndPower()
    {
        // Arrange
        var timeline = Create(new Vec2(4, 16), new Vec2(4, 10));
        timeline.Append(new HitRecord("a", Up, 0.2));
        var b1 = new HitRecord("b", Down, 0.4);
        var before = timeline.Append(b1);

        // Act
        var replayed = timeline.Replace(0, new Vec2(1, 0), 0.2);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(replayed, Has.Count.EqualTo(2));
            Assert.That(replayed[1].Record, Is.EqualTo(b1));
            Assert.That(replayed[1].Start[0].Position.X, Is.GreaterThan(6));
            Assert.That(replayed[1].End[1].Position, Is.Not.EqualTo(before.End[1].Position));
            Assert.That(before.Bounces, Has.Some.Matches<TimedBounce>(b => b.Bounce.Kind == BounceKind.Ball));
            Assert.That(replayed[1].Bounces, Has.None.Matches<TimedBounce>(b => b.Bounce.Kind == BounceKind.Ball));
            Assert.That(replayed[1].Bounces, Has.All.Matches<TimedBounce>(b => b.TMs > 0 && b.TMs <= replayed[1].DurationMs));
        }
    }

    [Test]
    public void Replace_CanUnholeAnotherPlayer()
    {
        // Arrange
        var timeline = Create(new Vec2(2, 16), new Vec2(4, 10));
        timeline.Append(new HitRecord("a", Down, 0.1));
        var holedBefore = timeline.Append(new HitRecord("b", Up, HolePower)).End[1].Holed;

        // Act
        var replayed = timeline.Replace(0, new Vec2(2, -9).Normalized, 0.388);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(holedBefore, Is.True);
            Assert.That(replayed[1].End[1].Holed, Is.False);
        }
    }

    [Test]
    public void RecordOfHoledBall_ReplaysVoid()
    {
        // Arrange
        var timeline = Create(new Vec2(2, 16), new Vec2(4, 10));
        timeline.Append(new HitRecord("b", Up, 0.1));
        var holedBySecond = timeline.Append(new HitRecord("b", Up, 0.331)).End[1].Holed;

        // Act
        var replayed = timeline.Replace(0, Up, HolePower);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(holedBySecond, Is.True);
            Assert.That(replayed[0].End[1].Holed, Is.True);
            Assert.That(replayed[1].Void, Is.True);
            Assert.That(replayed[1].DurationMs, Is.Zero);
            Assert.That(replayed[1].Tracks, Is.Empty);
            Assert.That(timeline.HitsOf("b"), Is.EqualTo(1));
        }
    }

    [Test]
    public void SameRecords_ProduceIdenticalResults()
    {
        // Arrange
        HitRecord[] records = [new("a", Up, 0.3), new("b", new Vec2(-1, 0.4).Normalized, 0.7), new("a", new Vec2(0.3, 1).Normalized, 0.9)];
        var first = Create(new Vec2(4, 16), new Vec2(4, 10));
        var second = Create(new Vec2(4, 16), new Vec2(4, 10));

        // Act
        foreach (var record in records)
        {
            first.Append(record);
            second.Append(record);
        }

        second.Simulate(0);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.Present, Is.EqualTo(first.Present));
            Assert.That(second.Results.SelectMany(r => r.Tracks.SelectMany(t => t.Points)), Is.EqualTo(first.Results.SelectMany(r => r.Tracks.SelectMany(t => t.Points))));
            Assert.That(second.Results.Select(r => r.DurationMs), Is.EqualTo(first.Results.Select(r => r.DurationMs)));
        }
    }

    [Test]
    public void Track_StartsAtCheckpointSamplesAtTrackHzAndEndsAtRest()
    {
        // Arrange
        var timeline = Create(new Vec2(2, 16), new Vec2(4, 10));

        // Act
        var result = timeline.Append(new HitRecord("a", Up, 0.3));

        // Assert
        var points = result.Tracks[0].Points;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(points[0], Is.EqualTo(result.Start[0].Position));
            Assert.That(points[^1], Is.EqualTo(result.End[0].Position));
            Assert.That(points, Has.Count.EqualTo((int)Math.Ceiling((result.DurationMs / 50) - 1e-9) + 1));
        }
    }

    [Test]
    public void WorldClock_StartsAtZeroAndAccumulatesEarlierDurations()
    {
        // Arrange
        var timeline = Create(new Vec2(2, 16), new Vec2(4, 10));

        // Act
        timeline.Append(new HitRecord("a", new Vec2(1, 0), 0.2));
        timeline.Append(new HitRecord("b", Down, 0.3));
        timeline.Append(new HitRecord("a", Up, 0.2));

        // Assert
        var r = timeline.Results;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(r[0].WorldTimeMs, Is.Zero);
            Assert.That(r[1].WorldTimeMs, Is.EqualTo(r[0].DurationMs));
            Assert.That(r[2].WorldTimeMs, Is.EqualTo(r[0].DurationMs + r[1].DurationMs));
        }
    }

    [Test]
    public void WorldClock_VoidRecordAddsNoTime()
    {
        // Arrange
        var timeline = Create(new Vec2(2, 16), new Vec2(4, 10));
        timeline.Append(new HitRecord("b", Up, HolePower));

        // Act
        var voidHit = timeline.Append(new HitRecord("b", Up, 0.5));
        var next = timeline.Append(new HitRecord("a", Up, 0.2));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(voidHit.Void, Is.True);
            Assert.That(next.WorldTimeMs, Is.EqualTo(voidHit.WorldTimeMs));
        }
    }

    [Test]
    public void Replace_ShiftsWorldTimeOfLaterRecords()
    {
        // Arrange
        var timeline = Create(new Vec2(2, 16), new Vec2(4, 10));
        timeline.Append(new HitRecord("a", new Vec2(1, 0), 0.2));
        var before = timeline.Append(new HitRecord("b", Down, 0.3)).WorldTimeMs;

        // Act
        var replayed = timeline.Replace(0, new Vec2(1, 0), 0.5);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(replayed[1].WorldTimeMs, Is.EqualTo(replayed[0].DurationMs));
            Assert.That(replayed[1].WorldTimeMs, Is.Not.EqualTo(before));
        }
    }

    [Test]
    public void WindmillLevel_SameRecords_ProduceIdenticalResults()
    {
        // Arrange
        HitRecord[] records = [new("a", new Vec2(1, 0), 0.6), new("b", new Vec2(1, 0.05).Normalized, 0.7), new("a", new Vec2(-1, 0), 0.5)];
        var first = CreateWindmill();
        var second = CreateWindmill();

        // Act
        foreach (var record in records)
        {
            first.Append(record);
            second.Append(record);
        }

        second.Simulate(0);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.Present, Is.EqualTo(first.Present));
            Assert.That(second.Results.SelectMany(r => r.Bounces), Is.EqualTo(first.Results.SelectMany(r => r.Bounces)));
            Assert.That(second.Results.Select(r => r.WorldTimeMs), Is.EqualTo(first.Results.Select(r => r.WorldTimeMs)));
        }
    }

    [Test]
    public void WindmillLevel_SameShotAtDifferentWorldTime_CanEndDifferently()
    {
        // Arrange
        var shot = new HitRecord("a", new Vec2(1, 0), 0.6);
        var early = CreateWindmill();
        var late = CreateWindmill();
        late.Append(new HitRecord("b", new Vec2(0, 1), 0.2));

        // Act
        var a = early.Append(shot);
        var b = late.Append(shot);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(b.WorldTimeMs, Is.GreaterThan(0));
            Assert.That(b.End[0].Position, Is.Not.EqualTo(a.End[0].Position));
        }
    }

    private static Timeline CreateWindmill() =>
        new(PhysicsTests.WindmillCorridor(0), Options, [new BallState("a", new Vec2(6, 2.5), false), new BallState("b", new Vec2(3, 2.5), false)]);

    private static Timeline Create(Vec2 a, Vec2 b) =>
        new(TestLevels.Corridor(), Options, [new BallState("a", a, false), new BallState("b", b, false)]);
}