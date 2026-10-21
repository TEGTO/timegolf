using Microsoft.Extensions.Logging.Abstractions;
using TimeGolf.Game;

namespace TimeGolf.Tests;

public sealed class LobbyManagerTests
{
    private const long Start = 1_000_000;

    private static IEnumerable<TestCaseData> InvalidLobbies()
    {
        yield return new TestCaseData("", 8).SetArgDisplayNames("empty name", "8 players");
        yield return new TestCaseData("   ", 8).SetArgDisplayNames("blank name", "8 players");
        yield return new TestCaseData(new string('x', 25), 8).SetArgDisplayNames("25 chars", "8 players");
        yield return new TestCaseData("Fun", 0).SetArgDisplayNames("Fun", "0 players");
        yield return new TestCaseData("Fun", 17).SetArgDisplayNames("Fun", "17 players");
    }

    [Test]
    public void CreateThenJoin_WelcomesWithLobbyInfo()
    {
        // Arrange
        var lobbies = NewManager();
        var id = lobbies.Create("  Fun  ", null, 4, Start);

        // Act
        var welcome = lobbies.Join("a", id, null, "Alice", "key-a", Start);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(welcome.Lobby, Is.EqualTo(new LobbyInfoDto(id, "Fun", false, 1, 4, "Lobby")));
            Assert.That(lobbies.List("m"), Is.EqualTo(new[] { welcome.Lobby }));
        }
    }

    [TestCaseSource(nameof(InvalidLobbies))]
    public void Create_Invalid_Throws(string name, int maxPlayers)
    {
        // Arrange
        var lobbies = NewManager();

        // Act
        void Create() => lobbies.Create(name, null, maxPlayers, Start);

        // Assert
        Assert.That(Create, Throws.TypeOf<LobbyException>());
    }

    [Test]
    public void Join_UnknownLobby_Throws()
    {
        // Arrange
        var lobbies = NewManager();

        // Act
        void Join() => lobbies.Join("a", "nope", null, "Alice", "key-a", Start);

        // Assert
        Assert.That(Join, Throws.TypeOf<LobbyException>().With.Message.EqualTo("Lobby not found"));
    }

    [Test]
    public void Join_LockedLobby_RequiresPassword()
    {
        // Arrange
        var lobbies = NewManager();
        var id = lobbies.Create("Secret", "pw", 4, Start);

        // Act
        void JoinWrong() => lobbies.Join("a", id, "nope", "Alice", "key-a", Start);
        void JoinMissing() => lobbies.Join("a", id, null, "Alice", "key-a", Start);
        var welcome = lobbies.Join("b", id, "pw", "Bob", "key-b", Start);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JoinWrong, Throws.TypeOf<LobbyException>().With.Message.EqualTo("Wrong password"));
            Assert.That(JoinMissing, Throws.TypeOf<LobbyException>().With.Message.EqualTo("Wrong password"));
            Assert.That(welcome.Lobby.Locked, Is.True);
            Assert.That(welcome.Lobby.Players, Is.EqualTo(1));
        }
    }

    [Test]
    public void Join_FullLobby_RejectsNewPlayerButAllowsReconnect()
    {
        // Arrange
        var lobbies = NewManager();
        var id = lobbies.Create("Duo", null, 2, Start);
        var a = lobbies.Join("a", id, null, "Alice", "key-a", Start).PlayerId;
        lobbies.Join("b", id, null, "Bob", "key-b", Start);
        lobbies.MatchOf("a")!.SetReady("a", true, Start);
        lobbies.MatchOf("b")!.SetReady("b", true, Start);
        lobbies.Disconnect("a", Start);
        lobbies.Join("c", id, null, "Carol", "key-c", Start);

        // Act
        void JoinNew() => lobbies.Join("d", id, null, "Dave", "key-d", Start);
        var reconnected = lobbies.Join("a2", id, null, "Alice", "key-a", Start);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JoinNew, Throws.TypeOf<LobbyException>().With.Message.EqualTo("Lobby is full"));
            Assert.That(reconnected.PlayerId, Is.EqualTo(a));
            Assert.That(reconnected.Lobby.Players, Is.EqualTo(3));
        }
    }

    [Test]
    public void EmptyLobby_IsDeletedAfterEmptyLobbySeconds()
    {
        // Arrange
        var lobbies = NewManager();
        var id = lobbies.Create("Fun", null, 4, Start);
        lobbies.Join("a", id, null, "Alice", "key-a", Start);
        lobbies.Disconnect("a", Start);
        lobbies.Step(Start);

        // Act
        lobbies.Step(Start + 59_999);
        var before = lobbies.List("m").Length;
        lobbies.Step(Start + 60_000);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before, Is.EqualTo(1));
            Assert.That(lobbies.List("m"), Is.Empty);
        }
    }

    [Test]
    public void OccupiedLobby_IsKept()
    {
        // Arrange
        var lobbies = NewManager();
        var id = lobbies.Create("Fun", null, 4, Start);
        lobbies.Join("a", id, null, "Alice", "key-a", Start);

        // Act
        lobbies.Step(Start + 120_000);

        // Assert
        Assert.That(lobbies.List("m").Single().Id, Is.EqualTo(id));
    }

    [Test]
    public void TwoLobbies_AreIndependent()
    {
        // Arrange
        var lobbies = NewManager();
        var first = lobbies.Create("One", null, 4, Start);
        var second = lobbies.Create("Two", null, 4, Start);
        lobbies.Join("a", first, null, "Alice", "key-a", Start);
        lobbies.Join("b", second, null, "Bob", "key-b", Start);
        lobbies.Drain();

        // Act
        lobbies.MatchOf("a")!.SetReady("a", true, Start);
        lobbies.Act("b", m => m.Skip("b", Start));
        lobbies.Step(Start);

        // Assert
        var sent = SentEvent.From(lobbies.Drain());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lobbies.MatchOf("a")!.Phase, Is.EqualTo(Phase.Playing));
            Assert.That(lobbies.MatchOf("b")!.Phase, Is.EqualTo(Phase.Lobby));
            Assert.That(sent.Where(m => m.Method == nameof(IGameClient.TurnStarted)).SelectMany(m => m.Recipients), Is.EqualTo(new[] { "a" }));
            Assert.That(sent.Single(m => m.Method == nameof(IGameClient.ActionRejected)).To, Is.EqualTo("b"));
            Assert.That(sent.Where(m => m.Method != nameof(IGameClient.ActionRejected)).SelectMany(m => m.Recipients), Has.None.EqualTo("b"));
        }
    }

    [Test]
    public void Drain_GroupsMessagesPerLobbyAndMenu()
    {
        // Arrange
        var lobbies = NewManager();
        var first = lobbies.Create("One", null, 4, Start);
        var second = lobbies.Create("Two", null, 4, Start);
        lobbies.Join("a", first, null, "Alice", "key-a", Start);
        lobbies.Join("b", second, null, "Bob", "key-b", Start);
        lobbies.MatchOf("a")!.SetReady("a", false, Start);
        lobbies.MatchOf("b")!.SetReady("b", false, Start);
        lobbies.Act("m", m => m.Skip("m", Start));

        // Act
        var batches = lobbies.Drain();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(batches.Select(b => b.Key), Is.EqualTo(new[] { LobbyManager.MenuKey, first, second }));
            Assert.That(batches[1].Messages.SelectMany(m => m.Recipients).Distinct(), Is.EqualTo(new[] { "a" }));
            Assert.That(batches[2].Messages.SelectMany(m => m.Recipients).Distinct(), Is.EqualTo(new[] { "b" }));
        }
    }

    [Test]
    public void Lobbies_ArePushedOnlyToMenuConnections()
    {
        // Arrange
        var lobbies = NewManager();
        lobbies.List("m");
        lobbies.List("x");
        var id = lobbies.Create("Fun", null, 4, Start);
        lobbies.Join("x", id, null, "Xena", "key-x", Start);
        lobbies.Drain();

        // Act
        lobbies.Step(Start);

        // Assert
        var pushed = SentEvent.From(lobbies.Drain()).Single(m => m.Method == nameof(IGameClient.Lobbies));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pushed.Recipients, Is.EqualTo(new[] { "m" }));
            Assert.That(((LobbyInfoDto[])pushed.Payload).Single().Players, Is.EqualTo(1));
        }
    }

    [Test]
    public void Lobbies_UnchangedState_IsNotPushedAgain()
    {
        // Arrange
        var lobbies = NewManager();
        lobbies.List("m");
        lobbies.Create("Fun", null, 4, Start);
        lobbies.Step(Start);
        lobbies.Drain();

        // Act
        lobbies.Step(Start + 50);

        // Assert
        Assert.That(lobbies.Drain(), Is.Empty);
    }

    [Test]
    public void Leave_ReturnsConnectionToMenu()
    {
        // Arrange
        var lobbies = NewManager();
        var id = lobbies.Create("Fun", null, 4, Start);
        lobbies.Join("a", id, null, "Alice", "key-a", Start);
        lobbies.Join("b", id, null, "Bob", "key-b", Start);
        lobbies.Drain();

        // Act
        lobbies.Leave("a", Start);

        // Assert
        var sent = SentEvent.From(lobbies.Drain());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lobbies.MatchOf("a"), Is.Null);
            Assert.That(lobbies.MatchOf("b")!.Players.Count(), Is.EqualTo(1));
            Assert.That(((LobbyInfoDto[])sent.Single(m => m.Method == nameof(IGameClient.Lobbies) && m.To == "a").Payload).Single().Players, Is.EqualTo(1));
        }
    }

    private static LobbyManager NewManager()
    {
        var options = new GameOptions();
        return new LobbyManager(options, s => new Match(s, options, (_, _) => TestLevels.Corridor(), new Random(1)), NullLogger<LobbyManager>.Instance);
    }
}