namespace TimeGolf.Game;

// Sliding window, so a burst straddling a second boundary still stays within the limit.
internal sealed class AimThrottle
{
    private const int MaxPerWindow = 20;
    private const long WindowMs = 1000;

    private readonly Dictionary<string, Queue<long>> accepted = [];

    public bool TryAcquire(string connectionId, long now)
    {
        if (!accepted.TryGetValue(connectionId, out var times))
        {
            times = new Queue<long>();
            accepted[connectionId] = times;
        }

        while (times.Count > 0 && now - times.Peek() >= WindowMs)
        {
            times.Dequeue();
        }

        if (times.Count >= MaxPerWindow)
        {
            return false;
        }

        times.Enqueue(now);
        return true;
    }

    public void Forget(string connectionId) => accepted.Remove(connectionId);
}