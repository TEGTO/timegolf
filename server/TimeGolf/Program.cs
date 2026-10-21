using System.Text.Json;
using Microsoft.Extensions.Options;
using TimeGolf;
using TimeGolf.Game;
using TimeGolf.Game.Levels;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<GameOptions>()
    .Bind(builder.Configuration.GetSection(GameOptions.Section))
    .Validate(
        o => o is { RoundsPerMatch: > 0, EmptyLobbySeconds: >= 0, MaxLobbyPlayers: > 0, TurnsPerPlayer: > 0, TurnSeconds: > 0, ReplaySpeed: > 0, MaxHitSimSeconds: > 0, TickRate: > 0, Substeps: > 0, TrackHz: > 0, BallRadius: > 0, MaxHitSpeed: > 0 }
            && (o.TickRate * o.Substeps) % o.TrackHz == 0
            && o.MaxHitSpeed / (o.TickRate * o.Substeps) < o.BallRadius,
        "Invalid Game options (step distance must stay below ball radius, steps per second divisible by TrackHz).")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<GameOptions>>().Value;
    return new LobbyManager(options, lobby => new Match(lobby, options, (seed, round) => LevelGenerator.Generate(seed, round, options), Random.Shared), sp.GetRequiredService<ILogger<LobbyManager>>());
});
builder.Services.AddSingleton<OutboxDispatcher>();
builder.Services.AddHostedService<GameLoop>();
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var app = builder.Build();

app.MapGet("/health", () => TypedResults.Ok("ok"));
app.MapHub<GameHub>("/hubs/game");

await app.RunAsync();