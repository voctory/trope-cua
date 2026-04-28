using System.Drawing;

namespace CuaDriver.Win.Cursor;

internal static class AgentCursorPathPlanner
{
    private const double MotionEpsilon = 0.001;
    internal const double DirectDistanceThreshold = 18;
    internal const double ShortMoveDirectDistanceThreshold = 72;
    internal const double OutOfBoundsPenalty = 45;
    internal const double AngleChangeEnergyWeight = 320;
    internal const double MaxAngleChangeWeight = 140;
    internal const double TotalTurnWeight = 180;

    public static PlannedCursorPath Plan(
        double x0,
        double y0,
        double th0,
        double x1,
        double y1,
        double th1,
        double radius,
        double endVisualHeading,
        PointF targetPoint,
        RectangleF? bounds = null)
    {
        var start = new PointF((float)x0, (float)y0);
        var end = new PointF((float)x1, (float)y1);
        return PlannedCursorPath.Bezier(start, end, th0, th1, endVisualHeading, targetPoint, bounds);
    }
}

internal readonly record struct CursorPathState(double X, double Y, double Heading);

internal readonly record struct CursorMotionSegment(PointF Start, PointF Control1, PointF Control2, PointF End, double Length)
{
    public PointF PointAt(double progress)
    {
        var u = Math.Clamp(progress, 0, 1);
        var inverse = 1 - u;
        var a = inverse * inverse * inverse;
        var b = 3 * inverse * inverse * u;
        var c = 3 * inverse * u * u;
        var d = u * u * u;
        return new PointF(
            (float)(a * Start.X + b * Control1.X + c * Control2.X + d * End.X),
            (float)(a * Start.Y + b * Control1.Y + c * Control2.Y + d * End.Y));
    }

    public PointF TangentAt(double progress)
    {
        var u = Math.Clamp(progress, 0, 1);
        var inverse = 1 - u;
        var a = 3 * inverse * inverse;
        var b = 6 * inverse * u;
        var c = 3 * u * u;
        return new PointF(
            (float)(a * (Control1.X - Start.X) + b * (Control2.X - Control1.X) + c * (End.X - Control2.X)),
            (float)(a * (Control1.Y - Start.Y) + b * (Control2.Y - Control1.Y) + c * (End.Y - Control2.Y)));
    }

    public static CursorMotionSegment Create(PointF start, PointF control1, PointF control2, PointF end)
    {
        var segment = new CursorMotionSegment(start, control1, control2, end, 0);
        return segment with { Length = MeasureLength(segment) };
    }

    private static double MeasureLength(CursorMotionSegment segment)
    {
        var length = 0.0;
        var previous = segment.Start;
        for (var i = 1; i <= 32; i++)
        {
            var point = segment.PointAt(i / 32.0);
            length += Hypot(point.X - previous.X, point.Y - previous.Y);
            previous = point;
        }

        return length;
    }

    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);
}

internal readonly record struct CursorMotionMeasurement(
    double Length,
    double AngleChangeEnergy,
    double MaxAngleChange,
    double TotalTurn,
    bool StaysInBounds);

internal readonly record struct PlannedCursorPath(
    double Length,
    double StraightLineDistance,
    double EndVisualHeading,
    PointF TargetPoint,
    CursorMotionSegment[] Segments)
{
    public static PlannedCursorPath Bezier(
        PointF start,
        PointF end,
        double startHeading,
        double endHeading,
        double endVisualHeading,
        PointF targetPoint,
        RectangleF? bounds)
    {
        var delta = new PointF(end.X - start.X, end.Y - start.Y);
        var distance = Hypot(delta.X, delta.Y);
        var segments = distance < 4
            ? DirectSegments(start, end, delta, distance, startHeading, endHeading)
            : CandidateSegments(start, end, delta, distance, startHeading, endHeading, bounds);
        var length = Math.Max(1, segments.Sum(segment => segment.Length));
        return new PlannedCursorPath(length, distance, endVisualHeading, targetPoint, segments);
    }

    public CursorPathState Sample(double distance)
    {
        if (Segments.Length == 0)
            return new CursorPathState(TargetPoint.X, TargetPoint.Y, EndVisualHeading);

        if (distance <= 0)
            return StateFor(Segments[0], 0);

        var targetLength = Math.Min(distance, Length);
        var accumulated = 0.0;
        for (var i = 0; i < Segments.Length; i++)
        {
            var segment = Segments[i];
            var segmentLength = Math.Max(segment.Length, 0.001);
            if (targetLength <= accumulated + segmentLength || i == Segments.Length - 1)
            {
                var progress = (targetLength - accumulated) / segmentLength;
                return StateFor(segment, progress);
            }

            accumulated += segmentLength;
        }

        return StateFor(Segments[^1], 1);
    }

    private static CursorPathState StateFor(CursorMotionSegment segment, double progress)
    {
        var point = segment.PointAt(progress);
        var tangent = segment.TangentAt(progress);
        var heading = Math.Atan2(tangent.Y, tangent.X);
        return new CursorPathState(point.X, point.Y, heading);
    }

    private static CursorMotionSegment[] CandidateSegments(
        PointF start,
        PointF end,
        PointF delta,
        double distance,
        double startHeading,
        double endHeading,
        RectangleF? bounds)
    {
        var unit = new PointF((float)(delta.X / distance), (float)(delta.Y / distance));
        var normal = new PointF(-unit.Y, unit.X);
        var startUnit = UnitFor(startHeading);
        var endUnit = UnitFor(endHeading);
        var baseControl = Math.Min(Math.Min(640, distance * 0.9), Math.Max(Math.Min(48, distance * 0.33), distance * 0.41960295031576633));
        var baseArc = Math.Min(Math.Min(440, distance * 0.65), Math.Max(Math.Min(18, distance * 0.18), distance * 0.2765523188064277));
        var direct = DirectSegments(start, end, delta, distance, startHeading, endHeading);
        if (distance <= AgentCursorPathPlanner.DirectDistanceThreshold)
            return direct;

        var preferredSign = delta.X >= 0 ? 1.0 : -1.0;
        var candidates = new List<(CursorMotionSegment[] Segments, CursorMotionMeasurement Measurement, double Score)>();
        if (distance <= AgentCursorPathPlanner.ShortMoveDirectDistanceThreshold)
        {
            var measurement = Measure(direct, bounds, 72);
            candidates.Add((direct, measurement, Score(measurement)));
        }

        foreach (var sign in new[] { preferredSign, -preferredSign })
        foreach (var controlScale in new[] { 0.55, 0.8, 1.05 })
        foreach (var arcScale in new[] { 0.65, 1.0, 1.35 })
        {
            var control = Math.Min(baseControl * controlScale, distance * 0.47);
            var midControl = Math.Min(baseControl * 0.65, distance * 0.38);
            var arc = baseArc * arcScale * sign;
            var mid = new PointF(
                (float)(start.X + delta.X * 0.5 + normal.X * arc),
                (float)(start.Y + delta.Y * 0.5 + normal.Y * arc));
            var segments = new[]
            {
                CursorMotionSegment.Create(
                    start,
                    new PointF((float)(start.X + startUnit.X * control), (float)(start.Y + startUnit.Y * control)),
                    new PointF((float)(mid.X - unit.X * midControl), (float)(mid.Y - unit.Y * midControl)),
                    mid),
                CursorMotionSegment.Create(
                    mid,
                    new PointF((float)(mid.X + unit.X * midControl), (float)(mid.Y + unit.Y * midControl)),
                    new PointF((float)(end.X - endUnit.X * control), (float)(end.Y - endUnit.Y * control)),
                    end)
            };
            var measurement = Measure(segments, bounds, 72);
            candidates.Add((segments, measurement, Score(measurement)));
        }

        var sorted = candidates
            .OrderByDescending(candidate => candidate.Measurement.StaysInBounds)
            .ThenBy(candidate => candidate.Score)
            .ToArray();
        return sorted.FirstOrDefault(candidate => candidate.Measurement.StaysInBounds).Segments
               ?? sorted.FirstOrDefault().Segments
               ?? direct;
    }

    private static CursorMotionSegment[] DirectSegments(
        PointF start,
        PointF end,
        PointF delta,
        double distance,
        double startHeading,
        double endHeading)
    {
        if (distance <= 0)
        {
            return [CursorMotionSegment.Create(start, start, end, end)];
        }

        var directControl = Math.Min(
            Math.Min(Math.Min(640, distance * 0.9), Math.Max(Math.Min(48, distance * 0.33), distance * 0.41960295031576633)),
            distance * 0.45);
        var startUnit = UnitFor(startHeading);
        var endUnit = UnitFor(endHeading);
        return
        [
            CursorMotionSegment.Create(
                start,
                new PointF((float)(start.X + startUnit.X * directControl), (float)(start.Y + startUnit.Y * directControl)),
                new PointF((float)(end.X - endUnit.X * directControl), (float)(end.Y - endUnit.Y * directControl)),
                end)
        ];
    }

    private static CursorMotionMeasurement Measure(CursorMotionSegment[] segments, RectangleF? bounds, int count)
    {
        var samples = Samples(segments, count);
        var length = 0.0;
        var angleChangeEnergy = 0.0;
        var maxAngleChange = 0.0;
        var totalTurn = 0.0;
        double? previousAngle = null;
        var staysInBounds = true;
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            if (bounds is { } b && !b.Contains(sample))
                staysInBounds = false;

            if (i == 0)
                continue;

            var previous = samples[i - 1];
            var dx = sample.X - previous.X;
            var dy = sample.Y - previous.Y;
            length += Hypot(dx, dy);
            if (Hypot(dx, dy) <= 0.001)
                continue;

            var angle = Math.Atan2(dy, dx);
            if (previousAngle is { } p)
            {
                var change = Math.Abs(NormalizedAngleDelta(angle - p));
                angleChangeEnergy += change * change;
                maxAngleChange = Math.Max(maxAngleChange, change);
                totalTurn += change;
            }

            previousAngle = angle;
        }

        return new CursorMotionMeasurement(length, angleChangeEnergy, maxAngleChange, totalTurn, staysInBounds);
    }

    private static double Score(CursorMotionMeasurement measurement) =>
        measurement.Length
        + measurement.AngleChangeEnergy * AgentCursorPathPlanner.AngleChangeEnergyWeight
        + measurement.MaxAngleChange * AgentCursorPathPlanner.MaxAngleChangeWeight
        + measurement.TotalTurn * AgentCursorPathPlanner.TotalTurnWeight
        + (measurement.StaysInBounds ? 0 : AgentCursorPathPlanner.OutOfBoundsPenalty);

    private static PointF[] Samples(CursorMotionSegment[] segments, int count)
    {
        var clampedCount = Math.Max(2, count);
        var totalLength = Math.Max(1, segments.Sum(segment => segment.Length));
        var samples = new PointF[clampedCount];
        var path = new PlannedCursorPath(totalLength, 0, 0, default, segments);
        for (var i = 0; i < clampedCount; i++)
        {
            var state = path.Sample(i / (double)(clampedCount - 1) * totalLength);
            samples[i] = new PointF((float)state.X, (float)state.Y);
        }

        return samples;
    }

    private static double NormalizedAngleDelta(double value)
    {
        var delta = value;
        while (delta > Math.PI)
            delta -= 2 * Math.PI;
        while (delta < -Math.PI)
            delta += 2 * Math.PI;
        return delta;
    }

    private static PointF UnitFor(double heading) => new((float)Math.Cos(heading), (float)Math.Sin(heading));

    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);
}
