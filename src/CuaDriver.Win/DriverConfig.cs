using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Automation;
using CuaDriver.Win.Cursor;
using CuaDriver.Win.Recording;

namespace CuaDriver.Win;

public enum CaptureMode
{
    Som,
    Ax,
    Vision
}

public sealed record DriverConfig
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("capture_mode")]
    public CaptureMode CaptureMode { get; init; } = CaptureMode.Som;

    [JsonPropertyName("max_image_dimension")]
    public int MaxImageDimension { get; init; } = 1568;

    [JsonPropertyName("chromium_debugging_port")]
    public int? ChromiumDebuggingPort { get; init; }

    [JsonPropertyName("allow_parent_sendinput")]
    public bool AllowParentSendInput { get; init; } = false;

    [JsonPropertyName("agent_cursor")]
    public AgentCursorConfig AgentCursor { get; init; } = new();

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cua-driver-win");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static DriverConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return new DriverConfig();

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<DriverConfig>(json, JsonUtil.SerializerOptions) ?? new DriverConfig();
        }
        catch
        {
            return new DriverConfig();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDirectory);
        var tmp = Path.Combine(ConfigDirectory, $"config.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonUtil.SerializerOptions));
        File.Move(tmp, ConfigPath, overwrite: true);
    }

    public DriverConfig With(string key, string value)
    {
        return key.ToLowerInvariant() switch
        {
            "capture_mode" => this with { CaptureMode = ParseCaptureMode(value) },
            "max_image_dimension" => this with { MaxImageDimension = int.Parse(value, CultureInfo.InvariantCulture) },
            "chromium_debugging_port" => this with { ChromiumDebuggingPort = string.IsNullOrWhiteSpace(value) ? null : int.Parse(value, CultureInfo.InvariantCulture) },
            "allow_parent_sendinput" => this with { AllowParentSendInput = bool.Parse(value) },
            "agent_cursor.enabled" => this with { AgentCursor = AgentCursor with { Enabled = bool.Parse(value) } },
            "agent_cursor.motion.start_handle" => this with { AgentCursor = AgentCursor with { Motion = AgentCursor.Motion with { StartHandle = double.Parse(value, CultureInfo.InvariantCulture) } } },
            "agent_cursor.motion.end_handle" => this with { AgentCursor = AgentCursor with { Motion = AgentCursor.Motion with { EndHandle = double.Parse(value, CultureInfo.InvariantCulture) } } },
            "agent_cursor.motion.arc_size" => this with { AgentCursor = AgentCursor with { Motion = AgentCursor.Motion with { ArcSize = double.Parse(value, CultureInfo.InvariantCulture) } } },
            "agent_cursor.motion.arc_flow" => this with { AgentCursor = AgentCursor with { Motion = AgentCursor.Motion with { ArcFlow = double.Parse(value, CultureInfo.InvariantCulture) } } },
            "agent_cursor.motion.spring" => this with { AgentCursor = AgentCursor with { Motion = AgentCursor.Motion with { Spring = double.Parse(value, CultureInfo.InvariantCulture) } } },
            "agent_cursor.motion.glide_duration_ms" => this with { AgentCursor = AgentCursor with { Motion = AgentCursor.Motion with { GlideDurationMs = double.Parse(value, CultureInfo.InvariantCulture) } } },
            "agent_cursor.motion.dwell_after_click_ms" => this with { AgentCursor = AgentCursor with { Motion = AgentCursor.Motion with { DwellAfterClickMs = double.Parse(value, CultureInfo.InvariantCulture) } } },
            "agent_cursor.motion.idle_hide_ms" => this with { AgentCursor = AgentCursor with { Motion = AgentCursor.Motion with { IdleHideMs = double.Parse(value, CultureInfo.InvariantCulture) } } },
            _ => throw new ArgumentException($"Unknown config key: {key}")
        };
    }

    public static CaptureMode ParseCaptureMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "som" => CaptureMode.Som,
            "ax" => CaptureMode.Ax,
            "vision" => CaptureMode.Vision,
            "screenshot" => CaptureMode.Vision,
            _ => throw new ArgumentException("capture_mode must be one of som, ax, vision")
        };
    }
}

public sealed record AgentCursorConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("motion")]
    public AgentCursorMotionConfig Motion { get; init; } = new();
}

public sealed record AgentCursorMotionConfig
{
    [JsonPropertyName("start_handle")]
    public double StartHandle { get; init; } = 0.3;

    [JsonPropertyName("end_handle")]
    public double EndHandle { get; init; } = 0.3;

    [JsonPropertyName("arc_size")]
    public double ArcSize { get; init; } = 0.25;

    [JsonPropertyName("arc_flow")]
    public double ArcFlow { get; init; } = 0.0;

    [JsonPropertyName("spring")]
    public double Spring { get; init; } = 0.72;

    [JsonPropertyName("glide_duration_ms")]
    public double GlideDurationMs { get; init; } = 750;

    [JsonPropertyName("dwell_after_click_ms")]
    public double DwellAfterClickMs { get; init; } = 400;

    [JsonPropertyName("idle_hide_ms")]
    public double IdleHideMs { get; init; } = 20000;
}

public sealed class DriverState
{
    public DriverConfig Config { get; set; } = DriverConfig.Load();
    public Uia.UiAutomationTree UiaTree { get; } = new();
    public Capture.WindowCapture Capture { get; } = new();
    public ConcurrentDictionary<(int Pid, long WindowId), double> ImageResizeRatio { get; } = new();
    public ConcurrentDictionary<int, ZoomContext> ZoomContexts { get; } = new();
    public ConcurrentDictionary<(int Pid, long WindowId), IntPtr> LastTargetHwnd { get; } = new();
    public ConcurrentDictionary<(int Pid, long WindowId), AutomationElement> LastUiaTextTarget { get; } = new();
    public AgentCursorOverlay AgentCursor { get; } = new();
    public RecordingSession Recording { get; } = new();
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public DriverState()
    {
        var motion = Config.AgentCursor.Motion;
        AgentCursor.UpdateMotion(
            motion.StartHandle,
            motion.EndHandle,
            motion.ArcSize,
            motion.ArcFlow,
            motion.Spring,
            motion.GlideDurationMs,
            motion.DwellAfterClickMs,
            motion.IdleHideMs);
        AgentCursor.SetEnabled(Config.AgentCursor.Enabled);
    }
}

public sealed record ZoomContext(int OriginX, int OriginY, int Width, int Height, double Ratio, long WindowId);

public static class JsonUtil
{
    public static readonly JsonSerializerOptions SerializerOptions = Create(writeIndented: true);
    public static readonly JsonSerializerOptions LineSerializerOptions = Create(writeIndented: false);

    private static JsonSerializerOptions Create(bool writeIndented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = writeIndented,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
