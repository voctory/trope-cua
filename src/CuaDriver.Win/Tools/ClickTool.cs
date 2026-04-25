using System.Text.Json.Nodes;
using System.Windows.Automation;
using CuaDriver.Win.Browser;
using CuaDriver.Win.Capture;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Uia;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class ClickTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "click",
        ToolDescriptions.Click,
        JsonArgs.SchemaWithAnyOf(["pid"], [["element_index"], ["x", "y"]],
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND. Required for element_index; recommended for pixel clicks.")),
            ("element_index", JsonArgs.Prop("integer", "Element index from get_window_state.")),
            ("x", JsonArgs.Prop("number", "Window-local screenshot X.")),
            ("y", JsonArgs.Prop("number", "Window-local screenshot Y.")),
            ("action", JsonArgs.Prop("string", "UIA action name: press, show_menu, pick, confirm, cancel, open.")),
            ("modifier", JsonArgs.Prop("array", "Modifier keys held during pixel clicks: ctrl, shift, alt/option, win/cmd.")),
            ("modifiers", JsonArgs.Prop("array", "Alias for modifier.")),
            ("count", JsonArgs.Prop("integer", "Click count. Pixel path only.")),
            ("from_zoom", JsonArgs.Prop("boolean", "When true, x/y are pixel coordinates in the last zoom image for this pid.")),
            ("debug_image_out", JsonArgs.Prop("string", "Optional path. For pixel clicks, capture the target window, draw a red crosshair at the received x/y in resized screenshot coordinates, and write a PNG before dispatch. Requires window_id; incompatible with from_zoom.")),
            ("cdp_port", JsonArgs.Prop("integer", "Optional Chromium remote debugging port for browser pixel route.")),
            ("allow_parent_sendinput", JsonArgs.Prop("boolean", "Explicit unsafe override for local experiments only. Do not set for background automation; default false and currently reported, not used."))),
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
        var count = JsonArgs.OptionalInt(args, "count") ?? 1;
        var action = JsonArgs.OptionalString(args, "action") ?? "press";
        var fromZoom = JsonArgs.OptionalBool(args, "from_zoom");
        var debugImageOut = JsonArgs.OptionalString(args, "debug_image_out");
        var modifiers = JsonArgs.OptionalStringArray(args, "modifier");
        if (modifiers.Length == 0)
            modifiers = JsonArgs.OptionalStringArray(args, "modifiers");

        if (index is not null && (x is not null || y is not null))
            return ToolResult.Error("Provide either element_index or x/y, not both.");
        if (index is null && (x is null || y is null))
            return ToolResult.Error("Provide element_index or both x and y.");
        if (index is not null && fromZoom)
            return ToolResult.Error("from_zoom only applies to pixel clicks.");
        if (index is not null && windowId is null)
            return ToolResult.Error("window_id is required for element_index clicks.");
        if (!string.IsNullOrWhiteSpace(debugImageOut))
        {
            if (index is not null)
                return ToolResult.Error("debug_image_out only applies to pixel clicks (x, y); element_index clicks do not have a coordinate to verify.");
            if (fromZoom)
                return ToolResult.Error("debug_image_out is incompatible with from_zoom because the received x/y are in zoom-crop space, not window-local screenshot space.");
            if (windowId is null)
                return ToolResult.Error("debug_image_out requires window_id so the tool can capture the window for the crosshair overlay.");
        }

        ActionReceipt receipt;

        if (index is not null)
        {
            if (!ToolWindows.TryFindForPid(pid, windowId!.Value, out var window, out var error))
                return error!;

            var element = context.State.UiaTree.GetCachedElement(pid, window.WindowId, index.Value);
            await AgentCursorTooling.MoveToElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
            if (BrowserWindowClassifier.IsLikelyBrowser(window))
            {
                var rect = element.Current.BoundingRectangle;
                if (!rect.IsEmpty)
                {
                    var msaaReceipt = MsaaActions.DoDefaultActionAtElement(window.Hwnd, element);
                    if (msaaReceipt.Ok || msaaReceipt.ForegroundChanged || msaaReceipt.CursorMoved)
                    {
                        if (msaaReceipt.Ok)
                            await AgentCursorTooling.PulseAtElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
                        return ActionToolResult.FromReceipt(msaaReceipt);
                    }

                    var cdpPort = BrowserToolArgs.CdpPort(args, context);
                    if (cdpPort is not null)
                    {
                        var localX = rect.X + rect.Width / 2 - window.Bounds.X;
                        var localY = rect.Y + rect.Height / 2 - window.Bounds.Y;
                        receipt = await CdpBrowserBridge.TryClickAsync(window.Hwnd, window.WindowId, localX, localY, count, rightButton: false, cdpPort, cancellationToken).ConfigureAwait(false)
                                  ?? ActionReceipt.Failure("cdp.input.dispatch_mouse", $"No page tab found on CDP port {cdpPort}.");
                        if (receipt.Ok)
                            await AgentCursorTooling.PulseAtElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
                        return ActionToolResult.FromReceipt(receipt);
                    }
                }

                receipt = BrowserNavigation.RefuseForegroundOnlyLinkRoute(UiAutomationActions.TryGetValue(element))
                          ?? ActionReceipt.Failure("requires_browser_semantic_route", "Browser element did not expose a safe MSAA default action and no CDP port was configured; refusing UIA Invoke because browser providers can foreground the target.");
                return ActionToolResult.FromReceipt(receipt);
            }

            receipt = await UiAutomationActions.InvokeElementAsync(element, action, cancellationToken).ConfigureAwait(false);
            if (!receipt.Ok && !receipt.ForegroundChanged && !receipt.CursorMoved)
            {
                var msaaReceipt = MsaaActions.DoDefaultActionAtElement(window.Hwnd, element);
                if (msaaReceipt.Ok || msaaReceipt.ForegroundChanged || msaaReceipt.CursorMoved)
                    receipt = msaaReceipt;
            }
            context.State.LastUiaTextTarget[(pid, windowId.Value)] = element;
            await AgentCursorTooling.PulseAtElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (windowId is null && fromZoom && context.State.ZoomContexts.TryGetValue(pid, out var zoomContext))
            {
                if (!ToolWindows.TryFindForPid(pid, zoomContext.WindowId, out var zoomWindow, out var error))
                    return error!;
                return await InvokePixelClickAsync(args, context, pid, x, y, count, action, fromZoom, debugImageOut, modifiers, zoomWindow, cancellationToken).ConfigureAwait(false);
            }

            if (!ToolWindows.TryFindMainOrForPid(pid, windowId, out var resolvedWindow, out var resolvedError))
            {
                return resolvedError!;
            }
            return await InvokePixelClickAsync(args, context, pid, x, y, count, action, fromZoom, debugImageOut, modifiers, resolvedWindow, cancellationToken).ConfigureAwait(false);
        }

        return ActionToolResult.FromReceipt(receipt);
    }

    private static async Task<ToolResult> InvokePixelClickAsync(
        JsonObject args,
        ToolContext context,
        int pid,
        double? x,
        double? y,
        int count,
        string action,
        bool fromZoom,
        string? debugImageOut,
        string[] modifiers,
        WindowInfo window,
        CancellationToken cancellationToken)
    {
        ActionReceipt receipt;
        if (!string.IsNullOrWhiteSpace(debugImageOut))
        {
            try
            {
                DebugCrosshair.WriteCrosshair(
                    window,
                    new System.Drawing.PointF((float)x!.Value, (float)y!.Value),
                    context.State.Config.MaxImageDimension,
                    debugImageOut);
            }
            catch (Exception ex)
            {
                return ToolResult.Error($"debug_image_out write failed: {ex.Message}. Not dispatching click; fix the path and retry.");
            }
        }

        double clickX;
        double clickY;
        if (fromZoom)
        {
            if (!context.State.ZoomContexts.TryGetValue(pid, out var zoom))
                return ToolResult.Error($"from_zoom=true but no zoom context for pid {pid}. Call zoom first.");
            if (zoom.WindowId != window.WindowId)
                return ToolResult.Error($"from_zoom context belongs to window_id {zoom.WindowId}, not {window.WindowId}.");
            clickX = zoom.OriginX + x!.Value;
            clickY = zoom.OriginY + y!.Value;
        }
        else
        {
            var ratio = context.State.ImageResizeRatio.TryGetValue((pid, window.WindowId), out var r) ? r : 1.0;
            clickX = x!.Value * ratio;
            clickY = y!.Value * ratio;
        }
        var resolved = WindowMessageInput.ResolvePointTarget(window.Hwnd, clickX, clickY);

        var hit = UiAutomationTree.HitTest(pid, window.WindowId, resolved.ScreenPoint);
        if (hit is { IsClickAction: true } && modifiers.Length == 0)
        {
            await context.State.AgentCursor.MoveToAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
            if (BrowserWindowClassifier.IsLikelyBrowser(window))
            {
                var msaaReceipt = MsaaActions.DoDefaultActionAtPoint(window.Hwnd, resolved.ScreenPoint);
                if (msaaReceipt.Ok || msaaReceipt.ForegroundChanged || msaaReceipt.CursorMoved)
                {
                    if (msaaReceipt.Ok)
                        await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                    return ActionToolResult.FromReceipt(msaaReceipt);
                }

                var hitCdpPort = BrowserToolArgs.CdpPort(args, context);
                if (hitCdpPort is not null)
                {
                    receipt = await CdpBrowserBridge.TryClickAsync(window.Hwnd, window.WindowId, clickX, clickY, count, rightButton: false, hitCdpPort, cancellationToken, modifiers).ConfigureAwait(false)
                              ?? ActionReceipt.Failure("cdp.input.dispatch_mouse", $"No page tab found on CDP port {hitCdpPort}.");
                    if (receipt.Ok)
                        await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                    return ActionToolResult.FromReceipt(receipt);
                }

                var navReceipt = BrowserNavigation.RefuseForegroundOnlyLinkRoute(UiAutomationActions.TryGetValue(hit.Element));
                if (navReceipt is not null)
                {
                    receipt = navReceipt with { Route = "uia.hit_test." + navReceipt.Route };
                    return ActionToolResult.FromReceipt(receipt);
                }

                receipt = ActionReceipt.Failure("requires_browser_semantic_route", "Browser UIA hit-test found an actionable element, but it did not expose a safe MSAA default action, URL value, or CDP route; refusing UIA Invoke because browser providers commonly raise/focus the window.");
                return ActionToolResult.FromReceipt(receipt);
            }

            var hitReceipt = await UiAutomationActions.InvokeElementAsync(hit.Element, action, cancellationToken).ConfigureAwait(false);
            if (!hitReceipt.Ok && !hitReceipt.ForegroundChanged && !hitReceipt.CursorMoved)
            {
                var msaaReceipt = MsaaActions.DoDefaultActionAtPoint(window.Hwnd, resolved.ScreenPoint);
                if (msaaReceipt.Ok || msaaReceipt.ForegroundChanged || msaaReceipt.CursorMoved)
                    hitReceipt = msaaReceipt;
            }
            if (hitReceipt.Ok)
            {
                receipt = hitReceipt with { Route = "uia.hit_test." + hitReceipt.Route };
                context.State.LastUiaTextTarget[(pid, window.WindowId)] = hit.Element;
                await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                return ActionToolResult.FromReceipt(receipt);
            }
        }

        if (hit is { IsTextInput: true } && BrowserWindowClassifier.IsLikelyBrowser(window))
        {
            await context.State.AgentCursor.MoveToAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
            context.State.LastUiaTextTarget[(pid, window.WindowId)] = hit.Element;
            var textCdpPort = BrowserToolArgs.CdpPort(args, context);
            if (textCdpPort is not null)
            {
                receipt = await CdpBrowserBridge.TryClickAsync(window.Hwnd, window.WindowId, clickX, clickY, count, rightButton: false, textCdpPort, cancellationToken, modifiers).ConfigureAwait(false)
                          ?? ActionReceipt.Failure("cdp.input.dispatch_mouse", $"No page tab found on CDP port {textCdpPort}.");
                if (receipt.Ok)
                    await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                return ActionToolResult.FromReceipt(receipt);
            }

            receipt = ActionReceipt.Failure(
                "requires_cdp_or_element_text",
                "Browser text input target was cached for a following type_text call, but no background-safe click/focus route is available without cdp_port; refusing to report this as a delivered click.");
            return ActionToolResult.FromReceipt(receipt);
        }

        await context.State.AgentCursor.MoveToAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
        if (BrowserWindowClassifier.IsLikelyBrowser(window) && modifiers.Length == 0)
        {
            var msaaReceipt = MsaaActions.DoDefaultActionAtPoint(window.Hwnd, resolved.ScreenPoint);
            if (msaaReceipt.Ok || msaaReceipt.ForegroundChanged || msaaReceipt.CursorMoved)
            {
                if (msaaReceipt.Ok)
                    await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                return ActionToolResult.FromReceipt(msaaReceipt);
            }
        }

        var cdpPort = BrowserToolArgs.CdpPort(args, context);
        receipt = await CdpBrowserBridge.TryClickAsync(window.Hwnd, window.WindowId, clickX, clickY, count, rightButton: false, cdpPort, cancellationToken, modifiers).ConfigureAwait(false)
                  ?? (BrowserWindowClassifier.IsLikelyBrowser(window)
                      ? ActionReceipt.Failure("requires_cdp_or_uia_hit_test", "Browser web content did not expose an actionable UIA target and no CDP port was configured; refusing to report a blind PostMessage click as delivered.")
                      : (await WindowMessageInput.ClickAsync(window.Hwnd, clickX, clickY, count, rightButton: false, cancellationToken, modifiers).ConfigureAwait(false)).Receipt);

        if (receipt.Ok)
        {
            context.State.LastTargetHwnd[(pid, window.WindowId)] = resolved.TargetHwnd;
            await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
        }

        return ActionToolResult.FromReceipt(receipt);
    }
}
