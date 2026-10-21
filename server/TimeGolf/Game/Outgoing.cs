namespace TimeGolf.Game;

// To = one connection; otherwise Recipients = connections joined when the event was emitted, so a late joiner
// never receives an event its WelcomeDto already reflects.
public sealed record Outgoing(Func<IGameClient, Task> Send, string? To, IReadOnlyList<string> Recipients)
{
    public static Outgoing Create<T>(Func<IGameClient, T, Task> send, T payload, string? to, IReadOnlyList<string> recipients) =>
        new(client => send(client, payload), to, recipients);
}