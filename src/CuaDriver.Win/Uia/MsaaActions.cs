using Accessibility;
using CuaDriver.Win.Input;
using CuaDriver.Win.Win32;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace CuaDriver.Win.Uia;

public static class MsaaActions
{
    private const int ChildIdSelf = 0;
    private const int Ia2TextOffsetLength = -1;
    private static readonly Guid IidAccessible2 = new("E89F726E-C4F4-4C19-BB19-B647D7FA8478");
    private static readonly Guid IidAccessibleAction = new("B70D9F59-3B5A-4DBA-AB9E-22012F607DF5");
    private static readonly Guid IidAccessibleEditableText = new("A59AA09A-7011-4b65-939D-32B1FB5547E3");

    public static ActionReceipt DoDefaultActionAtElement(IntPtr rootHwnd, AutomationElement element)
    {
        try
        {
            var rect = PreferredActionRect(element);
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

    private static System.Windows.Rect PreferredActionRect(AutomationElement element)
    {
        var rect = element.Current.BoundingRectangle;
        if (rect.IsEmpty)
            return rect;

        var type = Safe(() => element.Current.LocalizedControlType) ?? "";
        if (!type.Contains("link", StringComparison.OrdinalIgnoreCase))
            return rect;

        var heading = FindDescendantRect(
            element,
            rect,
            depth: 0,
            candidate => candidate.Contains("heading", StringComparison.OrdinalIgnoreCase)
                         || candidate.Contains("header", StringComparison.OrdinalIgnoreCase));
        if (!heading.IsEmpty)
            return heading;

        var actionable = FindDescendantRect(
            element,
            rect,
            depth: 0,
            candidate => candidate.Contains("button", StringComparison.OrdinalIgnoreCase)
                         || candidate.Contains("text", StringComparison.OrdinalIgnoreCase));
        return actionable.IsEmpty ? rect : actionable;
    }

    private static System.Windows.Rect FindDescendantRect(
        AutomationElement element,
        System.Windows.Rect parent,
        int depth,
        Func<string, bool> predicate)
    {
        if (depth > 4)
            return System.Windows.Rect.Empty;

        var walker = TreeWalker.ControlViewWalker;
        AutomationElement? child = null;
        try { child = walker.GetFirstChild(element); } catch { }

        while (child is not null)
        {
            try
            {
                var current = child.Current;
                var childRect = current.BoundingRectangle;
                var localized = current.LocalizedControlType ?? "";
                if (!childRect.IsEmpty
                    && childRect.Width > 4
                    && childRect.Height > 4
                    && parent.Contains(new System.Windows.Point(childRect.X + childRect.Width / 2, childRect.Y + childRect.Height / 2))
                    && predicate(localized))
                {
                    return childRect;
                }

                var nested = FindDescendantRect(child, parent, depth + 1, predicate);
                if (!nested.IsEmpty)
                    return nested;
            }
            catch
            {
                // Element vanished; continue siblings.
            }

            try { child = walker.GetNextSibling(child); }
            catch { break; }
        }

        return System.Windows.Rect.Empty;
    }

    public static ActionReceipt DoDefaultActionAtPoint(IntPtr rootHwnd, POINT screenPoint)
    {
        var guard = NoRegressionGuard.Capture();

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
        var guard = NoRegressionGuard.Capture();

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
        var guard = NoRegressionGuard.Capture();

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
                _ => Convert.ToInt32(value)
            };
        }
        catch
        {
            return null;
        }
    }

    private static T? Safe<T>(Func<T> func)
    {
        try
        {
            return func();
        }
        catch
        {
            return default;
        }
    }

    private static IAccessible? AsAccessible(object? value)
    {
        if (value is null)
            return null;

        if (value is IAccessible accessible)
            return accessible;

        if (!Marshal.IsComObject(value))
            return null;

        IntPtr unknown = IntPtr.Zero;
        IntPtr accessiblePtr = IntPtr.Zero;
        try
        {
            unknown = Marshal.GetIUnknownForObject(value);
            var iid = NativeMethods.IID_IAccessible;
            if (Marshal.QueryInterface(unknown, ref iid, out accessiblePtr) != 0 || accessiblePtr == IntPtr.Zero)
                return null;

            return Marshal.GetObjectForIUnknown(accessiblePtr) as IAccessible;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (accessiblePtr != IntPtr.Zero)
                Marshal.Release(accessiblePtr);
            if (unknown != IntPtr.Zero)
                Marshal.Release(unknown);
        }
    }

    private static IAccessibleAction? AsAccessibleAction(object? value)
    {
        if (value is null)
            return null;

        if (value is IAccessibleAction action)
            return action;

        if (!Marshal.IsComObject(value))
            return null;

        IntPtr unknown = IntPtr.Zero;
        IntPtr actionPtr = IntPtr.Zero;
        try
        {
            unknown = Marshal.GetIUnknownForObject(value);
            var iid = IidAccessibleAction;
            if (Marshal.QueryInterface(unknown, ref iid, out actionPtr) != 0 || actionPtr == IntPtr.Zero)
            {
                if (value is not IServiceProvider serviceProvider)
                    return null;

                var service = IidAccessible2;
                iid = IidAccessibleAction;
                if (serviceProvider.QueryService(ref service, ref iid, out actionPtr) != 0 || actionPtr == IntPtr.Zero)
                    return null;
            }

            return Marshal.GetObjectForIUnknown(actionPtr) as IAccessibleAction;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (actionPtr != IntPtr.Zero)
                Marshal.Release(actionPtr);
            if (unknown != IntPtr.Zero)
                Marshal.Release(unknown);
        }
    }

    private static IAccessibleEditableText? AsAccessibleEditableText(object? value)
    {
        if (value is null)
            return null;

        if (value is IAccessibleEditableText editable)
            return editable;

        if (!Marshal.IsComObject(value))
            return null;

        IntPtr unknown = IntPtr.Zero;
        IntPtr editablePtr = IntPtr.Zero;
        try
        {
            unknown = Marshal.GetIUnknownForObject(value);
            var iid = IidAccessibleEditableText;
            if (Marshal.QueryInterface(unknown, ref iid, out editablePtr) != 0 || editablePtr == IntPtr.Zero)
            {
                if (value is not IServiceProvider serviceProvider)
                    return null;

                var service = IidAccessible2;
                iid = IidAccessibleEditableText;
                if (serviceProvider.QueryService(ref service, ref iid, out editablePtr) != 0 || editablePtr == IntPtr.Zero)
                    return null;
            }

            return Marshal.GetObjectForIUnknown(editablePtr) as IAccessibleEditableText;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (editablePtr != IntPtr.Zero)
                Marshal.Release(editablePtr);
            if (unknown != IntPtr.Zero)
                Marshal.Release(unknown);
        }
    }

    private static IAccessibleText? AsAccessibleText(object? value)
    {
        if (value is null)
            return null;

        if (value is IAccessibleText text)
            return text;

        if (!Marshal.IsComObject(value))
            return null;

        IntPtr unknown = IntPtr.Zero;
        IntPtr textPtr = IntPtr.Zero;
        try
        {
            unknown = Marshal.GetIUnknownForObject(value);
            var iid = IidAccessibleText;
            if (Marshal.QueryInterface(unknown, ref iid, out textPtr) != 0 || textPtr == IntPtr.Zero)
            {
                if (value is not IServiceProvider serviceProvider)
                    return null;

                var service = IidAccessible2;
                iid = IidAccessibleText;
                if (serviceProvider.QueryService(ref service, ref iid, out textPtr) != 0 || textPtr == IntPtr.Zero)
                    return null;
            }

            return Marshal.GetObjectForIUnknown(textPtr) as IAccessibleText;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (textPtr != IntPtr.Zero)
                Marshal.Release(textPtr);
            if (unknown != IntPtr.Zero)
                Marshal.Release(unknown);
        }
    }

    [ComImport]
    [Guid("B70D9F59-3B5A-4DBA-AB9E-22012F607DF5")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccessibleAction
    {
        [PreserveSig]
        int nActions(out int nActions);

        [PreserveSig]
        int doAction(int actionIndex);
    }

    [ComImport]
    [Guid("A59AA09A-7011-4b65-939D-32B1FB5547E3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccessibleEditableText
    {
        [PreserveSig]
        int copyText(int startOffset, int endOffset);

        [PreserveSig]
        int deleteText(int startOffset, int endOffset);

        [PreserveSig]
        int insertText(int offset, [MarshalAs(UnmanagedType.BStr)] ref string text);

        [PreserveSig]
        int cutText(int startOffset, int endOffset);

        [PreserveSig]
        int pasteText(int offset);

        [PreserveSig]
        int replaceText(int startOffset, int endOffset, [MarshalAs(UnmanagedType.BStr)] ref string text);

        [PreserveSig]
        int setAttributes(int startOffset, int endOffset, [MarshalAs(UnmanagedType.BStr)] ref string attributes);
    }

    private static readonly Guid IidAccessibleText = new("24FD2FFB-3AAD-4a08-8335-A3AD89C0FB4B");

    [ComImport]
    [Guid("24FD2FFB-3AAD-4a08-8335-A3AD89C0FB4B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccessibleText
    {
        [PreserveSig]
        int addSelection(int startOffset, int endOffset);

        [PreserveSig]
        int get_attributes(int offset, out int startOffset, out int endOffset, [MarshalAs(UnmanagedType.BStr)] out string textAttributes);

        [PreserveSig]
        int get_caretOffset(out int offset);

        [PreserveSig]
        int get_characterExtents(int offset, int coordType, out int x, out int y, out int width, out int height);

        [PreserveSig]
        int get_nSelections(out int nSelections);

        [PreserveSig]
        int get_offsetAtPoint(int x, int y, int coordType, out int offset);

        [PreserveSig]
        int get_selection(int selectionIndex, out int startOffset, out int endOffset);

        [PreserveSig]
        int get_text(int startOffset, int endOffset, [MarshalAs(UnmanagedType.BStr)] out string text);

        [PreserveSig]
        int get_textBeforeOffset(int offset, int boundaryType, out int startOffset, out int endOffset, [MarshalAs(UnmanagedType.BStr)] out string text);

        [PreserveSig]
        int get_textAfterOffset(int offset, int boundaryType, out int startOffset, out int endOffset, [MarshalAs(UnmanagedType.BStr)] out string text);

        [PreserveSig]
        int get_textAtOffset(int offset, int boundaryType, out int startOffset, out int endOffset, [MarshalAs(UnmanagedType.BStr)] out string text);

        [PreserveSig]
        int removeSelection(int selectionIndex);

        [PreserveSig]
        int setCaretOffset(int offset);

        [PreserveSig]
        int setSelection(int selectionIndex, int startOffset, int endOffset);

        [PreserveSig]
        int get_nCharacters(out int nCharacters);

        [PreserveSig]
        int scrollSubstringTo(int startIndex, int endIndex, int scrollType);

        [PreserveSig]
        int scrollSubstringToPoint(int startIndex, int endIndex, int coordinateType, int x, int y);
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    private sealed record AccessibleHit(IAccessible Accessible, object ChildId);
}
