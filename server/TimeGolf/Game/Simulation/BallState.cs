using TimeGolf.Game.Geometry;

namespace TimeGolf.Game.Simulation;

public readonly record struct BallState(string PlayerId, Vec2 Position, bool Holed);