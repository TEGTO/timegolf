using TimeGolf.Game.Geometry;

namespace TimeGolf.Game.Simulation;

public readonly record struct Bounce(Vec2 Position, double Speed, BounceKind Kind);