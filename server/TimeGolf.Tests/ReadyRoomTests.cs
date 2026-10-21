using TimeGolf.Game;
using TimeGolf.Game.Geometry;

namespace TimeGolf.Tests;

public sealed class ReadyRoomTests
{
    [Test]
    public void Join_StaysInLobbyWithoutStarting()
    {
        // Arrange
        var h = new MatchHarness();

        // Act
        var welcome = h.Join("a");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(welcome.Phase, Is.EqualTo("Lobby"));
            Assert.That(welcome.Lobby, Is.EqualTo(new LobbyInfoDto("lobby", "Lobby", false, 1, 16, "Lobby")));
            Assert.That(welcome.LastMatch, Is.Null);
            Assert.That(welcome.Level, Is.Null);
            Assert.That(h.Events<TurnDto>(nameof(IGameClient.TurnStarted)), Is.Empty);
        }
    }

    [Test]
    public void Ready_StartsMatchOnlyWhenAllActivePlayersReady()
    {
        // Arrange
        var h = new MatchHarness();
        var ids = new[] { h.Join("a").PlayerId, h.Join("b").PlayerId };

        // Act
        h.SetReady("a", true);
        var phaseAfterFirst = h.Match.Phase;
        h.SetReady("b", true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(phaseAfterFirst, Is.EqualTo(Phase.Lobby));
            Assert.That(h.Match.Phase, Is.EqualTo(Phase.Playing));
            Assert.That(h.Match.Round, Is.EqualTo(1));
            Assert.That(h.Match.Order.Select(p => p.Id), Is.EqualTo(ids));
            Assert.That(ids.Select(id => h.PlayerOf(id).Ready), Is.All.False);
            Assert.That(h.Match.Turn!.PlayerId, Is.EqualTo(ids[0]));
        }
    }

    [Test]
    public void Unready_BlocksStart()
    {
        // Arrange
        var h = new MatchHarness();
        var a = h.Join("a").PlayerId;
        h.Join("b");
        h.SetReady("a", true);

        // Act
        h.SetReady("a", false);
        h.SetReady("b", true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.Match.Phase, Is.EqualTo(Phase.Lobby));
            Assert.That(h.PlayerOf(a).Ready, Is.False);
            Assert.That(h.Events<PlayerDto[]>(nameof(IGameClient.Players))[^1].Select(p => p.Ready), Is.EqualTo(new[] { false, true }));
        }
    }

    [Test]
    public void ReadyRoomDisconnect_RemovesPlayerAndStartsIfRestAreReady()
    {
        // Arrange
        var h = new MatchHarness();
        var a = h.Join("a").PlayerId;
        var b = h.Join("b").PlayerId;
        h.SetReady("a", true);

        // Act
        h.Disconnect("b");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.Match.Players.Select(p => p.Id), Is.EqualTo(new[] { a }));
            Assert.That(h.Match.Players.Select(p => p.Id), Does.Not.Contain(b));
            Assert.That(h.Match.Phase, Is.EqualTo(Phase.Playing));
        }
    }

    [Test]
    public void SetReady_DuringGame_IsIgnored()
    {
        // Arrange
        var h = new MatchHarness();
        var a = h.Start("a")[0];

        // Act
        h.SetReady("a", true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.PlayerOf(a).Ready, Is.False);
            Assert.That(h.Match.Round, Is.EqualTo(1));
        }
    }

    [Test]
    public void LastPlayerLeavingDuringGame_ReturnsToLobbyAndPurgesInactive()
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a", "b");

        // Act
        h.Disconnect("a");
        h.Leave("b");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.Match.Phase, Is.EqualTo(Phase.Lobby));
            Assert.That(h.Match.Players, Is.Empty);
        }
    }

    [Test]
    public void SoloPlayerReconnectingMidGame_ResumesSameRoundAndTurnWithFreshDeadline()
    {
        // Arrange
        var h = new MatchHarness();
        var a = h.Start("a")[0];
        h.Hit("a", new Vec2(0, -1), 0.3);
        h.WaitForTurn();
        var turn = h.Match.Turn;
        h.Disconnect("a");
        h.Advance((long)(h.Options.TurnSeconds * 1000 * 2));

        // Act
        var welcome = h.Join("a2", "a", "key-a");
        h.Advance(50);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(welcome.PlayerId, Is.EqualTo(a));
            Assert.That(welcome.Phase, Is.EqualTo("Playing"));
            Assert.That(welcome.Round, Is.EqualTo(1));
            Assert.That(welcome.Turn, Is.EqualTo(turn! with { EndsAt = h.Now - 50 + (long)(h.Options.TurnSeconds * 1000) }));
            Assert.That(h.Match.Turn, Is.EqualTo(welcome.Turn));
            Assert.That(h.Events<TurnDto>(nameof(IGameClient.TurnStarted))[^1], Is.EqualTo(welcome.Turn));
            Assert.That(welcome.Timeline, Has.Length.EqualTo(1));
            Assert.That(h.Events<TurnSkippedDto>(nameof(IGameClient.TurnSkipped)), Is.Empty);
        }
    }
}