using TimeGolf.Game.Levels;
using TimeGolf.Game.Simulation;

namespace TimeGolf.Game;

public static class Mapping
{
    public static double R(double v) => Math.Round(v, 3);

    public static LevelDto ToDto(this Level level) =>
        new(
            level.Seed,
            level.Round,
            level.Width,
            level.Height,
            string.Concat(level.Cells.Select(SurfaceExtensions.ToChar)),
            level.Heights,
            [.. level.Walls.Select(w => new[] { R(w.A.X), R(w.A.Z), R(w.B.X), R(w.B.Z) })],
            [.. level.Pillars.Select(p => new PillarDto(R(p.X), R(p.Z), R(p.W), R(p.D)))],
            [.. level.Bumpers.Select(b => new BumperDto(R(b.X), R(b.Z), R(b.R)))],
            [.. level.Windmills.Select(w => new WindmillDto(R(w.X), R(w.Z), w.Axis, R(w.Phase)))],
            new RectDto(R(level.Start.X), R(level.Start.Z), R(level.Start.W), R(level.Start.D)),
            [.. level.Spawns.Select(s => new[] { R(s.X), R(s.Z) })],
            new PointDto(R(level.Hole.X), R(level.Hole.Z)),
            level.Palette);

    public static BallPosDto ToDto(this BallState ball) => new(ball.PlayerId, R(ball.Position.X), R(ball.Position.Z), ball.Holed);

    public static BallPosDto[] ToDto(this IEnumerable<BallState> balls) => [.. balls.Select(ToDto)];

    public static HitPlayDto ToDto(this HitResult hit) =>
        new(
            hit.Index,
            hit.Record.PlayerId,
            R(hit.Record.Dir.X),
            R(hit.Record.Dir.Z),
            R(hit.Record.Power),
            hit.Void,
            hit.Start.ToDto(),
            [.. hit.Tracks.Select(t => new TrackDto(t.PlayerId, [.. t.Points.SelectMany(p => new[] { R(p.X), R(p.Z) })]))],
            [.. hit.Bounces.Select(b => new[] { R(b.TMs), R(b.Bounce.Position.X), R(b.Bounce.Position.Z), R(b.Bounce.Speed), (double)b.Bounce.Kind })],
            hit.End.ToDto(),
            R(hit.DurationMs),
            R(hit.WorldTimeMs));

    public static PlayerDto ToDto(this Player player) =>
        new(
            player.Id,
            player.Name,
            player.Color,
            player.Points,
            player.Hits,
            player.Holed,
            player.Distance,
            player.Score,
            player.Active,
            player.Spectator,
            player.RestartVote,
            player.Ready);
}