namespace CuaDriver.Win.Capture;

/// <summary>
/// Production WGC capture seam. Kept separate from the default build because the
/// implementation needs Windows-only WinRT/D3D interop packages and validation
/// against the user's target OS build. See docs/capture.md.
/// </summary>
internal static class WgcCaptureSeam
{
    public static bool IsSupportedByOs()
        => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362);

    public static string Status()
        => IsSupportedByOs()
            ? "Windows.Graphics.Capture OS support is present; wire the D3D11/WinRT capture broker for production occluded-window capture."
            : "Windows.Graphics.Capture CreateForWindow requires Windows 10 1903 or newer.";
}
