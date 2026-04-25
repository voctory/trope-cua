using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;
using IOPath = System.IO.Path;

namespace CuaDriver.Win.Tools;

public sealed class LaunchAppTool : IDriverTool
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
            ("unsafe_allow_foreground", JsonArgs.Prop("boolean", "Explicitly allow a parent-session launch that may foreground the target app. Do not use for routine background automation; only set when the user explicitly requests a visible foreground launch."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var path = JsonArgs.OptionalString(args, "path")
                   ?? JsonArgs.OptionalString(args, "exe")
                   ?? JsonArgs.OptionalString(args, "name");
        var appId = JsonArgs.OptionalString(args, "app_id")
                    ?? JsonArgs.OptionalString(args, "bundle_id");
        var arguments = JsonArgs.OptionalString(args, "arguments") ?? "";
        if (args.ContainsKey("allow_foreground"))
        {
            var renamed = ActionReceipt.Failure(
                "foreground_launch_not_background_safe",
                "allow_foreground was removed from the advertised schema. Parent-session Windows ShellExecute cannot guarantee a background launch. If a human explicitly wants a visible foreground launch, pass unsafe_allow_foreground=true.");
            return ToolResult.Text("❌ " + renamed.ToJson(), true);
        }

        var unsafeAllowForeground = JsonArgs.OptionalBool(args, "unsafe_allow_foreground");

        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(appId))
            return ToolResult.Error("Provide path, exe, name, or app_id.");

        if (!unsafeAllowForeground)
        {
            var denied = ActionReceipt.Failure(
                "requires_background_launch_lane",
                "Parent-session Windows launches can foreground the target app. Reuse an existing window, use the child-session/AppBroadcast lane, or pass unsafe_allow_foreground=true only when the user explicitly asks for a visible foreground launch.");
            return ToolResult.Text("❌ " + denied.ToJson(), true);
        }

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
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);

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

        var receipt = guard.Finish(ActionReceipt.Success("shellexecute.unsafe_foreground"), allowForegroundChange: true);
        var sb = new StringBuilder();
        sb.AppendLine("✅ " + receipt.ToJson());
        sb.Append("Launch requested: ").AppendLine(appId ?? path);
        if (pid is not null)
            sb.AppendLine(CultureInfo.InvariantCulture, $"pid={pid}");
        if (windows.Length > 0 && windows[0].Pid != pid)
            sb.AppendLine(CultureInfo.InvariantCulture, $"resolved_pid={windows[0].Pid}");
        foreach (var w in windows)
            sb.AppendLine(CultureInfo.InvariantCulture, $"- window_id={w.WindowId} title=\"{w.Title}\" bounds=({w.Bounds.X},{w.Bounds.Y},{w.Bounds.Width},{w.Bounds.Height})");

        var structured = JsonNode.Parse(receipt.ToJson())!.AsObject();
        structured["requested"] = appId ?? path;
        structured["pid"] = pid;
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
        => window.AppName.Equals("cua-driver-win", StringComparison.OrdinalIgnoreCase)
           || window.Title.Contains("GDI+", StringComparison.OrdinalIgnoreCase)
           || window.ClassName.Contains("WindowsForms", StringComparison.OrdinalIgnoreCase);
}
