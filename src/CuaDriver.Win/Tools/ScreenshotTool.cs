using System.IO;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class ScreenshotTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "screenshot",
        "Capture a target window image. Uses the source-drop PrintWindow/GDI fallback; see docs/capture.md for WGC production seam.",
        JsonArgs.Schema(
            ("window_id", JsonArgs.Prop("integer", "Target HWND.")),
            ("pid", JsonArgs.Prop("integer", "Optional pid validation.")),
            ("out", JsonArgs.Prop("string", "Optional file path to write the JPEG."))),
        ReadOnly: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var windowId = JsonArgs.RequiredLong(args, "window_id");
        var pid = JsonArgs.OptionalInt(args, "pid");
        var window = WindowEnumerator.Find(windowId);
        if (window is null)
            return Task.FromResult(ToolResult.Error($"No window with window_id {windowId}."));
        if (pid is not null && window.Pid != pid)
            return Task.FromResult(ToolResult.Error($"window_id {windowId} belongs to pid {window.Pid}, not pid {pid}."));

        try
        {
            var capture = context.State.Capture.Capture(new IntPtr(windowId), context.State.Config.MaxImageDimension);
            context.State.ImageResizeRatio[(window.Pid, windowId)] = capture.Width > 0 ? capture.OriginalWidth / (double)capture.Width : 1.0;
            var outPath = JsonArgs.OptionalString(args, "out");
            if (!string.IsNullOrWhiteSpace(outPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
                File.WriteAllBytes(outPath, capture.Data);
            }

            var text = $"✅ screenshot route={capture.Route} width={capture.Width} height={capture.Height} original_width={capture.OriginalWidth} original_height={capture.OriginalHeight} image_resize_ratio={context.State.ImageResizeRatio[(window.Pid, windowId)]:0.###}"
                       + (string.IsNullOrWhiteSpace(outPath) ? "" : $" wrote=\"{outPath}\"");

            return Task.FromResult(new ToolResult
            {
                Content = [ContentBlock.ImageBlock(capture.Data, capture.MimeType), ContentBlock.TextBlock(text)],
                IsError = false
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Screenshot failed: {ex.Message}"));
        }
    }
}
