using System.Drawing;

namespace CuaDriver.Win.Cursor;

internal static class AgentCursorPathPlanner
{
    public static PlannedCursorPath Plan(
        double x0,
        double y0,
        double th0,
        double x1,
        double y1,
        double th1,
        double radius,
        double endVisualHeading,
        PointF targetPoint)
    {
        return PlanDubins(x0, y0, th0, x1, y1, th1, radius, endVisualHeading, targetPoint)
               ?? PlannedCursorPath.Linear(x0, y0, th0, x1, y1, th1, radius, endVisualHeading, targetPoint);
    }

    private static PlannedCursorPath? PlanDubins(
        double x0,
        double y0,
        double th0,
        double x1,
        double y1,
        double th1,
        double radius,
        double endVisualHeading,
        PointF targetPoint)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var distance = Hypot(dx, dy);
        if (distance <= 0.5)
            return null;

        var d = distance / radius;
        var theta = Mod2Pi(Math.Atan2(dy, dx));
        var a = Mod2Pi(th0 - theta);
        var b = Mod2Pi(th1 - theta);
        DubinsSolution? best = null;
        var bestLength = double.PositiveInfinity;

        foreach (var solver in DubinsSolvers)
        {
            var solution = solver(d, a, b);
            if (solution is not { } s || !double.IsFinite(s.Length) || s.Length < 0 || s.Length >= bestLength)
                continue;

            best = s;
            bestLength = s.Length;
        }

        return best is { } chosen
            ? PlannedCursorPath.Dubins(x0, y0, th0, radius, chosen, endVisualHeading, targetPoint, x1, y1, th1)
            : null;
    }

    private static readonly Func<double, double, double, DubinsSolution?>[] DubinsSolvers =
    [
        DubinsLsl,
        DubinsRsr,
        DubinsLsr,
        DubinsRsl,
        DubinsRlr,
        DubinsLrl
    ];

    private static DubinsSolution? DubinsLsl(double d, double a, double b)
    {
        var tmp0 = d + Math.Sin(a) - Math.Sin(b);
        var p2 = 2 + d * d - 2 * Math.Cos(a - b) + 2 * d * (Math.Sin(a) - Math.Sin(b));
        if (p2 < 0)
            return null;
        var tmp1 = Math.Atan2(Math.Cos(b) - Math.Cos(a), tmp0);
        return new DubinsSolution(Mod2Pi(-a + tmp1), Math.Sqrt(p2), Mod2Pi(b - tmp1), ['L', 'S', 'L']);
    }

    private static DubinsSolution? DubinsRsr(double d, double a, double b)
    {
        var tmp0 = d - Math.Sin(a) + Math.Sin(b);
        var p2 = 2 + d * d - 2 * Math.Cos(a - b) + 2 * d * (Math.Sin(b) - Math.Sin(a));
        if (p2 < 0)
            return null;
        var tmp1 = Math.Atan2(Math.Cos(a) - Math.Cos(b), tmp0);
        return new DubinsSolution(Mod2Pi(a - tmp1), Math.Sqrt(p2), Mod2Pi(-b + tmp1), ['R', 'S', 'R']);
    }

    private static DubinsSolution? DubinsLsr(double d, double a, double b)
    {
        var p2 = -2 + d * d + 2 * Math.Cos(a - b) + 2 * d * (Math.Sin(a) + Math.Sin(b));
        if (p2 < 0)
            return null;
        var p = Math.Sqrt(p2);
        var tmp1 = Math.Atan2(-Math.Cos(a) - Math.Cos(b), d + Math.Sin(a) + Math.Sin(b)) - Math.Atan2(-2, p);
        return new DubinsSolution(Mod2Pi(-a + tmp1), p, Mod2Pi(-Mod2Pi(b) + tmp1), ['L', 'S', 'R']);
    }

    private static DubinsSolution? DubinsRsl(double d, double a, double b)
    {
        var p2 = d * d - 2 + 2 * Math.Cos(a - b) - 2 * d * (Math.Sin(a) + Math.Sin(b));
        if (p2 < 0)
            return null;
        var p = Math.Sqrt(p2);
        var tmp1 = Math.Atan2(Math.Cos(a) + Math.Cos(b), d - Math.Sin(a) - Math.Sin(b)) - Math.Atan2(2, p);
        return new DubinsSolution(Mod2Pi(a - tmp1), p, Mod2Pi(b - tmp1), ['R', 'S', 'L']);
    }

    private static DubinsSolution? DubinsRlr(double d, double a, double b)
    {
        var tmp = (6 - d * d + 2 * Math.Cos(a - b) + 2 * d * (Math.Sin(a) - Math.Sin(b))) / 8;
        if (Math.Abs(tmp) > 1)
            return null;
        var p = Mod2Pi(2 * Math.PI - Math.Acos(tmp));
        var t = Mod2Pi(a - Math.Atan2(Math.Cos(a) - Math.Cos(b), d - Math.Sin(a) + Math.Sin(b)) + p / 2);
        return new DubinsSolution(t, p, Mod2Pi(a - b - t + p), ['R', 'L', 'R']);
    }

    private static DubinsSolution? DubinsLrl(double d, double a, double b)
    {
        var tmp = (6 - d * d + 2 * Math.Cos(a - b) + 2 * d * (Math.Sin(b) - Math.Sin(a))) / 8;
        if (Math.Abs(tmp) > 1)
            return null;
        var p = Mod2Pi(2 * Math.PI - Math.Acos(tmp));
        var t = Mod2Pi(-a + Math.Atan2(-Math.Cos(a) + Math.Cos(b), d + Math.Sin(a) - Math.Sin(b)) + p / 2);
        return new DubinsSolution(t, p, Mod2Pi(Mod2Pi(b) - a - t + p), ['L', 'R', 'L']);
    }

    private static double Mod2Pi(double value)
    {
        var tau = 2 * Math.PI;
        var result = value - tau * Math.Floor(value / tau);
        return result < 0 ? result + tau : result;
    }

    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);
}

internal readonly record struct CursorPathState(double X, double Y, double Heading);

internal readonly record struct DubinsSolution(double T, double P, double Q, char[] Types)
{
    public double Length => T + P + Q;
}

internal readonly record struct PlannedCursorPath(
    bool IsDubins,
    double Length,
    double EndVisualHeading,
    PointF TargetPoint,
    double X0,
    double Y0,
    double Th0,
    double Radius,
    double Seg1,
    double Seg2,
    double Seg3,
    char[] Types,
    double X1,
    double Y1,
    double Th1)
{
    public static PlannedCursorPath Linear(
        double x0,
        double y0,
        double th0,
        double x1,
        double y1,
        double th1,
        double radius,
        double endVisualHeading,
        PointF targetPoint)
    {
        return new PlannedCursorPath(false, Math.Max(1, Hypot(x1 - x0, y1 - y0)), endVisualHeading, targetPoint, x0, y0, th0, radius, 0, 0, 0, [], x1, y1, th1);
    }

    public static PlannedCursorPath Dubins(
        double x0,
        double y0,
        double th0,
        double radius,
        DubinsSolution solution,
        double endVisualHeading,
        PointF targetPoint,
        double x1,
        double y1,
        double th1)
    {
        return new PlannedCursorPath(true, solution.Length * radius, endVisualHeading, targetPoint, x0, y0, th0, radius, solution.T, solution.P, solution.Q, solution.Types, x1, y1, th1);
    }

    public CursorPathState Sample(double distance)
    {
        return IsDubins ? SampleDubins(distance) : SampleLinear(distance);
    }

    private CursorPathState SampleLinear(double distance)
    {
        var u = Math.Clamp(distance / Length, 0, 1);
        var diff = Th1 - Th0;
        while (diff > Math.PI) diff -= 2 * Math.PI;
        while (diff < -Math.PI) diff += 2 * Math.PI;
        return new CursorPathState(X0 + (X1 - X0) * u, Y0 + (Y1 - Y0) * u, Th0 + diff * u);
    }

    private CursorPathState SampleDubins(double inputDistance)
    {
        if (inputDistance <= 0)
            return new CursorPathState(X0, Y0, Th0);

        var l1 = Seg1 * Radius;
        var l2 = Seg2 * Radius;
        var l3 = Seg3 * Radius;
        var radius = Radius;
        var distance = Math.Min(inputDistance, l1 + l2 + l3);
        var x = X0;
        var y = Y0;
        var heading = Th0;

        void Advance(double length, char type)
        {
            if (type == 'S')
            {
                x += Math.Cos(heading) * length;
                y += Math.Sin(heading) * length;
                return;
            }

            var deltaHeading = length / radius * (type == 'L' ? 1 : -1);
            var perpendicular = type == 'L' ? Math.PI / 2 : -Math.PI / 2;
            var centerX = x + Math.Cos(heading + perpendicular) * radius;
            var centerY = y + Math.Sin(heading + perpendicular) * radius;
            var angle = Math.Atan2(y - centerY, x - centerX);
            x = centerX + Math.Cos(angle + deltaHeading) * radius;
            y = centerY + Math.Sin(angle + deltaHeading) * radius;
            heading += deltaHeading;
        }

        if (distance <= l1)
        {
            Advance(distance, Types[0]);
            return new CursorPathState(x, y, heading);
        }

        Advance(l1, Types[0]);
        if (distance <= l1 + l2)
        {
            Advance(distance - l1, Types[1]);
            return new CursorPathState(x, y, heading);
        }

        Advance(l2, Types[1]);
        Advance(distance - l1 - l2, Types[2]);
        return new CursorPathState(x, y, heading);
    }

    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);
}
