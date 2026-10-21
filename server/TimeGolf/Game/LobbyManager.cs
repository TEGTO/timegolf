namespace TimeGolf.Game;

// Lock order is always manager → match; matches never call back into the manager.
public sealed class LobbyManager(GameOptions options, Func<LobbySettings, Match> matchFactory, ILogger<LobbyManager> logger)
{
    // Lobby ids are GUIDs, so this key never collides with a lobby's batch.
    public const string MenuKey = "menu";

    private const int MaxNameLength = 24;

    private readonly Lock gate = new();
    private readonly List<Lobby> lobbies = [];
    private readonly Dictionary<string, Lobby> connections = [];
    private readonly HashSet<string> menu = [];
    private List<Outgoing> outbox = [];
    private LobbyInfoDto[] published = [];

    public LobbyInfoDto[] List(string connectionId)
    {
        lock (gate)
        {
            if (!connections.ContainsKey(connectionId))
            {
                menu.Add(connectionId);
            }

            return Infos();
        }
    }

    public string Create(string name, string? password, int maxPlayers, long now)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > MaxNameLength)
        {
            throw new LobbyException($"Lobby name must be 1-{MaxNameLength} characters.");
        }

        if (maxPlayers < 1 || maxPlayers > options.MaxLobbyPlayers)
        {
            throw new LobbyException($"Max players must be 1-{options.MaxLobbyPlayers}.");
        }

        var settings = new LobbySettings(Guid.NewGuid().ToString(), name, string.IsNullOrEmpty(password) ? null : password, maxPlayers);
        lock (gate)
        {
            lobbies.Add(new Lobby(matchFactory(settings)) { EmptySince = now });
        }

        logger.LobbyCreated(settings.Id, name);
        return settings.Id;
    }

    public WelcomeDto Join(string connectionId, string lobbyId, string? password, string name, string playerKey, long now)
    {
        lock (gate)
        {
            var lobby = lobbies.Find(l => l.Match.Settings.Id == lobbyId) ?? throw new LobbyException("Lobby not found");
            var settings = lobby.Match.Settings;
            if (settings.Password != null && settings.Password != password)
            {
                throw new LobbyException("Wrong password");
            }

            if (!lobby.Match.Knows(playerKey) && lobby.Match.Info().Players >= settings.MaxPlayers)
            {
                throw new LobbyException("Lobby is full");
            }

            var welcome = lobby.Match.Join(connectionId, name, playerKey, now);
            if (connections.TryGetValue(connectionId, out var previous) && previous != lobby)
            {
                previous.Match.Leave(connectionId, now);
            }

            connections[connectionId] = lobby;
            menu.Remove(connectionId);
            return welcome;
        }
    }

    public void Leave(string connectionId, long now)
    {
        lock (gate)
        {
            if (connections.Remove(connectionId, out var lobby))
            {
                lobby.Match.Leave(connectionId, now);
                // The reload grace only matters for disconnects; nobody comes back after Quit.
                if (lobby.Match.Info().Players == 0)
                {
                    Delete(lobby);
                }
            }

            menu.Add(connectionId);
            outbox.Add(Outgoing.Create(static (c, l) => c.Lobbies(l), Infos(), connectionId, []));
        }
    }

    public void Disconnect(string connectionId, long now)
    {
        lock (gate)
        {
            menu.Remove(connectionId);
            if (connections.Remove(connectionId, out var lobby))
            {
                lobby.Match.Disconnect(connectionId, now);
            }
        }
    }

    public Match? MatchOf(string connectionId)
    {
        lock (gate)
        {
            return connections.GetValueOrDefault(connectionId)?.Match;
        }
    }

    public void Act(string connectionId, Action<Match> action)
    {
        var match = MatchOf(connectionId);
        if (match != null)
        {
            action(match);
            return;
        }

        lock (gate)
        {
            outbox.Add(Outgoing.Create(static (c, r) => c.ActionRejected(r), new ActionRejectedDto("Not joined"), connectionId, []));
        }
    }

    public void Step(long now)
    {
        Lobby[] snapshot;
        lock (gate)
        {
            snapshot = [.. lobbies];
        }

        // Matches only take their own lock, so they can step concurrently outside the manager lock.
        Parallel.ForEach(snapshot, lobby => StepMatch(lobby.Match, now));

        lock (gate)
        {
            RemoveEmpty(now);
            var infos = Infos();
            if (infos.SequenceEqual(published))
            {
                return;
            }

            published = infos;
            if (menu.Count > 0)
            {
                outbox.Add(Outgoing.Create(static (c, l) => c.Lobbies(l), infos, null, [.. menu]));
            }
        }
    }

    public List<OutboxBatch> Drain()
    {
        lock (gate)
        {
            List<OutboxBatch> batches = [];
            if (outbox.Count > 0)
            {
                batches.Add(new OutboxBatch(MenuKey, outbox));
                outbox = [];
            }

            foreach (var match in lobbies.Select(l => l.Match))
            {
                var messages = match.Drain();
                if (messages.Count > 0)
                {
                    batches.Add(new OutboxBatch(match.Settings.Id, messages));
                }
            }

            return batches;
        }
    }

    // One failing match must not stall the others or skip the empty-lobby cleanup.
    private void StepMatch(Match match, long now)
    {
        try
        {
            match.Step(now);
        }
        catch (Exception e)
        {
            logger.MatchStepFailed(e, match.Settings.Id);
        }
    }

    private void RemoveEmpty(long now)
    {
        var emptyMs = (long)(options.EmptyLobbySeconds * 1000);
        foreach (var lobby in lobbies.ToList())
        {
            if (lobby.Match.Info().Players > 0)
            {
                lobby.EmptySince = null;
                continue;
            }

            lobby.EmptySince ??= now;
            if (now - lobby.EmptySince < emptyMs)
            {
                continue;
            }

            Delete(lobby);
        }
    }

    private void Delete(Lobby lobby)
    {
        lobbies.Remove(lobby);
        foreach (var connectionId in connections.Where(c => c.Value == lobby).Select(c => c.Key).ToList())
        {
            connections.Remove(connectionId);
        }

        logger.LobbyDeleted(lobby.Match.Settings.Id);
    }

    private LobbyInfoDto[] Infos() => [.. lobbies.Select(l => l.Match.Info())];

    private sealed class Lobby(Match match)
    {
        public Match Match { get; } = match;
        public long? EmptySince { get; set; }
    }
}