using TimeGolf.Game;
using TimeGolf.Game.Geometry;

namespace TimeGolf.Tests;

public sealed class ActionTests
{
    private static readonly Vec2 Up = new(0, -1);

    private static IEnumerable<TestCaseData> InvalidShots()
    {
        yield return new TestCaseData(double.NaN, 0.0, 0.5).SetArgDisplayNames("nan x", "z 0", "power half");
        yield return new TestCaseData(double.PositiveInfinity, 0.0, 0.5).SetArgDisplayNames("infinite x", "z 0", "power half");
        yield return new TestCaseData(0.0, 0.0, 0.5).SetArgDisplayNames("x 0", "z 0", "power half");
        yield return new TestCaseData(1.0, 0.0, 0.0).SetArgDisplayNames("x 1", "z 0", "power 0");
        yield return new TestCaseData(1.0, 0.0, -1.0).SetArgDisplayNames("x 1", "z 0", "power negative");
        yield return new TestCaseData(1.0, 0.0, double.NaN).SetArgDisplayNames("x 1", "z 0", "power nan");
    }

    [Test]
    public void Hit_NotJoined_IsRejected()
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a");

        // Act
        h.Hit("ghost", Up, 0.5);

        // Assert
        Assert.That(h.Rejections("ghost"), Is.EqualTo(new[] { "Not joined" }));
    }

    [Test]
    public void HitAndSkip_OutOfTurn_AreRejected()
    {
        // Arrange
        var h = new MatchHarness();
        h.JoinAllThenStartRoundTwo("a", "b");
        var other = h.ConnectionOf(h.Match.Order[1].Id);

        // Act
        h.Hit(other, Up, 0.5);
        h.Skip(other);
        h.Replace(other, 0, Up, 0.5);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.Rejections(other), Is.EqualTo(new[] { "Not your turn", "Not your turn", "Not your turn" }));
            Assert.That(h.Match.Timeline!.Records, Is.Empty);
        }
    }

    [TestCaseSource(nameof(InvalidShots))]
    public void Hit_InvalidNumbers_IsRejected(double dirX, double dirZ, double power)
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a");

        // Act
        h.Match.Hit("a", dirX, dirZ, power, h.Now);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.Rejections("a"), Is.EqualTo(new[] { "Invalid hit" }));
            Assert.That(h.Match.Turn, Is.Not.Null);
        }
    }

    [Test]
    public void Replace_OtherPlayersHit_IsRejected()
    {
        // Arrange
        var h = new MatchHarness();
        h.JoinAllThenStartRoundTwo("a", "b");
        h.Hit(h.CurrentConnection, Up, 0.2);
        h.WaitForTurn();
        var second = h.CurrentConnection;

        // Act
        h.Replace(second, 0, Up, 0.3);

        // Assert
        Assert.That(h.Rejections(second), Is.EqualTo(new[] { "Not your hit" }));
    }

    [Test]
    public void Replace_UnknownIndex_IsRejected()
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a");

        // Act
        h.Replace("a", 0, Up, 0.3);

        // Assert
        Assert.That(h.Rejections("a"), Is.EqualTo(new[] { "Hit not found" }));
    }

    [Test]
    public void PresentHit_BroadcastsSingleEntryAtNewIndex()
    {
        // Arrange
        var h = new MatchHarness();
        h.Start("a");

        // Act
        h.Hit("a", Up, 0.2);

        // Assert
        var played = h.Events<TimelinePlayedDto>(nameof(IGameClient.TimelinePlayed)).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(played.Replaced, Is.False);
            Assert.That(played.FromIndex, Is.Zero);
            Assert.That(played.Hits.Single().Index, Is.Zero);
            Assert.That(played.Hits[0].Tracks.Single().Points.Length % 2, Is.Zero);
            Assert.That(h.Match.Turn, Is.Null);
        }
    }
}