using System.Text.Json.Nodes;
using CuaDriver.Win.Browser;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Uia;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class DoubleClickTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "double_click",
        ToolDescriptions.DoubleClick,
        JsonArgs.SchemaWithAnyOf(["pid"], [["element_index"], ["x", "y"]],
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND. Required for element_index; recommended for pixel double-clicks.")),
            ("element_index", JsonArgs.Prop("integer", "Element index from get_window_state.")),
            ("x", JsonArgs.Prop("number", "Window-local screenshot X.")),
            ("y", JsonArgs.Prop("number", "Window-local screenshot Y.")),
            ("modifier", JsonArgs.Prop("array", "Modifier keys held during pixel double-clicks: ctrl, shift, alt/option, win/cmd.")),
            ("modifiers", JsonArgs.Prop("array", "Alias for modifier.")),
            ("cdp_port", JsonArgs.Prop("integer", "Optional Chromium remote debugging port."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var windowId = JsonArgs.OptionalLong(args, "window_id");
        var index = JsonArgs.OptionalInt(args, "element_index");
        var x = JsonArgs.OptionalDouble(args, "x");
        var y = JsonArgs.OptionalDouble(args, "y");
        var modifiers = JsonArgs.OptionalStringArray(args, "modifier");
        if (modifiers.Length == 0)
            modifiers = JsonArgs.OptionalStringArray(args, "modifiers");

        if (index is not null && (x is not null || y is not null))
            return ToolResult.Error("Provide either element_index or x/y, not both.");
        if (index is null && (x is null || y is null))
            return ToolResult.Error("Provide element_index or both x and y.");
        if (index is not null && windowId is null)
            return ToolResult.Error("window_id is required for element_index double_click.");

        if (index is null)
        {
            args["count"] = 2;
            return await new ClickTool().InvokeAsync(args, context, cancellationToken).ConfigureAwait(false);
        }

        var window = WindowEnumerator.Find(windowId!.Value);
        if (window is null)
            return ToolResult.Error($"No window with window_id {windowId.Value}.");
        if (window.Pid != pid)
            return ToolResult.Error($"window_id {windowId.Value} belongs to pid {window.Pid}, not pid {pid}.");

        var element = context.State.UiaTree.GetCachedElement(pid, windowId.Value, index.Value);
        var rect = element.Current.BoundingRectangle;
        if (rect.IsEmpty)
            return ToolResult.Error($"Element {index.Value} has no on-screen bounds; cannot double-click without a resolvable center.");

        var localX = rect.X + rect.Width / 2 - window.Bounds.X;
        var localY = rect.Y + rect.Height / 2 - window.Bounds.Y;
        var screenPoint = new POINT((int)Math.Round(rect.X + rect.Width / 2), (int)Math.Round(rect.Y + rect.Height / 2));
        await context.State.AgentCursor.MoveToAsync(screenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);

        ActionReceipt receipt;
        if (BrowserWindowClassifier.IsLikelyBrowser(window))
        {
            receipt = MsaaActions.DoDefaultActionAtElement(window.Hwnd, element);
            if (receipt.Ok || receipt.ForegroundChanged || receipt.CursorMoved)
            {
                if (receipt.Ok)
                    await context.State.AgentCursor.ClickPulseAsync(screenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
            }

            var cdpPort = BrowserToolArgs.CdpPort(args, context);
            if (cdpPort is null)
            {
                receipt = ActionReceipt.Failure("requires_browser_semantic_route", "Browser element did not expose a safe MSAA/IA2 default action and no CDP port was configured; refusing UIA double-click because browser providers can foreground the target.");
                return ToolResult.Text("❌ " + receipt.ToJson(), true);
            }

            receipt = await CdpBrowserBridge.TryClickAsync(window.Hwnd, window.WindowId, localX, localY, 2, rightButton: false, cdpPort, cancellationToken, modifiers).ConfigureAwait(false)
                      ?? ActionReceipt.Failure("cdp.input.dispatch_mouse.double", $"No page tab found on CDP port {cdpPort}.");
        }
        else
        {
            var dispatch = await WindowMessageInput.ClickAsync(window.Hwnd, localX, localY, 2, rightButton: false, cancellationToken, modifiers).ConfigureAwait(false);
            receipt = dispatch.Receipt;
            if (receipt.Ok)
                context.State.LastTargetHwnd[(pid, window.WindowId)] = dispatch.TargetHwnd;
        }

        if (receipt.Ok)
            await context.State.AgentCursor.ClickPulseAsync(screenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);

        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
    }
}
