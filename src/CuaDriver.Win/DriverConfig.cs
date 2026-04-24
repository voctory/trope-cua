using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Automation;
using CuaDriver.Win.Cursor;

namespace CuaDriver.Win;

public enum CaptureMode
{
    Som,
    Ax,
    Vision
}

public sealed record DriverConfig
{
    public CaptureMode CaptureMode { get; init; } = CaptureMode.Som;
    public int MaxImageDimension { get; init; } = 1600;
    public int? ChromiumDebuggingPort { get; init; }
    public bool AllowParentSendInput { get; init; } = false;

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
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonUtil.SerializerOptions));
    }

    public DriverConfig With(string key, string value)
    {
        return key.ToLowerInvariant() switch
        {
            "capture_mode" => this with { CaptureMode = ParseCaptureMode(value) },
            "max_image_dimension" => this with { MaxImageDimension = int.Parse(value) },
            "chromium_debugging_port" => this with { ChromiumDebuggingPort = string.IsNullOrWhiteSpace(value) ? null : int.Parse(value) },
            "allow_parent_sendinput" => this with { AllowParentSendInput = bool.Parse(value) },
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

public sealed class DriverState
{
    public DriverConfig Config { get; set; } = DriverConfig.Load();
    public Uia.UiAutomationTree UiaTree { get; } = new();
    public Capture.WindowCapture Capture { get; } = new();
    public ConcurrentDictionary<(int Pid, long WindowId), double> ImageResizeRatio { get; } = new();
    public ConcurrentDictionary<(int Pid, long WindowId), IntPtr> LastTargetHwnd { get; } = new();
    public ConcurrentDictionary<(int Pid, long WindowId), AutomationElement> LastUiaTextTarget { get; } = new();
    public AgentCursorOverlay AgentCursor { get; } = new();
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
}

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
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
