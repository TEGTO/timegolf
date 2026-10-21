namespace TimeGolf.Game;

public sealed record LobbySettings(string Id, string Name, string? Password, int MaxPlayers);