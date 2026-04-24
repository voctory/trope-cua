using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Input;

public sealed class NoRegressionGuard
{
    private readonly POINT _cursorBefore;
    private readonly IntPtr _foregroundBefore;

    private NoRegressionGuard(POINT cursorBefore, IntPtr foregroundBefore)
    {
        _cursorBefore = cursorBefore;
        _foregroundBefore = foregroundBefore;
    }

    public static NoRegressionGuard Capture()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var foreground = NativeMethods.GetForegroundWindow();
        return new NoRegressionGuard(cursor, foreground);
    }

    public ActionReceipt Finish(ActionReceipt receipt, bool allowCursorMove = false, bool allowForegroundChange = false)
    {
        NativeMethods.GetCursorPos(out var cursorAfter);
        var foregroundAfter = NativeMethods.GetForegroundWindow();

        var cursorMoved = cursorAfter.X != _cursorBefore.X || cursorAfter.Y != _cursorBefore.Y;
        var foregroundChanged = foregroundAfter != _foregroundBefore;

        var backgroundSafe = receipt.BackgroundSafe
                             && (!cursorMoved || allowCursorMove)
                             && (!foregroundChanged || allowForegroundChange);

        var reason = receipt.Reason;
        var ok = receipt.Ok;
        if (receipt.Ok && !backgroundSafe)
        {
            ok = false;
            reason = $"No-regression guard detected {(cursorMoved ? "cursor movement" : "")}{(cursorMoved && foregroundChanged ? " and " : "")}{(foregroundChanged ? "foreground change" : "")}.";
        }

        return receipt with
        {
            Ok = ok,
            CursorMoved = cursorMoved,
            ForegroundChanged = foregroundChanged,
            BackgroundSafe = backgroundSafe,
            Reason = reason
        };
    }
}
