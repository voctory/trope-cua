using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CuaDriver.Win.Cursor;

internal sealed record AgentCursorMotion
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

    public static AgentCursorMotion Default { get; } = new();

    public JsonObject ToJsonObject() => JsonUtil.ToJsonObject(this);

    public AgentCursorMotion WithOverrides(
        double? startHandle,
        double? endHandle,
        double? arcSize,
        double? arcFlow,
        double? spring,
        double? glideDurationMs,
        double? dwellAfterClickMs,
        double? idleHideMs,
        double? pressDurationMs) => this with
        {
            StartHandle = Clamp(startHandle ?? StartHandle, 0, 1),
            EndHandle = Clamp(endHandle ?? EndHandle, 0, 1),
            ArcSize = Clamp(arcSize ?? ArcSize, 0, 1),
            ArcFlow = Clamp(arcFlow ?? ArcFlow, -1, 1),
            Spring = Clamp(spring ?? Spring, 0.3, 1),
            GlideDurationMs = Clamp(glideDurationMs ?? GlideDurationMs, 50, 5000),
            DwellAfterClickMs = Clamp(dwellAfterClickMs ?? DwellAfterClickMs, 0, 5000),
            IdleHideMs = Clamp(idleHideMs ?? IdleHideMs, 0, 60000),
            PressDurationMs = Clamp(pressDurationMs ?? PressDurationMs, 0, 5000),
        };

    private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));
}
