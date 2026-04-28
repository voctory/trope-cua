using System.Windows.Automation;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Uia;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

internal static class AgentCursorTooling
{
    public static POINT? ElementCenter(AutomationElement element)
    {
        try
        {
            var rect = element.Current.BoundingRectangle;
            if (rect.IsEmpty)
                return null;

            foreach (var point in CandidatePoints(rect))
            {
                if (HitBelongsToElement(point, element))
                    return point;
            }

            return PointInRect(rect, 0.5, 0.5);
        }
        catch
        {
            return null;
        }
    }

    public static Task MoveToElementAsync(ToolContext context, AutomationElement element, CancellationToken ct)
        => MoveToElementAsync(context, element, IntPtr.Zero, ct);

    public static async Task MoveToElementAsync(ToolContext context, AutomationElement element, IntPtr targetHwnd, CancellationToken ct)
    {
        var center = ElementCenter(element);
        if (center is { } point)
            await context.State.AgentCursor.MoveToAsync(point, TargetForElement(element, targetHwnd), ct).ConfigureAwait(false);
    }

    public static Task PulseAtElementAsync(ToolContext context, AutomationElement element, CancellationToken ct)
        => PulseAtElementAsync(context, element, IntPtr.Zero, ct);

    public static async Task PulseAtElementAsync(ToolContext context, AutomationElement element, IntPtr targetHwnd, CancellationToken ct)
    {
        var center = ElementCenter(element);
        if (center is { } point)
            await context.State.AgentCursor.ClickPulseAsync(point, TargetForElement(element, targetHwnd), ct).ConfigureAwait(false);
    }

    public static IntPtr TargetRoot(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            return IntPtr.Zero;

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        return root == IntPtr.Zero ? hwnd : root;
    }

    private static IntPtr TargetForElement(AutomationElement element, IntPtr fallbackHwnd)
    {
        var fallback = TargetRoot(fallbackHwnd);
        var native = AutomationElementNative.HwndOrZero(element);
        if (native != IntPtr.Zero)
        {
            var root = TargetRoot(native);
            if (root != IntPtr.Zero)
                return root;
        }

        return fallback;
    }

    private static IEnumerable<POINT> CandidatePoints(System.Windows.Rect rect)
    {
        yield return PointInRect(rect, 0.5, 0.5);
        yield return PointInRect(rect, 0.33, 0.5);
        yield return PointInRect(rect, 0.67, 0.5);
        yield return PointInRect(rect, 0.5, 0.33);
        yield return PointInRect(rect, 0.5, 0.67);
        yield return PointInRect(rect, 0.25, 0.25);
        yield return PointInRect(rect, 0.75, 0.25);
        yield return PointInRect(rect, 0.25, 0.75);
        yield return PointInRect(rect, 0.75, 0.75);
    }

    private static POINT PointInRect(System.Windows.Rect rect, double fx, double fy)
    {
        var insetX = Math.Min(3, Math.Max(0, rect.Width / 4));
        var insetY = Math.Min(3, Math.Max(0, rect.Height / 4));
        var x = rect.X + insetX + Math.Max(0, rect.Width - insetX * 2) * fx;
        var y = rect.Y + insetY + Math.Max(0, rect.Height - insetY * 2) * fy;
        return new POINT((int)Math.Round(x), (int)Math.Round(y));
    }

    private static bool HitBelongsToElement(POINT point, AutomationElement target)
    {
        try
        {
            var hit = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            if (hit is null)
                return false;

            if (Automation.Compare(hit, target))
                return true;

            var walker = TreeWalker.ControlViewWalker;
            var current = hit;
            for (var depth = 0; depth < 12; depth++)
            {
                current = walker.GetParent(current);
                if (current is null)
                    return false;
                if (Automation.Compare(current, target))
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
