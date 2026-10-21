using TimeGolf.Game;

namespace TimeGolf;

public sealed class GameLoop(LobbyManager lobbies, OutboxDispatcher outbox, TimeProvider time, ILogger<GameLoop> logger) : BackgroundService
{
    private const int Hz = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / Hz));
        logger.LoopStarted(Hz);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                lobbies.Step(time.GetUtcNow().ToUnixTimeMilliseconds());
                outbox.Dispatch(lobbies.Drain(), stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LoopFailed(e);
            }
        }
    }
}