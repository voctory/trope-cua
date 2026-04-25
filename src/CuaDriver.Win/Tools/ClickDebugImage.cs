using CuaDriver.Win.Capture;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

internal static class ClickDebugImage
{
    public static ToolResult? Validate(ClickTargetArgs target, bool fromZoom, string? debugImageOut)
    {
        if (string.IsNullOrWhiteSpace(debugImageOut))
            return null;

        if (target.HasElement)
            return ToolResult.Error("debug_image_out only applies to pixel clicks (x, y); element_index clicks do not have a coordinate to verify.");
        if (fromZoom)
            return ToolResult.Error("debug_image_out is incompatible with from_zoom because the received x/y are in zoom-crop space, not window-local screenshot space.");
        if (target.WindowId is null)
            return ToolResult.Error("debug_image_out requires window_id so the tool can capture the window for the crosshair overlay.");

        return null;
    }

    public static ToolResult? Write(WindowInfo window, double x, double y, int maxImageDimension, string? debugImageOut)
    {
        if (string.IsNullOrWhiteSpace(debugImageOut))
            return null;

        try
        {
            DebugCrosshair.WriteCrosshair(
                window,
                new System.Drawing.PointF((float)x, (float)y),
                maxImageDimension,
                debugImageOut);
            return null;
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"debug_image_out write failed: {ex.Message}. Not dispatching click; fix the path and retry.");
        }
    }
}
