using Microsoft.AspNetCore.SignalR;
using TimeGolf.Game;

namespace TimeGolf;

public sealed class GameHub(LobbyManager lobbies, TimeProvider time) : Hub<IGameClient>
{
    public LobbyInfoDto[] ListLobbies() => lobbies.List(Context.ConnectionId);

    public string CreateLobby(string name, string? password, int maxPlayers) => Guard(() => lobbies.Create(name, password, maxPlayers, Now));

    public WelcomeDto Join(string lobbyId, string? password, string name, string playerKey) =>
        Guard(() => lobbies.Join(Context.ConnectionId, lobbyId, password, name, playerKey, Now));

    public void Leave() => lobbies.Leave(Context.ConnectionId, Now);

    public void SetReady(bool ready) => lobbies.MatchOf(Context.ConnectionId)?.SetReady(Context.ConnectionId, ready, Now);

    public void Hit(double dirX, double dirZ, double power) =>
        lobbies.Act(Context.ConnectionId, m => m.Hit(Context.ConnectionId, dirX, dirZ, power, Now));

    public void Replace(int hitIndex, double dirX, double dirZ, double power) =>
        lobbies.Act(Context.ConnectionId, m => m.Replace(Context.ConnectionId, hitIndex, dirX, dirZ, power, Now));

    public void Skip() => lobbies.Act(Context.ConnectionId, m => m.Skip(Context.ConnectionId, Now));

    public void Aim(int? viewIndex, double dirX, double dirZ, double? power) =>
        lobbies.MatchOf(Context.ConnectionId)?.Aim(Context.ConnectionId, viewIndex, dirX, dirZ, power, Now);

    public void VoteRestart() => lobbies.MatchOf(Context.ConnectionId)?.VoteRestart(Context.ConnectionId);

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        lobbies.Disconnect(Context.ConnectionId, Now);
        return base.OnDisconnectedAsync(exception);
    }

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception e) when (e is ArgumentException or LobbyException)
        {
            throw new HubException(e.Message);
        }
    }

    private long Now => time.GetUtcNow().ToUnixTimeMilliseconds();
}