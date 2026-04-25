using System.Windows.Automation;
using CuaDriver.Win.Tooling;
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

            return new POINT(
                (int)Math.Round(rect.X + rect.Width / 2),
                (int)Math.Round(rect.Y + rect.Height / 2));
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
        try
        {
            var native = element.Current.NativeWindowHandle;
            if (native != 0)
            {
                var root = TargetRoot(new IntPtr(native));
                if (root != IntPtr.Zero)
                    return root;
            }
        }
        catch
        {
            // Use the root window supplied by the tool when UIA cannot expose a native HWND.
        }

        return fallback;
    }
}
