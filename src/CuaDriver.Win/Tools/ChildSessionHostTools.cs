using System.Globalization;
using System.Security.Principal;
using System.Text.Json.Nodes;
using CuaDriver.Win.HardCases;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class ChildSessionStartTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "child_session_start",
        "Start a no-activate RDP ActiveX host for the child-session/PiP lane and wait for WTSGetChildSessionId.",
        JsonArgs.Schema(
            ("width", JsonArgs.Prop("integer", "Remote desktop width in pixels. Default 1280.")),
            ("height", JsonArgs.Prop("integer", "Remote desktop height in pixels. Default 720.")),
            ("timeout_ms", JsonArgs.Prop("integer", "How long to wait for WTSGetChildSessionId. Default 15000.")),
            ("visible", JsonArgs.Prop("boolean", "Show the PiP host window. Default true."))),
        ReadOnly: false,
        Idempotent: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var width = JsonArgs.OptionalInt(args, "width") ?? 1280;
        var height = JsonArgs.OptionalInt(args, "height") ?? 720;
        var timeoutMs = JsonArgs.OptionalInt(args, "timeout_ms") ?? 15_000;
        var visible = JsonArgs.OptionalBool(args, "visible", true);

        var cursorBeforeKnown = NativeMethods.GetCursorPos(out var cursorBefore);
        var foregroundBefore = NativeMethods.GetForegroundWindow();

        var lines = new List<string>
        {
            "Child-session/PiP probe",
            $"admin={new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)}",
            $"parent_session_id={ChildSessionBroker.CurrentProcessSessionId()}",
            $"active_console_session_id={ChildSessionBroker.ActiveConsoleSessionId()}",
            $"child_sessions_enabled_before={ChildSessionBroker.IsEnabled()}"
        };

        var enabled = ChildSessionBroker.TryEnableChildSessions(out var enableMessage);
        lines.Add($"enable_child_sessions_ok={enabled}");
        lines.Add($"enable_child_sessions_message=\"{enableMessage}\"");
        lines.Add($"child_sessions_enabled_after={ChildSessionBroker.IsEnabled()}");

        if (!enabled)
        {
            lines.Add("requires_elevation=true");
            lines.Add("next_step=\"Run the driver once elevated to enable child sessions, then start the child-session host from the normal background daemon.\"");
            return ToolResult.Text(string.Join(Environment.NewLine, lines), StartStructured(false, "enable_child_sessions_failed", null, false, lines), isError: true);
        }

        var options = new ChildSessionHostOptions(
            Width: Math.Clamp(width, 640, 7680),
            Height: Math.Clamp(height, 480, 4320),
            TimeoutMs: Math.Clamp(timeoutMs, 1_000, 120_000),
            Visible: visible);

        var result = await ChildSessionHost.StartAsync(options, cancellationToken).ConfigureAwait(false);

        var cursorAfterKnown = NativeMethods.GetCursorPos(out var cursorAfter);
        var foregroundAfter = NativeMethods.GetForegroundWindow();
        var foregroundChanged = foregroundBefore != IntPtr.Zero && foregroundAfter != foregroundBefore;
        var foregroundRestored = false;
        if (foregroundChanged)
        {
            foregroundRestored = NativeMethods.SetForegroundWindow(foregroundBefore)
                                 && SpinWait.SpinUntil(() => NativeMethods.GetForegroundWindow() == foregroundBefore, TimeSpan.FromMilliseconds(50));
        }

        lines.Add($"ok={result.Ok}");
        lines.Add("route=rdp.activex.child_session");
        lines.Add("lane=child_session");
        lines.Add("background_safe=true");
        lines.Add($"host_running={result.HostRunning}");
        lines.Add($"child_session_id={(result.ChildSessionId?.ToString(CultureInfo.InvariantCulture) ?? "none")}");
        lines.Add($"status=\"{result.Status}\"");
        lines.Add($"cursor_moved={(cursorBeforeKnown && cursorAfterKnown && (cursorBefore.X != cursorAfter.X || cursorBefore.Y != cursorAfter.Y))}");
        lines.Add($"foreground_changed={foregroundChanged}");
        lines.Add($"foreground_restored={foregroundRestored}");
        foreach (var logLine in result.Log)
            lines.Add($"log=\"{logLine}\"");

        return ToolResult.Text(string.Join(Environment.NewLine, lines), StartStructured(result.Ok, result.Status, result.ChildSessionId, result.HostRunning, result.Log), isError: !result.Ok);
    }

    private static JsonObject StartStructured(bool ok, string status, int? childSessionId, bool hostRunning, IEnumerable<string> log) => new()
    {
        ["ok"] = ok,
        ["route"] = "rdp.activex.child_session",
        ["lane"] = "child_session",
        ["background_safe"] = true,
        ["status"] = status,
        ["host_running"] = hostRunning,
        ["child_session_id"] = childSessionId,
        ["parent_session_id"] = ChildSessionBroker.CurrentProcessSessionId(),
        ["active_console_session_id"] = ChildSessionBroker.ActiveConsoleSessionId(),
        ["child_sessions_enabled"] = ChildSessionBroker.IsEnabled(),
        ["log"] = ToolJson.Array(log)
    };
}

public sealed class ChildSessionStopTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "child_session_stop",
        "Stop the child-session/PiP RDP ActiveX host if it is running.",
        JsonArgs.Schema(),
        ReadOnly: false,
        Idempotent: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var ok = ChildSessionHost.Stop(out var message);
        var lines = new[]
        {
            $"ok={ok}",
            $"message=\"{message}\"",
            ChildSessionHost.StatusText(),
            ChildSessionBroker.Status()
        };
        return Task.FromResult(ToolResult.Text(string.Join(Environment.NewLine, lines), new JsonObject
        {
            ["ok"] = ok,
            ["message"] = message,
            ["host"] = ChildSessionHost.StatusObject(),
            ["child_session_id"] = ChildSessionBroker.GetChildSessionId(),
            ["child_session_connected"] = ChildSessionBroker.GetChildSessionId() is not null
        }, isError: !ok));
    }
}
