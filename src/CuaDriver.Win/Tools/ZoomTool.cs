using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class ZoomTool : IDriverTool
{
    private const double MaxZoomWidth = 500;

    public ToolDefinition Definition { get; } = new(
        "zoom",
        ToolDescriptions.Zoom,
        JsonArgs.RequiredSchema(["pid", "x1", "y1", "x2", "y2"],
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("x1", JsonArgs.Prop("number", "Left edge in resized screenshot pixels.")),
            ("y1", JsonArgs.Prop("number", "Top edge in resized screenshot pixels.")),
            ("x2", JsonArgs.Prop("number", "Right edge in resized screenshot pixels.")),
            ("y2", JsonArgs.Prop("number", "Bottom edge in resized screenshot pixels."))),
        ReadOnly: true,
        Idempotent: false);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var x1 = JsonArgs.RequiredDouble(args, "x1");
        var y1 = JsonArgs.RequiredDouble(args, "y1");
        var x2 = JsonArgs.RequiredDouble(args, "x2");
        var y2 = JsonArgs.RequiredDouble(args, "y2");

        if (x2 <= x1 || y2 <= y1)
            return Task.FromResult(ToolResult.Error("Invalid region: x2 must be > x1 and y2 must be > y1."));
        if (x2 - x1 > MaxZoomWidth)
            return Task.FromResult(ToolResult.Error($"Zoom region too wide: {(int)(x2 - x1)} px > {(int)MaxZoomWidth} px max."));

        var window = WindowEnumerator.MainWindowForPid(pid);
        if (window is null)
            return Task.FromResult(ToolResult.Error($"No capturable window for pid {pid}."));

        try
        {
            var ratio = context.State.ImageResizeRatio.TryGetValue((pid, window.WindowId), out var r) ? r : 1.0;
            var origX1 = (int)Math.Round(x1 * ratio);
            var origY1 = (int)Math.Round(y1 * ratio);
            var origX2 = (int)Math.Round(x2 * ratio);
            var origY2 = (int)Math.Round(y2 * ratio);
            var padW = (int)Math.Round((origX2 - origX1) * 0.20);
            var padH = (int)Math.Round((origY2 - origY1) * 0.20);

            var capture = context.State.Capture.Capture(window.Hwnd, maxImageDimension: 0, quality: 90);
            using var input = new MemoryStream(capture.Data);
            using var full = new Bitmap(input);

            var cropX = Math.Clamp(origX1 - padW, 0, Math.Max(0, full.Width - 1));
            var cropY = Math.Clamp(origY1 - padH, 0, Math.Max(0, full.Height - 1));
            var cropRight = Math.Clamp(origX2 + padW, cropX + 1, full.Width);
            var cropBottom = Math.Clamp(origY2 + padH, cropY + 1, full.Height);
            var cropW = cropRight - cropX;
            var cropH = cropBottom - cropY;
            if (cropW <= 0 || cropH <= 0)
                return Task.FromResult(ToolResult.Error("Crop region is empty after clamping to image bounds."));

            using var cropped = full.Clone(new Rectangle(cropX, cropY, cropW, cropH), full.PixelFormat);
            var data = EncodeJpeg(cropped, 90);
            context.State.ZoomContexts[pid] = new ZoomContext(cropX, cropY, cropW, cropH, ratio, window.WindowId);

            return Task.FromResult(new ToolResult
            {
                Content =
                [
                    ContentBlock.ImageBlock(data, "image/jpeg"),
                    ContentBlock.TextBlock("✅ Zoomed region captured at native resolution. To click a target in this image, use click(pid, window_id, x, y, from_zoom=true) where x,y are pixel coordinates in this zoomed image.")
                ],
                IsError = false
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Zoom failed: {ex.Message}"));
        }
    }

    private static byte[] EncodeJpeg(Bitmap bitmap, long quality)
    {
        using var ms = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 1, 100));
        bitmap.Save(ms, encoder, parameters);
        return ms.ToArray();
    }
}
