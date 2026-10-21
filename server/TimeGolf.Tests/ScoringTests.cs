using TimeGolf.Game;
using TimeGolf.Game.Geometry;

namespace TimeGolf.Tests;

public sealed class ScoringTests
{
    [TestCase(1, 100)]
    [TestCase(2, 50)]
    [TestCase(3, 34)]
    [TestCase(7, 15)]
    public void Score_Holed_Is100DividedByHitsRoundedUp(int hits, int expected)
    {
        // Act
        var score = Scoring.Score(hits, true, 0);

        // Assert
        Assert.That(score, Is.EqualTo(expected));
    }

    [Test]
    public void Score_NotHoled_Is100DividedByHitsPlusDistance()
    {
        // Act
        var score = Scoring.Score(2, false, 4.5);

        // Assert
        Assert.That(score, Is.EqualTo(16));
    }

    [Test]
    public void Missed_IsWorstScoreMinusTwoNotBelowZero()
    {
        // Act
        var missed = Scoring.Missed([50, 12, 34]);
        var floor = Scoring.Missed([1]);
        var none = Scoring.Missed([]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(missed, Is.EqualTo(10));
            Assert.That(floor, Is.Zero);
            Assert.That(none, Is.Zero);
        }
    }

    [Test]
    public void Rank_OrdersByScoreDescending()
    {
        // Arrange
        Player[] players = [WithScore("a", 34), WithScore("b", 100), WithScore("c", 7)];

        // Act
        var ranked = Scoring.Rank(players).Select(p => p.Id);

        // Assert
        Assert.That(ranked, Is.EqualTo(new[] { "b", "a", "c" }));
    }

    [Test]
    public void Winners_NobodyAboveZero_IsEmpty()
    {
        // Arrange
        Player[] players = [WithScore("a", 1), WithScore("b", 2)];

        // Act
        var winners = Scoring.Winners(players);

        // Assert
        Assert.That(winners, Is.Empty);
    }

    [Test]
    public void Winners_TiedTopPoints_AreShared()
    {
        // Arrange
        Player[] players = [WithScore("a", 1), WithScore("b", 2), WithScore("c", 3)];
        players[0].Points = 200;
        players[1].Points = 200;
        players[2].Points = 120;

        // Act
        var winners = Scoring.Winners(players);

        // Assert
        Assert.That(winners, Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void RoundEnd_AddsScoresAndGivesInactiveWorstMinusTwo()
    {
        // Arrange
        var h = new MatchHarness(new GameOptions { TurnsPerPlayer = 3 }, MatchHarness.WithSpawns(new Vec2(4, 10), new Vec2(2, 16), new Vec2(6, 16)));
        var ids = h.JoinAllThenStartRoundTwo("a", "b", "c");
        var aBefore = h.PlayerOf(ids[0]).Points;
        h.Hit("b", new Vec2(0, -1), 0.345);
        h.Disconnect("a");

        // Act
        h.FinishRound();

        // Assert
        var rows = h.Events<RoundEndedDto>(nameof(IGameClient.RoundEnded)).Single().Rows;
        var cDistance = Math.Round(h.Match.Level!.DistanceTo(new Vec2(2, 16)), 1);
        var cScore = Scoring.Score(0, false, cDistance);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows.Select(r => r.PlayerId), Is.EqualTo(new[] { ids[1], ids[2] }));
            Assert.That(rows[0], Is.EqualTo(new RoundRowDto(ids[1], "b", rows[0].Color, 1, true, null, 100, 100)));
            Assert.That(rows[1], Is.EqualTo(new RoundRowDto(ids[2], "c", rows[1].Color, 0, false, cDistance, cScore, cScore)));
            Assert.That(h.PlayerOf(ids[0]).Points, Is.EqualTo(aBefore + cScore - 2));
        }
    }

    [Test]
    public void LateJoiner_GetsMissedScoresOfFinishedRounds()
    {
        // Arrange
        var h = new MatchHarness(new GameOptions { TurnsPerPlayer = 1 });
        h.Start("a");
        h.FinishRound();
        var missed = h.Events<RoundEndedDto>(nameof(IGameClient.RoundEnded)).Single().Rows.Min(r => r.Score) - 2;

        // Act
        var welcome = h.Join("b");

        // Assert
        Assert.That(h.PlayerOf(welcome.PlayerId).Points, Is.EqualTo(Math.Max(0, missed)));
    }

    private static Player WithScore(string id, int score) => new(id, id, id, "c", 0) { Active = true, Score = score };
}