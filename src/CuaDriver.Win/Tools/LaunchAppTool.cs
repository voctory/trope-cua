using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;
using IOPath = System.IO.Path;

namespace CuaDriver.Win.Tools;

internal sealed class LaunchAppTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "launch_app",
        ToolDescriptions.LaunchApp,
        JsonArgs.SchemaWithAnyOf([], [["path"], ["exe"], ["name"], ["app_id"]],
            ("path", JsonArgs.Prop("string", "Executable, document, shortcut, or URL to launch.")),
            ("exe", JsonArgs.Prop("string", "Executable name or path.")),
            ("name", JsonArgs.Prop("string", "Alias for exe.")),
            ("app_id", JsonArgs.Prop("string", "UWP/AppUserModelID launched through shell:AppsFolder.")),
            ("arguments", JsonArgs.Prop("string", "Optional command-line arguments.")),
            ("unsafe_allow_foreground", JsonArgs.Prop("boolean", "Explicitly allow unsafe parent-session launch. Do not use for routine background automation; driver tries to restore foreground."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var path = JsonArgs.OptionalFirstString(args, "path", "exe", "name");
        var appId = JsonArgs.OptionalFirstString(args, "app_id", "bundle_id");
        var arguments = JsonArgs.OptionalString(args, "arguments") ?? "";
        if (args.ContainsKey("allow_foreground"))
        {
            var renamed = ActionReceipt.Failure(
                "foreground_launch_not_background_safe",
                "allow_foreground was removed from the advertised schema. Parent-session Windows ShellExecute cannot guarantee a background launch. Reuse an existing window or use a child-session/AppBroadcast lane. Only use unsafe_allow_foreground=true when the human explicitly requests an unsafe visible foreground launch.");
            return ActionToolResult.FromReceipt(renamed);
        }

        var unsafeAllowForeground = JsonArgs.OptionalBool(args, "unsafe_allow_foreground");

        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(appId))
            return ToolResult.Error("Provide path, exe, name, or app_id.");
        if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(appId))
            return ToolResult.Error("Provide either app_id or path/exe/name, not both.");

        if (!unsafeAllowForeground)
        {
            var denied = ActionReceipt.Failure(
                "requires_background_launch_lane",
                "Parent-session Windows launches can foreground the target app. Reuse an existing window or use the child-session/AppBroadcast lane. Do not start a separate CDP/debugging browser or pass unsafe_allow_foreground unless the human explicitly asks for an unsafe visible foreground launch.");
            return ActionToolResult.FromReceipt(denied);
        }

        var foregroundBefore = NativeMethods.GetForegroundWindow();
        using var guard = NoRegressionGuard.Capture();
        var beforeWindows = WindowEnumerator.AllWindows().Select(w => w.WindowId).ToHashSet();
        ProcessStartInfo psi;
        if (!string.IsNullOrWhiteSpace(appId))
        {
            psi = new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{appId}") { UseShellExecute = true };
        }
        else
        {
            psi = new ProcessStartInfo(path!, arguments) { UseShellExecute = true };
        }

        var process = Process.Start(psi);
        await PollLaunchWindowsToBackAsync(beforeWindows, path, appId, foregroundBefore, cancellationToken).ConfigureAwait(false);

        var pid = process?.Id;
        var allAfter = WindowEnumerator.AllWindows();
        WindowInfo[] windows = pid is null ? Array.Empty<WindowInfo>() : allAfter.Where(w => w.Pid == pid.Value).ToArray();
        if (windows.Length == 0)
        {
            var newWindows = allAfter
                .Where(w => !beforeWindows.Contains(w.WindowId))
                .Where(w => w.Pid != Environment.ProcessId)
                .Where(w => !IsDriverAuxWindow(w))
                .ToArray();
            windows = newWindows.Length > 0 ? newWindows : allAfter.Where(w => MatchesLaunchTarget(w, path, appId)).ToArray();
        }

        var sentToBack = SendWindowsToBack(windows);
        var receipt = guard.Finish(
            ActionReceipt.UnsafeSuccess("shellexecute.unsafe_foreground"),
            allowForegroundChange: true,
            restoreAllowedForegroundChange: true,
            allowUnsafeRoute: true);
        var foregroundRestored = receipt.ForegroundChanged
                                 && foregroundBefore != IntPtr.Zero
                                 && NativeMethods.GetForegroundWindow() == foregroundBefore;

        var sb = new StringBuilder();
        sb.AppendLine(ToolText.OkPrefix + receipt.ToJson());
        sb.Append("Launch requested: ").AppendLine(appId ?? path);
        if (pid is not null)
            sb.AppendLine(CultureInfo.InvariantCulture, $"pid={pid}");
        if (sentToBack > 0)
            sb.AppendLine(CultureInfo.InvariantCulture, $"sent_to_back={sentToBack}");
        if (receipt.ForegroundChanged)
            sb.AppendLine(CultureInfo.InvariantCulture, $"foreground_restored={foregroundRestored}");
        if (windows.Length > 0 && windows[0].Pid != pid)
            sb.AppendLine(CultureInfo.InvariantCulture, $"resolved_pid={windows[0].Pid}");
        foreach (var w in windows)
            sb.AppendLine(CultureInfo.InvariantCulture, $"- window_id={w.WindowId} title=\"{w.Title}\" bounds=({w.Bounds.X},{w.Bounds.Y},{w.Bounds.Width},{w.Bounds.Height})");

        var structured = ActionToolResult.StructuredReceipt(receipt);
        structured["requested"] = appId ?? path;
        structured["pid"] = pid;
        structured["sent_to_back"] = sentToBack;
        structured["foreground_restored"] = foregroundRestored;
        if (windows.Length > 0 && windows[0].Pid != pid)
            structured["resolved_pid"] = windows[0].Pid;
        structured["windows"] = ToolJson.Array(windows, ToolJson.Window);

        return ToolResult.Text(sb.ToString().TrimEnd(), structured, !receipt.Ok);
    }

    private static bool MatchesLaunchTarget(WindowInfo window, string? path, string? appId)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var name = IOPath.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(name) && string.Equals(window.AppName, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return !string.IsNullOrWhiteSpace(appId) && window.Title.Contains(appId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDriverAuxWindow(WindowInfo window)
        => window.AppName.Equals("trope-cua", StringComparison.OrdinalIgnoreCase)
           || window.Title.Contains("GDI+", StringComparison.OrdinalIgnoreCase)
           || window.ClassName.Contains("WindowsForms", StringComparison.OrdinalIgnoreCase);

    private static int SendWindowsToBack(WindowInfo[] windows)
    {
        var moved = 0;
        const uint flags = NativeMethods.SWP_NOMOVE
                           | NativeMethods.SWP_NOSIZE
                           | NativeMethods.SWP_NOACTIVATE
                           | NativeMethods.SWP_NOOWNERZORDER
                           | NativeMethods.SWP_NOSENDCHANGING;

        foreach (var window in windows)
        {
            if (NativeMethods.SetWindowPos(window.Hwnd, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0, flags))
                moved++;
        }

        return moved;
    }

    private static async Task PollLaunchWindowsToBackAsync(
        HashSet<long> beforeWindows,
        string? path,
        string? appId,
        IntPtr foregroundBefore,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidates = WindowEnumerator.AllWindows()
                .Where(w => !beforeWindows.Contains(w.WindowId) || MatchesLaunchTarget(w, path, appId))
                .Where(w => w.Pid != Environment.ProcessId)
                .Where(w => !IsDriverAuxWindow(w))
                .ToArray();

            if (candidates.Length > 0)
            {
                SendWindowsToBack(candidates);
                if (foregroundBefore != IntPtr.Zero && NativeMethods.GetForegroundWindow() != foregroundBefore)
                    NativeMethods.SetForegroundWindow(foregroundBefore);
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }
}
