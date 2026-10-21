using TimeGolf.Game.Geometry;

namespace TimeGolf.Tests;

public sealed class AimRelayTests
{
    private const int RateLimit = 20;

    private static readonly Vec2 Up = new(0, -1);

    private static IEnumerable<TestCaseData> InvalidAims()
    {
        yield return new TestCaseData(null, double.NaN, 0.0, 0.5).SetArgDisplayNames("now", "nan x", "z 0", "power half");
        yield return new TestCaseData(null, 0.0, double.PositiveInfinity, 0.5).SetArgDisplayNames("now", "x 0", "infinite z", "power half");
        yield return new TestCaseData(null, 1.0, 0.0, double.NaN).SetArgDisplayNames("now", "x 1", "z 0", "power nan");
        yield return new TestCaseData(null, 1.0, 0.0, double.NegativeInfinity).SetArgDisplayNames("now", "x 1", "z 0", "power infinite");
        yield return new TestCaseData(-1, 1.0, 0.0, 0.5).SetArgDisplayNames("view negative", "x 1", "z 0", "power half");
        yield return new TestCaseData(1, 1.0, 0.0, 0.5).SetArgDisplayNames("view past end", "x 1", "z 0", "power half");
    }

    [Test]
    public void Aim_FromCurrentPlayer_IsRelayedToOthersOnly()
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a", "b");
        var current = h.CurrentConnection;
        var other = current == "a" ? "b" : "a";
        h.Sent().Clear();

        // Act
        h.Match.Aim(current, null, 1, 0, 0.5, h.Now);

        // Assert
        var sent = h.Sent().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent.Method, Is.EqualTo(nameof(IGameClient.AimUpdated)));
            Assert.That(sent.To, Is.Null);
            Assert.That(sent.Recipients, Is.EqualTo(new[] { other }));
            Assert.That(sent.Payload, Is.EqualTo(new AimDto(h.Match.Turn!.PlayerId, null, 1, 0, 0.5)));
        }
    }

    [Test]
    public void Aim_WithoutPower_IsRelayed()
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a", "b");

        // Act
        h.Match.Aim(h.CurrentConnection, null, 0, 0, null, h.Now);

        // Assert
        Assert.That(Aims(h).Single().Power, Is.Null);
    }

    [Test]
    public void Aim_FromNonCurrentPlayer_IsIgnored()
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a", "b");
        var other = h.CurrentConnection == "a" ? "b" : "a";

        // Act
        h.Match.Aim(other, null, 1, 0, 0.5, h.Now);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Aims(h), Is.Empty);
            Assert.That(h.Rejections(other), Is.Empty);
        }
    }

    [Test]
    public void Aim_FromSpectator_IsIgnoredWhileCurrentPlayersAimReachesSpectator()
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a");
        h.Hit("a", Up, 0.2);
        h.Join("s");
        h.WaitForTurn();
        h.Sent().Clear();

        // Act
        h.Match.Aim("s", null, 1, 0, 0.5, h.Now);
        h.Match.Aim("a", 0, 1, 0, 0.5, h.Now);

        // Assert
        var relayed = h.Sent().Single(m => m.Method == nameof(IGameClient.AimUpdated));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((AimDto)relayed.Payload).PlayerId, Is.EqualTo(h.Match.Turn!.PlayerId));
            Assert.That(relayed.Recipients, Is.EqualTo(new[] { "s" }));
        }
    }

    [Test]
    public void Aim_DuringPlayback_IsIgnored()
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a", "b");
        var current = h.CurrentConnection;
        h.Hit(current, Up, 0.2);

        // Act
        h.Match.Aim(current, null, 1, 0, null, h.Now);

        // Assert
        Assert.That(Aims(h), Is.Empty);
    }

    [Test]
    public void Aim_InReadyRoom_IsIgnored()
    {
        // Arrange
        var h = new MatchHarness();
        h.Join("a");
        h.Join("b");

        // Act
        h.Match.Aim("a", null, 1, 0, 0.5, h.Now);

        // Assert
        Assert.That(Aims(h), Is.Empty);
    }

    [Test]
    public void Aim_InRoundResults_IsIgnored()
    {
        // Arrange
        var h = new MatchHarness(new GameOptions { TurnsPerPlayer = 1 });
        h.Start("a", "b");
        h.FinishRound();

        // Act
        h.Match.Aim("a", null, 1, 0, 0.5, h.Now);

        // Assert
        Assert.That(Aims(h), Is.Empty);
    }

    [TestCaseSource(nameof(InvalidAims))]
    public void Aim_InvalidValues_IsIgnored(int? viewIndex, double dirX, double dirZ, double? power)
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a", "b");
        h.Hit(h.CurrentConnection, Up, 0.2);
        h.WaitForTurn();

        // Act
        h.Match.Aim(h.CurrentConnection, viewIndex, dirX, dirZ, power, h.Now);

        // Assert
        Assert.That(Aims(h), Is.Empty);
    }

    [Test]
    public void Aim_ViewIndexWithinTimeline_IsRelayed()
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a", "b");
        h.Hit(h.CurrentConnection, Up, 0.2);
        h.WaitForTurn();

        // Act
        h.Match.Aim(h.CurrentConnection, 0, 1, 0, 0.5, h.Now);

        // Assert
        Assert.That(Aims(h).Single().ViewIndex, Is.Zero);
    }

    [Test]
    public void Aim_OverRateLimit_DropsExtrasUntilWindowPasses()
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a", "b");
        var current = h.CurrentConnection;

        // Act
        for (var i = 0; i < RateLimit + 5; i++)
        {
            h.Match.Aim(current, null, 1, 0, 0.5, h.Now);
        }

        var burst = Aims(h).Count;
        h.Advance(1000);
        h.Match.Aim(current, null, 1, 0, 0.5, h.Now);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(burst, Is.EqualTo(RateLimit));
            Assert.That(Aims(h), Has.Count.EqualTo(RateLimit + 1));
        }
    }

    private static List<AimDto> Aims(MatchHarness h) => h.Events<AimDto>(nameof(IGameClient.AimUpdated));
}