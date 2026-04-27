using System.Windows.Automation;
using Accessibility;
using CuaDriver.Win.Input;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Uia;

internal static class MsaaActions
{
    private const int Ia2TextOffsetLength = -1;

    public static ActionReceipt DoDefaultActionAtElement(IntPtr rootHwnd, AutomationElement element)
    {
        try
        {
            var points = MsaaActionTargeting.PreferredActionPoints(element).ToArray();
            if (points.Length == 0)
                return ActionReceipt.Failure("msaa.default_action", "Element has no bounding rectangle.");

            ActionReceipt? last = null;
            foreach (var point in points)
            {
                var receipt = DoDefaultActionAtPoint(rootHwnd, point);
                if (receipt.ShouldStopFallback)
                    return receipt;

                last = receipt;
            }

            return last ?? ActionReceipt.Failure("msaa.default_action", "Element has no actionable point.");
        }
        catch (Exception ex)
        {
            return ActionReceipt.Failure("msaa.default_action", ex.Message);
        }
    }

    public static ActionReceipt DoDefaultActionAtPoint(IntPtr rootHwnd, POINT screenPoint)
    {
        using var guard = NoRegressionGuard.Capture();

        try
        {
            var iid = NativeMethods.IID_IAccessible;
            var hr = NativeMethods.AccessibleObjectFromWindow(
                rootHwnd,
                NativeMethods.OBJID_CLIENT,
                ref iid,
                out var root);
            if (hr < 0 || root is null)
                return guard.Finish(ActionReceipt.Failure("msaa.default_action", $"AccessibleObjectFromWindow failed with HRESULT 0x{hr:X8}."));

            var target = MsaaAccessibleTree.HitTestDeep(root, screenPoint.X, screenPoint.Y);
            if (target is null)
                return guard.Finish(ActionReceipt.Failure("msaa.default_action", "MSAA hit-test found no actionable object."));

            target = MsaaAccessibleTree.PromoteClickAncestor(target);
            var action = AsAccessibleAction(target.Accessible);
            if (action is not null && action.nActions(out var actions) == 0 && actions > 0)
            {
                var actionHr = action.doAction(0);
                if (actionHr == 0)
                    return guard.Finish(ActionReceipt.Success("ia2.action.do_action"));
            }

            target.Accessible.accDoDefaultAction(target.ChildId);
            return guard.Finish(ActionReceipt.Success("msaa.default_action"));
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("msaa.default_action", ex.Message));
        }
    }

    public static ActionReceipt SetEditableTextAtElement(IntPtr rootHwnd, AutomationElement element, string value)
    {
        using var guard = NoRegressionGuard.Capture();

        try
        {
            var rect = element.Current.BoundingRectangle;
            if (rect.IsEmpty)
                return guard.Finish(ActionReceipt.Failure("ia2.editable_text", "Element has no bounding rectangle."));

            var point = new POINT(
                (int)Math.Round(rect.X + rect.Width / 2),
                (int)Math.Round(rect.Y + rect.Height / 2));

            var iid = NativeMethods.IID_IAccessible;
            var hr = NativeMethods.AccessibleObjectFromWindow(
                rootHwnd,
                NativeMethods.OBJID_CLIENT,
                ref iid,
                out var root);
            if (hr < 0 || root is null)
                return guard.Finish(ActionReceipt.Failure("ia2.editable_text", $"AccessibleObjectFromWindow failed with HRESULT 0x{hr:X8}."));

            var target = MsaaAccessibleTree.HitTestDeep(root, point.X, point.Y);
            if (target is null)
                return guard.Finish(ActionReceipt.Failure("ia2.editable_text", "MSAA hit-test found no editable object."));

            var current = target.Accessible;
            var lastHr = 0;
            for (var depth = 0; current is not null && depth < 8; depth++)
            {
                var editable = AsAccessibleEditableText(current);
                if (editable is not null)
                {
                    var replacement = value;
                    lastHr = editable.replaceText(0, Ia2TextOffsetLength, ref replacement);
                    if (lastHr == 0)
                        return guard.Finish(ActionReceipt.Success("ia2.editable_text.replace_text"));
                }

                current = MsaaAccessibleTree.TryGetParent(current);
            }

            var reason = lastHr == 0
                ? "Element exposes no IAccessibleEditableText interface."
                : $"IAccessibleEditableText.replaceText failed with HRESULT 0x{lastHr:X8}.";
            return guard.Finish(ActionReceipt.Failure("ia2.editable_text", reason));
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("ia2.editable_text", ex.Message));
        }
    }

    public static ActionReceipt InsertEditableTextAtElement(IntPtr rootHwnd, AutomationElement element, string value)
    {
        using var guard = NoRegressionGuard.Capture();

        try
        {
            var rect = element.Current.BoundingRectangle;
            if (rect.IsEmpty)
                return guard.Finish(ActionReceipt.Failure("ia2.editable_text.insert", "Element has no bounding rectangle."));

            var point = new POINT(
                (int)Math.Round(rect.X + rect.Width / 2),
                (int)Math.Round(rect.Y + rect.Height / 2));

            var iid = NativeMethods.IID_IAccessible;
            var hr = NativeMethods.AccessibleObjectFromWindow(
                rootHwnd,
                NativeMethods.OBJID_CLIENT,
                ref iid,
                out var root);
            if (hr < 0 || root is null)
                return guard.Finish(ActionReceipt.Failure("ia2.editable_text.insert", $"AccessibleObjectFromWindow failed with HRESULT 0x{hr:X8}."));

            var target = MsaaAccessibleTree.HitTestDeep(root, point.X, point.Y);
            if (target is null)
                return guard.Finish(ActionReceipt.Failure("ia2.editable_text.insert", "MSAA hit-test found no editable object."));

            var current = target.Accessible;
            var lastHr = 0;
            for (var depth = 0; current is not null && depth < 8; depth++)
            {
                var editable = AsAccessibleEditableText(current);
                var text = AsAccessibleText(current);
                if (editable is not null && text is not null)
                {
                    lastHr = text.get_caretOffset(out var offset);
                    if (lastHr == 0 && offset >= 0)
                    {
                        var insertion = value;
                        lastHr = editable.insertText(offset, ref insertion);
                        if (lastHr == 0)
                            return guard.Finish(ActionReceipt.Success("ia2.editable_text.insert_text"));
                    }
                }

                current = MsaaAccessibleTree.TryGetParent(current);
            }

            var reason = lastHr == 0
                ? "Element exposes no IAccessibleEditableText plus IAccessibleText caret interface."
                : $"IAccessibleEditableText.insertText failed with HRESULT 0x{lastHr:X8}.";
            return guard.Finish(ActionReceipt.Failure("ia2.editable_text.insert", reason));
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("ia2.editable_text.insert", ex.Message));
        }
    }

    private static IAccessibleAction? AsAccessibleAction(object? value)
        => Ia2ComQuery.AsIa2Service<IAccessibleAction>(value, Ia2ComQuery.AccessibleAction);

    private static IAccessibleEditableText? AsAccessibleEditableText(object? value)
        => Ia2ComQuery.AsIa2Service<IAccessibleEditableText>(value, Ia2ComQuery.AccessibleEditableText);

    private static IAccessibleText? AsAccessibleText(object? value)
        => Ia2ComQuery.AsIa2Service<IAccessibleText>(value, Ia2ComQuery.AccessibleText);
}
