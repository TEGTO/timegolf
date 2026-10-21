using System.Globalization;
using TimeGolf.Game.Geometry;
using TimeGolf.Game.Levels;
using TimeGolf.Game.Simulation;

namespace TimeGolf.Game;

public sealed class Match(LobbySettings settings, GameOptions options, Func<int, int, Level> levelFactory, Random random)
{
    private const int MaxNameLength = 16;
    private const int MaxKeyLength = 64;
    private const double MsPerSecond = 1000;
    // Golden-angle hues squeezed past the grass band so no ball blends into the grass.
    private const double GoldenAngleDeg = 137.508;
    private const double FullTurnDeg = 360;
    private const double GrassHueFrom = 70;
    private const double GrassHueWidth = 100;

    private readonly Lock gate = new();
    private readonly Dictionary<string, Player> players = [];
    private readonly Dictionary<string, Player> connections = [];
    private readonly AimThrottle aimThrottle = new();
    private List<Outgoing> outbox = [];
    private List<Player> order = [];

    private Level? level;
    private LevelDto? levelDto;
    private Timeline? timeline;
    private int round;
    private long phaseEndsAt;
    private int seat;
    // Sum of missed-round scores of finished rounds, credited to players joining mid-match.
    private int missedPoints;
    private TurnDto? turn;
    private long nextTurnAt;
    private int nextJoin;
    private MatchEndedDto? lastMatch;

    public LobbySettings Settings => settings;
    internal Phase Phase { get; private set; } = Phase.Lobby;
    internal int Round => round;
    internal Level? Level => level;
    internal Timeline? Timeline => timeline;
    internal TurnDto? Turn => turn;
    internal long NextTurnAt => nextTurnAt;
    internal IReadOnlyList<Player> Order => order;
    internal IEnumerable<Player> Players => players.Values;

    public WelcomeDto Join(string connectionId, string name, string playerKey, long now)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > MaxNameLength)
        {
            throw new ArgumentException($"Name must be 1-{MaxNameLength} characters.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(playerKey) || playerKey.Length > MaxKeyLength)
        {
            throw new ArgumentException("Invalid player key.", nameof(playerKey));
        }

        lock (gate)
        {
            if (connections.Remove(connectionId, out var previous) && previous.Key != playerKey)
            {
                Drop(previous);
            }

            if (!players.TryGetValue(playerKey, out var player))
            {
                player = new Player(Guid.NewGuid().ToString(), playerKey, UniqueName(name, null), ColorFor(nextJoin), nextJoin++)
                {
                    Points = missedPoints
                };
                players[playerKey] = player;
            }
            else
            {
                player.Name = UniqueName(name, player);
            }

            // Same key from a new connection (reload, second tab) takes the player over.
            if (player.ConnectionId != null)
            {
                connections.Remove(player.ConnectionId);
                aimThrottle.Forget(player.ConnectionId);
            }

            player.ConnectionId = connectionId;
            player.Active = true;
            player.Spectator = Phase == Phase.Playing && !order.Contains(player) && !TryJoinRound(player);
            EmitPlayers();
            if (turn?.PlayerId == player.Id)
            {
                turn = turn with { EndsAt = now + Ms(options.TurnSeconds) };
                Emit(static (c, t) => c.TurnStarted(t), turn);
            }

            connections[connectionId] = player;
            return new WelcomeDto(
                LobbyInfo(),
                lastMatch,
                player.Id,
                now,
                Phase.ToString(),
                phaseEndsAt,
                round,
                options.RoundsPerMatch,
                levelDto,
                OrderIds(),
                timeline?.Present.ToDto() ?? [],
                [.. timeline?.Results.Select(r => r.ToDto()) ?? []],
                turn,
                PlayerDtos(),
                new ConfigDto(
                    options.BallRadius,
                    options.HoleRadius,
                    options.MaxHitSpeed,
                    options.ReplaySpeed,
                    Ms(options.PlaybackGapSeconds),
                    options.WindmillOmega,
                    options.WindmillBladeHalfAngle));
        }
    }

    public void Hit(string connectionId, double dirX, double dirZ, double power, long now) =>
        Act(connectionId, player => TryHit(player, dirX, dirZ, power, now));

    public void Replace(string connectionId, int hitIndex, double dirX, double dirZ, double power, long now) =>
        Act(connectionId, player => TryReplace(player, hitIndex, dirX, dirZ, power, now));

    public void Skip(string connectionId, long now) =>
        Act(connectionId, player =>
        {
            Skipped(player, "skip", now);
            return null;
        });

    // Transient relay: silently dropped when invalid, since a stale aim racing a turn change is expected.
    public void Aim(string connectionId, int? viewIndex, double dirX, double dirZ, double? power, long now)
    {
        lock (gate)
        {
            if (!connections.TryGetValue(connectionId, out var player)
                || Phase != Phase.Playing
                || turn?.PlayerId != player.Id
                || !IsValidAim(viewIndex, dirX, dirZ, power)
                || !aimThrottle.TryAcquire(connectionId, now))
            {
                return;
            }

            outbox.Add(Outgoing.Create(
                static (c, a) => c.AimUpdated(a),
                new AimDto(player.Id, viewIndex, dirX, dirZ, power),
                null,
                [.. connections.Keys.Where(c => c != connectionId)]));
        }
    }

    public void SetReady(string connectionId, bool ready, long now)
    {
        lock (gate)
        {
            if (Phase != Phase.Lobby || !connections.TryGetValue(connectionId, out var player))
            {
                return;
            }

            player.Ready = ready;
            StartIfAllReady(now);
        }
    }

    public void VoteRestart(string connectionId)
    {
        lock (gate)
        {
            if (!connections.TryGetValue(connectionId, out var player) || Phase == Phase.Lobby)
            {
                return;
            }

            player.RestartVote = !player.RestartVote;
            var active = players.Values.Where(p => p.Active).ToList();
            if (active.Count(p => p.RestartVote) > active.Count / 2)
            {
                EnterLobby();
            }
            else
            {
                EmitPlayers();
            }
        }
    }

    // A plain disconnect (page reload) keeps the game even with nobody left; the empty-lobby cleanup ends it.
    public void Disconnect(string connectionId, long now) => Remove(connectionId, false, now);

    public void Leave(string connectionId, long now) => Remove(connectionId, true, now);

    public LobbyInfoDto Info()
    {
        lock (gate)
        {
            return LobbyInfo();
        }
    }

    public bool Knows(string playerKey)
    {
        lock (gate)
        {
            return players.ContainsKey(playerKey);
        }
    }

    public void Step(long now)
    {
        lock (gate)
        {
            // Frozen while everyone is away so a reconnect resumes the same turn.
            if (!players.Values.Any(p => p.Active))
            {
                return;
            }

            switch (Phase)
            {
                case Phase.Playing when turn != null:
                    var current = order.Find(p => p.Id == turn.PlayerId)!;
                    if (!current.Active)
                    {
                        Skipped(current, "inactive", now);
                    }
                    else if (now >= turn.EndsAt)
                    {
                        Skipped(current, "timeout", now);
                    }

                    break;
                case Phase.Playing when now >= nextTurnAt:
                    NextTurn(now);
                    break;
                case Phase.RoundResults when now >= phaseEndsAt:
                    if (round < options.RoundsPerMatch)
                    {
                        StartRound(round + 1, now);
                    }
                    else
                    {
                        EndMatch(now);
                    }

                    break;
                case Phase.MatchResults when now >= phaseEndsAt:
                    EnterLobby();
                    break;
            }
        }
    }

    public List<Outgoing> Drain()
    {
        lock (gate)
        {
            var drained = outbox;
            outbox = [];
            return drained;
        }
    }

    private void Act(string connectionId, Func<Player, string?> action)
    {
        lock (gate)
        {
            string? reason;
            if (!connections.TryGetValue(connectionId, out var player))
            {
                reason = "Not joined";
            }
            else if (Phase != Phase.Playing)
            {
                reason = "Round is not in play";
            }
            else if (turn?.PlayerId != player.Id)
            {
                reason = "Not your turn";
            }
            else
            {
                reason = action(player);
            }

            if (reason != null)
            {
                Emit(static (c, r) => c.ActionRejected(r), new ActionRejectedDto(reason), connectionId);
            }
        }
    }

    private string? TryHit(Player player, double dirX, double dirZ, double power, long now)
    {
        if (!TryShot(dirX, dirZ, power, out var dir, out var clamped))
        {
            return "Invalid hit";
        }

        if (player.Holed)
        {
            return "Ball is holed";
        }

        var result = timeline!.Append(new HitRecord(player.Id, dir, clamped));
        Played(result.Index, false, [result], result.DurationMs, now);
        return null;
    }

    private string? TryReplace(Player player, int hitIndex, double dirX, double dirZ, double power, long now)
    {
        if (!TryShot(dirX, dirZ, power, out var dir, out var clamped))
        {
            return "Invalid hit";
        }

        if (hitIndex < 0 || hitIndex >= timeline!.Records.Count)
        {
            return "Hit not found";
        }

        if (timeline.Records[hitIndex].PlayerId != player.Id)
        {
            return "Not your hit";
        }

        if (timeline.Results[hitIndex].Void)
        {
            return "Hit is void";
        }

        var hits = timeline.Replace(hitIndex, dir, clamped);
        var playbackMs = hits[0].DurationMs + (hits.Skip(1).Sum(h => h.DurationMs) / options.ReplaySpeed);
        Played(hitIndex, true, hits, playbackMs, now);
        return null;
    }

    private static bool TryShot(double dirX, double dirZ, double power, out Vec2 dir, out double clamped)
    {
        dir = new Vec2(dirX, dirZ).Normalized;
        clamped = Math.Min(power, 1);
        return double.IsFinite(dirX) && double.IsFinite(dirZ) && double.IsFinite(power) && power > 0 && dir != Vec2.Zero;
    }

    private bool IsValidAim(int? viewIndex, double dirX, double dirZ, double? power) =>
        double.IsFinite(dirX)
        && double.IsFinite(dirZ)
        && (power == null || double.IsFinite(power.Value))
        && (viewIndex == null || (viewIndex >= 0 && viewIndex < timeline!.Records.Count));

    private void Played(int fromIndex, bool replaced, IReadOnlyList<HitResult> hits, double playbackMs, long now)
    {
        Emit(static (c, t) => c.TimelinePlayed(t), new TimelinePlayedDto(fromIndex, replaced, [.. hits.Select(h => h.ToDto())]));
        UpdateStats();
        EmitPlayers();
        EndTurn(now + (long)Math.Ceiling(playbackMs) + Ms(options.PlaybackGapSeconds));
    }

    private void Skipped(Player player, string reason, long now)
    {
        Emit(static (c, t) => c.TurnSkipped(t), new TurnSkippedDto(Rotation, player.Id, reason));
        EndTurn(now + Ms(options.PlaybackGapSeconds));
    }

    private void EndTurn(long nextAt)
    {
        turn = null;
        seat++;
        nextTurnAt = nextAt;
    }

    // Turn number shown to players = rotation through the order, so each player gets TurnsPerPlayer turns.
    private int Rotation => (seat / Math.Max(order.Count, 1)) + 1;

    private void NextTurn(long now)
    {
        if (order.Where(p => p.Active).All(p => p.Holed))
        {
            EndRound(now);
            return;
        }

        // Holed players sit out their seat in the rotation; a replay that un-holes them brings them back.
        while (order[seat % order.Count].Holed && seat < options.TurnsPerPlayer * order.Count)
        {
            seat++;
        }

        if (seat >= options.TurnsPerPlayer * order.Count)
        {
            EndRound(now);
            return;
        }

        var player = order[seat % order.Count];
        if (!player.Active)
        {
            Skipped(player, "inactive", now);
            return;
        }

        turn = new TurnDto(Rotation, options.TurnsPerPlayer, player.Id, now + Ms(options.TurnSeconds));
        Emit(static (c, t) => c.TurnStarted(t), turn);
    }

    private void Remove(string connectionId, bool leaving, long now)
    {
        lock (gate)
        {
            if (!connections.Remove(connectionId, out var player))
            {
                return;
            }

            aimThrottle.Forget(connectionId);
            Drop(player);
            if (Phase == Phase.Lobby)
            {
                StartIfAllReady(now);
            }
            else if (leaving && !players.Values.Any(p => p.Active))
            {
                EnterLobby();
            }
            else
            {
                EmitPlayers();
            }
        }
    }

    private void StartIfAllReady(long now)
    {
        var active = players.Values.Where(p => p.Active).ToList();
        if (active.Count > 0 && active.TrueForAll(p => p.Ready))
        {
            StartMatch(now);
        }
        else
        {
            EmitPlayers();
        }
    }

    private void StartMatch(long now)
    {
        ResetPlayers();
        StartRound(1, now);
    }

    private void ResetPlayers()
    {
        missedPoints = 0;
        foreach (var inactive in players.Values.Where(p => !p.Active).ToList())
        {
            players.Remove(inactive.Key);
        }

        foreach (var player in players.Values)
        {
            player.Points = 0;
            player.RestartVote = false;
            player.Ready = false;
            player.Spectator = false;
        }
    }

    private void StartRound(int number, long now)
    {
        var active = players.Values.Where(p => p.Active).OrderBy(p => p.JoinOrder).ToList();
        if (active.Count == 0)
        {
            EnterLobby();
            return;
        }

        round = number;
        level = levelFactory(random.Next(), number);
        levelDto = level.ToDto();
        var shift = (number - 1) % active.Count;
        order = [.. active.Skip(shift), .. active.Take(shift)];
        timeline = NewTimeline();
        seat = 0;
        turn = null;
        foreach (var player in players.Values)
        {
            player.Spectator = false;
        }

        UpdateStats();
        Enter(Phase.Playing, 0);
        Emit(static (c, r) => c.RoundStarted(r), new RoundStartedDto(round, options.RoundsPerMatch, levelDto, OrderIds(), timeline.Present.ToDto(), PlayerDtos()));
        EmitPlayers();
        NextTurn(now);
    }

    // Until the first hit is recorded nothing can be rewritten, so late joiners can still enter the round fairly.
    private bool TryJoinRound(Player player)
    {
        if (Phase != Phase.Playing || timeline!.Records.Count > 0)
        {
            return false;
        }

        order.Add(player);
        timeline = NewTimeline();
        UpdateStats();
        Emit(static (c, r) => c.RoundStarted(r), new RoundStartedDto(round, options.RoundsPerMatch, levelDto!, OrderIds(), timeline.Present.ToDto(), PlayerDtos()));
        return true;
    }

    private Timeline NewTimeline() =>
        new(level!, options, [.. order.Select((p, i) => new BallState(p.Id, level!.Spawns[i % level.Spawns.Count], false))]);

    private void EndRound(long now)
    {
        var played = Scoring.Rank(order.Where(p => p.Active));
        var missed = Scoring.Missed(played.Select(p => p.Score!.Value));
        missedPoints += missed;
        foreach (var player in players.Values.Where(p => (p.Active || order.Contains(p)) && !played.Contains(p)))
        {
            player.Points += missed;
        }

        var rows = played
            .Select(p =>
            {
                p.Points += p.Score!.Value;
                return new RoundRowDto(p.Id, p.Name, p.Color, p.Hits, p.Holed, p.Distance, p.Score.Value, p.Score.Value);
            })
            .ToArray();
        Emit(static (c, r) => c.RoundEnded(r), new RoundEndedDto(round, rows));
        Enter(Phase.RoundResults, now + Ms(options.RoundResultsSeconds));
        EmitPlayers();
    }

    private void EndMatch(long now)
    {
        var rows = players.Values
            .Where(p => p.Active)
            .OrderByDescending(p => p.Points)
            .ThenBy(p => p.JoinOrder)
            .Select(p => new MatchRowDto(p.Id, p.Name, p.Color, p.Points));
        lastMatch = new MatchEndedDto([.. rows], Scoring.Winners(players.Values));
        Emit(static (c, m) => c.MatchEnded(m), lastMatch);
        Enter(Phase.MatchResults, now + Ms(options.MatchResultsSeconds));
    }

    // Inactive players are purged: in the ready room a disconnect means leaving.
    private void EnterLobby()
    {
        level = null;
        levelDto = null;
        timeline = null;
        order = [];
        round = 0;
        turn = null;
        seat = 0;
        ResetPlayers();
        UpdateStats();
        Enter(Phase.Lobby, 0);
        EmitPlayers();
    }

    private void Enter(Phase phase, long endsAt)
    {
        Phase = phase;
        phaseEndsAt = endsAt;
        Emit(static (c, p) => c.Phase(p), new PhaseDto(Phase.ToString(), phaseEndsAt, round, options.RoundsPerMatch));
    }

    private void UpdateStats()
    {
        foreach (var player in players.Values)
        {
            player.Hits = 0;
            player.Holed = false;
            player.Distance = null;
            player.Score = null;
        }

        if (timeline == null)
        {
            return;
        }

        var present = timeline.Present;
        for (var k = 0; k < order.Count; k++)
        {
            var player = order[k];
            player.Hits = timeline.HitsOf(player.Id);
            player.Holed = present[k].Holed;
            player.Distance = player.Holed ? null : Math.Round(level!.DistanceTo(present[k].Position), 1);
            player.Score = Scoring.Score(player.Hits, player.Holed, player.Distance ?? 0);
        }
    }

    private void Drop(Player player)
    {
        if (Phase == Phase.Lobby)
        {
            players.Remove(player.Key);
            return;
        }

        player.Active = false;
        player.ConnectionId = null;
        player.RestartVote = false;
    }

    private LobbyInfoDto LobbyInfo() =>
        new(settings.Id, settings.Name, settings.Password != null, players.Values.Count(p => p.Active), settings.MaxPlayers, Phase.ToString());

    private string UniqueName(string name, Player? self)
    {
        bool Taken(string candidate) => players.Values.Any(p => p != self && p.Active && p.Name == candidate);
        if (!Taken(name))
        {
            return name;
        }

        var suffix = 2;
        while (Taken($"{name}#{suffix}"))
        {
            suffix++;
        }

        return $"{name}#{suffix}";
    }

    private static string ColorFor(int index)
    {
        var hue = index * GoldenAngleDeg % FullTurnDeg * (FullTurnDeg - GrassHueWidth) / FullTurnDeg;
        return string.Create(CultureInfo.InvariantCulture, $"hsl({(hue < GrassHueFrom ? hue : hue + GrassHueWidth):0.#}, 70%, 55%)");
    }

    private static long Ms(double seconds) => (long)(seconds * MsPerSecond);

    private string[] OrderIds() => [.. order.Select(p => p.Id)];

    private PlayerDto[] PlayerDtos() => [.. players.Values.OrderBy(p => p.JoinOrder).Select(p => p.ToDto())];

    private void EmitPlayers() => Emit(static (c, p) => c.Players(p), PlayerDtos());

    // Payload is passed separately (and lambdas are static) so a send never reads match state after the lock is released.
    private void Emit<T>(Func<IGameClient, T, Task> send, T payload, string? to = null) =>
        outbox.Add(Outgoing.Create(send, payload, to, to == null ? [.. connections.Keys] : []));
}