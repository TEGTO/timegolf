namespace TimeGolf.Game;

// Messages of one source (a lobby, or the menu) in emit order; batches with the same key must be sent in sequence.
public sealed record OutboxBatch(string Key, IReadOnlyList<Outgoing> Messages);