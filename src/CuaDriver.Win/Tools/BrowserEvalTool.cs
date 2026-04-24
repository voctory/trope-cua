using System.Text.Json.Nodes;
using CuaDriver.Win.Browser;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

public sealed class BrowserEvalTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "browser_eval",
        "Chromium CDP Runtime.evaluate with userGesture=true for browser activation-gated flows. Requires cdp_port or config chromium_debugging_port.",
        JsonArgs.Schema(
            ("expression", JsonArgs.Prop("string", "JavaScript expression.")),
            ("cdp_port", JsonArgs.Prop("integer", "Chromium remote debugging port."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var expression = JsonArgs.RequiredString(args, "expression");
        var port = JsonArgs.OptionalInt(args, "cdp_port") ?? context.State.Config.ChromiumDebuggingPort;
        if (port is null)
            return ToolResult.Error("browser_eval requires cdp_port or config chromium_debugging_port.");

        var receipt = await new CdpBrowserBridge(context.State.UiaTree).EvaluateUserGestureAsync(port.Value, expression, cancellationToken).ConfigureAwait(false);
        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
    }
}
