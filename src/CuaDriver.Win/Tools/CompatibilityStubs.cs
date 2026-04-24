using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

public sealed class SetAgentCursorEnabledTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "set_agent_cursor_enabled",
        "Enable or disable the click-through visual agent cursor overlay. This never moves the real parent-session cursor.",
        JsonArgs.Schema(("enabled", JsonArgs.Prop("boolean", "Requested state."))),
        Destructive: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var enabled = JsonArgs.OptionalBool(args, "enabled", true);
        context.State.AgentCursor.SetEnabled(enabled);
        return Task.FromResult(ToolResult.Text("✅ " + context.State.AgentCursor.StateJson()));
    }
}

public sealed class GetAgentCursorStateTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new("get_agent_cursor_state", "Return the visual agent cursor overlay state.", JsonArgs.Schema(), ReadOnly: true);
    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken) => Task.FromResult(ToolResult.Text("✅ " + context.State.AgentCursor.StateJson()));
}

public sealed class SetRecordingTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new("set_recording", "Compatibility stub for trajectory recording.", JsonArgs.Schema(("enabled", JsonArgs.Prop("boolean", "Requested state."))), Destructive: true);
    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken) => Task.FromResult(ToolResult.Text("✅ recording stub acknowledged; implement renderer/recorder as needed."));
}

public sealed class GetRecordingStateTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new("get_recording_state", "Compatibility stub for trajectory recording state.", JsonArgs.Schema(), ReadOnly: true);
    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken) => Task.FromResult(ToolResult.Text("✅ {\"enabled\":false}"));
}
