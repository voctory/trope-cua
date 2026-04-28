using System.IO;
using System.Security.Principal;
using CuaDriver.Win.Input;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Browser;

internal sealed class BrowserAutomationLease : IDisposable
{
    private static readonly TimeSpan DefaultWait = TimeSpan.FromMilliseconds(250);
    private readonly Mutex _mutex;
    private bool _owns;

    private BrowserAutomationLease(Mutex mutex)
    {
        _mutex = mutex;
        _owns = true;
    }

    public static BrowserAutomationLease? TryAcquireChromiumFallback(WindowInfo window, int? cdpPort, out ActionReceipt? contention)
    {
        contention = null;
        if (cdpPort is not null || !BrowserWindowClassifier.IsLikelyChromium(window))
            return null;

        var mutex = new Mutex(initiallyOwned: false, MutexNameFor(window.Pid));
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(DefaultWait);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (acquired)
            return new BrowserAutomationLease(mutex);

        mutex.Dispose();
        contention = ActionReceipt.Failure(
            "browser.chromium_fallback.contended",
            $"Another trope-cua daemon is already driving Chromium accessibility for pid {window.Pid}. Retry this same action up to 3 times after the active action completes. If contention persists or true parallel browser work is required, use an isolated browser profile with its own CDP port or target a different browser process.");
        return null;
    }

    public void Dispose()
    {
        if (!_owns)
            return;

        _owns = false;
        try
        {
            _mutex.ReleaseMutex();
        }
        finally
        {
            _mutex.Dispose();
        }
    }

    private static string MutexNameFor(int pid) => $@"Local\trope-cua-browser-uia-{UserKey()}-{pid}";

    private static string UserKey()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        foreach (var ch in Path.GetInvalidFileNameChars())
            sid = sid.Replace(ch, '_');
        return sid;
    }
}
