using System.IO;
using System.Security.Principal;
using CuaDriver.Win.Input;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Browser;

internal sealed class BrowserAutomationLease : IDisposable
{
    private static readonly TimeSpan DefaultWait = TimeSpan.FromMilliseconds(250);
    private readonly Semaphore _semaphore;
    private bool _owns;

    private BrowserAutomationLease(Semaphore semaphore)
    {
        _semaphore = semaphore;
        _owns = true;
    }

    public static BrowserAutomationLease? TryAcquireChromiumFallback(WindowInfo window, int? cdpPort, out ActionReceipt? contention)
    {
        contention = null;
        if (cdpPort is not null || !BrowserWindowClassifier.IsLikelyChromium(window))
            return null;

        var semaphore = new Semaphore(initialCount: 1, maximumCount: 1, SemaphoreNameFor(window.Pid));
        bool acquired;
        try
        {
            acquired = semaphore.WaitOne(DefaultWait);
        }
        catch
        {
            semaphore.Dispose();
            throw;
        }

        if (acquired)
            return new BrowserAutomationLease(semaphore);

        semaphore.Dispose();
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
            _semaphore.Release();
        }
        finally
        {
            _semaphore.Dispose();
        }
    }

    private static string SemaphoreNameFor(int pid) => $@"Local\trope-cua-browser-uia-semaphore-{UserKey()}-{pid}";

    private static string UserKey()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        foreach (var ch in Path.GetInvalidFileNameChars())
            sid = sid.Replace(ch, '_');
        return sid;
    }
}
