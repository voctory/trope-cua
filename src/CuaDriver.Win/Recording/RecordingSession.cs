using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Capture;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Recording;

internal sealed record RecordingState(bool Enabled, string? OutputDirectory, int NextTurn, string? LastError);

internal sealed record RecordingTurn(string Directory, long SessionStartTimestamp);

internal sealed class RecordingSession
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

            var resolved = PathHelpers.ExpandUserPathToFullPath(outputDirectory);
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
        var turn = ReserveTurn();
        if (turn is null)
            return;

        try
        {
            Directory.CreateDirectory(turn.Directory);
            var pid = JsonArgs.TryOptionalInt(arguments, "pid");
            var window = ResolveWindow(arguments, pid);
            var clickPoint = ResolveClickPoint(arguments, context, window);

            RecordingActionWriter.Write(turn.Directory, toolName, arguments, result, pid, window, clickPoint, turn.SessionStartTimestamp, actionStartTimestamp);

            if (pid is not null && window is not null)
            {
                WriteAppStateJson(turn.Directory, pid.Value, window.WindowId, context);
                var screenshotPath = Path.Combine(turn.Directory, "screenshot.png");
                if (WriteScreenshotPng(screenshotPath, window, context) && clickPoint is not null)
                    RecordingClickMarker.Write(Path.Combine(turn.Directory, "click.png"), screenshotPath, clickPoint.Value, window);
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

    private RecordingTurn? ReserveTurn()
    {
        lock (_gate)
        {
            if (!_enabled || _outputDirectory is null)
                return null;

            var turnIndex = _nextTurn++;
            return new RecordingTurn(
                Path.Combine(_outputDirectory, $"turn-{turnIndex:00000}"),
                _sessionStartTimestamp);
        }
    }

    private static void WriteAppStateJson(string turnDir, int pid, long windowId, ToolContext context)
    {
        try
        {
            var snapshot = context.State.UiaTree.Snapshot(pid, windowId);
            AtomicFile.WriteAllText(
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
            AtomicFile.WriteAllBytes(path, ImageEncoding.EncodePng(image));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static WindowInfo? ResolveWindow(JsonObject arguments, int? pid)
    {
        var windowId = JsonArgs.TryOptionalLong(arguments, "window_id");
        if (windowId is not null)
            return WindowEnumerator.Find(windowId.Value);
        return pid is null ? null : WindowEnumerator.MainWindowForPid(pid.Value);
    }

    private static PointF? ResolveClickPoint(JsonObject arguments, ToolContext context, WindowInfo? window)
    {
        if (window is null)
            return null;

        var x = JsonArgs.TryOptionalDouble(arguments, "x");
        var y = JsonArgs.TryOptionalDouble(arguments, "y");
        if (x is not null && y is not null)
        {
            if (JsonArgs.TryOptionalBool(arguments, "from_zoom") == true &&
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

        var index = JsonArgs.TryOptionalInt(arguments, "element_index");
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
            ["duration_ms"] = endedTimestamp == 0 ? 0 : RecordingClock.ElapsedMs(_sessionStartTimestamp, endedTimestamp),
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
        AtomicFile.WriteAllText(Path.Combine(_outputDirectory!, "session.json"), payload.ToJsonString(JsonUtil.SerializerOptions));
    }

}
