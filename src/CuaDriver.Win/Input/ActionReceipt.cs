using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CuaDriver.Win.Input;

internal sealed record ActionReceipt
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("route")]
    public string Route { get; init; } = "";

    [JsonPropertyName("lane")]
    public string Lane { get; init; } = "same_session";

    [JsonPropertyName("background_safe")]
    public bool BackgroundSafe { get; init; } = true;

    [JsonPropertyName("cursor_moved")]
    public bool CursorMoved { get; init; }

    [JsonPropertyName("foreground_changed")]
    public bool ForegroundChanged { get; init; }

    [JsonPropertyName("session")]
    public string Session { get; init; } = "parent";

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    [JsonIgnore]
    public bool ShouldStopFallback => Ok || ForegroundChanged || CursorMoved;

    [JsonIgnore]
    public bool CanTryFallback => !ShouldStopFallback;

    public static ActionReceipt Success(string route, string lane = "same_session") =>
        new() { Ok = true, Route = route, Lane = lane, BackgroundSafe = true, Session = SessionForLane(lane) };

    public static ActionReceipt UnsafeSuccess(string route, string lane = "same_session") =>
        new() { Ok = true, Route = route, Lane = lane, BackgroundSafe = false, Session = SessionForLane(lane) };

    public static ActionReceipt Failure(string route, string reason, string lane = "same_session") =>
        new() { Ok = false, Route = route, Lane = lane, BackgroundSafe = false, Session = SessionForLane(lane), Reason = reason };

    public JsonObject ToJsonObject() => JsonUtil.ToJsonObject(this);

    public string ToJson() => ToJsonObject().ToJsonString(JsonUtil.SerializerOptions);

    private static string SessionForLane(string lane) => lane == "child_session" ? "child" : "parent";
}
