using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Cursor;

internal static class AgentCursorWindowing
{
    public const string OverlayWindowTitlePrefix = "CuaDriverWin.AgentCursorOverlay";

    public static string OverlayWindowTitleFor(string? instanceId) =>
        $"{OverlayWindowTitlePrefix}.{DriverInstance.Resolve(instanceId)}";

    public static bool IsDefaultOverlayTitle(string title) =>
        title.Equals(OverlayWindowTitleFor(DriverInstance.DefaultId), StringComparison.Ordinal);

    public static IntPtr WindowJustAboveTarget(IntPtr target, IntPtr overlay, bool targetTopmost)
    {
        var above = NativeMethods.GetWindow(target, NativeMethods.GW_HWNDPREV);
        while (above != IntPtr.Zero)
        {
            if (above != overlay && !IsAgentCursorOverlayWindow(above))
            {
                if (!targetTopmost && IsTopmostWindow(above))
                    return NativeMethods.HWND_TOP;

                return above;
            }

            above = NativeMethods.GetWindow(above, NativeMethods.GW_HWNDPREV);
        }

        return targetTopmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_TOP;
    }

    public static IntPtr NormalizeTargetHwnd(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            return IntPtr.Zero;

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        hwnd = root == IntPtr.Zero ? hwnd : root;
        return NativeMethods.IsWindow(hwnd) && NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd)
            ? hwnd
            : IntPtr.Zero;
    }

    public static bool IsTopmostWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        return (exStyle & NativeMethods.WS_EX_TOPMOST) != 0;
    }

    public static bool IsAgentCursorOverlayWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        var title = NativeMethods.GetWindowText(hwnd);
        return title.Equals(OverlayWindowTitlePrefix, StringComparison.Ordinal) ||
               title.StartsWith(OverlayWindowTitlePrefix + ".", StringComparison.Ordinal);
    }
}
