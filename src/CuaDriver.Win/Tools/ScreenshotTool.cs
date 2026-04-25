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
                if (pid is null)
                {
                    if (!ToolWindows.TryFind(windowId.Value, out var resolvedWindow, out var error))
                        return Task.FromResult(error!);
                    window = resolvedWindow;
                }
                else
                {
                    if (!ToolWindows.TryFindForPid(pid.Value, windowId.Value, out var resolvedWindow, out var error))
                        return Task.FromResult(error!);
                    window = resolvedWindow;
                }

                capture = WindowCapture.Capture(new IntPtr(windowId.Value), context.State.Config.MaxImageDimension, quality, format);
                context.State.ImageResizeRatio[(window.Pid, windowId.Value)] = capture.ResizeRatio;
            }
            else
            {
                if (pid is not null)
                    return Task.FromResult(ToolResult.Error("pid validation requires window_id. Omit pid for full-desktop screenshots or pass window_id."));
                capture = WindowCapture.CaptureVirtualScreen(context.State.Config.MaxImageDimension, quality, format);
            }

            var outPath = JsonArgs.OptionalString(args, "out");
            if (!string.IsNullOrWhiteSpace(outPath))
            {
                outPath = PathHelpers.ExpandUserPath(outPath);
                AtomicFile.WriteAllBytes(outPath, capture.Data);
            }

            var ratioText = window is null
                ? ""
                : $" image_resize_ratio={capture.ResizeRatio:0.###}";
            var targetText = window is null ? "desktop" : $"window_id={window.WindowId} pid={window.Pid}";
            var structured = new JsonObject
            {
                ["target"] = window is null ? "desktop" : "window",
                ["window"] = window is null ? null : ToolJson.Window(window),
                ["capture"] = ToolJson.Capture(capture),
                ["format"] = format,
                ["out"] = outPath
            };
            if (window is not null)
                structured["image_resize_ratio"] = capture.ResizeRatio;

            var text = $"✅ screenshot target={targetText} route={capture.Route} width={capture.Width} height={capture.Height} original_width={capture.OriginalWidth} original_height={capture.OriginalHeight} format={format}{ratioText}"
                       + (string.IsNullOrWhiteSpace(outPath) ? "" : $" wrote=\"{outPath}\"");

            if (window is null)
            {
                structured["visible_windows"] = ToolJson.Array(WindowEnumerator.AllWindows(visibleOnly: true).Take(20), ToolJson.Window);
                text += VisibleWindowsHint();
            }

            return Task.FromResult(new ToolResult
            {
                Content = [ContentBlock.ImageBlock(capture.Data, capture.MimeType), ContentBlock.TextBlock(text)],
                IsError = false,
                StructuredContent = structured
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
            _ => throw new ArgumentException("format must be one of png, jpeg, or jpg.")
        };
    }

}
