namespace CuaDriver.Win.Cursor;

internal static class AgentCursorKinematics
{
    private const double DefaultGlideDurationMs = 750;
    private const double PeakSpeed = 900;
    private const double MinStartSpeed = 300;
    private const double MinEndSpeed = 200;

    public static CursorTrip TripFor(double glideDurationMs, float scale)
    {
        var speedScale = DefaultGlideDurationMs / Math.Clamp(glideDurationMs, 50, 5000);
        return new CursorTrip(
            PeakSpeed * scale * speedScale,
            MinStartSpeed * scale * speedScale,
            MinEndSpeed * scale * speedScale);
    }

    public static double CurrentSpeed(CursorTrip trip, double pathProgress)
    {
        var u = Math.Clamp(pathProgress, 0, 1);
        var profileValue = SmootherSpeedProfile(u);
        var floorSpeed = u < 0.5 ? trip.MinStart : trip.MinEnd;
        return floorSpeed + (trip.Peak - floorSpeed) * profileValue;
    }

    public static double RotateToward(double current, double desired, double maxStep)
    {
        var diff = AngularDifference(current, desired);
        return current + Math.Max(-maxStep, Math.Min(maxStep, diff));
    }

    public static double AngularDifference(double current, double desired)
    {
        var diff = desired - current;
        while (diff > Math.PI) diff -= 2 * Math.PI;
        while (diff < -Math.PI) diff += 2 * Math.PI;
        return diff;
    }

    private static double SmootherSpeedProfile(double u) => (30 * u * u * (1 - u) * (1 - u)) / 1.875;
}

internal readonly record struct CursorTrip(double Peak, double MinStart, double MinEnd);
