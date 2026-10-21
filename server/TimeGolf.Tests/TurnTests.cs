using TimeGolf.Game;
using TimeGolf.Game.Geometry;

namespace TimeGolf.Tests;

public sealed class TurnTests
{
    private static readonly Vec2 Up = new(0, -1);

    [Test]
    public void Turns_GoRoundRobinAndOrderRotatesByRound()
    {
        // Arrange
        var h = new MatchHarness(new GameOptions { TurnsPerPlayer = 4 });
        var ids = h.JoinAllThenStartRoundTwo("a", "b", "c");
        h.SkipTurns(4);
        var roundTwoTurns = h.Events<TurnDto>(nameof(IGameClient.TurnStarted)).Select(t => t.PlayerId).ToList();

        // Act
        h.FinishRound();
        h.Advance((long)(h.Options.RoundResultsSeconds * 1000));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(roundTwoTurns, Is.EqualTo(new[] { ids[1], ids[2], ids[0], ids[1] }));
            Assert.That(h.Match.Round, Is.EqualTo(3));
            Assert.That(h.Match.Order.Select(p => p.Id), Is.EqualTo(new[] { ids[2], ids[0], ids[1] }));
            Assert.That(h.Match.Turn!.PlayerId, Is.EqualTo(ids[2]));
        }
    }

    [Test]
    public void AllTurnsPerPlayerIncludingSkips_EndRoundAfterLastPlayback()
    {
        // Arrange
        var h = new MatchHarness();
        h.JoinAllThenStartRoundTwo("a", "b");
        var turns = h.Options.TurnsPerPlayer;
        h.SkipTurns((turns * 2) - 1);
        h.WaitForTurn();

        // Act
        h.Hit(h.CurrentConnection, Up, 0.3);
        var phaseRightAfterHit = h.Match.Phase;
        h.AdvanceTo(h.Match.NextTurnAt);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(phaseRightAfterHit, Is.EqualTo(Phase.Playing));
            Assert.That(h.Match.Phase, Is.EqualTo(Phase.RoundResults));
            Assert.That(h.Events<TurnDto>(nameof(IGameClient.TurnStarted)).Select(t => t.Turn), Is.EqualTo(Enumerable.Range(1, turns).SelectMany(t => new[] { t, t })));
            Assert.That(h.Events<RoundEndedDto>(nameof(IGameClient.RoundEnded)).Single().Round, Is.EqualTo(2));
        }
    }

    [Test]
    public void TurnTimeout_AutoSkips()
    {
        // Arrange
        var h = new MatchHarness();
        var a = h.Start("a", "b")[0];

        // Act
        h.Advance((long)(h.Options.TurnSeconds * 1000));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.Events<TurnSkippedDto>(nameof(IGameClient.TurnSkipped)), Is.EqualTo(new[] { new TurnSkippedDto(1, a, "timeout") }));
            Assert.That(h.Match.Turn, Is.Null);
        }
    }

    [Test]
    public void InactivePlayer_IsSkippedImmediately()
    {
        // Arrange
        var h = new MatchHarness();
        var ids = h.JoinAllThenStartRoundTwo("a", "b", "c");
        h.Disconnect("c");

        // Act
        h.Skip("b");
        h.Advance((long)(h.Options.PlaybackGapSeconds * 1000));
        h.Advance((long)(h.Options.PlaybackGapSeconds * 1000));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.Events<TurnSkippedDto>(nameof(IGameClient.TurnSkipped)), Is.EqualTo(new[] { new TurnSkippedDto(1, ids[1], "skip"), new TurnSkippedDto(1, ids[2], "inactive") }));
            Assert.That(h.Match.Turn, Is.EqualTo(h.Events<TurnDto>(nameof(IGameClient.TurnStarted))[^1]));
            Assert.That(h.Match.Turn!.PlayerId, Is.EqualTo(ids[0]));
            Assert.That(h.Match.Turn.Turn, Is.EqualTo(1));
        }
    }

    [Test]
    public void CurrentPlayerDisconnecting_IsSkippedOnNextStep()
    {
        // Arrange
        var h = new MatchHarness();
        var a = h.Start("a", "b")[0];

        // Act
        h.Disconnect("a");
        h.Advance(50);

        // Assert
        Assert.That(h.Events<TurnSkippedDto>(nameof(IGameClient.TurnSkipped)), Is.EqualTo(new[] { new TurnSkippedDto(1, a, "inactive") }));
    }

    [Test]
    public void NextTurn_WaitsForPresentHitPlaybackAtNormalSpeed()
    {
        // Arrange
        var h = new MatchHarness();
        var a = h.JoinAllThenStartRoundTwo("a", "b")[0];
        var hitAt = h.Now;

        // Act
        h.Hit("b", Up, 0.4);
        var duration = h.Match.Timeline!.Results[0].DurationMs;
        var expected = hitAt + (long)Math.Ceiling(duration) + 400;
        h.AdvanceTo(expected - 1);
        var turnBeforeEnd = h.Match.Turn;
        h.AdvanceTo(expected);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(duration, Is.GreaterThan(1000));
            Assert.That(turnBeforeEnd, Is.Null);
            Assert.That(h.Match.Turn!.PlayerId, Is.EqualTo(a));
        }
    }

    [Test]
    public void NextTurn_AfterReplace_WaitsForReplacedHitAtNormalSpeedAndRewrittenHitsAtReplaySpeed()
    {
        // Arrange
        var h = new MatchHarness();
        h.JoinAllThenStartRoundTwo("a", "b");
        h.Hit("b", Up, 0.4);
        h.WaitForTurn();
        h.Hit("a", Up, 0.6);
        h.WaitForTurn();
        var replaceAt = h.Now;

        // Act
        h.Replace("b", 0, new Vec2(1, -1).Normalized, 0.5);

        // Assert
        var results = h.Match.Timeline!.Results;
        var playback = results[0].DurationMs + (results[1].DurationMs / 3);
        Assert.That(h.Match.NextTurnAt, Is.EqualTo(replaceAt + (long)Math.Ceiling(playback) + 400));
    }

    [Test]
    public void LastRound_EndsMatchWithWinner()
    {
        // Arrange
        var h = new MatchHarness(new GameOptions { RoundsPerMatch = 1, TurnsPerPlayer = 1 });
        var a = h.Start("a")[0];
        h.FinishRound();

        // Act
        h.Advance((long)(h.Options.RoundResultsSeconds * 1000));

        // Assert
        var ended = h.Events<MatchEndedDto>(nameof(IGameClient.MatchEnded)).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.Match.Phase, Is.EqualTo(Phase.MatchResults));
            Assert.That(ended.WinnerIds, Is.EqualTo(new[] { a }));
            Assert.That(ended.Rows.Single().Points, Is.EqualTo(h.Events<RoundEndedDto>(nameof(IGameClient.RoundEnded)).Single().Rows.Single().Score));
        }
    }

    [Test]
    public void MatchResults_TimeoutReturnsToLobbyWithReadyClearedAndLastMatchKept()
    {
        // Arrange
        var h = new MatchHarness(new GameOptions { RoundsPerMatch = 1, TurnsPerPlayer = 1 });
        var a = h.Start("a")[0];
        h.FinishRound();
        h.Advance((long)(h.Options.RoundResultsSeconds * 1000));

        // Act
        h.Advance((long)(h.Options.MatchResultsSeconds * 1000));
        var welcome = h.Join("b");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.Match.Phase, Is.EqualTo(Phase.Lobby));
            Assert.That(h.PlayerOf(a).Points, Is.Zero);
            Assert.That(h.PlayerOf(a).Ready, Is.False);
            Assert.That(welcome.Phase, Is.EqualTo("Lobby"));
            Assert.That(welcome.LastMatch, Is.EqualTo(h.Events<MatchEndedDto>(nameof(IGameClient.MatchEnded)).Single()));
        }
    }
}