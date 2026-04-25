using System.Globalization;
using System.Text.Json.Nodes;

namespace CuaDriver.Win.HardCases;

internal sealed record ChildSessionHostSnapshot(bool HostRunning, int? ChildSessionId, string State, string[] Log)
{
    public string ToStatusText()
    {
        var child = ChildSessionId?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return HostRunning
            ? $"host_running=true child_session_id={child} state=\"{State}\""
            : $"host_running=false child_session_id={child}";
    }

    public JsonObject ToJsonObject() => new()
    {
        ["host_running"] = HostRunning,
        ["child_session_id"] = ChildSessionId,
        ["state"] = HostRunning ? State : "stopped",
        ["log"] = JsonArrayFrom(Log)
    };

    private static JsonArray JsonArrayFrom(IEnumerable<string> values)
    {
        var arr = new JsonArray();
        foreach (var value in values)
            arr.Add(value);
        return arr;
    }
}
