using Microsoft.AspNetCore.SignalR;
using TimeGolf.Game;

namespace TimeGolf;

// One send chain per batch key: a lobby's messages leave in emit order, while a slow or failing lobby never
// holds up the game loop or the other lobbies. Only the game loop calls Dispatch, so the map needs no lock.
public sealed class OutboxDispatcher(IHubContext<GameHub, IGameClient> hub, ILogger<OutboxDispatcher> logger)
{
    private readonly Dictionary<string, Task> chains = [];

    internal Task Idle => Task.WhenAll(chains.Values);

    public void Dispatch(IEnumerable<OutboxBatch> batches, CancellationToken ct)
    {
        foreach (var key in chains.Where(c => c.Value.IsCompleted).Select(c => c.Key).ToList())
        {
            chains.Remove(key);
        }

        foreach (var batch in batches)
        {
            var previous = chains.GetValueOrDefault(batch.Key, Task.CompletedTask);
            chains[batch.Key] = Task.Run(() => SendAfter(previous, batch, ct), CancellationToken.None);
        }
    }

    private async Task SendAfter(Task previous, OutboxBatch batch, CancellationToken ct)
    {
        await previous;
        foreach (var message in batch.Messages)
        {
            try
            {
                var target = message.To != null ? hub.Clients.Client(message.To) : hub.Clients.Clients(message.Recipients);
                await message.Send(target).WaitAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.SendFailed(e, batch.Key);
            }
        }
    }
}