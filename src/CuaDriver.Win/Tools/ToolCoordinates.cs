using System.Windows.Automation;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

internal sealed record NativePoint(double X, double Y);

internal sealed record ElementCenterPoint(POINT ScreenPoint, double LocalX, double LocalY);

internal static class ToolCoordinates
{
    public static double ResizeRatio(ToolContext context, int pid, WindowInfo window) =>
        context.State.ImageResizeRatio.TryGetValue((pid, window.WindowId), out var ratio) ? ratio : 1.0;

    public static NativePoint ToNativePoint(ToolContext context, int pid, WindowInfo window, double x, double y)
    {
        var ratio = ResizeRatio(context, pid, window);
        return new NativePoint(x * ratio, y * ratio);
    }

    public static ElementCenterPoint? ElementCenter(AutomationElement element, WindowInfo window)
    {
        var rect = element.Current.BoundingRectangle;
        if (rect.IsEmpty)
            return null;

        var screenX = rect.X + rect.Width / 2;
        var screenY = rect.Y + rect.Height / 2;
        return new ElementCenterPoint(
            new POINT((int)Math.Round(screenX), (int)Math.Round(screenY)),
            screenX - window.Bounds.X,
            screenY - window.Bounds.Y);
    }
}
