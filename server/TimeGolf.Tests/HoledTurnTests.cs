using TimeGolf.Game;
using TimeGolf.Game.Geometry;

namespace TimeGolf.Tests;

public sealed class HoledTurnTests
{
    private static readonly Vec2 Up = new(0, -1);
    private static readonly Vec2 Right = new(1, 0);

    [Test]
    public void AllActivePlayersHoled_EndsRound()
    {
        // Arrange
        var h = new MatchHarness(level: MatchHarness.WithSpawns(new Vec2(4, 10)));
        h.Start("a");
        h.Hit("a", Up, 0.1);
        h.WaitForTurn();

        // Act
        h.Hit("a", Up, 0.331);
        h.WaitForTurn();

        // Assert
        Assert.That(h.Match.Phase, Is.EqualTo(Phase.RoundResults));
    }

    [Test]
    public void HoledPlayer_IsSkippedWithoutUsingTurns()
    {
        // Arrange
        var h = TwoPlayersWithAHoled(out var b);

        // Act
        h.Skip("b");
        h.WaitForTurn();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.Match.Turn!.PlayerId, Is.EqualTo(b));
            Assert.That(h.Match.Turn.Turn, Is.EqualTo(3));
        }
    }

    [Test]
    public void PlayerUnholedByReplay_GetsTurnsAgain()
    {
        // Arrange
        var h = TwoPlayersWithAHoled(out _);
        var a = h.Match.Order[0].Id;

        // Act
        h.Replace("b", 1, new Vec2(0.06, -1), 0.45);
        h.WaitForTurn();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.PlayerOf(a).Holed, Is.False);
            Assert.That(h.Match.Turn!.PlayerId, Is.EqualTo(a));
        }
    }

    // Order [a, b]: a 0.1, b nudges right, a holes → turn 4 is b's.
    private static MatchHarness TwoPlayersWithAHoled(out string b)
    {
        var h = new MatchHarness(level: MatchHarness.WithSpawns(new Vec2(4, 10), new Vec2(4, 12)));
        b = h.Start("a", "b")[1];
        h.Hit("a", Up, 0.1);
        h.WaitForTurn();
        h.Hit("b", Right, 0.05);
        h.WaitForTurn();
        h.Hit("a", Up, 0.331);
        h.WaitForTurn();
        return h;
    }
}