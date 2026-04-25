using System.Windows.Automation;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Uia;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

internal sealed record NativePoint(double X, double Y);

internal sealed record ElementCenterPoint(POINT ScreenPoint, double LocalX, double LocalY);

internal sealed record PixelTargetPoint(double LocalX, double LocalY, WindowMessageDispatch Resolved, UiaHitTestResult? Hit);

internal static class ToolCoordinates
{
    public static double ResizeRatio(ToolContext context, int pid, WindowInfo window) =>
        context.State.ImageResizeRatio.TryGetValue((pid, window.WindowId), out var ratio) ? ratio : 1.0;

    public static NativePoint ToNativePoint(ToolContext context, int pid, WindowInfo window, double x, double y)
    {
        var ratio = ResizeRatio(context, pid, window);
        return new NativePoint(x * ratio, y * ratio);
    }

    public static PixelTargetPoint ResolvePixelTarget(ToolContext context, int pid, WindowInfo window, double x, double y)
    {
        var native = ToNativePoint(context, pid, window, x, y);
        var resolved = ResolveNativePointTarget(window, native.X, native.Y);
        var hit = UiAutomationTree.HitTest(pid, window.WindowId, resolved.ScreenPoint);
        return new PixelTargetPoint(native.X, native.Y, resolved, hit);
    }

    public static PixelTargetPoint ResolveNativePixelTarget(int pid, WindowInfo window, double localX, double localY)
    {
        var resolved = ResolveNativePointTarget(window, localX, localY);
        var hit = UiAutomationTree.HitTest(pid, window.WindowId, resolved.ScreenPoint);
        return new PixelTargetPoint(localX, localY, resolved, hit);
    }

    public static WindowMessageDispatch ResolvePointTarget(ToolContext context, int pid, WindowInfo window, double x, double y)
    {
        var native = ToNativePoint(context, pid, window, x, y);
        return ResolveNativePointTarget(window, native.X, native.Y);
    }

    public static WindowMessageDispatch ResolveNativePointTarget(WindowInfo window, double localX, double localY) =>
        WindowMessageTargeting.ResolvePointTarget(window.Hwnd, localX, localY);

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
