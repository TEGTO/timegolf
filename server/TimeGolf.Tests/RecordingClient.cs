using System.Runtime.CompilerServices;

namespace TimeGolf.Tests;

internal sealed class RecordingClient : IGameClient
{
    private readonly Lock gate = new();
    private readonly List<(string Method, object Payload)> calls = [];

    public Func<Task> Delivery { get; set; } = () => Task.CompletedTask;

    public List<(string Method, object Payload)> Calls
    {
        get
        {
            lock (gate)
            {
                return [.. calls];
            }
        }
    }

    public Task TurnStarted(TurnDto turn) => Record(turn);

    public Task TurnSkipped(TurnSkippedDto skipped) => Record(skipped);

    public Task TimelinePlayed(TimelinePlayedDto played) => Record(played);

    public Task RoundStarted(RoundStartedDto started) => Record(started);

    public Task RoundEnded(RoundEndedDto ended) => Record(ended);

    public Task MatchEnded(MatchEndedDto ended) => Record(ended);

    public Task Phase(PhaseDto phase) => Record(phase);

    public Task Players(PlayerDto[] players) => Record(players);

    public Task ActionRejected(ActionRejectedDto rejected) => Record(rejected);

    public Task Lobbies(LobbyInfoDto[] lobbies) => Record(lobbies);

    public Task AimUpdated(AimDto aim) => Record(aim);

    private Task Record(object payload, [CallerMemberName] string method = "")
    {
        lock (gate)
        {
            calls.Add((method, payload));
        }

        return Delivery();
    }
}