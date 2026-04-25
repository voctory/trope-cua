using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Input;

public sealed class NoRegressionGuard : IDisposable
{
    private readonly POINT _cursorBefore;
    private readonly IntPtr _foregroundBefore;
    private readonly ForegroundSampler _foregroundSampler;

    private NoRegressionGuard(POINT cursorBefore, IntPtr foregroundBefore)
    {
        _cursorBefore = cursorBefore;
        _foregroundBefore = foregroundBefore;
        _foregroundSampler = new ForegroundSampler(foregroundBefore);
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
        var transientForegroundChanged = _foregroundSampler.Stop();

        var cursorMoved = cursorAfter.X != _cursorBefore.X || cursorAfter.Y != _cursorBefore.Y;
        var foregroundChanged = foregroundAfter != _foregroundBefore || transientForegroundChanged;
        if (foregroundChanged && !allowForegroundChange)
            RestoreForeground();

        var backgroundSafe = receipt.BackgroundSafe
                             && !cursorMoved
                             && !foregroundChanged;
        var allowed = receipt.BackgroundSafe
                      && (!cursorMoved || allowCursorMove)
                      && (!foregroundChanged || allowForegroundChange);

        var reason = receipt.Reason;
        var ok = receipt.Ok;
        if (receipt.Ok && !allowed)
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

    public void Dispose() => _foregroundSampler.Dispose();

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

            NativeMethods.SetForegroundWindow(_foregroundBefore);
            Thread.Sleep(50);
        }
        finally
        {
            if (currentForegroundThread != 0 && currentForegroundThread != currentThread)
                NativeMethods.AttachThreadInput(currentThread, currentForegroundThread, false);
            if (previousForegroundThread != 0 && previousForegroundThread != currentThread)
                NativeMethods.AttachThreadInput(currentThread, previousForegroundThread, false);
        }
    }

    private sealed class ForegroundSampler : IDisposable
    {
        private readonly IntPtr _initialForeground;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _task;
        private int _changed;
        private int _stopped;

        public ForegroundSampler(IntPtr initialForeground)
        {
            _initialForeground = initialForeground;
            _task = Task.Run(SampleLoop);
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

            _cts.Cancel();
            try
            {
                _task.Wait(TimeSpan.FromMilliseconds(50));
            }
            catch
            {
                // Best-effort diagnostics only; action receipts must still return.
            }
            finally
            {
                _cts.Dispose();
            }
        }

        private async Task SampleLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                if (NativeMethods.GetForegroundWindow() != _initialForeground)
                    Interlocked.Exchange(ref _changed, 1);

                try
                {
                    await Task.Delay(10, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
