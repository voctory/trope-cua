using System.Text.Json.Nodes;
using CuaDriver.Win.Browser;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Uia;

namespace CuaDriver.Win.Tools;

internal sealed class SetValueTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "set_value",
        ToolDescriptions.SetValue,
        JsonArgs.RequiredSchema(["pid", "window_id", "element_index", "value"],
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND.")),
            ("element_index", JsonArgs.Prop("integer", "Element index from get_window_state.")),
            ("value", JsonArgs.Prop("string", "String value, or numeric text for range controls.")),
            ("allow_transient_foreground", JsonArgs.Prop("boolean", "Allow a brief native UIA foreground/focus blip when no verified background value route exists, then attempt to restore the previous foreground. Defaults true for existing-window actions; receipts still report background_safe=false when this happens."))),
        Destructive: true,
        Idempotent: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var windowId = JsonArgs.RequiredLong(args, "window_id");
        var index = JsonArgs.RequiredInt(args, "element_index");
        var value = JsonArgs.RequiredString(args, "value");
        var allowTransientForeground = JsonArgs.OptionalBool(args, "allow_transient_foreground", true);

        if (!ToolWindows.TryFindForPid(pid, windowId, out var window, out var error))
            return error!;

        var targetHwnd = window.Hwnd;
        var element = context.State.UiaTree.GetCachedElement(pid, windowId, index);
        await AgentCursorTooling.MoveToElementAsync(context, element, targetHwnd, cancellationToken).ConfigureAwait(false);

        if (BrowserWindowClassifier.IsLikelyChromium(window) && !double.TryParse(value, out _))
        {
            var browserReceipt = MsaaActions.SetEditableTextAtElement(window.Hwnd, element, value);
            if (browserReceipt.Ok)
                return ActionToolResult.FromReceipt(browserReceipt with { Route = "browser.ia2." + browserReceipt.Route });

            return ActionToolResult.FromReceipt(ActionReceipt.Failure(
                "use_type_text_for_browser_text",
                $"Chromium text fields should be filled with type_text using the same element_index, then press_key for Enter/Return. set_value is reserved for atomic ValuePattern controls; the safe IA2 replacement route failed ({browserReceipt.Route}: {browserReceipt.Reason}). Configure cdp_port or chromium_debugging_port only when this is a CDP-enabled browser window, otherwise use child-session/AppBroadcast or report the blocker."));
        }

        ActionReceipt receipt;
        if (double.TryParse(value, out var number))
        {
            if (RequiresIsolatedRangeLane(element) && !allowTransientForeground)
                return ActionToolResult.FromReceipt(ActionReceipt.Failure(
                    "requires_child_session",
                    "This range control did not expose a verified background-safe value route, and UIA RangeValue.SetValue can foreground native WinUI apps. Keep allow_transient_foreground enabled for the existing window, use a semantic scrollbar/button action, or use the child-session/AppBroadcast lane."));

            receipt = UiAutomationActions.SetRangeValue(element, number, allowTransientForeground: allowTransientForeground);
            if (!receipt.Ok && !receipt.ShouldStopFallback)
                receipt = MsaaActions.SetEditableTextAtElement(window.Hwnd, element, value);
            if (!receipt.Ok && !receipt.ShouldStopFallback)
            {
                if (RequiresIsolatedValueLane(element) && !allowTransientForeground)
                    return ActionToolResult.FromReceipt(ActionReceipt.Failure(
                        "requires_child_session",
                        "This value control did not expose a background-safe IA2/MSAA value route, and UIA ValuePattern.SetValue can foreground native WinUI apps. Keep allow_transient_foreground enabled for the existing window, use a semantic button action, or use the child-session/AppBroadcast lane."));

                receipt = UiAutomationActions.SetValue(element, value, allowTransientForeground: allowTransientForeground);
            }
        }
        else
        {
            receipt = MsaaActions.SetEditableTextAtElement(window.Hwnd, element, value);
            if (!receipt.Ok && !receipt.ShouldStopFallback)
            {
                if (RequiresIsolatedValueLane(element) && !allowTransientForeground)
                    return ActionToolResult.FromReceipt(ActionReceipt.Failure(
                        "requires_child_session",
                        "This value control did not expose a background-safe IA2/MSAA value route, and UIA ValuePattern.SetValue can foreground native WinUI apps. Keep allow_transient_foreground enabled for the existing window, use a semantic button action, or use the child-session/AppBroadcast lane."));

                receipt = UiAutomationActions.SetValue(element, value, allowTransientForeground: allowTransientForeground);
            }
        }

        return ActionToolResult.FromReceipt(receipt);
    }

    private static bool RequiresIsolatedRangeLane(System.Windows.Automation.AutomationElement element)
    {
        try
        {
            var current = element.Current;
            if (current.NativeWindowHandle != 0)
                return false;

            return element.TryGetCurrentPattern(System.Windows.Automation.RangeValuePattern.Pattern, out _);
        }
        catch
        {
            return true;
        }
    }

    private static bool RequiresIsolatedValueLane(System.Windows.Automation.AutomationElement element)
    {
        try
        {
            var current = element.Current;
            if (current.NativeWindowHandle != 0)
                return false;

            return element.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out _);
        }
        catch
        {
            return true;
        }
    }

}
