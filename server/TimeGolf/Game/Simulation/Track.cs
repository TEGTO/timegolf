using TimeGolf.Game.Geometry;

namespace TimeGolf.Game.Simulation;

public sealed record Track(string PlayerId, IReadOnlyList<Vec2> Points);