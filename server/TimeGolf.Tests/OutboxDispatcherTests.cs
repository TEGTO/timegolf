using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TimeGolf.Game;

namespace TimeGolf.Tests;

public sealed class OutboxDispatcherTests
{
    private readonly Dictionary<string, RecordingClient> clients = [];
    private OutboxDispatcher dispatcher = null!;

    [SetUp]
    public void SetUp()
    {
        clients.Clear();
        var hubClients = new Mock<IHubClients<IGameClient>>();
        hubClients.Setup(c => c.Client(It.IsAny<string>())).Returns<string>(ClientOf);
        hubClients.Setup(c => c.Clients(It.IsAny<IReadOnlyList<string>>())).Returns<IReadOnlyList<string>>(ids => ClientOf(ids.Single()));
        var hub = new Mock<IHubContext<GameHub, IGameClient>>();
        hub.Setup(h => h.Clients).Returns(hubClients.Object);
        dispatcher = new OutboxDispatcher(hub.Object, NullLogger<OutboxDispatcher>.Instance);
    }

    [Test]
    public async Task Dispatch_SlowLobby_DoesNotHoldUpOtherLobbies()
    {
        // Arrange
        var stuck = new TaskCompletionSource();
        ClientOf("slow").Delivery = () => stuck.Task;

        // Act
        dispatcher.Dispatch([Batch("one", Turn("slow", 1)), Batch("two", Turn("fast", 1))], CancellationToken.None);
        await WaitUntil(() => ClientOf("fast").Calls.Count == 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ClientOf("fast").Calls.Single().Method, Is.EqualTo(nameof(IGameClient.TurnStarted)));
            Assert.That(dispatcher.Idle.IsCompleted, Is.False);
        }

        stuck.SetResult();
        await dispatcher.Idle;
    }

    [Test]
    public async Task Dispatch_SameLobbyAcrossTicks_KeepsEmitOrder()
    {
        // Arrange
        var stuck = new TaskCompletionSource();
        ClientOf("a").Delivery = () => stuck.Task;
        dispatcher.Dispatch([Batch("one", Turn("a", 1), Turn("a", 2))], CancellationToken.None);
        await WaitUntil(() => ClientOf("a").Calls.Count == 1);

        // Act
        dispatcher.Dispatch([Batch("one", Turn("a", 3))], CancellationToken.None);
        ClientOf("a").Delivery = () => Task.CompletedTask;
        stuck.SetResult();
        await dispatcher.Idle;

        // Assert
        Assert.That(ClientOf("a").Calls.Select(c => ((TurnDto)c.Payload).Turn), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public async Task Dispatch_FailedSend_StillSendsTheRestOfTheBatch()
    {
        // Arrange
        ClientOf("a").Delivery = () => Task.FromException(new InvalidOperationException());

        // Act
        dispatcher.Dispatch([Batch("one", Turn("a", 1), Turn("b", 2))], CancellationToken.None);
        await dispatcher.Idle;

        // Assert
        Assert.That(ClientOf("b").Calls, Has.Count.EqualTo(1));
    }

    private static OutboxBatch Batch(string key, params Outgoing[] messages) => new(key, messages);

    private static Outgoing Turn(string connectionId, int turn) =>
        Outgoing.Create(static (c, t) => c.TurnStarted(t), new TurnDto(turn, 7, "p", 0), connectionId, []);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    private RecordingClient ClientOf(string connectionId)
    {
        lock (clients)
        {
            if (!clients.TryGetValue(connectionId, out var client))
            {
                clients[connectionId] = client = new RecordingClient();
            }

            return client;
        }
    }
}