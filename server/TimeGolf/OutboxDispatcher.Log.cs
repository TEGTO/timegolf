namespace TimeGolf;

internal static partial class OutboxDispatcherLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Sending a message of {BatchKey} failed")]
    public static partial void SendFailed(this ILogger logger, Exception exception, string batchKey);
}