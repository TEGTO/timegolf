using TimeGolf.Game;
using TimeGolf.Game.Geometry;

namespace TimeGolf.Tests;

public sealed class PlayerTests
{
    [Test]
    public void JoinDuringPlaying_SpectatesUntilNextRound()
    {
        // Arrange
        var h = new MatchHarness(new GameOptions { TurnsPerPlayer = 2 });
        h.Start("a");
        h.Hit("a", new Vec2(0, -1), 0.3);

        // Act
        var welcome = h.Join("b");
        var spectating = h.PlayerOf(welcome.PlayerId).Spectator;
        h.FinishRound();
        h.Advance((long)(h.Options.RoundResultsSeconds * 1000));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(welcome.Order, Does.Not.Contain(welcome.PlayerId));
            Assert.That(welcome.Balls.Select(x => x.PlayerId), Does.Not.Contain(welcome.PlayerId));
            Assert.That(spectating, Is.True);
            Assert.That(h.Match.Order.Select(p => p.Id), Does.Contain(welcome.PlayerId));
            Assert.That(h.PlayerOf(welcome.PlayerId).Spectator, Is.False);
        }
    }

    [Test]
    public void JoinBeforeFirstHit_EntersCurrentRound()
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a");

        // Act
        var welcome = h.Join("b");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(welcome.Order, Does.Contain(welcome.PlayerId));
            Assert.That(welcome.Balls.Select(x => x.PlayerId), Does.Contain(welcome.PlayerId));
            Assert.That(h.PlayerOf(welcome.PlayerId).Spectator, Is.False);
        }
    }

    [Test]
    public void JoinDuringRoundResults_EntersNextRound()
    {
        // Arrange
        var h = new MatchHarness(new GameOptions { TurnsPerPlayer = 1 });
        h.Start("a");
        h.FinishRound();

        // Act
        var welcome = h.Join("b");
        h.Advance((long)(h.Options.RoundResultsSeconds * 1000));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(welcome.Phase, Is.EqualTo("RoundResults"));
            Assert.That(welcome.Players.Single(p => p.Id == welcome.PlayerId).Spectator, Is.False);
            Assert.That(h.Match.Order.Select(p => p.Id), Does.Contain(welcome.PlayerId));
        }
    }

    [Test]
    public void Reconnect_WithSameKey_RestoresPlayerAndBall()
    {
        // Arrange
        var h = new MatchHarness();
        var b = h.JoinAllThenStartRoundTwo("a", "b")[1];
        h.Hit("b", new Vec2(0, -1), 0.3);
        h.WaitForTurn();
        var before = h.PlayerOf(b);
        var (color, points) = (before.Color, before.Points);
        var ball = h.Match.Timeline!.Present.Single(x => x.PlayerId == b);
        h.Disconnect("b");

        // Act
        var welcome = h.Join("b2", "b", "key-b");

        // Assert
        var player = h.PlayerOf(b);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(welcome.PlayerId, Is.EqualTo(b));
            Assert.That(player.Active, Is.True);
            Assert.That(player.Spectator, Is.False);
            Assert.That(player.Color, Is.EqualTo(color));
            Assert.That(player.Points, Is.EqualTo(points));
            Assert.That(player.Hits, Is.EqualTo(1));
            Assert.That(welcome.Order, Does.Contain(b));
            Assert.That(welcome.Balls.Single(x => x.PlayerId == b), Is.EqualTo(ball.ToDto()));
        }
    }

    [Test]
    public void DuplicateNameAmongActive_GetsSuffix()
    {
        // Arrange
        var h = new MatchHarness();
        h.Join("a", "Bob");

        // Act
        var second = h.Join("b", "Bob");
        var third = h.Join("c", "Bob");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.PlayerOf(second.PlayerId).Name, Is.EqualTo("Bob#2"));
            Assert.That(h.PlayerOf(third.PlayerId).Name, Is.EqualTo("Bob#3"));
        }
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("ThisNameIsWayTooLong")]
    public void Join_InvalidName_Throws(string name)
    {
        // Arrange
        var h = new MatchHarness();

        // Act
        void Join() => h.Join("a", name);

        // Assert
        Assert.That(Join, Throws.ArgumentException);
    }

    [Test]
    public void AllPlayersLeave_ReturnsToLobbyAndClearsWorld()
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a", "b");
        h.Hit("a", new Vec2(0, -1), 0.3);

        // Act
        h.Leave("a");
        h.Leave("b");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.Match.Phase, Is.EqualTo(Phase.Lobby));
            Assert.That(h.Match.Level, Is.Null);
            Assert.That(h.Match.Timeline, Is.Null);
            Assert.That(h.Match.Turn, Is.Null);
            Assert.That(h.Events<PhaseDto>(nameof(IGameClient.Phase))[^1], Is.EqualTo(new PhaseDto("Lobby", 0, 0, h.Options.RoundsPerMatch)));
        }
    }

    [Test]
    public void Welcome_IncludesTimelinePresentBallsAndCurrentTurn()
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a");
        h.Hit("a", new Vec2(0, -1), 0.3);
        h.WaitForTurn();

        // Act
        var welcome = h.Join("b");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(welcome.Timeline.Single().Tracks, Is.Not.Empty);
            Assert.That(welcome.Balls, Is.EqualTo(welcome.Timeline[0].End));
            Assert.That(welcome.Turn, Is.EqualTo(h.Match.Turn));
            Assert.That(welcome.Config.PlaybackGapMs, Is.EqualTo(400));
        }
    }

    [Test]
    public void BroadcastsBeforeJoin_AreNotDeliveredToJoiner()
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a");
        h.Hit("a", new Vec2(0, -1), 0.3);

        // Act
        h.Join("b");
        h.WaitForTurn();

        // Assert
        var sent = h.Sent();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent.Single(m => m.Method == nameof(IGameClient.TimelinePlayed)).Recipients, Is.EqualTo(new[] { "a" }));
            Assert.That(sent.Last(m => m.Method == nameof(IGameClient.TurnStarted)).Recipients, Is.EquivalentTo(new[] { "a", "b" }));
        }
    }
}