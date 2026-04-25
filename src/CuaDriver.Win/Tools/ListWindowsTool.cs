using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class ListWindowsTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "list_windows",
        ToolDescriptions.ListWindows,
        JsonArgs.Schema(
            ("pid", JsonArgs.Prop("integer", "Optional pid filter.")),
            ("on_screen_only", JsonArgs.Prop("boolean", "When true, omit hidden/minimized windows."))),
        ReadOnly: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.OptionalInt(args, "pid");
        var visibleOnly = JsonArgs.OptionalBool(args, "on_screen_only");
        var windows = WindowEnumerator.AllWindows(pid, visibleOnly);

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"{ToolText.OkPrefix}Found {windows.Count} window(s).");
        foreach (var w in windows)
        {
            sb.Append("- ")
              .Append(w.AppName)
              .Append(" pid=").Append(w.Pid)
              .Append(" window_id=").Append(w.WindowId)
              .Append(" z_index=").Append(w.ZIndex)
              .Append(w.IsVisible ? " visible" : " hidden")
              .Append(w.IsMinimized ? " minimized" : "")
              .Append(" dpi=").Append(w.Dpi)
              .Append(" bounds=(").Append(w.Bounds.X).Append(',').Append(w.Bounds.Y).Append(',').Append(w.Bounds.Width).Append(',').Append(w.Bounds.Height).Append(')')
              .Append(" class=").Append(w.ClassName)
              .Append(" title=\"").Append(w.Title.Replace("\"", "\\\"")).AppendLine("\"");
        }

        return Task.FromResult(ToolResult.Text(sb.ToString().TrimEnd(), new JsonObject
        {
            ["count"] = windows.Count,
            ["pid_filter"] = pid,
            ["on_screen_only"] = visibleOnly,
            ["windows"] = ToolJson.Array(windows, ToolJson.Window)
        }));
    }
}
