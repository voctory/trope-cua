using System.Globalization;
using System.Windows.Automation;
using Accessibility;
using CuaDriver.Win.Input;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Uia;

internal static class MsaaActions
{
    private const int ChildIdSelf = 0;
    private const int Ia2TextOffsetLength = -1;

    public static ActionReceipt DoDefaultActionAtElement(IntPtr rootHwnd, AutomationElement element)
    {
        try
        {
            var rect = MsaaActionTargeting.PreferredActionRect(element);
            if (rect.IsEmpty)
                return ActionReceipt.Failure("msaa.default_action", "Element has no bounding rectangle.");

            var point = new POINT(
                (int)Math.Round(rect.X + rect.Width / 2),
                (int)Math.Round(rect.Y + rect.Height / 2));
            return DoDefaultActionAtPoint(rootHwnd, point);
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

            var target = HitTestDeep(root, screenPoint.X, screenPoint.Y, depth: 0);
            if (target is null)
                return guard.Finish(ActionReceipt.Failure("msaa.default_action", "MSAA hit-test found no actionable object."));

            target = PromoteClickAncestor(target);
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

            var target = HitTestDeep(root, point.X, point.Y, depth: 0);
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

                current = TryGetParent(current);
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

            var target = HitTestDeep(root, point.X, point.Y, depth: 0);
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

                current = TryGetParent(current);
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

    private static AccessibleHit? HitTestDeep(IAccessible current, int x, int y, int depth)
    {
        if (depth > 12)
            return new AccessibleHit(current, ChildIdSelf);

        object? hit;
        try
        {
            hit = current.accHitTest(x, y);
        }
        catch
        {
            return new AccessibleHit(current, ChildIdSelf);
        }

        if (hit is null)
            return null;

        var childAccessible = AsAccessible(hit);
        if (childAccessible is not null)
            return HitTestDeep(childAccessible, x, y, depth + 1) ?? new AccessibleHit(childAccessible, ChildIdSelf);

        var childId = CoerceChildId(hit);
        if (childId is null)
            return new AccessibleHit(current, ChildIdSelf);

        if (childId.Value == ChildIdSelf)
            return new AccessibleHit(current, ChildIdSelf);

        var nested = TryGetChild(current, childId.Value);
        if (nested is not null)
            return HitTestDeep(nested, x, y, depth + 1) ?? new AccessibleHit(nested, ChildIdSelf);

        return new AccessibleHit(current, childId.Value);
    }

    private static IAccessible? TryGetChild(IAccessible accessible, int childId)
    {
        try
        {
            return AsAccessible(accessible.get_accChild(childId));
        }
        catch
        {
            return null;
        }
    }

    private static AccessibleHit PromoteClickAncestor(AccessibleHit hit)
    {
        var current = hit;
        for (var depth = 0; depth < 12; depth++)
        {
            var action = DefaultAction(current);
            if (!string.IsNullOrWhiteSpace(action)
                && !action.Equals("clickAncestor", StringComparison.OrdinalIgnoreCase)
                && !action.Equals("click ancestor", StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            var parent = TryGetParent(current.Accessible);
            if (parent is null)
                return current;

            current = new AccessibleHit(parent, ChildIdSelf);
        }

        return current;
    }

    private static string? DefaultAction(AccessibleHit hit)
    {
        try
        {
            return hit.Accessible.get_accDefaultAction(hit.ChildId);
        }
        catch
        {
            return null;
        }
    }

    private static IAccessible? TryGetParent(IAccessible accessible)
    {
        try
        {
            return AsAccessible(accessible.accParent);
        }
        catch
        {
            return null;
        }
    }

    private static int? CoerceChildId(object value)
    {
        try
        {
            return value switch
            {
                int i => i,
                short s => s,
                long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
                _ => Convert.ToInt32(value, CultureInfo.InvariantCulture)
            };
        }
        catch
        {
            return null;
        }
    }

    private static IAccessible? AsAccessible(object? value)
        => Ia2ComQuery.AsAccessible(value);

    private static IAccessibleAction? AsAccessibleAction(object? value)
        => Ia2ComQuery.AsIa2Service<IAccessibleAction>(value, Ia2ComQuery.AccessibleAction);

    private static IAccessibleEditableText? AsAccessibleEditableText(object? value)
        => Ia2ComQuery.AsIa2Service<IAccessibleEditableText>(value, Ia2ComQuery.AccessibleEditableText);

    private static IAccessibleText? AsAccessibleText(object? value)
        => Ia2ComQuery.AsIa2Service<IAccessibleText>(value, Ia2ComQuery.AccessibleText);

    private sealed record AccessibleHit(IAccessible Accessible, object ChildId);
}
