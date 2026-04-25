using System.Text.Json.Nodes;
using CuaDriver.Win.Browser;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Uia;

namespace CuaDriver.Win.Tools;

internal sealed class RightClickTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "right_click",
        ToolDescriptions.RightClick,
        JsonArgs.SchemaWithAnyOf(["pid"], [["element_index"], ["x", "y"]],
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND.")),
            ("element_index", JsonArgs.Prop("integer", "Element index from get_window_state.")),
            ("x", JsonArgs.Prop("number", "Window-local screenshot X.")),
            ("y", JsonArgs.Prop("number", "Window-local screenshot Y.")),
            ("modifier", JsonArgs.Prop("array", "Modifier keys held during pixel right-clicks: ctrl, shift, alt/option, win/cmd.")),
            ("modifiers", JsonArgs.Prop("array", "Alias for modifier.")),
            ("cdp_port", JsonArgs.Prop("integer", "Optional Chromium remote debugging port."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var parseError = ClickTargetArgs.TryParse(args, "right_click", out var target);
        if (parseError is not null)
            return parseError;

        ActionReceipt receipt;
        if (target.HasElement)
        {
            if (!ToolWindows.TryFindForPid(target.Pid, target.WindowId!.Value, out var window, out var error))
                return error!;
            var element = context.State.UiaTree.GetCachedElement(target.Pid, target.WindowId.Value, target.ElementIndex!.Value);
            await AgentCursorTooling.MoveToElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
            if (BrowserWindowClassifier.IsLikelyBrowser(window))
            {
                var rect = element.Current.BoundingRectangle;
                var cdpPort = BrowserToolArgs.CdpPort(args, context);
                if (!rect.IsEmpty && cdpPort is not null)
                {
                    var localX = rect.X + rect.Width / 2 - window.Bounds.X;
                    var localY = rect.Y + rect.Height / 2 - window.Bounds.Y;
                    receipt = await CdpBrowserBridge.TryClickAsync(window.Hwnd, window.WindowId, localX, localY, 1, rightButton: true, cdpPort, cancellationToken).ConfigureAwait(false)
                              ?? BrowserToolArgs.NoPageReceipt("cdp.input.dispatch_mouse.right", cdpPort.Value);
                    if (receipt.Ok)
                        await AgentCursorTooling.PulseAtElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
                    return ActionToolResult.FromReceipt(receipt);
                }

                receipt = ActionReceipt.Failure("requires_cdp_or_child_session", "Refusing browser UIA show_menu from element_index because browser providers can foreground the target. Provide cdp_port for Chromium or use the child-session/AppBroadcast lane.");
                return ActionToolResult.FromReceipt(receipt);
            }
            receipt = await UiAutomationActions.InvokeElementAsync(element, "show_menu", cancellationToken).ConfigureAwait(false);
            if (receipt.Ok)
                await AgentCursorTooling.PulseAtElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (!ToolWindows.TryFindMainOrForPid(target.Pid, target.WindowId, out var window, out var error))
                return error!;

            var ratio = context.State.ImageResizeRatio.TryGetValue((target.Pid, window.WindowId), out var r) ? r : 1.0;
            var clickX = target.X!.Value * ratio;
            var clickY = target.Y!.Value * ratio;
            var resolved = WindowMessageInput.ResolvePointTarget(window.Hwnd, clickX, clickY);

            var hit = UiAutomationTree.HitTest(target.Pid, window.WindowId, resolved.ScreenPoint);
            if (hit is { IsClickAction: true } && target.Modifiers.Length == 0)
            {
                await context.State.AgentCursor.MoveToAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                if (BrowserWindowClassifier.IsLikelyBrowser(window))
                {
                    var hitCdpPort = BrowserToolArgs.CdpPort(args, context);
                    if (hitCdpPort is not null)
                    {
                        receipt = await CdpBrowserBridge.TryClickAsync(window.Hwnd, window.WindowId, clickX, clickY, 1, rightButton: true, hitCdpPort, cancellationToken, target.Modifiers).ConfigureAwait(false)
                                  ?? BrowserToolArgs.NoPageReceipt("cdp.input.dispatch_mouse.right", hitCdpPort.Value);
                        if (receipt.Ok)
                            await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                        return ActionToolResult.FromReceipt(receipt);
                    }

                    receipt = ActionReceipt.Failure("requires_cdp_or_child_session", "Refusing browser UIA show_menu from hit-test because browser providers can foreground the target. Provide cdp_port for Chromium or use the child-session/AppBroadcast lane.");
                    return ActionToolResult.FromReceipt(receipt);
                }

                var hitReceipt = await UiAutomationActions.InvokeElementAsync(hit.Element, "show_menu", cancellationToken).ConfigureAwait(false);
                if (hitReceipt.Ok)
                {
                    receipt = hitReceipt with { Route = "uia.hit_test." + hitReceipt.Route };
                    await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                    return ActionToolResult.FromReceipt(receipt);
                }
            }

            var cdpPort = BrowserToolArgs.CdpPort(args, context);
            await context.State.AgentCursor.MoveToAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
            receipt = await CdpBrowserBridge.TryClickAsync(window.Hwnd, window.WindowId, clickX, clickY, 1, rightButton: true, cdpPort, cancellationToken, target.Modifiers).ConfigureAwait(false)
                      ?? (BrowserWindowClassifier.IsLikelyBrowser(window)
                          ? ActionReceipt.Failure("requires_cdp_or_uia_hit_test", "Browser web content did not expose an actionable UIA target and no CDP port was configured; refusing to report a blind PostMessage right-click as delivered.")
                          : (await WindowMessageInput.ClickAsync(window.Hwnd, clickX, clickY, 1, rightButton: true, cancellationToken, target.Modifiers).ConfigureAwait(false)).Receipt);

            if (receipt.Ok)
            {
                context.State.LastTargetHwnd[(target.Pid, window.WindowId)] = resolved.TargetHwnd;
                await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
            }
        }

        return ActionToolResult.FromReceipt(receipt);
    }
}
