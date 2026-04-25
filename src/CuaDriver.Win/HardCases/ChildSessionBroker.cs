using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CuaDriver.Win.HardCases;

/// <summary>
/// Windows Terminal Services broker primitives for the child-session/PiP lane.
/// A complete PiP implementation still needs an RDP ActiveX host with
/// IMsRdpExtendedSettings["ConnectToChildSession"] = true.
/// </summary>
internal static class ChildSessionBroker
{
    public static bool IsSupportedByOs() => OperatingSystem.IsWindowsVersionAtLeast(10);

    public static bool IsEnabled()
    {
        if (!IsSupportedByOs())
            return false;

        return WTSIsChildSessionsEnabled(out var enabled) && enabled;
    }

    public static bool TryEnableChildSessions(out string message)
    {
        if (!IsSupportedByOs())
        {
            message = "Child sessions require Windows 10+.";
            return false;
        }

        if (WTSEnableChildSessions(true))
        {
            message = "WTSEnableChildSessions(TRUE) succeeded.";
            return true;
        }

        message = new Win32Exception(Marshal.GetLastWin32Error()).Message;
        return false;
    }

    public static int? GetChildSessionId()
    {
        if (WTSGetChildSessionId(out var sessionId))
            return sessionId;
        return null;
    }

    public static string Status()
    {
        var child = GetChildSessionId();
        return child is null
            ? "No connected child session is currently reported. Host an RDP child session before launching the child agent."
            : $"Connected child session id: {child}";
    }

    public static int CurrentProcessSessionId()
    {
        return ProcessIdToSessionId(Environment.ProcessId, out var sessionId) ? sessionId : -1;
    }

    public static int ActiveConsoleSessionId()
    {
        unchecked
        {
            return (int)WTSGetActiveConsoleSessionId();
        }
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSEnableChildSessions(bool bEnable);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSIsChildSessionsEnabled(out bool pbEnabled);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSGetChildSessionId(out int pSessionId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(int dwProcessId, out int pSessionId);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
