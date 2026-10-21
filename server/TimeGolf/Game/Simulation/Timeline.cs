using TimeGolf.Game.Geometry;
using TimeGolf.Game.Levels;

namespace TimeGolf.Game.Simulation;

public sealed class Timeline
{
    private const double MsPerSecond = 1000;

    private readonly Level level;
    private readonly GameOptions options;
    private readonly Physics physics;
    private readonly List<Ball> balls;
    private readonly List<HitRecord> records = [];
    private readonly List<BallState[]> checkpoints;
    private readonly List<HitResult> results = [];
    private readonly List<Ball> stopped = [];
    private readonly List<Ball> holed = [];
    private readonly double dt;
    private readonly int trackEvery;
    private readonly int maxSteps;

    public Timeline(Level level, GameOptions options, IReadOnlyList<BallState> spawn)
    {
        this.level = level;
        this.options = options;
        physics = new Physics(options);
        balls = [.. spawn.Select(s => new Ball(s.PlayerId, s.Position) { Holed = s.Holed })];
        checkpoints = [[.. spawn]];
        var stepsPerSecond = options.TickRate * options.Substeps;
        dt = 1.0 / stepsPerSecond;
        trackEvery = stepsPerSecond / options.TrackHz;
        maxSteps = (int)Math.Round(options.MaxHitSimSeconds * stepsPerSecond);
    }

    public IReadOnlyList<HitRecord> Records => records;
    public IReadOnlyList<HitResult> Results => results;
    public IReadOnlyList<BallState> Present => checkpoints[^1];

    public HitResult Append(HitRecord record)
    {
        records.Add(record);
        Simulate(records.Count - 1);
        return results[^1];
    }

    public IReadOnlyList<HitResult> Replace(int index, Vec2 dir, double power)
    {
        records[index] = records[index] with { Dir = dir, Power = power };
        Simulate(index);
        return results[index..];
    }

    public int HitsOf(string playerId) => results.Count(r => r.Record.PlayerId == playerId && !r.Void);

    public void Simulate(int fromIndex)
    {
        checkpoints.RemoveRange(fromIndex + 1, checkpoints.Count - fromIndex - 1);
        results.RemoveRange(fromIndex, results.Count - fromIndex);
        Restore(checkpoints[fromIndex]);
        for (var i = fromIndex; i < records.Count; i++)
        {
            results.Add(Run(i));
            checkpoints.Add([.. results[^1].End]);
        }
    }

    private HitResult Run(int index)
    {
        var record = records[index];
        var start = checkpoints[index];
        var owner = balls.Find(b => b.Id == record.PlayerId);
        // The world clock only advances while records simulate, so record k starts where record k-1 ended.
        var worldMs = index == 0 ? 0 : results[index - 1].WorldTimeMs + results[index - 1].DurationMs;
        if (owner == null || owner.Holed)
        {
            return new HitResult(index, record, true, start, [], [], start, 0, worldMs);
        }

        var points = balls.Select(b => new List<Vec2> { b.Position }).ToList();
        var moved = new bool[balls.Count];
        List<TimedBounce> bounces = [];
        physics.Bounces.Clear();
        owner.Launch(record.Dir * (record.Power * options.MaxHitSpeed));

        var step = 0;
        while (balls.Exists(b => b.Moving) && step < maxSteps)
        {
            physics.Step(level, balls, dt, (worldMs / MsPerSecond) + (step * dt), stopped, holed);
            step++;
            stopped.Clear();
            holed.Clear();
            bounces.AddRange(physics.Bounces.Select(b => new TimedBounce(step * dt * MsPerSecond, b)));
            physics.Bounces.Clear();
            for (var k = 0; k < balls.Count; k++)
            {
                moved[k] |= balls[k].Moving;
            }

            if (step % trackEvery == 0)
            {
                AddPoints(points);
            }
        }

        foreach (var ball in balls)
        {
            ball.Velocity = Vec2.Zero;
            ball.Moving = false;
        }

        if (step % trackEvery != 0)
        {
            AddPoints(points);
        }

        var end = Snapshot();
        List<Track> tracks = [];
        for (var k = 0; k < balls.Count; k++)
        {
            if (moved[k] || end[k].Position != start[k].Position)
            {
                tracks.Add(new Track(balls[k].Id, points[k]));
            }
        }

        return new HitResult(index, record, false, start, tracks, bounces, end, step * dt * MsPerSecond, worldMs);
    }

    private void AddPoints(List<List<Vec2>> points)
    {
        for (var k = 0; k < balls.Count; k++)
        {
            points[k].Add(balls[k].Position);
        }
    }

    private BallState[] Snapshot() => [.. balls.Select(b => new BallState(b.Id, b.Position, b.Holed))];

    private void Restore(BallState[] state)
    {
        for (var k = 0; k < balls.Count; k++)
        {
            balls[k].Position = state[k].Position;
            balls[k].Holed = state[k].Holed;
            balls[k].Velocity = Vec2.Zero;
            balls[k].Moving = false;
        }
    }
}