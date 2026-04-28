using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CuaDriver.Win.Capture;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

internal sealed class GetWindowStateTool : IDriverTool
{
    private readonly CaptureMode? _modeOverride;

    public GetWindowStateTool(CaptureMode? modeOverride = null)
    {
        _modeOverride = modeOverride;
    }

    public ToolDefinition Definition { get; } = new(
        "get_window_state",
        ToolDescriptions.GetWindowState,
        JsonArgs.RequiredSchema(["pid", "window_id"],
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND as returned by list_windows.")),
            ("capture_mode", JsonArgs.Prop("string", "One-call override: som, ax, or vision.")),
            ("query", JsonArgs.Prop("string", "Optional case-insensitive tree filter.")),
            ("include_structured_tree", JsonArgs.Prop("boolean", "Duplicate tree_markdown into structuredContent. Default false saves tokens."))),
        ReadOnly: true,
        Idempotent: false);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var windowId = JsonArgs.RequiredLong(args, "window_id");
        var modeArg = JsonArgs.OptionalString(args, "capture_mode");
        var query = JsonArgs.OptionalString(args, "query");
        var includeStructuredTree = JsonArgs.OptionalBool(args, "include_structured_tree");

        if (!ToolWindows.TryFindForPid(pid, windowId, out var window, out var error))
            return Task.FromResult(error!);

        var mode = _modeOverride ?? (string.IsNullOrWhiteSpace(modeArg)
            ? context.State.Config.CaptureMode
            : DriverConfig.ParseCaptureMode(modeArg!));
        var content = new List<ContentBlock>();
        var sb = new StringBuilder();
        var structured = new JsonObject
        {
            ["pid"] = pid,
            ["window_id"] = windowId,
            ["window"] = ToolJson.Window(window),
            ["capture_mode"] = mode.ToString().ToLowerInvariant(),
            ["query"] = query
        };

        if (mode != CaptureMode.Ax)
        {
            try
            {
                var capture = WindowCapture.Capture(new IntPtr(windowId), context.State.Config.MaxImageDimension);
                var resizeRatio = capture.ResizeRatio;
                context.State.ImageResizeRatio[(pid, windowId)] = resizeRatio;
                content.Add(ContentBlock.ImageBlock(capture.Data, capture.MimeType));
                structured["capture"] = ToolJson.Capture(capture);
                structured["image_resize_ratio"] = resizeRatio;
                sb.AppendLine(CultureInfo.InvariantCulture, $"{ToolText.OkPrefix}screenshot route={capture.Route} width={capture.Width} height={capture.Height} original_width={capture.OriginalWidth} original_height={capture.OriginalHeight} scale_factor={capture.ScaleFactor:0.###} image_resize_ratio={resizeRatio:0.###}");
            }
            catch (Exception ex)
            {
                structured["capture_error"] = ex.Message;
                sb.Append(ToolText.WarningPrefix + "screenshot failed: ").AppendLine(ex.Message);
            }
        }

        if (mode != CaptureMode.Vision)
        {
            try
            {
                var snapshot = context.State.UiaTree.Snapshot(pid, windowId, query);
                var uia = new JsonObject
                {
                    ["turn_id"] = snapshot.TurnId,
                    ["element_count"] = snapshot.ElementCount,
                    ["tree_markdown_chars"] = snapshot.TreeMarkdown.Length,
                    ["tree_markdown_in_structured"] = includeStructuredTree,
                    ["metrics"] = ToolJson.UiSnapshotMetrics(snapshot.Metrics)
                };
                if (includeStructuredTree)
                    uia["tree_markdown"] = snapshot.TreeMarkdown;
                structured["uia"] = uia;
                sb.AppendLine(CultureInfo.InvariantCulture, $"{ToolText.OkPrefix}{window.AppName} — {snapshot.ElementCount} elements, turn {snapshot.TurnId} [uia/{mode.ToString().ToLowerInvariant()} mode; [eN] = element_index N]");
                if (snapshot.ElementCount <= 15)
                    sb.AppendLine(ToolText.WarningPrefix + "Small UIA tree — target may be custom-rendered. Use CDP, HWND-message pixel route, or child-session lane for raw surfaces.");
                if (!string.IsNullOrWhiteSpace(snapshot.TreeMarkdown))
                    sb.AppendLine().AppendLine(snapshot.TreeMarkdown);
            }
            catch (Exception ex)
            {
                structured["uia_error"] = ex.Message;
                sb.Append(ToolText.WarningPrefix + "UIA snapshot failed: ").AppendLine(ex.Message);
            }
        }

        content.Add(ContentBlock.TextBlock(sb.ToString().TrimEnd()));
        return Task.FromResult(new ToolResult { Content = content, IsError = false, StructuredContent = structured });
    }
}
