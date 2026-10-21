namespace TimeGolf;

// Method names are the SignalR event names on the wire (PRD §7); renaming one breaks the client.
public interface IGameClient
{
    Task TurnStarted(TurnDto turn);

    Task TurnSkipped(TurnSkippedDto skipped);

    Task TimelinePlayed(TimelinePlayedDto played);

    Task RoundStarted(RoundStartedDto started);

    Task RoundEnded(RoundEndedDto ended);

    Task MatchEnded(MatchEndedDto ended);

    Task Phase(PhaseDto phase);

    Task Players(PlayerDto[] players);

    Task ActionRejected(ActionRejectedDto rejected);

    Task Lobbies(LobbyInfoDto[] lobbies);

    Task AimUpdated(AimDto aim);
}