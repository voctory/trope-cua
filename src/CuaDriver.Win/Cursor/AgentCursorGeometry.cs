using System.Drawing;

namespace CuaDriver.Win.Cursor;

internal static class AgentCursorGeometry
{
    public const double RestingHeadingRadians = Math.PI / 4;

    private const float CursorTipOffset = 16f;
    private const float InitialOffscreenPosition = -200f;

    public static PointF InitialPosition(float scale) =>
        new(InitialOffscreenPosition * scale, InitialOffscreenPosition * scale);

    public static PointF VisualPositionForTip(PointF tip, float scale)
    {
        return VisualPositionForTip(tip, scale, RestingHeadingRadians);
    }

    public static PointF VisualPositionForTip(PointF tip, float scale, double heading)
    {
        var offset = CursorTipOffset * scale;
        return new PointF(
            tip.X + (float)(Math.Cos(heading) * offset),
            tip.Y + (float)(Math.Sin(heading) * offset));
    }

    public static PointF TipPointFromVisualPosition(PointF visualPosition, float scale)
    {
        return TipPointFromVisualPosition(visualPosition, scale, RestingHeadingRadians);
    }

    public static PointF TipPointFromVisualPosition(PointF visualPosition, float scale, double heading)
    {
        var offset = CursorTipOffset * scale;
        return new PointF(
            visualPosition.X - (float)(Math.Cos(heading) * offset),
            visualPosition.Y - (float)(Math.Sin(heading) * offset));
    }
}
