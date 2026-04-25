using System.Text.Json.Nodes;
using CuaDriver.Win.Browser;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Uia;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class RightClickTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "right_click",
        ToolDescriptions.RightClick,
        JsonArgs.Schema(
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
        var pid = JsonArgs.RequiredInt(args, "pid");
        var windowId = JsonArgs.OptionalLong(args, "window_id");
        var index = JsonArgs.OptionalInt(args, "element_index");
        var x = JsonArgs.OptionalDouble(args, "x");
        var y = JsonArgs.OptionalDouble(args, "y");
        var modifiers = JsonArgs.OptionalStringArray(args, "modifier");
        if (modifiers.Length == 0)
            modifiers = JsonArgs.OptionalStringArray(args, "modifiers");

        ActionReceipt receipt;
        if (index is not null)
        {
            if (windowId is null)
                return ToolResult.Error("window_id is required for element_index right_click.");
            var window = WindowEnumerator.Find(windowId.Value);
            if (window is null)
                return ToolResult.Error($"No window with window_id {windowId.Value}.");
            if (window.Pid != pid)
                return ToolResult.Error($"window_id {windowId.Value} belongs to pid {window.Pid}, not pid {pid}.");
            var element = context.State.UiaTree.GetCachedElement(pid, windowId.Value, index.Value);
            await AgentCursorTooling.MoveToElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
            if (BrowserWindowClassifier.IsLikelyBrowser(window))
            {
                var rect = element.Current.BoundingRectangle;
                var cdpPort = JsonArgs.OptionalInt(args, "cdp_port") ?? context.State.Config.ChromiumDebuggingPort;
                if (!rect.IsEmpty && cdpPort is not null)
                {
                    var localX = rect.X + rect.Width / 2 - window.Bounds.X;
                    var localY = rect.Y + rect.Height / 2 - window.Bounds.Y;
                    var cdp = new CdpBrowserBridge(context.State.UiaTree);
                    receipt = await cdp.TryClickAsync(window.Hwnd, window.WindowId, localX, localY, 1, rightButton: true, cdpPort, cancellationToken).ConfigureAwait(false)
                              ?? ActionReceipt.Failure("cdp.input.dispatch_mouse.right", $"No page tab found on CDP port {cdpPort}.");
                    if (receipt.Ok)
                        await AgentCursorTooling.PulseAtElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
                    return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
                }

                receipt = ActionReceipt.Failure("requires_cdp_or_child_session", "Refusing browser UIA show_menu from element_index because browser providers can foreground the target. Provide cdp_port for Chromium or use the child-session/AppBroadcast lane.");
                return ToolResult.Text("❌ " + receipt.ToJson(), true);
            }
            receipt = await UiAutomationActions.InvokeElementAsync(element, "show_menu", cancellationToken).ConfigureAwait(false);
            if (receipt.Ok)
                await AgentCursorTooling.PulseAtElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (x is null || y is null)
                return ToolResult.Error("Provide element_index or both x and y.");
            if (windowId is null)
            {
                var w = WindowEnumerator.MainWindowForPid(pid);
                if (w is null)
                    return ToolResult.Error($"No window found for pid {pid}.");
                windowId = w.WindowId;
            }
            var window = WindowEnumerator.Find(windowId.Value);
            if (window is null)
                return ToolResult.Error($"No window with window_id {windowId.Value}.");

            var ratio = context.State.ImageResizeRatio.TryGetValue((pid, window.WindowId), out var r) ? r : 1.0;
            var clickX = x.Value * ratio;
            var clickY = y.Value * ratio;
            var resolved = WindowMessageInput.ResolvePointTarget(window.Hwnd, clickX, clickY);

            var hit = context.State.UiaTree.HitTest(pid, window.WindowId, resolved.ScreenPoint);
            if (hit is { IsClickAction: true } && modifiers.Length == 0)
            {
                await context.State.AgentCursor.MoveToAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                if (BrowserWindowClassifier.IsLikelyBrowser(window))
                {
                    var hitCdpPort = JsonArgs.OptionalInt(args, "cdp_port") ?? context.State.Config.ChromiumDebuggingPort;
                    if (hitCdpPort is not null)
                    {
                        var hitCdp = new CdpBrowserBridge(context.State.UiaTree);
                        receipt = await hitCdp.TryClickAsync(window.Hwnd, window.WindowId, clickX, clickY, 1, rightButton: true, hitCdpPort, cancellationToken, modifiers).ConfigureAwait(false)
                                  ?? ActionReceipt.Failure("cdp.input.dispatch_mouse.right", $"No page tab found on CDP port {hitCdpPort}.");
                        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
                    }

                    receipt = ActionReceipt.Failure("requires_cdp_or_child_session", "Refusing browser UIA show_menu from hit-test because browser providers can foreground the target. Provide cdp_port for Chromium or use the child-session/AppBroadcast lane.");
                    return ToolResult.Text("❌ " + receipt.ToJson(), true);
                }

                var hitReceipt = await UiAutomationActions.InvokeElementAsync(hit.Element, "show_menu", cancellationToken).ConfigureAwait(false);
                if (hitReceipt.Ok)
                {
                    receipt = hitReceipt with { Route = "uia.hit_test." + hitReceipt.Route };
                    await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
                    return ToolResult.Text("✅ " + receipt.ToJson());
                }
            }

            var cdpPort = JsonArgs.OptionalInt(args, "cdp_port") ?? context.State.Config.ChromiumDebuggingPort;
            var cdp = new CdpBrowserBridge(context.State.UiaTree);
            await context.State.AgentCursor.MoveToAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
            receipt = await cdp.TryClickAsync(window.Hwnd, window.WindowId, clickX, clickY, 1, rightButton: true, cdpPort, cancellationToken, modifiers).ConfigureAwait(false)
                      ?? (BrowserWindowClassifier.IsLikelyBrowser(window)
                          ? ActionReceipt.Failure("requires_cdp_or_uia_hit_test", "Browser web content did not expose an actionable UIA target and no CDP port was configured; refusing to report a blind PostMessage right-click as delivered.")
                          : (await WindowMessageInput.ClickAsync(window.Hwnd, clickX, clickY, 1, rightButton: true, cancellationToken, modifiers).ConfigureAwait(false)).Receipt);

            if (receipt.Ok)
            {
                context.State.LastTargetHwnd[(pid, window.WindowId)] = resolved.TargetHwnd;
                await context.State.AgentCursor.ClickPulseAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
            }
        }

        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
    }
}
