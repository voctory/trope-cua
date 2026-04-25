using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Recording;

internal static class RecordingActionWriter
{
    public static void Write(
        string turnDir,
        string toolName,
        JsonObject arguments,
        ToolResult result,
        int? pid,
        WindowInfo? window,
        PointF? clickPoint,
        long sessionStartTimestamp,
        long actionStartTimestamp)
    {
        var now = Stopwatch.GetTimestamp();
        var payload = new JsonObject
        {
            ["tool"] = toolName,
            ["arguments"] = arguments.DeepClone(),
            ["result_summary"] = result.FirstText(),
            ["result_structured"] = result.StructuredContent?.DeepClone(),
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["t_ms_from_session_start"] = RecordingClock.ElapsedMs(sessionStartTimestamp, now),
            ["t_start_ms_from_session_start"] = RecordingClock.ElapsedMs(sessionStartTimestamp, actionStartTimestamp == 0 ? now : actionStartTimestamp),
        };

        if (pid is not null)
            payload["pid"] = pid.Value;
        if (window is not null)
        {
            payload["window_id"] = window.WindowId;
            payload["window_bounds"] = new JsonObject
            {
                ["x"] = window.Bounds.X,
                ["y"] = window.Bounds.Y,
                ["width"] = window.Bounds.Width,
                ["height"] = window.Bounds.Height,
            };
        }
        if (clickPoint is not null)
        {
            payload["click_point"] = new JsonObject
            {
                ["x"] = clickPoint.Value.X,
                ["y"] = clickPoint.Value.Y,
            };
        }

        AtomicFile.WriteAllText(
            Path.Combine(turnDir, "action.json"),
            payload.ToJsonString(JsonUtil.SerializerOptions));
    }
}
