using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CuaDriver.Win;

internal enum CaptureMode
{
    Som,
    Ax,
    Vision
}

internal sealed record DriverConfig
{
    private const string ConfigDirectoryEnvironmentVariable = "TROPE_CUA_CONFIG_DIR";
    internal const int MinImageDimension = 0;
    internal const int MaxImageDimensionLimit = 8192;
    internal const int MinTcpPort = 1;
    internal const int MaxTcpPort = 65535;

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 2;

    [JsonPropertyName("capture_mode")]
    public CaptureMode CaptureMode { get; init; } = CaptureMode.Som;

    [JsonPropertyName("max_image_dimension")]
    public int MaxImageDimension { get; init; } = 1024;

    [JsonPropertyName("chromium_debugging_port")]
    public int? ChromiumDebuggingPort { get; init; }

    [JsonPropertyName("allow_parent_sendinput")]
    public bool AllowParentSendInput { get; init; } = false;

    [JsonPropertyName("agent_cursor")]
    public AgentCursorConfig AgentCursor { get; init; } = new();

    public static string ConfigDirectory
    {
        get
        {
            var overrideDirectory = Environment.GetEnvironmentVariable(ConfigDirectoryEnvironmentVariable);
            return string.IsNullOrWhiteSpace(overrideDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "trope-cua")
                : Path.GetFullPath(overrideDirectory);
        }
    }

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static DriverConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return new DriverConfig();

            var json = File.ReadAllText(ConfigPath);
            return (JsonSerializer.Deserialize<DriverConfig>(json, JsonUtil.SerializerOptions) ?? new DriverConfig()).Normalize();
        }
        catch
        {
            return new DriverConfig();
        }
    }

    public void Save()
    {
        AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonUtil.SerializerOptions));
    }

    public JsonObject ToJsonObject() => JsonUtil.ToJsonObject(this);

    public DriverConfig Normalize()
    {
        var maxImageDimension = SchemaVersion < 2 && MaxImageDimension == 1568 ? 1024 : MaxImageDimension;
        return this with
        {
            SchemaVersion = 2,
            MaxImageDimension = Math.Clamp(maxImageDimension, MinImageDimension, MaxImageDimensionLimit),
            ChromiumDebuggingPort = NormalizeTcpPortOrNull(ChromiumDebuggingPort),
            AgentCursor = AgentCursor.Normalize()
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

    internal static int ValidateTcpPort(int port)
    {
        if (port is < MinTcpPort or > MaxTcpPort)
            throw new ArgumentException($"TCP port must be between {MinTcpPort} and {MaxTcpPort}.");
        return port;
    }

    private static int? NormalizeTcpPortOrNull(int? port) =>
        port is >= MinTcpPort and <= MaxTcpPort ? port : null;
}

internal sealed record AgentCursorConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("motion")]
    public AgentCursorMotionConfig Motion { get; init; } = new();

    public AgentCursorConfig Normalize() => this with
    {
        Motion = Motion.Normalize()
    };
}

internal sealed record AgentCursorMotionConfig
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
    public double GlideDurationMs { get; init; } = 160;

    [JsonPropertyName("dwell_after_click_ms")]
    public double DwellAfterClickMs { get; init; } = 80;

    [JsonPropertyName("idle_hide_ms")]
    public double IdleHideMs { get; init; } = 20000;

    [JsonPropertyName("press_duration_ms")]
    public double PressDurationMs { get; init; } = 120;

    public AgentCursorMotionConfig Normalize() => this with
    {
        StartHandle = Clamp(StartHandle, 0, 1),
        EndHandle = Clamp(EndHandle, 0, 1),
        ArcSize = Clamp(ArcSize, 0, 1),
        ArcFlow = Clamp(ArcFlow, -1, 1),
        Spring = Clamp(Spring, 0.3, 1),
        GlideDurationMs = Clamp(GlideDurationMs, 50, 5000),
        DwellAfterClickMs = Clamp(DwellAfterClickMs, 0, 5000),
        IdleHideMs = Clamp(IdleHideMs, 0, 60000),
        PressDurationMs = Clamp(PressDurationMs, 0, 5000)
    };

    public AgentCursorMotionConfig WithOverrides(
        double? startHandle,
        double? endHandle,
        double? arcSize,
        double? arcFlow,
        double? spring,
        double? glideDurationMs,
        double? dwellAfterClickMs,
        double? idleHideMs,
        double? pressDurationMs) => (this with
        {
            StartHandle = startHandle ?? StartHandle,
            EndHandle = endHandle ?? EndHandle,
            ArcSize = arcSize ?? ArcSize,
            ArcFlow = arcFlow ?? ArcFlow,
            Spring = spring ?? Spring,
            GlideDurationMs = glideDurationMs ?? GlideDurationMs,
            DwellAfterClickMs = dwellAfterClickMs ?? DwellAfterClickMs,
            IdleHideMs = idleHideMs ?? IdleHideMs,
            PressDurationMs = pressDurationMs ?? PressDurationMs
        }).Normalize();

    private static double Clamp(double value, double min, double max) =>
        double.IsFinite(value) ? Math.Min(max, Math.Max(min, value)) : min;
}
