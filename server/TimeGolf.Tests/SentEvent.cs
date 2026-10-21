using TimeGolf.Game;

namespace TimeGolf.Tests;

internal sealed record SentEvent(string Method, object Payload, string? To, IReadOnlyList<string> Recipients)
{
    public static SentEvent From(Outgoing message)
    {
        var client = new RecordingClient();
        message.Send(client);
        var (method, payload) = client.Calls.Single();
        return new SentEvent(method, payload, message.To, message.Recipients);
    }

    public static List<SentEvent> From(IEnumerable<OutboxBatch> batches) => [.. batches.SelectMany(b => b.Messages).Select(From)];
}