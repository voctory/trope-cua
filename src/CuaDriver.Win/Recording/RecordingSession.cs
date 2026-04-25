using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Capture;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Recording;

public sealed record RecordingState(bool Enabled, string? OutputDirectory, int NextTurn, string? LastError);

public sealed class RecordingSession
{
    private readonly object _gate = new();
    private bool _enabled;
    private string? _outputDirectory;
    private int _nextTurn = 1;
    private long _sessionStartTimestamp;
    private DateTimeOffset _sessionStartWallClock;
    private string? _lastError;

    public RecordingState CurrentState()
    {
        lock (_gate)
            return new RecordingState(_enabled, _outputDirectory, _nextTurn, _lastError);
    }

    public bool IsEnabled
    {
        get
        {
            lock (_gate) return _enabled;
        }
    }

    public void Configure(bool enabled, string? outputDirectory)
    {
        lock (_gate)
        {
            if (_enabled && _outputDirectory is not null)
                WriteFinalSessionJsonLocked();

            if (!enabled)
            {
                _enabled = false;
                _outputDirectory = null;
                _nextTurn = 1;
                _sessionStartTimestamp = 0;
                _lastError = null;
                return;
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("output_dir is required when enabling recording.");

            var resolved = ExpandPath(outputDirectory);
            Directory.CreateDirectory(resolved);
            _enabled = true;
            _outputDirectory = resolved;
            _nextTurn = 1;
            _sessionStartTimestamp = Stopwatch.GetTimestamp();
            _sessionStartWallClock = DateTimeOffset.UtcNow;
            _lastError = null;
            WriteInitialSessionJsonLocked();
        }
    }

    public void Record(
        string toolName,
        JsonObject arguments,
        ToolResult result,
        ToolContext context,
        long actionStartTimestamp)
    {
        string turnDir;
        int turnIndex;
        long sessionStartTimestamp;
        lock (_gate)
        {
            if (!_enabled || _outputDirectory is null)
                return;

            turnIndex = _nextTurn++;
            turnDir = Path.Combine(_outputDirectory, $"turn-{turnIndex:00000}");
            sessionStartTimestamp = _sessionStartTimestamp;
        }

        try
        {
            Directory.CreateDirectory(turnDir);
            var pid = TryGetInt(arguments, "pid");
            var window = ResolveWindow(arguments, pid);
            var clickPoint = ResolveClickPoint(arguments, context, window);

            WriteActionJson(turnDir, toolName, arguments, result, pid, window, clickPoint, sessionStartTimestamp, actionStartTimestamp);

            if (pid is not null && window is not null)
            {
                WriteAppStateJson(turnDir, pid.Value, window.WindowId, context);
                var screenshotPath = Path.Combine(turnDir, "screenshot.png");
                if (WriteScreenshotPng(screenshotPath, window, context) && clickPoint is not null)
                    WriteClickMarker(Path.Combine(turnDir, "click.png"), screenshotPath, clickPoint.Value, window);
            }

            SetLastError(null);
        }
        catch (Exception ex)
        {
            // Recording must never poison the action path.
            SetLastError($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void SetLastError(string? lastError)
    {
        lock (_gate)
            _lastError = lastError;
    }

    private static void WriteActionJson(
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
            ["result_summary"] = FirstText(result),
            ["result_structured"] = result.StructuredContent?.DeepClone(),
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["t_ms_from_session_start"] = ElapsedMs(sessionStartTimestamp, now),
            ["t_start_ms_from_session_start"] = ElapsedMs(sessionStartTimestamp, actionStartTimestamp == 0 ? now : actionStartTimestamp),
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

        File.WriteAllText(
            Path.Combine(turnDir, "action.json"),
            payload.ToJsonString(JsonUtil.SerializerOptions));
    }

    private static void WriteAppStateJson(string turnDir, int pid, long windowId, ToolContext context)
    {
        try
        {
            var snapshot = context.State.UiaTree.Snapshot(pid, windowId);
            File.WriteAllText(
                Path.Combine(turnDir, "app_state.json"),
                JsonSerializer.Serialize(snapshot, JsonUtil.SerializerOptions));
        }
        catch
        {
            // App state is best-effort; screenshots and action metadata are still useful.
        }
    }

    private static bool WriteScreenshotPng(string path, WindowInfo window, ToolContext context)
    {
        try
        {
            var capture = WindowCapture.Capture(window.Hwnd, context.State.Config.MaxImageDimension);
            context.State.ImageResizeRatio[(window.Pid, window.WindowId)] = capture.Width > 0
                ? capture.OriginalWidth / (double)capture.Width
                : 1.0;
            using var ms = new MemoryStream(capture.Data);
            using var image = Image.FromStream(ms);
            image.Save(path, ImageFormat.Png);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteClickMarker(string destination, string screenshotPath, PointF screenPoint, WindowInfo window)
    {
        try
        {
            using var image = Image.FromFile(screenshotPath);
            using var bitmap = new Bitmap(image);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var localX = screenPoint.X - window.Bounds.X;
            var localY = screenPoint.Y - window.Bounds.Y;
            var scaleX = bitmap.Width / (float)Math.Max(1, window.Bounds.Width);
            var scaleY = bitmap.Height / (float)Math.Max(1, window.Bounds.Height);
            var x = localX * scaleX;
            var y = localY * scaleY;

            using var fill = new SolidBrush(Color.FromArgb(210, 255, 64, 64));
            using var outline = new Pen(Color.White, 2);
            graphics.FillEllipse(fill, x - 8, y - 8, 16, 16);
            graphics.DrawEllipse(outline, x - 8, y - 8, 16, 16);
            bitmap.Save(destination, ImageFormat.Png);
        }
        catch
        {
            // Marker is best-effort.
        }
    }

    private static WindowInfo? ResolveWindow(JsonObject arguments, int? pid)
    {
        var windowId = TryGetLong(arguments, "window_id");
        if (windowId is not null)
            return WindowEnumerator.Find(windowId.Value);
        return pid is null ? null : WindowEnumerator.MainWindowForPid(pid.Value);
    }

    private static PointF? ResolveClickPoint(JsonObject arguments, ToolContext context, WindowInfo? window)
    {
        if (window is null)
            return null;

        var x = TryGetDouble(arguments, "x");
        var y = TryGetDouble(arguments, "y");
        if (x is not null && y is not null)
        {
            if (TryGetBool(arguments, "from_zoom") == true &&
                context.State.ZoomContexts.TryGetValue(window.Pid, out var zoom) &&
                zoom.WindowId == window.WindowId)
            {
                return new PointF(
                    (float)(window.Bounds.X + zoom.OriginX + x.Value),
                    (float)(window.Bounds.Y + zoom.OriginY + y.Value));
            }

            var ratio = context.State.ImageResizeRatio.TryGetValue((window.Pid, window.WindowId), out var r) ? r : 1.0;
            return new PointF(
                (float)(window.Bounds.X + x.Value * ratio),
                (float)(window.Bounds.Y + y.Value * ratio));
        }

        var index = TryGetInt(arguments, "element_index");
        if (index is not null)
        {
            try
            {
                var element = context.State.UiaTree.GetCachedElement(window.Pid, window.WindowId, index.Value);
                var rect = element.Current.BoundingRectangle;
                if (!rect.IsEmpty)
                    return new PointF((float)(rect.X + rect.Width / 2), (float)(rect.Y + rect.Height / 2));
            }
            catch
            {
                // Missing element cache is normal during replay.
            }
        }

        return null;
    }

    private void WriteInitialSessionJsonLocked()
    {
        if (_outputDirectory is null)
            return;
        WriteSessionJsonLocked(endedTimestamp: 0);
    }

    private void WriteFinalSessionJsonLocked()
    {
        if (_outputDirectory is null || _sessionStartTimestamp == 0)
            return;
        WriteSessionJsonLocked(Stopwatch.GetTimestamp());
    }

    private void WriteSessionJsonLocked(long endedTimestamp)
    {
        var payload = new JsonObject
        {
            ["schema_version"] = 1,
            ["started_at_wall_clock"] = _sessionStartWallClock.ToString("O"),
            ["started_at_monotonic_ns"] = _sessionStartTimestamp,
            ["ended_at_monotonic_ns"] = endedTimestamp,
            ["duration_ms"] = endedTimestamp == 0 ? 0 : ElapsedMs(_sessionStartTimestamp, endedTimestamp),
            ["video"] = new JsonObject
            {
                ["path"] = "recording.mp4",
                ["present"] = false,
                ["width"] = 0,
                ["height"] = 0,
                ["framerate_target"] = 0,
                ["frame_count"] = 0,
            },
            ["cursor"] = new JsonObject
            {
                ["path"] = "cursor.jsonl",
                ["present"] = false,
                ["sample_hz"] = 0,
                ["sample_count"] = 0,
            }
        };
        File.WriteAllText(Path.Combine(_outputDirectory!, "session.json"), payload.ToJsonString(JsonUtil.SerializerOptions));
    }

    private static string FirstText(ToolResult result) =>
        result.Content.FirstOrDefault(c => c.Type == "text" && c.Text is not null)?.Text ?? "";

    private static long ElapsedMs(long start, long end)
    {
        if (start <= 0 || end < start)
            return 0;
        return (long)((end - start) * 1000.0 / Stopwatch.Frequency);
    }

    private static string ExpandPath(string path)
    {
        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal) || path.StartsWith("~/", StringComparison.Ordinal))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return Path.GetFullPath(path);
    }

    private static int? TryGetInt(JsonObject obj, string key)
    {
        try { return obj.TryGetPropertyValue(key, out var node) && node is not null ? node.GetValue<int>() : null; }
        catch { return null; }
    }

    private static long? TryGetLong(JsonObject obj, string key)
    {
        try { return obj.TryGetPropertyValue(key, out var node) && node is not null ? node.GetValue<long>() : null; }
        catch { return null; }
    }

    private static double? TryGetDouble(JsonObject obj, string key)
    {
        try { return obj.TryGetPropertyValue(key, out var node) && node is not null ? node.GetValue<double>() : null; }
        catch { return null; }
    }

    private static bool? TryGetBool(JsonObject obj, string key)
    {
        try { return obj.TryGetPropertyValue(key, out var node) && node is not null ? node.GetValue<bool>() : null; }
        catch { return null; }
    }
}
