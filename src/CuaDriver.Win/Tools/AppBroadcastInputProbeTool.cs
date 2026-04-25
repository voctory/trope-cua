using System.Text.Json.Nodes;
using CuaDriver.Win.HardCases;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

public sealed class AppBroadcastInputProbeTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "appbroadcast_input_probe",
        "Probe whether InputInjector.TryCreateForAppBroadcastOnly injects into the parent cursor on this machine.",
        JsonArgs.Schema(("appbroadcast_only", JsonArgs.Prop("boolean", "Use TryCreateForAppBroadcastOnly instead of generic TryCreate. Default true."))),
        ReadOnly: false,
        Idempotent: false);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var appBroadcastOnly = JsonArgs.OptionalBool(args, "appbroadcast_only", true);
        var text = AppBroadcastInputInjector.ProbeMouseDelta(appBroadcastOnly);
        var isError = text.Contains("ok=false", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(ToolResult.Text(text, isError));
    }
}
