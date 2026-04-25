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
        JsonArgs.Schema(
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
            ("allow_parent_sendinput", JsonArgs.Prop("boolean", "Explicit unsafe override; default false. Currently reported, not used."))),
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
            var window = WindowEnumerator.Find(windowId!.Value);
            if (window is null)
                return ToolResult.Error($"No window with window_id {windowId.Value}.");
            if (window.Pid != pid)
                return ToolResult.Error($"window_id {windowId.Value} belongs to pid {window.Pid}, not pid {pid}.");

            var element = context.State.UiaTree.GetCachedElement(pid, windowId!.Value, index.Value);
            if (BrowserWindowClassifier.IsLikelyBrowser(window))
            {
                var rect = element.Current.BoundingRectangle;
                if (!rect.IsEmpty)
                {
                    var cdpPort = JsonArgs.OptionalInt(args, "cdp_port") ?? context.State.Config.ChromiumDebuggingPort;
                    if (cdpPort is not null)
                    {
                        var localX = rect.X + rect.Width / 2 - window.Bounds.X;
                        var localY = rect.Y + rect.Height / 2 - window.Bounds.Y;
                        await context.State.AgentCursor.MoveToAsync(
                            new POINT((int)Math.Round(rect.X + rect.Width / 2), (int)Math.Round(rect.Y + rect.Height / 2)),
                            cancellationToken).ConfigureAwait(false);
                        var cdp = new CdpBrowserBridge(context.State.UiaTree);
                        receipt = await cdp.TryClickAsync(window.Hwnd, window.WindowId, localX, localY, count, rightButton: false, cdpPort, cancellationToken).ConfigureAwait(false)
                                  ?? ActionReceipt.Failure("cdp.input.dispatch_mouse", $"No page tab found on CDP port {cdpPort}.");
                        if (receipt.Ok)
                            await PulseCursorAtElementAsync(context, element, cancellationToken).ConfigureAwait(false);
                        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
                    }
                }

                receipt = BrowserNavigation.RefuseForegroundOnlyLinkRoute(UiAutomationActions.TryGetValue(element))
                          ?? ActionReceipt.Failure("requires_cdp_or_child_session", "Refusing browser UIA Invoke from element_index because browser providers can foreground the target. Provide cdp_port for Chromium or use the child-session/AppBroadcast lane.");
                return ToolResult.Text("❌ " + receipt.ToJson(), true);
            }

            await MoveCursorToElementAsync(context, element, cancellationToken).ConfigureAwait(false);
            receipt = await UiAutomationActions.InvokeElementAsync(element, action, cancellationToken).ConfigureAwait(false);
            context.State.LastUiaTextTarget[(pid, windowId.Value)] = element;
            await PulseCursorAtElementAsync(context, element, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (windowId is null)
            {
                var w = fromZoom && context.State.ZoomContexts.TryGetValue(pid, out var zoom)
                    ? WindowEnumerator.Find(zoom.WindowId)
                    : WindowEnumerator.MainWindowForPid(pid);
                if (w is null)
                    return ToolResult.Error($"No window found for pid {pid}.");
                windowId = w.WindowId;
            }

            var window = WindowEnumerator.Find(windowId.Value);
            if (window is null)
                return ToolResult.Error($"No window with window_id {windowId.Value}.");
            if (window.Pid != pid)
                return ToolResult.Error($"window_id {windowId.Value} belongs to pid {window.Pid}, not pid {pid}.");

            if (!string.IsNullOrWhiteSpace(debugImageOut))
            {
                try
                {
                    DebugCrosshair.WriteCrosshair(
                        context.State.Capture,
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

            var hit = context.State.UiaTree.HitTest(pid, window.WindowId, resolved.ScreenPoint);
            if (hit is { IsClickAction: true } && modifiers.Length == 0)
            {
                await context.State.AgentCursor.MoveToAsync(resolved.ScreenPoint, cancellationToken).ConfigureAwait(false);
                if (BrowserWindowClassifier.IsLikelyBrowser(window))
                {
                    var hitCdpPort = JsonArgs.OptionalInt(args, "cdp_port") ?? context.State.Config.ChromiumDebuggingPort;
                    if (hitCdpPort is not null)
                    {
                        var hitCdp = new CdpBrowserBridge(context.State.UiaTree);
                        receipt = await hitCdp.TryClickAsync(window.Hwnd, window.WindowId, clickX, clickY, count, rightButton: false, hitCdpPort, cancellationToken, modifiers).ConfigureAwait(false)
                                  ?? ActionReceipt.Failure("cdp.input.dispatch_mouse", $"No page tab found on CDP port {hitCdpPort}.");
                        if (receipt.Ok)
                            await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, cancellationToken).ConfigureAwait(false);
                        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
                    }

                    var navReceipt = BrowserNavigation.RefuseForegroundOnlyLinkRoute(UiAutomationActions.TryGetValue(hit.Element));
                    if (navReceipt is not null)
                    {
                        receipt = navReceipt with { Route = "uia.hit_test." + navReceipt.Route };
                        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
                    }

                    receipt = ActionReceipt.Failure("requires_cdp_or_browser_link_value", "Browser UIA hit-test found an actionable element, but it did not expose a URL value and no CDP port was configured; refusing UIA Invoke because browser providers commonly raise/focus the window.");
                    return ToolResult.Text("❌ " + receipt.ToJson(), true);
                }

                var hitReceipt = await UiAutomationActions.InvokeElementAsync(hit.Element, action, cancellationToken).ConfigureAwait(false);
                if (hitReceipt.Ok)
                {
                    receipt = hitReceipt with { Route = "uia.hit_test." + hitReceipt.Route };
                    context.State.LastUiaTextTarget[(pid, window.WindowId)] = hit.Element;
                    await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, cancellationToken).ConfigureAwait(false);
                    return ToolResult.Text("✅ " + receipt.ToJson(), false);
                }
            }

            if (hit is { IsTextInput: true } && BrowserWindowClassifier.IsLikelyBrowser(window))
            {
                await context.State.AgentCursor.MoveToAsync(resolved.ScreenPoint, cancellationToken).ConfigureAwait(false);
                context.State.LastUiaTextTarget[(pid, window.WindowId)] = hit.Element;
                receipt = ActionReceipt.Success("uia.hit_test.text_target");
                await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, cancellationToken).ConfigureAwait(false);
                return ToolResult.Text("✅ " + receipt.ToJson(), false);
            }

            var cdpPort = JsonArgs.OptionalInt(args, "cdp_port") ?? context.State.Config.ChromiumDebuggingPort;
            var cdp = new CdpBrowserBridge(context.State.UiaTree);
            await context.State.AgentCursor.MoveToAsync(resolved.ScreenPoint, cancellationToken).ConfigureAwait(false);
            receipt = await cdp.TryClickAsync(window.Hwnd, window.WindowId, clickX, clickY, count, rightButton: false, cdpPort, cancellationToken, modifiers).ConfigureAwait(false)
                      ?? (BrowserWindowClassifier.IsLikelyBrowser(window)
                          ? ActionReceipt.Failure("requires_cdp_or_uia_hit_test", "Browser web content did not expose an actionable UIA target and no CDP port was configured; refusing to report a blind PostMessage click as delivered.")
                          : (await WindowMessageInput.ClickAsync(window.Hwnd, clickX, clickY, count, rightButton: false, cancellationToken, modifiers).ConfigureAwait(false)).Receipt);

            if (receipt.Ok)
            {
                context.State.LastTargetHwnd[(pid, window.WindowId)] = resolved.TargetHwnd;
                await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, cancellationToken).ConfigureAwait(false);
            }
        }

        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
    }

    private static async Task MoveCursorToElementAsync(ToolContext context, AutomationElement element, CancellationToken ct)
    {
        try
        {
            var rect = element.Current.BoundingRectangle;
            if (!rect.IsEmpty)
            {
                await context.State.AgentCursor.MoveToAsync(
                    new POINT((int)Math.Round(rect.X + rect.Width / 2), (int)Math.Round(rect.Y + rect.Height / 2)),
                    ct).ConfigureAwait(false);
            }
        }
        catch
        {
            // Cursor overlay is best effort.
        }
    }

    private static async Task PulseCursorAtElementAsync(ToolContext context, AutomationElement element, CancellationToken ct)
    {
        try
        {
            var rect = element.Current.BoundingRectangle;
            if (!rect.IsEmpty)
            {
                await context.State.AgentCursor.ClickPulseAsync(
                    new POINT((int)Math.Round(rect.X + rect.Width / 2), (int)Math.Round(rect.Y + rect.Height / 2)),
                    ct).ConfigureAwait(false);
            }
        }
        catch
        {
            // Cursor overlay is best effort.
        }
    }
}
