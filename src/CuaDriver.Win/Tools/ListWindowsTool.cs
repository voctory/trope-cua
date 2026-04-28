using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

internal sealed class ListWindowsTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "list_windows",
        ToolDescriptions.ListWindows,
        JsonArgs.Schema(
            ("pid", JsonArgs.Prop("integer", "Optional pid filter.")),
            ("on_screen_only", JsonArgs.Prop("boolean", "When true, omit hidden/minimized windows.")),
            ("verbose", JsonArgs.Prop("boolean", "When true, include class, DPI, z-index, and every returned window in text and structured output."))),
        ReadOnly: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.OptionalInt(args, "pid");
        var visibleOnly = JsonArgs.OptionalBool(args, "on_screen_only");
        var verbose = JsonArgs.OptionalBool(args, "verbose");
        var windows = WindowEnumerator.AllWindows(pid, visibleOnly);

        var sb = new StringBuilder();
        var shown = verbose ? windows : CompactWindowList(windows);
        sb.Append(CultureInfo.InvariantCulture, $"{ToolText.OkPrefix}Found {windows.Count} window(s)");
        if (!verbose)
            sb.Append(CultureInfo.InvariantCulture, $", showing {shown.Count} likely target(s)");
        sb.AppendLine(".");

        foreach (var w in shown)
        {
            AppendWindowLine(sb, w, verbose);
        }

        if (!verbose && shown.Count < windows.Count)
            sb.AppendLine(CultureInfo.InvariantCulture, $"-> Pass verbose=true for every window and expanded fields.");

        return Task.FromResult(ToolResult.Text(sb.ToString().TrimEnd(), new JsonObject
        {
            ["count"] = windows.Count,
            ["shown_count"] = shown.Count,
            ["omitted_count"] = windows.Count - shown.Count,
            ["pid_filter"] = pid,
            ["on_screen_only"] = visibleOnly,
            ["verbose"] = verbose,
            ["windows"] = verbose
                ? ToolJson.Array(windows, ToolJson.Window)
                : ToolJson.Array(shown, ToolJson.CompactWindow)
        }));
    }

    private static WindowInfo[] CompactWindowList(IReadOnlyList<WindowInfo> windows)
    {
        var likelyTargets = windows
            .Where(w => w.IsVisible && !w.IsMinimized && !string.IsNullOrWhiteSpace(w.Title))
            .Take(12)
            .ToArray();

        return likelyTargets.Length > 0 ? likelyTargets : windows.Take(12).ToArray();
    }

    private static void AppendWindowLine(StringBuilder sb, WindowInfo w, bool verbose)
    {
        sb.Append("- ")
          .Append(w.AppName)
          .Append(" pid=").Append(w.Pid)
          .Append(" window_id=").Append(w.WindowId)
          .Append(w.IsVisible ? " visible" : " hidden")
          .Append(w.IsMinimized ? " minimized" : "")
          .Append(" bounds=(").Append(w.Bounds.X).Append(',').Append(w.Bounds.Y).Append(',').Append(w.Bounds.Width).Append(',').Append(w.Bounds.Height).Append(')')
          .Append(" title=\"").Append(w.Title.Replace("\"", "\\\"")).Append('"');

        if (verbose)
        {
            sb.Append(" z_index=").Append(w.ZIndex)
              .Append(" dpi=").Append(w.Dpi)
              .Append(" class=").Append(w.ClassName);
        }

        sb.AppendLine();
    }
}
