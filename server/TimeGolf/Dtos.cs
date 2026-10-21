// Jagged arrays keep track/bounce payloads compact as JSON arrays instead of objects.
#pragma warning disable S2368

namespace TimeGolf;

public sealed record LevelDto(
    int Seed,
    int Round,
    int Width,
    int Height,
    string Cells,
    int[] Heights,
    double[][] Walls,
    PillarDto[] Pillars,
    BumperDto[] Bumpers,
    WindmillDto[] Windmills,
    RectDto Start,
    double[][] Spawns,
    PointDto Hole,
    int Palette);

public sealed record PillarDto(double X, double Z, double W, double D);

public sealed record BumperDto(double X, double Z, double R);

public sealed record WindmillDto(double X, double Z, int Axis, double Phase);

public sealed record RectDto(double X, double Z, double W, double D);

public sealed record PointDto(double X, double Z);

public sealed record BallPosDto(string PlayerId, double X, double Z, bool Holed);

public sealed record TrackDto(string PlayerId, double[] Points);

public sealed record HitPlayDto(
    int Index,
    string PlayerId,
    double DirX,
    double DirZ,
    double Power,
    bool Void,
    BallPosDto[] Start,
    TrackDto[] Tracks,
    double[][] Bounces,
    BallPosDto[] End,
    double DurationMs,
    double WorldTimeMs);

public sealed record TurnDto(int Turn, int Turns, string PlayerId, long EndsAt);

public sealed record PlayerDto(
    string Id,
    string Name,
    string Color,
    int Points,
    int Hits,
    bool Holed,
    double? Distance,
    int? Score,
    bool Active,
    bool Spectator,
    bool RestartVote,
    bool Ready);

public sealed record ConfigDto(
    double BallRadius,
    double HoleRadius,
    double MaxHitSpeed,
    double ReplaySpeed,
    double PlaybackGapMs,
    double WindmillOmega,
    double WindmillBladeHalfAngle);

public sealed record LobbyInfoDto(string Id, string Name, bool Locked, int Players, int MaxPlayers, string Phase);

public sealed record WelcomeDto(
    LobbyInfoDto Lobby,
    MatchEndedDto? LastMatch,
    string PlayerId,
    long ServerTime,
    string Phase,
    long PhaseEndsAt,
    int Round,
    int Rounds,
    LevelDto? Level,
    string[] Order,
    BallPosDto[] Balls,
    HitPlayDto[] Timeline,
    TurnDto? Turn,
    PlayerDto[] Players,
    ConfigDto Config);

public sealed record TurnSkippedDto(int Turn, string PlayerId, string Reason);

public sealed record TimelinePlayedDto(int FromIndex, bool Replaced, HitPlayDto[] Hits);

public sealed record RoundStartedDto(int Round, int Rounds, LevelDto Level, string[] Order, BallPosDto[] Balls, PlayerDto[] Players);

public sealed record RoundRowDto(string PlayerId, string Name, string Color, int Hits, bool Holed, double? Distance, int Score, int Points);

public sealed record RoundEndedDto(int Round, RoundRowDto[] Rows);

public sealed record MatchRowDto(string PlayerId, string Name, string Color, int Points);

public sealed record MatchEndedDto(MatchRowDto[] Rows, string[] WinnerIds);

public sealed record PhaseDto(string Phase, long PhaseEndsAt, int Round, int Rounds);

public sealed record ActionRejectedDto(string Reason);

public sealed record AimDto(string PlayerId, int? ViewIndex, double DirX, double DirZ, double? Power);