using System.Windows;
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
        using var guard = NoRegressionGuard.Capture();

        try
        {
            var points = MsaaActionTargeting.PreferredActionPoints(element).ToArray();
            if (points.Length == 0)
                return guard.Finish(ActionReceipt.Failure("msaa.default_action", "Element has no bounding rectangle."));

            var rootReceipt = TryGetRootAccessible(rootHwnd, "msaa.default_action", out var root);
            if (rootReceipt is not null || root is null)
                return guard.Finish(rootReceipt!);

            ActionReceipt? last = null;
            foreach (var point in points)
            {
                var target = MsaaAccessibleTree.HitTestDeep(root, point.X, point.Y);
                if (target is null)
                {
                    last = ActionReceipt.Failure("msaa.default_action", "MSAA hit-test found no actionable object.");
                    continue;
                }

                var receipt = TryDoDefaultAction(target);
                if (receipt.ShouldStopFallback)
                    return guard.Finish(receipt);

                last = receipt;
            }

            var bounds = element.Current.BoundingRectangle;
            foreach (var target in MsaaAccessibleTree.DescendantsWithin(root, bounds))
            {
                var receipt = TryDoDefaultAction(target, requireAdvertisedAction: true);
                if (receipt.Ok)
                    return guard.Finish(receipt with { Route = "msaa.bounds_search." + receipt.Route });

                last = receipt;
            }

            return guard.Finish(last ?? ActionReceipt.Failure("msaa.default_action", "Element has no actionable target."));
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("msaa.default_action", ex.Message));
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

            return guard.Finish(TryDoDefaultAction(target));
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

            var rootReceipt = TryGetRootAccessible(rootHwnd, "ia2.editable_text", out var root);
            if (rootReceipt is not null || root is null)
                return guard.Finish(rootReceipt!);

            var lastHr = 0;
            var candidateCount = 0;
            foreach (var current in EditableTextCandidates(root, element, requireText: false))
            {
                candidateCount++;
                var editable = AsAccessibleEditableText(current);
                if (editable is not null)
                {
                    var replacement = value;
                    lastHr = editable.replaceText(0, Ia2TextOffsetLength, ref replacement);
                    if (lastHr == 0)
                        return guard.Finish(ActionReceipt.Success("ia2.editable_text.replace_text"));
                }
            }

            var reason = candidateCount == 0
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

            var rootReceipt = TryGetRootAccessible(rootHwnd, "ia2.editable_text.insert", out var root);
            if (rootReceipt is not null || root is null)
                return guard.Finish(rootReceipt!);

            var lastHr = 0;
            var candidateCount = 0;
            foreach (var current in EditableTextCandidates(root, element, requireText: true))
            {
                candidateCount++;
                var editable = AsAccessibleEditableText(current);
                var text = AsAccessibleText(current);
                if (editable is not null && text is not null)
                {
                    var receipt = TryInsertEditableText(editable, text, value, out lastHr);
                    if (receipt is not null)
                        return guard.Finish(receipt);
                }
            }

            var reason = candidateCount == 0
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

    private static ActionReceipt? TryGetRootAccessible(IntPtr rootHwnd, string route, out IAccessible? root)
    {
        var iid = NativeMethods.IID_IAccessible;
        var hr = NativeMethods.AccessibleObjectFromWindow(
            rootHwnd,
            NativeMethods.OBJID_CLIENT,
            ref iid,
            out root);
        return hr < 0 || root is null
            ? ActionReceipt.Failure(route, $"AccessibleObjectFromWindow failed with HRESULT 0x{hr:X8}.")
            : null;
    }

    private static ActionReceipt TryDoDefaultAction(AccessibleHit hit, bool requireAdvertisedAction = false)
    {
        try
        {
            hit = MsaaAccessibleTree.PromoteClickAncestor(hit);
            var action = AsAccessibleAction(hit.Accessible);
            if (action is not null && action.nActions(out var actions) == 0 && actions > 0)
            {
                var actionHr = action.doAction(0);
                if (actionHr == 0)
                    return ActionReceipt.Success("ia2.action.do_action");
            }

            if (requireAdvertisedAction && !MsaaAccessibleTree.HasDefaultAction(hit))
                return ActionReceipt.Failure("msaa.default_action", "MSAA object exposes no default action.");

            hit.Accessible.accDoDefaultAction(hit.ChildId);
            return ActionReceipt.Success("msaa.default_action");
        }
        catch (Exception ex)
        {
            return ActionReceipt.Failure("msaa.default_action", ex.Message);
        }
    }

    private static ActionReceipt? TryInsertEditableText(IAccessibleEditableText editable, IAccessibleText text, string value, out int lastHr)
    {
        lastHr = text.get_nSelections(out var selections);
        if (lastHr == 0 && selections > 0)
        {
            lastHr = text.get_selection(0, out var selectionStart, out var selectionEnd);
            if (lastHr == 0 && selectionStart >= 0 && selectionEnd >= selectionStart && selectionEnd != selectionStart)
            {
                var replacement = value;
                lastHr = editable.replaceText(selectionStart, selectionEnd, ref replacement);
                if (lastHr == 0)
                    return ActionReceipt.Success("ia2.editable_text.replace_selection");
            }
        }

        lastHr = text.get_caretOffset(out var offset);
        if (lastHr == 0 && offset >= 0)
        {
            var insertion = value;
            lastHr = editable.insertText(offset, ref insertion);
            if (lastHr == 0)
                return ActionReceipt.Success("ia2.editable_text.insert_text");
        }

        lastHr = text.get_nCharacters(out var characters);
        if (lastHr == 0 && characters >= 0)
        {
            _ = text.setCaretOffset(characters);
            var insertion = value;
            lastHr = editable.insertText(characters, ref insertion);
            if (lastHr == 0)
                return ActionReceipt.Success("ia2.editable_text.insert_text.end");
        }

        return null;
    }

    private static IEnumerable<IAccessible> EditableTextCandidates(IAccessible root, AutomationElement element, bool requireText)
    {
        var seen = new HashSet<IntPtr>();
        foreach (var hit in PointCandidates(root, element))
        {
            foreach (var candidate in MsaaAccessibleTree.AncestorsAndSelf(hit))
            {
                if (IsEditableTextCandidate(candidate.Accessible, requireText)
                    && AddUnique(candidate.Accessible, seen))
                {
                    yield return candidate.Accessible;
                }
            }
        }

        var bounds = element.Current.BoundingRectangle;
        foreach (var candidate in MsaaAccessibleTree.DescendantsWithin(root, bounds))
        {
            if (IsEditableTextCandidate(candidate.Accessible, requireText)
                && AddUnique(candidate.Accessible, seen))
            {
                yield return candidate.Accessible;
            }
        }
    }

    private static bool IsEditableTextCandidate(IAccessible accessible, bool requireText)
        => AsAccessibleEditableText(accessible) is not null
            && (!requireText || AsAccessibleText(accessible) is not null);

    private static IEnumerable<AccessibleHit> PointCandidates(IAccessible root, AutomationElement element)
    {
        foreach (var point in MsaaActionTargeting.PreferredActionPoints(element))
        {
            var target = MsaaAccessibleTree.HitTestDeep(root, point.X, point.Y);
            if (target is not null)
                yield return target;
        }
    }

    private static bool AddUnique(IAccessible accessible, HashSet<IntPtr> seen)
    {
        var unknown = IntPtr.Zero;
        try
        {
            unknown = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(accessible);
            return unknown == IntPtr.Zero || seen.Add(unknown);
        }
        catch
        {
            return true;
        }
        finally
        {
            if (unknown != IntPtr.Zero)
                System.Runtime.InteropServices.Marshal.Release(unknown);
        }
    }
}
