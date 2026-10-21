namespace TimeGolf.Game;

internal static partial class LobbyManagerLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Lobby {LobbyId} created: {Name}")]
    public static partial void LobbyCreated(this ILogger logger, string lobbyId, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Empty lobby {LobbyId} deleted")]
    public static partial void LobbyDeleted(this ILogger logger, string lobbyId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lobby {LobbyId} step failed")]
    public static partial void MatchStepFailed(this ILogger logger, Exception exception, string lobbyId);
}