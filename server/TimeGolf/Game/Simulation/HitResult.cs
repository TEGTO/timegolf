namespace TimeGolf.Game.Simulation;

public sealed record HitResult(
    int Index,
    HitRecord Record,
    bool Void,
    IReadOnlyList<BallState> Start,
    IReadOnlyList<Track> Tracks,
    IReadOnlyList<TimedBounce> Bounces,
    IReadOnlyList<BallState> End,
    double DurationMs,
    double WorldTimeMs);