namespace TimeGolf;

public sealed record GameOptions
{
    public const string Section = "Game";

    public int RoundsPerMatch { get; init; } = 3;
    public int TurnsPerPlayer { get; init; } = 10;
    public double WindmillOmega { get; init; } = 1.6;
    public double WindmillBladeHalfAngleDeg { get; init; } = 18;
    public double WindmillBladeHalfAngle => WindmillBladeHalfAngleDeg * Math.PI / 180;
    public double TurnSeconds { get; init; } = 60;
    public double ReplaySpeed { get; init; } = 3;
    public double PlaybackGapSeconds { get; init; } = 0.4;
    public double MaxHitSimSeconds { get; init; } = 20;
    public double RoundResultsSeconds { get; init; } = 5;
    public double MatchResultsSeconds { get; init; } = 15;
    public double EmptyLobbySeconds { get; init; } = 60;
    public int MaxLobbyPlayers { get; init; } = 16;
    public double SpawnSpacing { get; init; } = 0.6;
    public double SpawnMargin { get; init; } = 0.05;

    public int TickRate { get; init; } = 60;
    public int Substeps { get; init; } = 2;
    public int TrackHz { get; init; } = 20;
    public double BallRadius { get; init; } = 0.14;
    public double MaxHitSpeed { get; init; } = 14;
    public double RollingDecel { get; init; } = 1.6;
    public double SandDecel { get; init; } = 6.5;
    public double SlopeAccel { get; init; } = 2.4;
    public double WallRestitution { get; init; } = 0.7;
    public double BallRestitution { get; init; } = 0.9;
    public double BumperRestitution { get; init; } = 1.3;
    public double RestSpeed { get; init; } = 0.06;
    public double HoleRadius { get; init; } = 0.22;
    public double HoleCaptureSpeed { get; init; } = 3.5;
}