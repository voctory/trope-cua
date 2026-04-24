using System.Text.Json.Nodes;
using CuaDriver.Win.Browser;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Uia;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class ScrollTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "scroll",
        "Scroll by UIA ScrollPattern where available, otherwise post WM_MOUSEWHEEL to the target child HWND under x/y or to the window center.",
        JsonArgs.Schema(
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND.")),
            ("x", JsonArgs.Prop("number", "Optional window-local screenshot X.")),
            ("y", JsonArgs.Prop("number", "Optional window-local screenshot Y.")),
            ("delta", JsonArgs.Prop("integer", "Wheel delta; positive up, negative down. Default -120."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var windowId = JsonArgs.OptionalLong(args, "window_id") ?? WindowEnumerator.MainWindowForPid(pid)?.WindowId;
        if (windowId is null)
            return ToolResult.Error($"No window found for pid {pid}.");

        var window = WindowEnumerator.Find(windowId.Value);
        if (window is null)
            return ToolResult.Error($"No window with window_id {windowId.Value}.");

        var x = JsonArgs.OptionalDouble(args, "x");
        var y = JsonArgs.OptionalDouble(args, "y");
        var delta = JsonArgs.OptionalInt(args, "delta") ?? -120;

        WindowMessageDispatch resolved;
        if (x is not null && y is not null)
        {
            var ratio = context.State.ImageResizeRatio.TryGetValue((pid, windowId.Value), out var r) ? r : 1.0;
            resolved = WindowMessageInput.ResolvePointTarget(window.Hwnd, x.Value * ratio, y.Value * ratio);
        }
        else
        {
            var center = WindowMessageInput.CenterLocal(window.Hwnd);
            resolved = WindowMessageInput.ResolvePointTarget(window.Hwnd, center.X, center.Y);
        }

        ActionReceipt receipt;
        var scrollHit = context.State.UiaTree.FindScrollableAtPoint(pid, windowId.Value, resolved.ScreenPoint);
        if (scrollHit is not null)
        {
            await context.State.AgentCursor.MoveToAsync(resolved.ScreenPoint, cancellationToken).ConfigureAwait(false);
            receipt = UiAutomationActions.Scroll(scrollHit.Element, delta);
            if (receipt.Ok)
                return ToolResult.Text("✅ " + (receipt with { Route = "uia.hit_test." + receipt.Route }).ToJson());
        }

        await context.State.AgentCursor.MoveToAsync(resolved.ScreenPoint, cancellationToken).ConfigureAwait(false);
        receipt = BrowserWindowClassifier.IsLikelyBrowser(window)
            ? ActionReceipt.Failure("requires_cdp_or_uia_scroll", "Browser content did not expose UIA ScrollPattern and no browser-specific scroll route is configured; refusing blind WM_MOUSEWHEEL.")
            : WindowMessageInput.Scroll(window.Hwnd, resolved.ScreenPoint.X - window.Bounds.X, resolved.ScreenPoint.Y - window.Bounds.Y, delta);

        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
    }
}
