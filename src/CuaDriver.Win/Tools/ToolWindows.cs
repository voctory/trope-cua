using System.Diagnostics.CodeAnalysis;
using System.Windows.Automation;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Uia;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

internal static class ToolWindows
{
    public static bool TryFind(long windowId, [NotNullWhen(true)] out WindowInfo? window, out ToolResult? error)
    {
        var found = WindowEnumerator.Find(windowId);
        if (found is null)
        {
            window = null;
            error = ToolResult.Error($"No window with window_id {windowId}.");
            return false;
        }

        window = found;
        error = null;
        return true;
    }

    public static bool TryFindForPid(int pid, long windowId, [NotNullWhen(true)] out WindowInfo? window, out ToolResult? error)
    {
        if (!TryFind(windowId, out var found, out error))
        {
            window = null;
            return false;
        }

        if (found.Pid != pid)
        {
            window = null;
            error = ToolResult.Error($"window_id {windowId} belongs to pid {found.Pid}, not pid {pid}.");
            return false;
        }

        window = found;
        error = null;
        return true;
    }

    public static bool TryFindMainOrForPid(
        int pid,
        long? windowId,
        [NotNullWhen(true)] out WindowInfo? window,
        out ToolResult? error,
        string? mainMissingMessage = null)
    {
        if (windowId is not null)
            return TryFindForPid(pid, windowId.Value, out window, out error);

        var main = WindowEnumerator.MainWindowForPid(pid);
        if (main is null)
        {
            window = null;
            error = ToolResult.Error(mainMissingMessage ?? $"No window found for pid {pid}.");
            return false;
        }

        window = main;
        error = null;
        return true;
    }

    public static IntPtr NativeHwndForElement(AutomationElement element) =>
        AutomationElementNative.HwndOrZero(element);
}
