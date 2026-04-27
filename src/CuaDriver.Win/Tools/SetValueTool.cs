using System.Text.Json.Nodes;
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
            ("allow_transient_foreground", JsonArgs.Prop("boolean", "Explicit unsafe override. Allows a native UIA value route to briefly foreground/focus the target, then attempts to restore the previous cursor and foreground. Receipt remains background_safe=false when this happens."))),
        Destructive: true,
        Idempotent: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var windowId = JsonArgs.RequiredLong(args, "window_id");
        var index = JsonArgs.RequiredInt(args, "element_index");
        var value = JsonArgs.RequiredString(args, "value");
        var allowTransientForeground = JsonArgs.OptionalBool(args, "allow_transient_foreground");

        if (!ToolWindows.TryFindForPid(pid, windowId, out var window, out var error))
            return error!;

        var targetHwnd = window.Hwnd;
        var element = context.State.UiaTree.GetCachedElement(pid, windowId, index);
        await AgentCursorTooling.MoveToElementAsync(context, element, targetHwnd, cancellationToken).ConfigureAwait(false);

        ActionReceipt receipt;
        if (double.TryParse(value, out var number))
        {
            if (RequiresIsolatedRangeLane(element) && !allowTransientForeground)
                return ActionToolResult.FromReceipt(ActionReceipt.Failure(
                    "requires_child_session",
                    "This range control did not expose a verified background-safe value route, and UIA RangeValue.SetValue can foreground native WinUI apps. Use a semantic scrollbar/button action, the child-session/AppBroadcast lane, or pass allow_transient_foreground=true for an explicit unsafe foreground/focus blip."));

            receipt = UiAutomationActions.SetRangeValue(element, number, allowTransientForeground: allowTransientForeground);
            if (!receipt.Ok && !receipt.ShouldStopFallback)
                receipt = MsaaActions.SetEditableTextAtElement(window.Hwnd, element, value);
            if (!receipt.Ok && !receipt.ShouldStopFallback)
            {
                if (RequiresIsolatedValueLane(element) && !allowTransientForeground)
                    return ActionToolResult.FromReceipt(ActionReceipt.Failure(
                        "requires_child_session",
                        "This value control did not expose a background-safe IA2/MSAA value route, and UIA ValuePattern.SetValue can foreground native WinUI apps. Use a semantic button action, the child-session/AppBroadcast lane, or pass allow_transient_foreground=true for an explicit unsafe foreground/focus blip."));

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
                        "This value control did not expose a background-safe IA2/MSAA value route, and UIA ValuePattern.SetValue can foreground native WinUI apps. Use a semantic button action, the child-session/AppBroadcast lane, or pass allow_transient_foreground=true for an explicit unsafe foreground/focus blip."));

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
