using System.Text.Json.Nodes;
using CuaDriver.Win.Capture;
using CuaDriver.Win.Uia;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

internal static class ToolJson
{
    public static JsonObject Window(WindowInfo window) => new()
    {
        ["window_id"] = window.WindowId,
        ["pid"] = window.Pid,
        ["app_name"] = window.AppName,
        ["title"] = window.Title,
        ["class_name"] = window.ClassName,
        ["bounds"] = Rect(window.Bounds),
        ["visible"] = window.IsVisible,
        ["minimized"] = window.IsMinimized,
        ["z_index"] = window.ZIndex,
        ["dpi"] = window.Dpi
    };

    public static JsonObject CompactWindow(WindowInfo window) => new()
    {
        ["window_id"] = window.WindowId,
        ["pid"] = window.Pid,
        ["app_name"] = window.AppName,
        ["title"] = window.Title,
        ["bounds"] = Rect(window.Bounds),
        ["visible"] = window.IsVisible,
        ["minimized"] = window.IsMinimized,
        ["z_index"] = window.ZIndex
    };

    public static JsonObject Capture(CapturedImage capture) => new()
    {
        ["route"] = capture.Route,
        ["width"] = capture.Width,
        ["height"] = capture.Height,
        ["original_width"] = capture.OriginalWidth,
        ["original_height"] = capture.OriginalHeight,
        ["scale_factor"] = capture.ScaleFactor,
        ["mime_type"] = capture.MimeType
    };

    public static JsonObject Element(UiElementInfo element) => new()
    {
        ["element_index"] = element.ElementIndex,
        ["control_type"] = element.ControlType,
        ["name"] = element.Name,
        ["automation_id"] = element.AutomationId,
        ["class_name"] = element.ClassName,
        ["bounds"] = Rect(element.Bounds),
        ["enabled"] = element.IsEnabled,
        ["offscreen"] = element.IsOffscreen,
        ["process_id"] = element.ProcessId,
        ["native_window_handle"] = element.NativeWindowHandle,
        ["patterns"] = Array(element.Patterns)
    };

    public static JsonObject UiSnapshotMetrics(UiSnapshotMetrics metrics) => new()
    {
        ["elapsed_ms"] = metrics.ElapsedMs,
        ["control_view_visited"] = metrics.ControlViewVisited,
        ["raw_view_visited"] = metrics.RawViewVisited,
        ["raw_elements_added"] = metrics.RawElementsAdded,
        ["raw_harvested"] = metrics.RawHarvested,
        ["markdown_chars"] = metrics.MarkdownChars
    };

    public static JsonObject Rect(RectDto rect) => new()
    {
        ["x"] = rect.X,
        ["y"] = rect.Y,
        ["width"] = rect.Width,
        ["height"] = rect.Height
    };

    public static JsonArray Array(IEnumerable<string> values)
    {
        var arr = new JsonArray();
        foreach (var value in values)
            arr.Add(value);
        return arr;
    }

    public static JsonArray Array<T>(IEnumerable<T> values, Func<T, JsonObject> map)
    {
        var arr = new JsonArray();
        foreach (var value in values)
            arr.Add(map(value));
        return arr;
    }
}
