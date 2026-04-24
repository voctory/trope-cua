using System.Diagnostics;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Win32;

public sealed record WindowInfo(
    long WindowId,
    int Pid,
    string AppName,
    string Title,
    string ClassName,
    RectDto Bounds,
    bool IsVisible,
    bool IsMinimized,
    int ZIndex,
    uint Dpi)
{
    public IntPtr Hwnd => new(WindowId);
}

public static class WindowEnumerator
{
    public static IReadOnlyList<WindowInfo> AllWindows(int? pidFilter = null, bool visibleOnly = false)
    {
        var result = new List<WindowInfo>();
        var z = 0;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            try
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out var rawPid);
                var pid = unchecked((int)rawPid);
                if (pidFilter is not null && pid != pidFilter.Value)
                    return true;

                var visible = NativeMethods.IsWindowVisible(hwnd);
                var minimized = NativeMethods.IsIconic(hwnd);
                if (visibleOnly && (!visible || minimized))
                    return true;

                var title = NativeMethods.GetWindowText(hwnd);
                var className = NativeMethods.GetClassName(hwnd);
                var rect = NativeMethods.GetBestWindowRect(hwnd);
                if (rect.IsEmpty)
                    return true;

                if (string.IsNullOrWhiteSpace(title) && IsNoiseClass(className))
                    return true;

                var appName = ProcessName(pid);
                var dpi = SafeDpi(hwnd);

                result.Add(new WindowInfo(
                    WindowId: hwnd.ToInt64(),
                    Pid: pid,
                    AppName: appName,
                    Title: title,
                    ClassName: className,
                    Bounds: RectDto.From(rect),
                    IsVisible: visible,
                    IsMinimized: minimized,
                    ZIndex: z++,
                    Dpi: dpi));
            }
            catch
            {
                // Keep enumeration robust across windows that disappear mid-enumeration.
            }
            return true;
        }, IntPtr.Zero);

        return result;
    }

    public static WindowInfo? Find(long windowId)
        => AllWindows().FirstOrDefault(w => w.WindowId == windowId);

    public static WindowInfo? MainWindowForPid(int pid)
        => AllWindows(pid, visibleOnly: false).FirstOrDefault(w => !w.IsMinimized) ?? AllWindows(pid, visibleOnly: false).FirstOrDefault();

    private static bool IsNoiseClass(string className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return true;
        return className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "DV2ControlHost" or "Windows.UI.Core.CoreWindow";
    }

    private static string ProcessName(int pid)
    {
        try
        {
            return Process.GetProcessById(pid).ProcessName;
        }
        catch
        {
            return $"pid {pid}";
        }
    }

    private static uint SafeDpi(IntPtr hwnd)
    {
        try { return NativeMethods.GetDpiForWindow(hwnd); }
        catch { return 96; }
    }
}
