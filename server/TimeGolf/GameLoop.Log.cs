namespace TimeGolf;

internal static partial class GameLoopLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Game loop started at {Hz} Hz")]
    public static partial void LoopStarted(this ILogger logger, int hz);

    [LoggerMessage(Level = LogLevel.Error, Message = "Game loop tick failed")]
    public static partial void LoopFailed(this ILogger logger, Exception exception);
}