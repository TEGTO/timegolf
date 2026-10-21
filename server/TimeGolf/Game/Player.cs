namespace TimeGolf.Game;

public sealed class Player(string id, string key, string name, string color, int joinOrder)
{
    public string Id { get; } = id;
    public string Key { get; } = key;
    public string Name { get; set; } = name;
    public string Color { get; } = color;
    public int JoinOrder { get; } = joinOrder;
    public string? ConnectionId { get; set; }
    public bool Active { get; set; }
    public bool Spectator { get; set; }
    public int Points { get; set; }
    public int Hits { get; set; }
    public bool Holed { get; set; }
    public double? Distance { get; set; }
    public int? Score { get; set; }
    public bool RestartVote { get; set; }
    public bool Ready { get; set; }
}