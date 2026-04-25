using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Input;

internal static class WindowMessageTargeting
{
    public static POINT WindowLocalToScreen(IntPtr hwnd, double x, double y)
    {
        var rect = NativeMethods.GetBestWindowRect(hwnd);
        return new POINT(rect.Left + (int)Math.Round(x), rect.Top + (int)Math.Round(y));
    }

    public static WindowMessageDispatch ResolvePointTarget(IntPtr hwnd, double x, double y)
    {
        var screen = WindowLocalToScreen(hwnd, x, y);
        var target = DeepestChildFromScreenPoint(hwnd, screen);
        var client = screen;
        NativeMethods.ScreenToClient(target, ref client);
        return new WindowMessageDispatch(ActionReceipt.Success("hwnd.resolve_point"), target, screen, client);
    }

    public static POINT CenterOf(IntPtr hwnd)
    {
        var rect = NativeMethods.GetBestWindowRect(hwnd);
        return new POINT(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
    }

    public static POINT CenterLocal(IntPtr hwnd)
    {
        var rect = NativeMethods.GetBestWindowRect(hwnd);
        return new POINT(rect.Width / 2, rect.Height / 2);
    }

    private static IntPtr DeepestChildFromScreenPoint(IntPtr root, POINT screen)
    {
        var current = root;
        for (var depth = 0; depth < 16; depth++)
        {
            var client = screen;
            NativeMethods.ScreenToClient(current, ref client);
            var child = NativeMethods.ChildWindowFromPointEx(
                current,
                client,
                NativeMethods.CWP_SKIPINVISIBLE | NativeMethods.CWP_SKIPDISABLED | NativeMethods.CWP_SKIPTRANSPARENT);
            if (child == IntPtr.Zero || child == current || !IsWithinRoot(root, child))
                return current;
            current = child;
        }

        return current;
    }

    private static bool IsWithinRoot(IntPtr root, IntPtr child)
    {
        if (root == child)
            return true;
        if (NativeMethods.IsChild(root, child))
            return true;
        var childRoot = NativeMethods.GetAncestor(child, NativeMethods.GA_ROOT);
        return childRoot == root;
    }
}
