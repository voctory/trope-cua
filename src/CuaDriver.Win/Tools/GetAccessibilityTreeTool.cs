using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

public sealed class GetAccessibilityTreeTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "get_accessibility_tree",
        "Alias of get_window_state in ax mode for one call.",
        JsonArgs.Schema(
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND.")),
            ("query", JsonArgs.Prop("string", "Optional tree filter."))),
        ReadOnly: true,
        Idempotent: false);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var old = context.State.Config;
        context.State.Config = old with { CaptureMode = CaptureMode.Ax };
        try
        {
            return await new GetWindowStateTool().InvokeAsync(args, context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            context.State.Config = old;
        }
    }
}
