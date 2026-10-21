using TimeGolf.Game.Geometry;

namespace TimeGolf.Game.Simulation;

public sealed record HitRecord(string PlayerId, Vec2 Dir, double Power);