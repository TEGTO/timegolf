using TimeGolf.Game;

namespace TimeGolf.Tests;

public sealed class RestartVoteTests
{
    [Test]
    public void MinorityVote_DoesNotRestart()
    {
        // Arrange
        var h = new MatchHarness();
        var ids = h.JoinAllThenStartRoundTwo("a", "b");

        // Act
        h.VoteRestart("a");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.Match.Round, Is.EqualTo(2));
            Assert.That(h.PlayerOf(ids[0]).RestartVote, Is.True);
        }
    }

    [Test]
    public void MajorityVote_ReturnsToLobbyAndClearsVotesAndReady()
    {
        // Arrange
        var h = new MatchHarness();
        var ids = h.JoinAllThenStartRoundTwo("a", "b", "c");
        h.VoteRestart("a");

        // Act
        h.VoteRestart("b");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.Match.Phase, Is.EqualTo(Phase.Lobby));
            Assert.That(h.Match.Round, Is.Zero);
            Assert.That(h.Match.Level, Is.Null);
            Assert.That(ids.Select(id => h.PlayerOf(id).Points), Is.All.Zero);
            Assert.That(ids.Select(id => h.PlayerOf(id).RestartVote), Is.All.False);
            Assert.That(ids.Select(id => h.PlayerOf(id).Ready), Is.All.False);
        }
    }

    [Test]
    public void SecondPress_WithdrawsVote()
    {
        // Arrange
        var h = new MatchHarness();
        var ids = h.JoinAllThenStartRoundTwo("a", "b");
        h.VoteRestart("a");

        // Act
        h.VoteRestart("a");

        // Assert
        Assert.That(h.PlayerOf(ids[0]).RestartVote, Is.False);
    }
}