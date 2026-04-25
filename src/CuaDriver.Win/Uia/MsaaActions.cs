using Accessibility;
using CuaDriver.Win.Input;
using CuaDriver.Win.Win32;
using System.Runtime.InteropServices;

namespace CuaDriver.Win.Uia;

public static class MsaaActions
{
    private const int ChildIdSelf = 0;
    private static readonly Guid IidAccessible2 = new("E89F726E-C4F4-4C19-BB19-B647D7FA8478");
    private static readonly Guid IidAccessibleAction = new("B70D9F59-3B5A-4DBA-AB9E-22012F607DF5");

    public static ActionReceipt DoDefaultActionAtElement(IntPtr rootHwnd, System.Windows.Automation.AutomationElement element)
    {
        try
        {
            var rect = element.Current.BoundingRectangle;
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
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    private sealed record AccessibleHit(IAccessible Accessible, object ChildId);
}
