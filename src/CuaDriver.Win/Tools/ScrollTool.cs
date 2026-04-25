using System.Text.Json.Nodes;
using System.Windows.Automation;
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
        ToolDescriptions.Scroll,
        JsonArgs.Schema(
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("direction", JsonArgs.Prop("string", "Mac-compatible direction: up, down, left, or right.")),
            ("amount", JsonArgs.Prop("integer", "Number of key or wheel repetitions. Default: 3 for direction mode.")),
            ("by", JsonArgs.Prop("string", "Scroll granularity for direction mode: line or page. Default: line.")),
            ("element_index", JsonArgs.Prop("integer", "Optional element index from get_window_state. With direction mode, targets that element's native HWND when available.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND. Required when element_index is used.")),
            ("x", JsonArgs.Prop("number", "Optional window-local screenshot X for Windows wheel mode.")),
            ("y", JsonArgs.Prop("number", "Optional window-local screenshot Y for Windows wheel mode.")),
            ("delta", JsonArgs.Prop("integer", "Windows wheel delta; positive up, negative down. Default -120. Used when direction is omitted."))),
        Destructive: false,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var direction = JsonArgs.OptionalString(args, "direction");
        if (!string.IsNullOrWhiteSpace(direction))
            return await ScrollByKeysAsync(pid, direction!, args, context, cancellationToken).ConfigureAwait(false);

        return await ScrollByWheelAsync(pid, args, context, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ToolResult> ScrollByKeysAsync(int pid, string direction, JsonObject args, ToolContext context, CancellationToken ct)
    {
        var amount = Math.Clamp(JsonArgs.OptionalInt(args, "amount") ?? 3, 1, 50);
        var by = JsonArgs.OptionalString(args, "by") ?? "line";
        var key = ScrollKey(direction, by);
        if (key is null)
            return ToolResult.Error($"Invalid direction/by combination: {direction}/{by}.");

        var index = JsonArgs.OptionalInt(args, "element_index");
        var windowId = JsonArgs.OptionalLong(args, "window_id");
        if (index is not null && windowId is null)
            return ToolResult.Error("window_id is required when element_index is used.");
        windowId ??= WindowEnumerator.MainWindowForPid(pid)?.WindowId;
        if (windowId is null)
            return ToolResult.Error($"No window found for pid {pid}.");

        var window = WindowEnumerator.Find(windowId.Value);
        if (window is null)
            return ToolResult.Error($"No window with window_id {windowId.Value}.");
        if (window.Pid != pid)
            return ToolResult.Error($"window_id {windowId.Value} belongs to pid {window.Pid}, not pid {pid}.");

        var targetHwnd = window.Hwnd;
        if (index is not null)
        {
            var element = context.State.UiaTree.GetCachedElement(pid, windowId.Value, index.Value);
            await AgentCursorTooling.MoveToElementAsync(context, element, ct).ConfigureAwait(false);
            var elementHwnd = ElementHwnd(element);
            if (elementHwnd != IntPtr.Zero)
                targetHwnd = elementHwnd;
        }
        else
        {
            await context.State.AgentCursor.MoveToAsync(WindowMessageInput.CenterOf(window.Hwnd), ct).ConfigureAwait(false);
        }

        ActionReceipt receipt = ActionReceipt.Success("hwnd.key.scroll");
        for (var i = 0; i < amount; i++)
        {
            receipt = await WindowMessageInput.PressKeyAsync(targetHwnd, key, [], ct).ConfigureAwait(false);
            if (!receipt.Ok)
                break;
        }

        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
    }

    private static async Task<ToolResult> ScrollByWheelAsync(int pid, JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var windowId = JsonArgs.OptionalLong(args, "window_id") ?? WindowEnumerator.MainWindowForPid(pid)?.WindowId;
        if (windowId is null)
            return ToolResult.Error($"No window found for pid {pid}.");

        var window = WindowEnumerator.Find(windowId.Value);
        if (window is null)
            return ToolResult.Error($"No window with window_id {windowId.Value}.");
        if (window.Pid != pid)
            return ToolResult.Error($"window_id {windowId.Value} belongs to pid {window.Pid}, not pid {pid}.");

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

    private static string? ScrollKey(string direction, string by)
    {
        return (direction.Trim().ToLowerInvariant(), by.Trim().ToLowerInvariant()) switch
        {
            ("up", "line") => "up",
            ("down", "line") => "down",
            ("left", "line") => "left",
            ("right", "line") => "right",
            ("up", "page") => "pageup",
            ("down", "page") => "pagedown",
            ("left", "page") => "left",
            ("right", "page") => "right",
            _ => null
        };
    }

    private static IntPtr ElementHwnd(AutomationElement element)
    {
        try
        {
            var hwnd = element.Current.NativeWindowHandle;
            return hwnd == 0 ? IntPtr.Zero : new IntPtr(hwnd);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
}
