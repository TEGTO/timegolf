using TimeGolf.Game.Geometry;
using TimeGolf.Game.Levels;

namespace TimeGolf.Game.Simulation;

public sealed class Physics(GameOptions options)
{
    // Below this impact speed a contact is a graze or resting push, not an audible bounce.
    private const double MinBounceSpeed = 0.4;

    // Any unit vector works when two centres coincide; it only has to be deterministic.
    private static readonly Vec2 FallbackNormal = new(1, 0);

    private readonly Dictionary<(int, int), List<Ball>> grid = [];
    private readonly List<Wall> closedGates = [];

    public List<Bounce> Bounces { get; } = [];

    // seconds = world clock at the start of this step; it only drives windmill gates.
    public void Step(Level level, IReadOnlyList<Ball> balls, double dt, double seconds, List<Ball> stopped, List<Ball> holed)
    {
        closedGates.Clear();
        for (var i = 0; i < level.Windmills.Count; i++)
        {
            if (level.Windmills[i].IsClosed(seconds, options.WindmillOmega, options.WindmillBladeHalfAngle))
            {
                closedGates.AddRange(level.GateWalls[i]);
            }
        }

        foreach (var ball in balls)
        {
            if (ball.Moving && !ball.Holed)
            {
                Integrate(level, ball, dt);
            }
        }

        CollideBalls(level, balls);

        foreach (var ball in balls)
        {
            if (!ball.Moving || ball.Holed)
            {
                continue;
            }

            CollideStatic(level, ball);
            var speed = ball.Velocity.Length;
            if ((ball.Position - level.Hole).Length < options.HoleRadius && speed < options.HoleCaptureSpeed)
            {
                ball.Holed = true;
                ball.Moving = false;
                holed.Add(ball);
            }
            else if (speed < options.RestSpeed && !level.SurfaceAt(ball.Position).IsSlope())
            {
                ball.Velocity = Vec2.Zero;
                ball.Moving = false;
                stopped.Add(ball);
            }
        }
    }

    private void Integrate(Level level, Ball ball, double dt)
    {
        var surface = level.SurfaceAt(ball.Position);
        var v = ball.Velocity + (surface.SlopeDirection() * (options.SlopeAccel * dt));
        var decel = (surface == Surface.Sand ? options.SandDecel : options.RollingDecel) * dt;
        var speed = v.Length;
        var newSpeed = Math.Min(speed - decel, options.MaxHitSpeed);
        ball.Velocity = newSpeed > 0 ? v * (newSpeed / speed) : Vec2.Zero;
        ball.Position += ball.Velocity * dt;
    }

    private void CollideBalls(Level level, IReadOnlyList<Ball> balls)
    {
        foreach (var list in grid.Values)
        {
            list.Clear();
        }

        foreach (var ball in balls.Where(b => !b.Holed))
        {
            var key = Cell(ball.Position);
            if (!grid.TryGetValue(key, out var list))
            {
                grid[key] = list = [];
            }

            list.Add(ball);
        }

        foreach (var a in balls.Where(b => !b.Holed))
        {
            var (cx, cz) = Cell(a.Position);
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (!grid.TryGetValue((cx + dx, cz + dz), out var list))
                    {
                        continue;
                    }

                    foreach (var b in list.Where(b => string.CompareOrdinal(b.Id, a.Id) > 0 && (a.Moving || b.Moving)))
                    {
                        Collide(level, a, b);
                    }
                }
            }
        }
    }

    private void Collide(Level level, Ball a, Ball b)
    {
        var d = b.Position - a.Position;
        var dist = d.Length;
        var min = 2 * options.BallRadius;
        if (dist >= min)
        {
            return;
        }

        var n = dist > Vec2.Epsilon ? d / dist : FallbackNormal;
        var push = n * ((min - dist) / 2);
        a.Position -= push;
        b.Position += push;

        var approach = Vec2.Dot(b.Velocity - a.Velocity, n);
        if (approach < 0)
        {
            var impulse = n * (-(1 + options.BallRestitution) * approach / 2);
            AddBounce(a.Position + (n * options.BallRadius), -approach, BounceKind.Ball);
            a.Launch(a.Velocity - impulse);
            b.Launch(b.Velocity + impulse);
        }

        // Moving balls are resolved against walls later this substep; resting ones would otherwise stay pushed into them.
        if (!a.Moving)
        {
            CollideStatic(level, a);
        }

        if (!b.Moving)
        {
            CollideStatic(level, b);
        }
    }

    private void CollideStatic(Level level, Ball ball)
    {
        foreach (var wall in level.Walls)
        {
            CollideWall(ball, wall);
        }

        foreach (var wall in level.PillarWalls)
        {
            CollideWall(ball, wall);
        }

        foreach (var wall in closedGates)
        {
            CollideWall(ball, wall);
        }

        foreach (var bumper in level.Bumpers)
        {
            CollideBumper(ball, bumper);
        }
    }

    private void CollideWall(Ball ball, Wall wall)
    {
        var closest = wall.ClosestPoint(ball.Position);
        var d = ball.Position - closest;
        var dist = d.Length;
        if (dist >= options.BallRadius)
        {
            return;
        }

        var (ax, az) = (wall.B - wall.A).Normalized;
        var n = dist > Vec2.Epsilon ? d / dist : new Vec2(-az, ax);
        ball.Position = closest + (n * options.BallRadius);
        Reflect(ball, n, options.WallRestitution, BounceKind.Wall);
    }

    private void CollideBumper(Ball ball, Bumper bumper)
    {
        var center = new Vec2(bumper.X, bumper.Z);
        var d = ball.Position - center;
        var dist = d.Length;
        var min = bumper.R + options.BallRadius;
        if (dist >= min)
        {
            return;
        }

        var n = dist > Vec2.Epsilon ? d / dist : FallbackNormal;
        ball.Position = center + (n * min);
        Reflect(ball, n, options.BumperRestitution, BounceKind.Bumper);
        if (ball.Velocity.Length > options.MaxHitSpeed)
        {
            ball.Velocity = ball.Velocity.Normalized * options.MaxHitSpeed;
        }
    }

    private void Reflect(Ball ball, Vec2 n, double restitution, BounceKind kind)
    {
        var vn = Vec2.Dot(ball.Velocity, n);
        if (vn < 0)
        {
            ball.Velocity -= n * ((1 + restitution) * vn);
            AddBounce(ball.Position, -vn, kind);
        }
    }

    private void AddBounce(Vec2 position, double speed, BounceKind kind)
    {
        if (speed >= MinBounceSpeed)
        {
            Bounces.Add(new Bounce(position, speed, kind));
        }
    }

    private static (int, int) Cell(Vec2 p) => ((int)Math.Floor(p.X), (int)Math.Floor(p.Z));
}