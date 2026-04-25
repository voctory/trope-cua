using System.Text.Json.Nodes;
using CuaDriver.Win.Recording;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

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
