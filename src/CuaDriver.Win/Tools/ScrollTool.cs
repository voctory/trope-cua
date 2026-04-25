using System.Text.Json.Nodes;
using System.Windows.Automation;
using CuaDriver.Win.Browser;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Uia;

namespace CuaDriver.Win.Tools;

internal sealed class ScrollTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "scroll",
        ToolDescriptions.Scroll,
        JsonArgs.RequiredSchema(["pid"],
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("direction", JsonArgs.EnumProp("Logical scroll direction.", "up", "down", "left", "right")),
            ("amount", JsonArgs.Prop("integer", "Number of key or wheel repetitions. Default: 3 for direction mode.")),
            ("by", JsonArgs.EnumProp("Scroll granularity for direction mode. Default: line.", "line", "page")),
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
        if (!ToolWindows.TryFindMainOrForPid(pid, windowId, out var window, out var error))
            return error!;

        var targetHwnd = window.Hwnd;
        if (index is not null)
        {
            var element = context.State.UiaTree.GetCachedElement(pid, window.WindowId, index.Value);
            await AgentCursorTooling.MoveToElementAsync(context, element, window.Hwnd, ct).ConfigureAwait(false);
            var elementHwnd = ElementHwnd(element);
            if (elementHwnd != IntPtr.Zero)
                targetHwnd = elementHwnd;
        }
        else
        {
            await context.State.AgentCursor.MoveToAsync(WindowMessageInput.CenterOf(window.Hwnd), window.Hwnd, ct).ConfigureAwait(false);
        }

        ActionReceipt receipt = ActionReceipt.Success("hwnd.key.scroll");
        for (var i = 0; i < amount; i++)
        {
            receipt = await WindowMessageInput.PressKeyAsync(targetHwnd, key, [], ct).ConfigureAwait(false);
            if (!receipt.Ok)
                break;
        }

        return ActionToolResult.FromReceipt(receipt);
    }

    private static async Task<ToolResult> ScrollByWheelAsync(int pid, JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var x = JsonArgs.OptionalDouble(args, "x");
        var y = JsonArgs.OptionalDouble(args, "y");
        if ((x is null) != (y is null))
            return ToolResult.Error("Provide both x and y for wheel scrolling, or neither.");

        if (!ToolWindows.TryFindMainOrForPid(pid, JsonArgs.OptionalLong(args, "window_id"), out var window, out var error))
            return error!;

        var delta = JsonArgs.OptionalInt(args, "delta") ?? -120;

        WindowMessageDispatch resolved;
        if (x is not null && y is not null)
        {
            resolved = ToolCoordinates.ResolvePointTarget(context, pid, window, x.Value, y.Value);
        }
        else
        {
            var center = WindowMessageInput.CenterLocal(window.Hwnd);
            resolved = ToolCoordinates.ResolveNativePointTarget(window, center.X, center.Y);
        }

        ActionReceipt receipt;
        var scrollHit = UiAutomationTree.FindScrollableAtPoint(pid, window.WindowId, resolved.ScreenPoint);
        if (scrollHit is not null)
        {
            await context.State.AgentCursor.MoveToAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
            receipt = UiAutomationActions.Scroll(scrollHit.Element, delta);
            if (receipt.Ok)
                return ActionToolResult.FromReceipt(receipt, "uia.hit_test.");
        }

        await context.State.AgentCursor.MoveToAsync(resolved.ScreenPoint, window.Hwnd, cancellationToken).ConfigureAwait(false);
        receipt = BrowserWindowClassifier.IsLikelyBrowser(window)
            ? ActionReceipt.Failure("requires_cdp_or_uia_scroll", "Browser content did not expose UIA ScrollPattern and no browser-specific scroll route is configured; refusing blind WM_MOUSEWHEEL.")
            : WindowMessageInput.Scroll(window.Hwnd, resolved.ScreenPoint.X - window.Bounds.X, resolved.ScreenPoint.Y - window.Bounds.Y, delta);

        return ActionToolResult.FromReceipt(receipt);
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
