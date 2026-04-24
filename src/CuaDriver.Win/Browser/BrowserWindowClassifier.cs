using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Browser;

public static class BrowserWindowClassifier
{
    public static bool IsLikelyBrowser(WindowInfo window)
    {
        var app = window.AppName.ToLowerInvariant();
        var cls = window.ClassName.ToLowerInvariant();
        return app is "firefox" or "chrome" or "msedge" or "brave" or "opera" or "vivaldi"
               || cls.Contains("mozillawindowclass", StringComparison.Ordinal)
               || cls.Contains("chrome_widgetwin", StringComparison.Ordinal);
    }
}
