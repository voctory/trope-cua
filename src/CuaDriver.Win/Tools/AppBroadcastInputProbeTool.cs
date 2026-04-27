using System.Text.Json.Nodes;
using CuaDriver.Win.HardCases;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

internal sealed class AppBroadcastInputProbeTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "appbroadcast_input_probe",
        "Probe whether InputInjector.TryCreateForAppBroadcastOnly injects into the parent cursor on this machine.",
        JsonArgs.Schema(
            ("appbroadcast_only", JsonArgs.Prop("boolean", "Use TryCreateForAppBroadcastOnly instead of generic TryCreate. Default true.")),
            ("probe", JsonArgs.EnumProp("Probe to run. Default mouse_delta.", "mouse_delta", "mouse_click", "keyboard_key", "keyboard_text", "broadcast_status", "broadcast_plugins", "api_context", "gamebar_services")),
            ("x", JsonArgs.Prop("integer", "Screen X coordinate for mouse_click.")),
            ("y", JsonArgs.Prop("integer", "Screen Y coordinate for mouse_click.")),
            ("key", JsonArgs.Prop("string", "Key for keyboard_key probe. Default f24.")),
            ("text", JsonArgs.Prop("string", "Text for keyboard_text probe.")),
            ("press_enter", JsonArgs.Prop("boolean", "Press Enter after keyboard_text. Default false.")),
            ("timeout_ms", JsonArgs.Prop("integer", "Timeout for gamebar_services probe. Default 10000."))),
        ReadOnly: false,
        Idempotent: false);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var appBroadcastOnly = JsonArgs.OptionalBool(args, "appbroadcast_only", true);
        var probe = JsonArgs.OptionalString(args, "probe") ?? "mouse_delta";
        var result = probe switch
        {
            "mouse_delta" => AppBroadcastInputInjector.ProbeMouseDelta(appBroadcastOnly),
            "mouse_click" => AppBroadcastInputInjector.ProbeMouseClick(appBroadcastOnly, JsonArgs.OptionalInt(args, "x") ?? 0, JsonArgs.OptionalInt(args, "y") ?? 0),
            "keyboard_key" => AppBroadcastInputInjector.ProbeKeyboardKey(appBroadcastOnly, JsonArgs.OptionalString(args, "key") ?? "f24"),
            "keyboard_text" => AppBroadcastInputInjector.ProbeKeyboardText(appBroadcastOnly, JsonArgs.OptionalString(args, "text") ?? "", JsonArgs.OptionalBool(args, "press_enter")),
            "broadcast_status" => AppBroadcastInputInjector.ProbeBroadcastStatus(),
            "broadcast_plugins" => AppBroadcastInputInjector.ProbeBroadcastPlugins(),
            "api_context" => AppBroadcastInputInjector.ProbeApiContext(),
            "gamebar_services" => AppBroadcastInputInjector.ProbeGameBarServices(JsonArgs.OptionalInt(args, "timeout_ms") ?? 10_000),
            _ => AppBroadcastInputInjector.ProbeMouseDelta(appBroadcastOnly)
        };
        return Task.FromResult(ToolResult.Text(result.Text, result.StructuredContent, result.IsError));
    }
}
