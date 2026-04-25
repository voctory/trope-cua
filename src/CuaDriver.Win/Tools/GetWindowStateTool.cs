using System.Text;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class GetWindowStateTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "get_window_state",
        ToolDescriptions.GetWindowState,
        JsonArgs.Schema(
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND as returned by list_windows.")),
            ("query", JsonArgs.Prop("string", "Optional case-insensitive tree filter."))),
        ReadOnly: true,
        Idempotent: false);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var windowId = JsonArgs.RequiredLong(args, "window_id");
        var query = JsonArgs.OptionalString(args, "query");

        var window = WindowEnumerator.Find(windowId);
        if (window is null)
            return Task.FromResult(ToolResult.Error($"No window with window_id {windowId}."));
        if (window.Pid != pid)
            return Task.FromResult(ToolResult.Error($"window_id {windowId} belongs to pid {window.Pid}, not pid {pid}."));

        var mode = context.State.Config.CaptureMode;
        var content = new List<ContentBlock>();
        var sb = new StringBuilder();

        if (mode != CaptureMode.Ax)
        {
            try
            {
                var capture = context.State.Capture.Capture(new IntPtr(windowId), context.State.Config.MaxImageDimension);
                context.State.ImageResizeRatio[(pid, windowId)] = capture.Width > 0 ? capture.OriginalWidth / (double)capture.Width : 1.0;
                content.Add(ContentBlock.ImageBlock(capture.Data, capture.MimeType));
                sb.AppendLine($"✅ screenshot route={capture.Route} width={capture.Width} height={capture.Height} original_width={capture.OriginalWidth} original_height={capture.OriginalHeight} scale_factor={capture.ScaleFactor:0.###} image_resize_ratio={context.State.ImageResizeRatio[(pid, windowId)]:0.###}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"⚠️ screenshot failed: {ex.Message}");
            }
        }

        if (mode != CaptureMode.Vision)
        {
            try
            {
                var snapshot = context.State.UiaTree.Snapshot(pid, windowId, query);
                sb.AppendLine($"✅ {window.AppName} — {snapshot.ElementCount} elements, turn {snapshot.TurnId} [uia/{mode.ToString().ToLowerInvariant()} mode]");
                if (snapshot.ElementCount <= 15)
                    sb.AppendLine("⚠️ Small UIA tree — target may be custom-rendered. Use CDP, HWND-message pixel route, or child-session lane for raw surfaces.");
                if (!string.IsNullOrWhiteSpace(snapshot.TreeMarkdown))
                    sb.AppendLine().AppendLine(snapshot.TreeMarkdown);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"⚠️ UIA snapshot failed: {ex.Message}");
            }
        }

        content.Add(ContentBlock.TextBlock(sb.ToString().TrimEnd()));
        return Task.FromResult(new ToolResult { Content = content, IsError = false });
    }
}
