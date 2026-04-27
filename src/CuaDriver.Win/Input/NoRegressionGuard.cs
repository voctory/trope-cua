using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Input;

internal sealed class NoRegressionGuard : IDisposable
{
    private readonly POINT _cursorBefore;
    private readonly bool _cursorBeforeKnown;
    private readonly IntPtr _foregroundBefore;
    private readonly IReadOnlyList<IntPtr> _zOrderBefore;
    private readonly ForegroundSampler _foregroundSampler;

    private NoRegressionGuard(POINT cursorBefore, bool cursorBeforeKnown, IntPtr foregroundBefore, IReadOnlyList<IntPtr> zOrderBefore)
    {
        _cursorBefore = cursorBefore;
        _cursorBeforeKnown = cursorBeforeKnown;
        _foregroundBefore = foregroundBefore;
        _zOrderBefore = zOrderBefore;
        _foregroundSampler = new ForegroundSampler(foregroundBefore);
    }

    public static NoRegressionGuard Capture()
    {
        var cursorKnown = NativeMethods.GetCursorPos(out var cursor);
        var foreground = NativeMethods.GetForegroundWindow();
        return new NoRegressionGuard(cursor, cursorKnown, foreground, CaptureVisibleNormalZOrder());
    }

    public ActionReceipt Finish(
        ActionReceipt receipt,
        bool allowCursorMove = false,
        bool allowForegroundChange = false,
        bool restoreAllowedCursorMove = false,
        bool restoreAllowedForegroundChange = false,
        bool allowUnsafeRoute = false)
    {
        var cursorAfterKnown = NativeMethods.GetCursorPos(out var cursorAfter);
        var foregroundAfter = NativeMethods.GetForegroundWindow();
        var transientForegroundChanged = _foregroundSampler.Stop();

        var cursorProbeFailed = !_cursorBeforeKnown || !cursorAfterKnown;
        var cursorMoved = cursorProbeFailed || cursorAfter.X != _cursorBefore.X || cursorAfter.Y != _cursorBefore.Y;
        var foregroundChanged = foregroundAfter != _foregroundBefore || transientForegroundChanged;
        if (cursorMoved && (!allowCursorMove || restoreAllowedCursorMove) && !cursorProbeFailed)
            NativeMethods.SetCursorPos(_cursorBefore.X, _cursorBefore.Y);
        if (foregroundChanged && (!allowForegroundChange || restoreAllowedForegroundChange))
        {
            RestoreForeground();
            RestoreZOrder();
        }

        var backgroundSafe = receipt.BackgroundSafe
                             && !cursorMoved
                             && !foregroundChanged;
        var allowed = (receipt.BackgroundSafe || allowUnsafeRoute)
                      && (!cursorMoved || allowCursorMove)
                      && (!foregroundChanged || allowForegroundChange);

        var reason = receipt.Reason;
        var ok = receipt.Ok;
        if (receipt.Ok && !allowed)
        {
            ok = false;
            reason = BuildGuardFailureReason(cursorProbeFailed, cursorMoved, foregroundChanged);
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

    public void Dispose() => _foregroundSampler.Dispose();

    private static string BuildGuardFailureReason(bool cursorProbeFailed, bool cursorMoved, bool foregroundChanged)
    {
        var failures = new List<string>();
        if (cursorProbeFailed)
            failures.Add("cursor position could not be verified");
        else if (cursorMoved)
            failures.Add("cursor movement");
        if (foregroundChanged)
            failures.Add("foreground change");
        if (failures.Count == 0)
            failures.Add("unsafe route state");

        return "No-regression guard detected " + string.Join(" and ", failures) + ".";
    }

    private void RestoreForeground()
    {
        if (_foregroundBefore == IntPtr.Zero || !NativeMethods.IsWindow(_foregroundBefore))
            return;

        var currentForeground = NativeMethods.GetForegroundWindow();
        var currentThread = NativeMethods.GetCurrentThreadId();
        var previousForegroundThread = NativeMethods.GetWindowThreadProcessId(_foregroundBefore, out _);
        var currentForegroundThread = currentForeground == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(currentForeground, out _);

        try
        {
            if (previousForegroundThread != 0 && previousForegroundThread != currentThread)
                NativeMethods.AttachThreadInput(currentThread, previousForegroundThread, true);
            if (currentForegroundThread != 0 && currentForegroundThread != currentThread)
                NativeMethods.AttachThreadInput(currentThread, currentForegroundThread, true);

            _ = NativeMethods.SetForegroundWindow(_foregroundBefore);
            SpinWait.SpinUntil(() => NativeMethods.GetForegroundWindow() == _foregroundBefore, TimeSpan.FromMilliseconds(50));
        }
        finally
        {
            if (currentForegroundThread != 0 && currentForegroundThread != currentThread)
                NativeMethods.AttachThreadInput(currentThread, currentForegroundThread, false);
            if (previousForegroundThread != 0 && previousForegroundThread != currentThread)
                NativeMethods.AttachThreadInput(currentThread, previousForegroundThread, false);
        }
    }

    private void RestoreZOrder()
    {
        if (_zOrderBefore.Count == 0)
            return;

        var insertAfter = NativeMethods.HWND_TOP;
        const uint flags = NativeMethods.SWP_NOMOVE
                           | NativeMethods.SWP_NOSIZE
                           | NativeMethods.SWP_NOACTIVATE
                           | NativeMethods.SWP_NOOWNERZORDER
                           | NativeMethods.SWP_NOSENDCHANGING;

        foreach (var hwnd in _zOrderBefore)
        {
            if (!IsVisibleNormalWindow(hwnd))
                continue;

            _ = NativeMethods.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, flags);
            insertAfter = hwnd;
        }
    }

    private static List<IntPtr> CaptureVisibleNormalZOrder()
    {
        var result = new List<IntPtr>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (IsVisibleNormalWindow(hwnd))
                result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static bool IsVisibleNormalWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero
            || !NativeMethods.IsWindow(hwnd)
            || !NativeMethods.IsWindowVisible(hwnd)
            || NativeMethods.IsIconic(hwnd))
        {
            return false;
        }

        var rect = NativeMethods.GetBestWindowRect(hwnd);
        if (rect.IsEmpty)
            return false;

        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        return (exStyle & NativeMethods.WS_EX_TOPMOST) == 0;
    }

    private sealed class ForegroundSampler : IDisposable
    {
        private readonly IntPtr _initialForeground;
        private readonly System.Threading.Timer _timer;
        private int _changed;
        private int _stopped;

        public ForegroundSampler(IntPtr initialForeground)
        {
            _initialForeground = initialForeground;
            _timer = new System.Threading.Timer(
                static state => ((ForegroundSampler)state!).Sample(),
                this,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(10));
        }

        public bool Stop()
        {
            Dispose();
            return Interlocked.CompareExchange(ref _changed, 0, 0) != 0;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
                return;

            _timer.Dispose();
        }

        private void Sample()
        {
            if (Volatile.Read(ref _stopped) != 0)
                return;

            if (NativeMethods.GetForegroundWindow() != _initialForeground)
                Interlocked.Exchange(ref _changed, 1);
        }
    }
}
