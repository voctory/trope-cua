using CuaDriver.Win.Input;

namespace CuaDriver.Win.Browser;

internal static class BrowserNavigation
{
    public static ActionReceipt? RefuseForegroundOnlyLinkRoute(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var candidate = value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme is not ("http" or "https"))
            return null;

        return ActionReceipt.Failure(
            "requires_cdp_or_child_session",
            "Browser link exposes a URL, but the only generic Windows route would foreground the browser through ShellExecute. Refusing to steal focus; provide cdp_port for Chromium or use the child-session/AppBroadcast lane.");
    }
}
