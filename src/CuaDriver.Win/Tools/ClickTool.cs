using System.Text.Json.Nodes;
using System.Windows.Automation;
using CuaDriver.Win.Browser;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Uia;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

internal sealed class ClickTool : IDriverTool
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
            ("debug_image_out", JsonArgs.Prop("string", "Optional PNG path for pixel-click crosshair debug image. Requires window_id; incompatible with from_zoom.")),
            ("cdp_port", JsonArgs.Prop("integer", "Optional Chromium remote debugging port for browser pixel route.")),
            ("allow_transient_foreground", JsonArgs.Prop("boolean", "Allow brief fallback focus/foreground blip. Defaults true; receipt still reports background_safe=false.")),
            ("allow_parent_sendinput", JsonArgs.Prop("boolean", "Unsafe local experiment flag. Do not set for background automation."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var parseError = ClickTargetArgs.TryParse(args, "clicks", out var target);
        if (parseError is not null)
            return parseError;

        var count = JsonArgs.OptionalInt(args, "count") ?? 1;
        var action = JsonArgs.OptionalString(args, "action") ?? "press";
        var fromZoom = JsonArgs.OptionalBool(args, "from_zoom");
        var debugImageOut = JsonArgs.OptionalString(args, "debug_image_out");
        var allowTransientForeground = JsonArgs.OptionalBool(args, "allow_transient_foreground", true);

        if (target.HasElement && fromZoom)
            return ToolResult.Error("from_zoom only applies to pixel clicks.");
        var debugError = ClickDebugImage.Validate(target, fromZoom, debugImageOut);
        if (debugError is not null)
            return debugError;

        ActionReceipt receipt;

        if (target.HasElement)
        {
            if (!ToolWindows.TryFindForPid(target.Pid, target.WindowId!.Value, out var window, out var error))
                return error!;

            var element = context.State.UiaTree.GetCachedElement(target.Pid, window.WindowId, target.ElementIndex!.Value);
            await AgentCursorTooling.MoveToElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
            if (BrowserWindowClassifier.IsLikelyBrowser(window))
            {
                var browserCdpPort = BrowserToolArgs.CdpPort(args, context);
                using var browserLease = BrowserAutomationLease.TryAcquireChromiumFallback(window, browserCdpPort, out var contention);
                if (contention is not null)
                    return ActionToolResult.FromReceipt(contention);

                var elementPoint = ToolCoordinates.ElementCenter(element, window);
                if (elementPoint is not null)
                {
                    var msaaReceipt = MsaaActions.DoDefaultActionAtElement(window.Hwnd, element);
                    if (msaaReceipt.ShouldStopFallback)
                    {
                        msaaReceipt = await StabilizeBrowserOneShotControlAsync(
                            target.Pid,
                            window,
                            element,
                            elementPoint.ScreenPoint,
                            msaaReceipt,
                            cancellationToken).ConfigureAwait(false);
                        if (msaaReceipt.Ok)
                            await AgentCursorTooling.PulseAtElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
                        return ActionToolResult.FromReceipt(msaaReceipt);
                    }

                    var hit = UiAutomationTree.HitTest(target.Pid, window.WindowId, elementPoint.ScreenPoint);
                    if (hit is { IsClickAction: true })
                    {
                        msaaReceipt = MsaaActions.DoDefaultActionAtPoint(window.Hwnd, elementPoint.ScreenPoint);
                        if (msaaReceipt.ShouldStopFallback)
                        {
                            msaaReceipt = await StabilizeBrowserOneShotControlAsync(
                                target.Pid,
                                window,
                                hit.Element,
                                elementPoint.ScreenPoint,
                                msaaReceipt,
                                cancellationToken).ConfigureAwait(false);
                            if (msaaReceipt.Ok)
                                await AgentCursorTooling.PulseAtElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
                            return ActionToolResult.FromReceipt(msaaReceipt with { Route = "uia.center_hit_test." + msaaReceipt.Route });
                        }
                    }

                    if (browserCdpPort is not null)
                    {
                        receipt = await CdpBrowserBridge.TryClickAsync(window.Hwnd, window.WindowId, elementPoint.LocalX, elementPoint.LocalY, count, rightButton: false, browserCdpPort, cancellationToken).ConfigureAwait(false)
                                  ?? BrowserToolArgs.NoPageReceipt("cdp.input.dispatch_mouse", browserCdpPort.Value);
                        if (receipt.Ok)
                            await AgentCursorTooling.PulseAtElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
                        return ActionToolResult.FromReceipt(receipt);
                    }

                    if (count == 1 && action.Equals("press", StringComparison.OrdinalIgnoreCase) && IsRetryableBrowserOneShotControl(element))
                    {
                        receipt = await TryVerifiedBrowserOneShotClickAsync(
                            target.Pid,
                            window,
                            element,
                            elementPoint.ScreenPoint,
                            elementPoint.LocalX,
                            elementPoint.LocalY,
                            cancellationToken).ConfigureAwait(false);
                        if (receipt.Ok)
                        {
                            await AgentCursorTooling.PulseAtElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
                            return ActionToolResult.FromReceipt(receipt);
                        }
                    }
                }

                var navigationReceipt = BrowserNavigation.RefuseForegroundOnlyLinkRoute(UiAutomationActions.TryGetValue(element));
                if (navigationReceipt is not null && !allowTransientForeground)
                    return ActionToolResult.FromReceipt(navigationReceipt);

                if (!allowTransientForeground)
                {
                    receipt = ActionReceipt.Failure(
                        "requires_browser_semantic_route",
                        "Browser element did not expose a safe MSAA default action and no CDP port was configured. Keep allow_transient_foreground enabled for the existing window, use a child-session/AppBroadcast lane, or report the blocker.");
                    return ActionToolResult.FromReceipt(receipt);
                }

                receipt = await UiAutomationActions.InvokeElementAsync(element, action, cancellationToken, allowTransientForeground: true).ConfigureAwait(false);
                if (receipt.Ok)
                    await AgentCursorTooling.PulseAtElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
                return ActionToolResult.FromReceipt(receipt);
            }

            receipt = await InvokeNativeElementActionAsync(window, element, action, cancellationToken, allowTransientForeground: allowTransientForeground).ConfigureAwait(false);
            context.State.LastUiaTextTarget[(target.Pid, target.WindowId.Value)] = element;
            await AgentCursorTooling.PulseAtElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (target.WindowId is null && fromZoom && context.State.ZoomContexts.TryGetValue(target.Pid, out var zoomContext))
            {
                if (!ToolWindows.TryFindForPid(target.Pid, zoomContext.WindowId, out var zoomWindow, out var error))
                    return error!;
                return await InvokePixelClickAsync(args, context, target.Pid, target.X, target.Y, count, action, fromZoom, debugImageOut, target.Modifiers, zoomWindow, allowTransientForeground, cancellationToken).ConfigureAwait(false);
            }

            if (!ToolWindows.TryFindMainOrForPid(target.Pid, target.WindowId, out var resolvedWindow, out var resolvedError))
            {
                return resolvedError!;
            }
            return await InvokePixelClickAsync(args, context, target.Pid, target.X, target.Y, count, action, fromZoom, debugImageOut, target.Modifiers, resolvedWindow, allowTransientForeground, cancellationToken).ConfigureAwait(false);
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
        bool allowTransientForeground,
        CancellationToken cancellationToken)
    {
        ActionReceipt receipt;
        var debugError = ClickDebugImage.Write(window, x!.Value, y!.Value, context.State.Config.MaxImageDimension, debugImageOut);
        if (debugError is not null)
            return debugError;

        PixelTargetPoint point;
        if (fromZoom)
        {
            if (!context.State.ZoomContexts.TryGetValue(pid, out var zoom))
                return ToolResult.Error($"from_zoom=true but no zoom context for pid {pid}. Call zoom first.");
            if (zoom.WindowId != window.WindowId)
                return ToolResult.Error($"from_zoom context belongs to window_id {zoom.WindowId}, not {window.WindowId}.");
            point = ToolCoordinates.ResolveNativePixelTarget(pid, window, zoom.OriginX + x!.Value, zoom.OriginY + y!.Value);
        }
        else
        {
            point = ToolCoordinates.ResolvePixelTarget(context, pid, window, x!.Value, y!.Value);
        }

        var clickX = point.LocalX;
        var clickY = point.LocalY;
        var resolved = point.Resolved;
        var hit = point.Hit;
        var browserCdpPort = BrowserToolArgs.CdpPort(args, context);
        using var browserLease = BrowserAutomationLease.TryAcquireChromiumFallback(window, browserCdpPort, out var contention);
        if (contention is not null)
            return ActionToolResult.FromReceipt(contention);

        if (hit is { IsClickAction: true } && modifiers.Length == 0)
        {
            await context.State.AgentCursor.MoveToAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
            if (BrowserWindowClassifier.IsLikelyBrowser(window))
            {
                var msaaReceipt = MsaaActions.DoDefaultActionAtPoint(window.Hwnd, resolved.ScreenPoint);
                if (msaaReceipt.ShouldStopFallback)
                {
                    msaaReceipt = await StabilizeBrowserOneShotControlAsync(
                        pid,
                        window,
                        hit.Element,
                        resolved.ScreenPoint,
                        msaaReceipt,
                        cancellationToken).ConfigureAwait(false);
                    if (msaaReceipt.Ok)
                        await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                    return ActionToolResult.FromReceipt(msaaReceipt);
                }

                if (browserCdpPort is not null)
                {
                    receipt = await CdpBrowserBridge.TryClickAsync(window.Hwnd, window.WindowId, clickX, clickY, count, rightButton: false, browserCdpPort, cancellationToken, modifiers).ConfigureAwait(false)
                              ?? BrowserToolArgs.NoPageReceipt("cdp.input.dispatch_mouse", browserCdpPort.Value);
                    if (receipt.Ok)
                        await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                    return ActionToolResult.FromReceipt(receipt);
                }

                if (count == 1 && action.Equals("press", StringComparison.OrdinalIgnoreCase) && IsRetryableBrowserOneShotControl(hit.Element))
                {
                    receipt = await TryVerifiedBrowserOneShotClickAsync(
                        pid,
                        window,
                        hit.Element,
                        resolved.ScreenPoint,
                        clickX,
                        clickY,
                        cancellationToken).ConfigureAwait(false);
                    if (receipt.Ok)
                    {
                        await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                        return ActionToolResult.FromReceipt(receipt);
                    }
                }

                var navReceipt = BrowserNavigation.RefuseForegroundOnlyLinkRoute(UiAutomationActions.TryGetValue(hit.Element));
                if (navReceipt is not null && !allowTransientForeground)
                {
                    receipt = navReceipt with { Route = "uia.hit_test." + navReceipt.Route };
                    return ActionToolResult.FromReceipt(receipt);
                }

                if (!allowTransientForeground)
                {
                    receipt = ActionReceipt.Failure(
                        "requires_browser_semantic_route",
                        "Browser UIA hit-test found an actionable element, but it did not expose a safe MSAA default action, URL value, or CDP route. Keep allow_transient_foreground enabled for the existing window, use a child-session/AppBroadcast lane, or report the blocker.");
                    return ActionToolResult.FromReceipt(receipt);
                }

                receipt = await UiAutomationActions.InvokeElementAsync(hit.Element, action, cancellationToken, allowTransientForeground: true).ConfigureAwait(false);
                if (receipt.Ok)
                {
                    receipt = receipt with { Route = "uia.hit_test." + receipt.Route };
                    context.State.LastUiaTextTarget[(pid, window.WindowId)] = hit.Element;
                    await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                }
                return ActionToolResult.FromReceipt(receipt);
            }

            var hitReceipt = await InvokeNativeElementActionAsync(
                window,
                hit.Element,
                action,
                cancellationToken,
                resolved.ScreenPoint,
                allowTransientForeground).ConfigureAwait(false);
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
            if (browserCdpPort is not null)
            {
                receipt = await CdpBrowserBridge.TryClickAsync(window.Hwnd, window.WindowId, clickX, clickY, count, rightButton: false, browserCdpPort, cancellationToken, modifiers).ConfigureAwait(false)
                          ?? BrowserToolArgs.NoPageReceipt("cdp.input.dispatch_mouse", browserCdpPort.Value);
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
            if (msaaReceipt.ShouldStopFallback)
            {
                msaaReceipt = await StabilizeBrowserOneShotControlAsync(
                    pid,
                    window,
                    hit?.Element,
                    resolved.ScreenPoint,
                    msaaReceipt,
                    cancellationToken).ConfigureAwait(false);
                if (msaaReceipt.Ok)
                    await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                return ActionToolResult.FromReceipt(msaaReceipt);
            }
        }

        receipt = await CdpBrowserBridge.TryClickAsync(window.Hwnd, window.WindowId, clickX, clickY, count, rightButton: false, browserCdpPort, cancellationToken, modifiers).ConfigureAwait(false)
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

    private static async Task<ActionReceipt> InvokeNativeElementActionAsync(
        WindowInfo window,
        AutomationElement element,
        string action,
        CancellationToken cancellationToken,
        POINT? screenPoint = null,
        bool allowTransientForeground = false)
    {
        if (RequiresIsolatedInputLane(element, action))
        {
            if (!allowTransientForeground)
                return ActionReceipt.Failure(
                    "requires_child_session",
                    "This native control has no verified parent-session background route: UIA can foreground the app, MSAA can move the real cursor, and posted HWND mouse messages do not change its state. Keep allow_transient_foreground enabled for the existing window, use the child-session/AppBroadcast lane, or report the blocker.");

            return await UiAutomationActions.InvokeElementAsync(element, action, cancellationToken, allowTransientForeground: true).ConfigureAwait(false);
        }

        var msaaReceipt = screenPoint is null
            ? MsaaActions.DoDefaultActionAtElement(window.Hwnd, element)
            : MsaaActions.DoDefaultActionAtPoint(window.Hwnd, screenPoint.Value);
        if (msaaReceipt.ShouldStopFallback)
            return msaaReceipt;

        if (RequiresIsolatedUiaFallback(element))
        {
            if (!allowTransientForeground)
                return ActionReceipt.Failure(
                    "requires_child_session",
                    "This virtual native control did not expose a safe MSAA action route, and UIA Invoke can foreground native WinUI apps. Keep allow_transient_foreground enabled for the existing window, use the child-session/AppBroadcast lane, or report the blocker.");

            return await UiAutomationActions.InvokeElementAsync(element, action, cancellationToken, allowTransientForeground: true).ConfigureAwait(false);
        }

        return await UiAutomationActions.InvokeElementAsync(element, action, cancellationToken, allowTransientForeground: allowTransientForeground).ConfigureAwait(false);
    }

    private static bool RequiresIsolatedInputLane(AutomationElement element, string action)
    {
        if (!string.Equals(action, "press", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var automationId = element.Current.AutomationId ?? "";
            return automationId.Contains("PlayPause", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<ActionReceipt> StabilizeBrowserOneShotControlAsync(
        int pid,
        WindowInfo window,
        AutomationElement? originalElement,
        POINT screenPoint,
        ActionReceipt receipt,
        CancellationToken cancellationToken)
    {
        if (!receipt.Ok || !IsRetryableBrowserOneShotControl(originalElement))
            return receipt;

        await Task.Delay(180, cancellationToken).ConfigureAwait(false);

        var hit = UiAutomationTree.HitTest(pid, window.WindowId, screenPoint);
        if (hit is null || !IsRetryableBrowserOneShotControl(hit.Element))
            return receipt;

        var retry = MsaaActions.DoDefaultActionAtPoint(window.Hwnd, screenPoint);
        return retry.Ok
            ? retry with { Route = receipt.Route + ".retry_still_present." + retry.Route }
            : receipt;
    }

    private static async Task<ActionReceipt> TryVerifiedBrowserOneShotClickAsync(
        int pid,
        WindowInfo window,
        AutomationElement originalElement,
        POINT screenPoint,
        double localX,
        double localY,
        CancellationToken cancellationToken)
    {
        var dispatch = await WindowMessageInput.ClickAsync(window.Hwnd, localX, localY, 1, rightButton: false, cancellationToken).ConfigureAwait(false);
        if (!dispatch.Receipt.Ok)
            return dispatch.Receipt with { Route = "browser.one_shot.verify_gone." + dispatch.Receipt.Route };

        await Task.Delay(220, cancellationToken).ConfigureAwait(false);

        var hit = UiAutomationTree.HitTest(pid, window.WindowId, screenPoint);
        if (hit is null || !IsSameBrowserOneShotControl(originalElement, hit.Element))
            return dispatch.Receipt with { Route = "browser.one_shot.verify_gone." + dispatch.Receipt.Route };

        return ActionReceipt.Failure(
            "browser.one_shot.verify_gone",
            "Browser one-shot control was still present after a background HWND click, so delivery could not be verified.");
    }

    private static bool IsSameBrowserOneShotControl(AutomationElement originalElement, AutomationElement hitElement)
    {
        if (!IsRetryableBrowserOneShotControl(hitElement))
            return false;

        try
        {
            if (Automation.Compare(originalElement, hitElement))
                return true;
        }
        catch
        {
            // Fall through to property comparison below.
        }

        try
        {
            var original = originalElement.Current;
            var hit = hitElement.Current;
            return string.Equals(original.AutomationId ?? "", hit.AutomationId ?? "", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(original.Name ?? "", hit.Name ?? "", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(original.ClassName ?? "", hit.ClassName ?? "", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsRetryableBrowserOneShotControl(AutomationElement? element)
    {
        if (element is null)
            return false;

        try
        {
            var current = element.Current;
            var name = current.Name ?? "";
            var automationId = current.AutomationId ?? "";
            var className = current.ClassName ?? "";
            return name.Contains("Skip", StringComparison.OrdinalIgnoreCase)
                   || automationId.Contains("skip", StringComparison.OrdinalIgnoreCase)
                   || className.Contains("skip", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool RequiresIsolatedUiaFallback(AutomationElement element)
    {
        try
        {
            var current = element.Current;
            if (current.NativeWindowHandle != 0)
                return false;

            var className = current.ClassName ?? "";
            return !string.IsNullOrWhiteSpace(className);
        }
        catch
        {
            return true;
        }
    }
}
