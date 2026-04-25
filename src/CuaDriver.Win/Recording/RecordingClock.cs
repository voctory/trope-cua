using System.Diagnostics;

namespace CuaDriver.Win.Recording;

internal static class RecordingClock
{
    public static long ElapsedMs(long start, long end)
    {
        if (start <= 0 || end < start)
            return 0;
        return (long)((end - start) * 1000.0 / Stopwatch.Frequency);
    }
}
