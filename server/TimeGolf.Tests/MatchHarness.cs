using TimeGolf.Game;
using TimeGolf.Game.Geometry;
using TimeGolf.Game.Levels;

namespace TimeGolf.Tests;

internal sealed class MatchHarness
{
    private readonly Dictionary<string, string> connectionOf = [];
    private readonly List<SentEvent> sent = [];

    public MatchHarness(GameOptions? options = null, Level? level = null)
    {
        Options = options ?? new GameOptions();
        var source = level ?? TestLevels.Corridor();
        Match = new Match(new LobbySettings("lobby", "Lobby", null, 16), Options, (_, _) => source, new Random(1));
    }

    public GameOptions Options { get; }

    public Match Match { get; }

    public long Now { get; private set; } = 1_000_000;

    public string CurrentConnection => connectionOf[Match.Turn!.PlayerId];

    public WelcomeDto Join(string connectionId, string? name = null, string? key = null)
    {
        var welcome = Match.Join(connectionId, name ?? connectionId, key ?? $"key-{connectionId}", Now);
        connectionOf[welcome.PlayerId] = connectionId;
        return welcome;
    }

    public string[] Start(params string[] connectionIds)
    {
        var ids = connectionIds.Select(c => Join(c).PlayerId).ToArray();
        foreach (var connectionId in connectionIds)
        {
            SetReady(connectionId, true);
        }

        return ids;
    }

    public string[] JoinAllThenStartRoundTwo(params string[] connectionIds)
    {
        var ids = Start(connectionIds);
        FinishRound();
        Sent().Clear();
        Advance((long)(Options.RoundResultsSeconds * 1000));
        return ids;
    }

    public string ConnectionOf(string playerId) => connectionOf[playerId];

    public void Hit(string connectionId, Vec2 dir, double power) => Match.Hit(connectionId, dir.X, dir.Z, power, Now);

    public void Replace(string connectionId, int hitIndex, Vec2 dir, double power) =>
        Match.Replace(connectionId, hitIndex, dir.X, dir.Z, power, Now);

    public void Skip(string connectionId) => Match.Skip(connectionId, Now);

    public void SetReady(string connectionId, bool ready) => Match.SetReady(connectionId, ready, Now);

    public void VoteRestart(string connectionId) => Match.VoteRestart(connectionId);

    public void Disconnect(string connectionId) => Match.Disconnect(connectionId, Now);

    public void Leave(string connectionId) => Match.Leave(connectionId, Now);

    public void Advance(long ms)
    {
        Now += ms;
        Match.Step(Now);
    }

    public void AdvanceTo(long time) => Advance(time - Now);

    public void WaitForTurn()
    {
        for (var i = 0; i < 10_000 && Match.Turn == null && Match.Phase == Phase.Playing; i++)
        {
            Advance(50);
        }
    }

    public void SkipTurns(int count)
    {
        for (var i = 0; i < count; i++)
        {
            WaitForTurn();
            Skip(CurrentConnection);
        }
    }

    public void FinishRound()
    {
        for (var i = 0; i < 10_000 && Match.Phase == Phase.Playing; i++)
        {
            if (Match.Turn != null)
            {
                Skip(CurrentConnection);
            }

            Advance(50);
        }
    }

    public List<SentEvent> Sent()
    {
        sent.AddRange(Match.Drain().Select(SentEvent.From));
        return sent;
    }

    public List<T> Events<T>(string method) => [.. Sent().Where(m => m.Method == method).Select(m => (T)m.Payload)];

    public List<string> Rejections(string connectionId) =>
        [.. Sent().Where(m => m.Method == nameof(IGameClient.ActionRejected) && m.To == connectionId).Select(m => ((ActionRejectedDto)m.Payload).Reason)];

    public Player PlayerOf(string playerId) => Match.Players.Single(p => p.Id == playerId);

    public static Level WithSpawns(params Vec2[] spawns)
    {
        var c = TestLevels.Corridor();
        return Level.Create(c.Seed, c.Round, c.Width, c.Height, c.Cells, c.Heights, c.Pillars, c.Bumpers, c.Windmills, c.Start, spawns, c.Hole, c.Palette);
    }
}