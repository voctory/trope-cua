using System.Windows.Automation;

namespace CuaDriver.Win.Uia;

internal static class AutomationElementNative
{
    public static IntPtr HwndOrZero(AutomationElement element)
    {
        try
        {
            var hwnd = element.Current.NativeWindowHandle;
            return hwnd == 0 ? IntPtr.Zero : new IntPtr(hwnd);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
}
