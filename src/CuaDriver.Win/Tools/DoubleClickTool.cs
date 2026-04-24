using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

public sealed class DoubleClickTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "double_click",
        "Pixel double-click convenience wrapper around click(count=2).",
        JsonArgs.Schema(
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND.")),
            ("x", JsonArgs.Prop("number", "Window-local screenshot X.")),
            ("y", JsonArgs.Prop("number", "Window-local screenshot Y.")),
            ("cdp_port", JsonArgs.Prop("integer", "Optional Chromium remote debugging port."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        args["count"] = 2;
        return new ClickTool().InvokeAsync(args, context, cancellationToken);
    }
}
