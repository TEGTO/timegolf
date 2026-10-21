using TimeGolf.Game.Geometry;

namespace TimeGolf.Game.Simulation;

public sealed class Ball(string id, Vec2 position)
{
    public string Id { get; } = id;
    public Vec2 Position { get; set; } = position;
    public Vec2 Velocity { get; set; }
    public bool Moving { get; set; }
    public bool Holed { get; set; }

    public void Launch(Vec2 velocity)
    {
        Velocity = velocity;
        Moving = true;
    }
}