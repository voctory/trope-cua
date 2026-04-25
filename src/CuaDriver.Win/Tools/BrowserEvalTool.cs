using System.Text.Json.Nodes;
using CuaDriver.Win.Browser;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class BrowserEvalTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "browser_eval",
        "Chromium CDP Runtime.evaluate with userGesture=true for browser activation-gated flows. Use this only as the browser-native background lane when UIA/MSAA cannot safely activate the target. Requires cdp_port or config chromium_debugging_port; pass pid/window_id when available so the CDP page is bound to the intended browser window.",
        JsonArgs.RequiredSchema(["expression"],
            ("expression", JsonArgs.Prop("string", "JavaScript expression.")),
            ("cdp_port", JsonArgs.Prop("integer", "Chromium remote debugging port.")),
            ("pid", JsonArgs.Prop("integer", "Optional pid validation when window_id is provided.")),
            ("window_id", JsonArgs.Prop("integer", "Optional browser HWND used to bind CDP to the matching tab/window."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var expression = JsonArgs.RequiredString(args, "expression");
        var port = JsonArgs.OptionalInt(args, "cdp_port") ?? context.State.Config.ChromiumDebuggingPort;
        if (port is null)
            return ToolResult.Error("browser_eval requires cdp_port or config chromium_debugging_port.");

        var windowId = JsonArgs.OptionalLong(args, "window_id");
        var pid = JsonArgs.OptionalInt(args, "pid");
        if (windowId is not null)
        {
            var window = WindowEnumerator.Find(windowId.Value);
            if (window is null)
                return ToolResult.Error($"No window with window_id {windowId.Value}.");
            if (pid is not null && window.Pid != pid.Value)
                return ToolResult.Error($"window_id {windowId.Value} belongs to pid {window.Pid}, not pid {pid.Value}.");
            if (!BrowserWindowClassifier.IsLikelyChromium(window))
                return ToolResult.Error($"window_id {windowId.Value} does not look like a Chromium browser window.");
        }

        var receipt = await new CdpBrowserBridge(context.State.UiaTree).EvaluateUserGestureAsync(port.Value, windowId, expression, cancellationToken).ConfigureAwait(false);
        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
    }
}
