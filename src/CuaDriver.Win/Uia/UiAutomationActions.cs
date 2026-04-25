using System.Windows.Automation;
using CuaDriver.Win.Input;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Uia;

public static class UiAutomationActions
{
    public static async Task<ActionReceipt> InvokeElementAsync(AutomationElement element, string actionName, CancellationToken ct)
    {
        using var guard = NoRegressionGuard.Capture();

        try
        {
            if (actionName is "show_menu" or "right_click")
            {
                var rect = element.Current.BoundingRectangle;
                if (!rect.IsEmpty)
                {
                    var hwnd = NativeHwnd(element);
                    if (hwnd != IntPtr.Zero)
                    {
                        var frame = NativeMethods.GetBestWindowRect(hwnd);
                        var dispatch = await WindowMessageInput.ClickAsync(
                            hwnd,
                            rect.X + rect.Width / 2 - frame.Left,
                            rect.Y + rect.Height / 2 - frame.Top,
                            1,
                            rightButton: true,
                            ct).ConfigureAwait(false);
                        return dispatch.Receipt;
                    }
                }
            }

            if (TryPattern<InvokePattern>(element, InvokePattern.Pattern, out var invoke))
            {
                invoke.Invoke();
                return guard.Finish(ActionReceipt.Success("uia.invoke"));
            }

            if (TryPattern<TogglePattern>(element, TogglePattern.Pattern, out var toggle))
            {
                toggle.Toggle();
                return guard.Finish(ActionReceipt.Success("uia.toggle"));
            }

            if (TryPattern<SelectionItemPattern>(element, SelectionItemPattern.Pattern, out var selection))
            {
                selection.Select();
                return guard.Finish(ActionReceipt.Success("uia.selection_item.select"));
            }

            if (TryPattern<ExpandCollapsePattern>(element, ExpandCollapsePattern.Pattern, out var expand))
            {
                if (expand.Current.ExpandCollapseState == ExpandCollapseState.Collapsed)
                    expand.Expand();
                else if (expand.Current.ExpandCollapseState == ExpandCollapseState.Expanded)
                    expand.Collapse();
                else
                    expand.Expand();
                return guard.Finish(ActionReceipt.Success("uia.expand_collapse"));
            }

            if (TryPattern<ScrollItemPattern>(element, ScrollItemPattern.Pattern, out var scrollItem))
            {
                scrollItem.ScrollIntoView();
                return guard.Finish(ActionReceipt.Success("uia.scroll_item"));
            }

            return guard.Finish(ActionReceipt.Failure("uia.unsupported", "Element exposes no supported action pattern."));
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("uia.action", ex.Message));
        }
    }

    public static ActionReceipt Scroll(AutomationElement element, int delta)
    {
        using var guard = NoRegressionGuard.Capture();

        try
        {
            if (!TryPattern<ScrollPattern>(element, ScrollPattern.Pattern, out var scroll))
                return guard.Finish(ActionReceipt.Failure("uia.scroll", "Element exposes no ScrollPattern."));

            if (scroll.Current.VerticallyScrollable)
            {
                var amount = delta < 0 ? ScrollAmount.LargeIncrement : ScrollAmount.LargeDecrement;
                scroll.ScrollVertical(amount);
                return guard.Finish(ActionReceipt.Success("uia.scroll.vertical"));
            }

            if (scroll.Current.HorizontallyScrollable)
            {
                var amount = delta < 0 ? ScrollAmount.LargeIncrement : ScrollAmount.LargeDecrement;
                scroll.ScrollHorizontal(amount);
                return guard.Finish(ActionReceipt.Success("uia.scroll.horizontal"));
            }

            return guard.Finish(ActionReceipt.Failure("uia.scroll", "Element is not scrollable."));
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("uia.scroll", ex.Message));
        }
    }

    public static ActionReceipt SetValue(AutomationElement element, string value)
    {
        using var guard = NoRegressionGuard.Capture();

        try
        {
            if (TryPattern<ValuePattern>(element, ValuePattern.Pattern, out var valuePattern))
            {
                if (valuePattern.Current.IsReadOnly)
                    return guard.Finish(ActionReceipt.Failure("uia.value", "ValuePattern is read-only."));
                valuePattern.SetValue(value);
                return guard.Finish(ActionReceipt.Success("uia.value.set"));
            }

            var hwnd = NativeHwnd(element);
            if (hwnd != IntPtr.Zero)
                return WindowMessageInput.SetText(hwnd, value);

            return guard.Finish(ActionReceipt.Failure("uia.value", "Element exposes neither ValuePattern nor NativeWindowHandle."));
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("uia.value", ex.Message));
        }
    }

    public static ActionReceipt SetRangeValue(AutomationElement element, double value)
    {
        using var guard = NoRegressionGuard.Capture();

        try
        {
            if (TryPattern<RangeValuePattern>(element, RangeValuePattern.Pattern, out var range))
            {
                if (range.Current.IsReadOnly)
                    return guard.Finish(ActionReceipt.Failure("uia.range_value", "RangeValuePattern is read-only."));
                range.SetValue(value);
                return guard.Finish(ActionReceipt.Success("uia.range_value.set"));
            }
            return guard.Finish(ActionReceipt.Failure("uia.range_value", "Element exposes no RangeValuePattern."));
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("uia.range_value", ex.Message));
        }
    }

    public static string? TryGetValue(AutomationElement element)
    {
        try
        {
            if (TryPattern<ValuePattern>(element, ValuePattern.Pattern, out var valuePattern))
                return valuePattern.Current.Value;
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool TryPattern<T>(AutomationElement element, AutomationPattern pattern, out T value) where T : class
    {
        if (element.TryGetCurrentPattern(pattern, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = null!;
        return false;
    }

    private static IntPtr NativeHwnd(AutomationElement element)
    {
        try
        {
            var h = element.Current.NativeWindowHandle;
            return h == 0 ? IntPtr.Zero : new IntPtr(h);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
}
