using System.Text.Json.Nodes;
using CuaDriver.Win.Browser;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Uia;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

internal sealed class DoubleClickTool : IDriverTool
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
        var parseError = ClickTargetArgs.TryParse(args, "double_click", out var target);
        if (parseError is not null)
            return parseError;

        if (!target.HasElement)
        {
            args["count"] = 2;
            return await new ClickTool().InvokeAsync(args, context, cancellationToken).ConfigureAwait(false);
        }

        if (!ToolWindows.TryFindForPid(target.Pid, target.WindowId!.Value, out var window, out var error))
            return error!;

        var element = context.State.UiaTree.GetCachedElement(target.Pid, target.WindowId.Value, target.ElementIndex!.Value);
        var rect = element.Current.BoundingRectangle;
        if (rect.IsEmpty)
            return ToolResult.Error($"Element {target.ElementIndex.Value} has no on-screen bounds; cannot double-click without a resolvable center.");

        var localX = rect.X + rect.Width / 2 - window.Bounds.X;
        var localY = rect.Y + rect.Height / 2 - window.Bounds.Y;
        var screenPoint = new POINT((int)Math.Round(rect.X + rect.Width / 2), (int)Math.Round(rect.Y + rect.Height / 2));
        await context.State.AgentCursor.MoveToAsync(screenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);

        ActionReceipt receipt;
        if (BrowserWindowClassifier.IsLikelyBrowser(window))
        {
            receipt = MsaaActions.DoDefaultActionAtElement(window.Hwnd, element);
            if (receipt.ShouldStopFallback)
            {
                if (receipt.Ok)
                    await context.State.AgentCursor.ClickPulseAsync(screenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                return ActionToolResult.FromReceipt(receipt);
            }

            var cdpPort = BrowserToolArgs.CdpPort(args, context);
            if (cdpPort is null)
            {
                receipt = ActionReceipt.Failure("requires_browser_semantic_route", "Browser element did not expose a safe MSAA/IA2 default action and no CDP port was configured; refusing UIA double-click because browser providers can foreground the target.");
                return ActionToolResult.FromReceipt(receipt);
            }

            receipt = await CdpBrowserBridge.TryClickAsync(window.Hwnd, window.WindowId, localX, localY, 2, rightButton: false, cdpPort, cancellationToken, target.Modifiers).ConfigureAwait(false)
                      ?? BrowserToolArgs.NoPageReceipt("cdp.input.dispatch_mouse.double", cdpPort.Value);
        }
        else
        {
            var dispatch = await WindowMessageInput.ClickAsync(window.Hwnd, localX, localY, 2, rightButton: false, cancellationToken, target.Modifiers).ConfigureAwait(false);
            receipt = dispatch.Receipt;
            if (receipt.Ok)
                context.State.LastTargetHwnd[(target.Pid, window.WindowId)] = dispatch.TargetHwnd;
        }

        if (receipt.Ok)
            await context.State.AgentCursor.ClickPulseAsync(screenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);

        return ActionToolResult.FromReceipt(receipt);
    }
}
