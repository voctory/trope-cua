using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

internal sealed record ClickTargetArgs(
    int Pid,
    long? WindowId,
    int? ElementIndex,
    double? X,
    double? Y,
    string[] Modifiers)
{
    public bool HasElement => ElementIndex is not null;

    public static ToolResult? TryParse(JsonObject args, string toolName, out ClickTargetArgs parsed)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var windowId = JsonArgs.OptionalLong(args, "window_id");
        var index = JsonArgs.OptionalInt(args, "element_index");
        var x = JsonArgs.OptionalDouble(args, "x");
        var y = JsonArgs.OptionalDouble(args, "y");
        var modifiers = JsonArgs.OptionalStringArray(args, "modifier", "modifiers");
        parsed = new ClickTargetArgs(pid, windowId, index, x, y, modifiers);

        if (index is not null && (x is not null || y is not null))
            return ToolResult.Error("Provide either element_index or x/y, not both.");
        if (index is null && (x is null || y is null))
            return ToolResult.Error("Provide element_index or both x and y.");
        if (index is not null && windowId is null)
            return ToolResult.Error($"window_id is required for element_index {toolName}.");

        return null;
    }
}
