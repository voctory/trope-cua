using System.IO;
using System.Text.Json.Nodes;
using CuaDriver.Win.Capture;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class ScreenshotTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "screenshot",
        ToolDescriptions.Screenshot,
        JsonArgs.Schema(
            ("format", JsonArgs.Prop("string", "Image format: png or jpeg. Default: png.")),
            ("quality", JsonArgs.Prop("integer", "JPEG quality 1-95; ignored for png. Default: 95.")),
            ("window_id", JsonArgs.Prop("integer", "Optional target HWND. When omitted, captures the full virtual desktop.")),
            ("pid", JsonArgs.Prop("integer", "Optional pid validation. Requires window_id.")),
            ("out", JsonArgs.Prop("string", "Optional file path to write the image."))),
        ReadOnly: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var format = NormalizeFormat(JsonArgs.OptionalString(args, "format") ?? "png");
        var quality = Math.Clamp(JsonArgs.OptionalInt(args, "quality") ?? 95, 1, 95);
        var windowId = JsonArgs.OptionalLong(args, "window_id");
        var pid = JsonArgs.OptionalInt(args, "pid");

        try
        {
            WindowInfo? window = null;
            CapturedImage capture;
            if (windowId is not null)
            {
                window = WindowEnumerator.Find(windowId.Value);
                if (window is null)
                    return Task.FromResult(ToolResult.Error($"No window with window_id {windowId}."));
                if (pid is not null && window.Pid != pid)
                    return Task.FromResult(ToolResult.Error($"window_id {windowId} belongs to pid {window.Pid}, not pid {pid}."));

                capture = context.State.Capture.Capture(new IntPtr(windowId.Value), context.State.Config.MaxImageDimension, quality, format);
                context.State.ImageResizeRatio[(window.Pid, windowId.Value)] = capture.Width > 0
                    ? capture.OriginalWidth / (double)capture.Width
                    : 1.0;
            }
            else
            {
                if (pid is not null)
                    return Task.FromResult(ToolResult.Error("pid validation requires window_id. Omit pid for full-desktop screenshots or pass window_id."));
                capture = context.State.Capture.CaptureVirtualScreen(context.State.Config.MaxImageDimension, quality, format);
            }

            var outPath = JsonArgs.OptionalString(args, "out");
            if (!string.IsNullOrWhiteSpace(outPath))
            {
                outPath = ExpandPath(outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
                File.WriteAllBytes(outPath, capture.Data);
            }

            var ratioText = window is null
                ? ""
                : $" image_resize_ratio={context.State.ImageResizeRatio[(window.Pid, window.WindowId)]:0.###}";
            var targetText = window is null ? "desktop" : $"window_id={window.WindowId} pid={window.Pid}";
            var text = $"✅ screenshot target={targetText} route={capture.Route} width={capture.Width} height={capture.Height} original_width={capture.OriginalWidth} original_height={capture.OriginalHeight} format={format}{ratioText}"
                       + (string.IsNullOrWhiteSpace(outPath) ? "" : $" wrote=\"{outPath}\"");

            if (window is null)
                text += VisibleWindowsHint();

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

    private static string VisibleWindowsHint()
    {
        var visibleWindows = WindowEnumerator.AllWindows(visibleOnly: true).Take(20).ToArray();
        if (visibleWindows.Length == 0)
            return "";

        var text = "\n\nOn-screen windows:";
        foreach (var w in visibleWindows)
        {
            var title = string.IsNullOrWhiteSpace(w.Title) ? "(no title)" : $"\"{w.Title.Replace("\"", "\\\"")}\"";
            text += $"\n- {w.AppName} pid={w.Pid} {title} window_id={w.WindowId}";
        }
        return text + "\n-> Call get_window_state(pid, window_id) to inspect a specific window.";
    }

    private static string NormalizeFormat(string format)
    {
        var normalized = format.Trim().ToLowerInvariant();
        return normalized switch
        {
            "jpg" => "jpeg",
            "jpeg" => "jpeg",
            "png" => "png",
            _ => "png"
        };
    }

    private static string ExpandPath(string path)
    {
        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal) || path.StartsWith("~/", StringComparison.Ordinal))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return path;
    }
}
