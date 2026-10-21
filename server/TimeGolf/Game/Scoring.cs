namespace TimeGolf.Game;

public static class Scoring
{
    private const int Max = 100;
    private const int MissedPenalty = 2;

    // Holed in n hits earns 100/n; unholed earns 100/(hits + walking distance). Rounded up so 3 hits = 34.
    public static int Score(int hits, bool holed, double distance) =>
        (int)Math.Ceiling(Max / Math.Max(holed ? hits : hits + distance, 1e-3));

    // Missing a round (inactive, spectating, joined later) is worth a bit less than the round's worst score.
    public static int Missed(IEnumerable<int> scores) => scores.Any() ? Math.Max(0, scores.Min() - MissedPenalty) : 0;

    public static List<Player> Rank(IEnumerable<Player> players) =>
        [.. players.Where(p => p.Score != null).OrderByDescending(p => p.Score).ThenBy(p => p.JoinOrder)];

    public static string[] Winners(IEnumerable<Player> players)
    {
        var scored = players.Where(p => p.Active && p.Points > 0).ToList();
        if (scored.Count == 0)
        {
            return [];
        }

        var top = scored.Max(p => p.Points);
        return [.. scored.Where(p => p.Points == top).Select(p => p.Id)];
    }
}