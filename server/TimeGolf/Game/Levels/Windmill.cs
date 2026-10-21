using TimeGolf.Game.Geometry;

namespace TimeGolf.Game.Levels;

// (X, Z) = tunnel center; Axis = direction the ball travels through the tunnel.
public sealed record Windmill(double X, double Z, int Axis, double Phase)
{
    public const int AxisX = 0;
    public const int AxisZ = 1;

    // Base: 3 wide across travel x 1.2 deep along it, with a 1-wide tunnel through the middle (PRD §4.1).
    private const double HalfDepth = 0.6;
    private const double HalfWidth = 1.5;
    private const double TunnelHalfWidth = 0.5;
    private const double BlockWidth = HalfWidth - TunnelHalfWidth;
    private const double BladeSpacing = Math.PI / 2;

    public Pillar[] Blocks() =>
        Axis == AxisX
            ? [new Pillar(X - HalfDepth, Z - HalfWidth, 2 * HalfDepth, BlockWidth), new Pillar(X - HalfDepth, Z + TunnelHalfWidth, 2 * HalfDepth, BlockWidth)]
            : [new Pillar(X - HalfWidth, Z - HalfDepth, BlockWidth, 2 * HalfDepth), new Pillar(X + TunnelHalfWidth, Z - HalfDepth, BlockWidth, 2 * HalfDepth)];

    // Both tunnel mouths, since the ball may enter from either side.
    public Wall[] Gates() =>
        Axis == AxisX
            ? [new Wall(new Vec2(X - HalfDepth, Z - TunnelHalfWidth), new Vec2(X - HalfDepth, Z + TunnelHalfWidth)), new Wall(new Vec2(X + HalfDepth, Z - TunnelHalfWidth), new Vec2(X + HalfDepth, Z + TunnelHalfWidth))]
            : [new Wall(new Vec2(X - TunnelHalfWidth, Z - HalfDepth), new Vec2(X + TunnelHalfWidth, Z - HalfDepth)), new Wall(new Vec2(X - TunnelHalfWidth, Z + HalfDepth), new Vec2(X + TunnelHalfWidth, Z + HalfDepth))];

    // Four blades sit BladeSpacing apart, so one points straight down exactly when the angle is near a multiple of it.
    public bool IsClosed(double seconds, double omega, double bladeHalfAngle) =>
        Math.Abs(Math.IEEERemainder(Phase + (omega * seconds), BladeSpacing)) <= bladeHalfAngle;
}