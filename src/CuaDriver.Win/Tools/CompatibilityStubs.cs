using System.Text.Json.Nodes;
using CuaDriver.Win.Recording;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

public sealed class SetAgentCursorEnabledTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "set_agent_cursor_enabled",
        "Enable or disable the click-through visual agent cursor overlay. This never moves the real parent-session cursor.",
        JsonArgs.RequiredSchema(["enabled"], ("enabled", JsonArgs.Prop("boolean", "Requested state."))),
        Destructive: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var enabled = JsonArgs.RequiredBool(args, "enabled");
        context.State.AgentCursor.SetEnabled(enabled);
        context.State.Config = context.State.Config with
        {
            AgentCursor = context.State.Config.AgentCursor with { Enabled = enabled }
        };
        context.State.Config.Save();
        var structured = context.State.AgentCursor.StateObject();
        return Task.FromResult(ToolResult.Text("✅ " + structured.ToJsonString(JsonUtil.SerializerOptions), structured));
    }
}

public sealed class GetAgentCursorStateTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new("get_agent_cursor_state", "Return the visual agent cursor overlay state.", JsonArgs.Schema(), ReadOnly: true);
    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var structured = context.State.AgentCursor.StateObject();
        return Task.FromResult(ToolResult.Text("✅ " + structured.ToJsonString(JsonUtil.SerializerOptions), structured));
    }
}

public sealed class SetAgentCursorMotionTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "set_agent_cursor_motion",
        "Tune the visual agent cursor's glide curve, click dwell, and idle hide timing.",
        JsonArgs.Schema(
            ("start_handle", JsonArgs.Prop("number", "Start-handle fraction in [0, 1]. Default 0.3.")),
            ("end_handle", JsonArgs.Prop("number", "End-handle fraction in [0, 1]. Default 0.3.")),
            ("arc_size", JsonArgs.Prop("number", "Perpendicular arc deflection as a fraction of path length. Default 0.25.")),
            ("arc_flow", JsonArgs.Prop("number", "Arc asymmetry in [-1, 1]. Default 0.")),
            ("spring", JsonArgs.Prop("number", "Reserved settle damping knob in [0.3, 1]. Default 0.72.")),
            ("glide_duration_ms", JsonArgs.Prop("number", "Cursor flight duration in milliseconds. Default 750.")),
            ("dwell_after_click_ms", JsonArgs.Prop("number", "Post-click rest time in milliseconds. Default 400.")),
            ("idle_hide_ms", JsonArgs.Prop("number", "Idle time before the overlay hides. 0 disables auto-hide. Default 20000.")),
            ("press_duration_ms", JsonArgs.Prop("number", "Click pulse duration in milliseconds. Default 650."))),
        Destructive: true,
        Idempotent: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var motion = context.State.AgentCursor.UpdateMotion(
            JsonArgs.OptionalDouble(args, "start_handle"),
            JsonArgs.OptionalDouble(args, "end_handle"),
            JsonArgs.OptionalDouble(args, "arc_size"),
            JsonArgs.OptionalDouble(args, "arc_flow"),
            JsonArgs.OptionalDouble(args, "spring"),
            JsonArgs.OptionalDouble(args, "glide_duration_ms"),
            JsonArgs.OptionalDouble(args, "dwell_after_click_ms"),
            JsonArgs.OptionalDouble(args, "idle_hide_ms"),
            JsonArgs.OptionalDouble(args, "press_duration_ms"));

        context.State.Config = context.State.Config with
        {
            AgentCursor = context.State.Config.AgentCursor with
            {
                Motion = context.State.Config.AgentCursor.Motion with
                {
                    StartHandle = motion.StartHandle,
                    EndHandle = motion.EndHandle,
                    ArcSize = motion.ArcSize,
                    ArcFlow = motion.ArcFlow,
                    Spring = motion.Spring,
                    GlideDurationMs = motion.GlideDurationMs,
                    DwellAfterClickMs = motion.DwellAfterClickMs,
                    IdleHideMs = motion.IdleHideMs,
                    PressDurationMs = motion.PressDurationMs
                }
            }
        };
        context.State.Config.Save();

        return Task.FromResult(ToolResult.Text("✅ " + System.Text.Json.JsonSerializer.Serialize(motion, JsonUtil.SerializerOptions)));
    }
}

public sealed class SetRecordingTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "set_recording",
        "Toggle trajectory recording. When enabled, subsequent action tools write turn-NNNNN folders with action.json, app_state.json, screenshot.png, and click.png when applicable.",
        JsonArgs.RequiredSchema(["enabled"],
            ("enabled", JsonArgs.Prop("boolean", "True to start recording subsequent action tool calls; false to stop.")),
            ("output_dir", JsonArgs.Prop("string", "Directory where turn folders are written. Required when enabled=true.")),
            ("video_experimental", JsonArgs.Prop("boolean", "Accepted for schema compatibility; Windows currently records trajectory files only."))),
        Destructive: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var enabled = JsonArgs.RequiredBool(args, "enabled");
        if (JsonArgs.OptionalBool(args, "video_experimental"))
            return Task.FromResult(ToolResult.Error("video_experimental is not implemented on Windows yet; use trajectory recording without video_experimental."));

        try
        {
            context.State.Recording.Configure(enabled, enabled ? JsonArgs.OptionalString(args, "output_dir") : null);
            var state = context.State.Recording.CurrentState();
            var text = state.Enabled
                ? $"✅ Recording enabled -> {state.OutputDirectory}"
                : "✅ Recording disabled.";
            return Task.FromResult(ToolResult.Text(text, RecordingStateObject(state)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to configure recording: {ex.Message}"));
        }
    }

    public static JsonObject RecordingStateObject(RecordingState state) => new()
    {
        ["enabled"] = state.Enabled,
        ["output_dir"] = state.OutputDirectory,
        ["next_turn"] = state.NextTurn,
        ["last_error"] = state.LastError
    };
}

public sealed class GetRecordingStateTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new("get_recording_state", "Return the trajectory recorder state.", JsonArgs.Schema(), ReadOnly: true);
    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var state = context.State.Recording.CurrentState();
        var text = state.Enabled
            ? $"✅ recording: enabled output_dir={state.OutputDirectory} next_turn={state.NextTurn}"
            : "✅ recording: disabled";
        return Task.FromResult(ToolResult.Text(text, SetRecordingTool.RecordingStateObject(state)));
    }
}
