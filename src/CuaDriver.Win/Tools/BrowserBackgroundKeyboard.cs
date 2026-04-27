using System.Windows.Automation;
using CuaDriver.Win.Input;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

internal static class BrowserBackgroundKeyboard
{
    public static async Task<ActionReceipt> PressKeyAsync(
        WindowInfo window,
        AutomationElement element,
        string key,
        string[] modifiers,
        CancellationToken cancellationToken)
    {
        var point = ToolCoordinates.ElementCenter(element, window);
        if (point is null)
            return ActionReceipt.Failure("browser.hwnd.focus_click.key", "Element has no resolvable screen point for browser focus.");

        var focus = await WindowMessageInput.ClickAsync(window.Hwnd, point.LocalX, point.LocalY, 1, rightButton: false, cancellationToken).ConfigureAwait(false);
        if (!focus.Receipt.Ok)
            return focus.Receipt with { Route = "browser.hwnd.focus_click." + focus.Receipt.Route };

        var keyReceipt = await WindowMessageInput.PressKeyAsync(window.Hwnd, key, modifiers, cancellationToken).ConfigureAwait(false);
        return keyReceipt with { Route = "browser.hwnd.focus_click." + keyReceipt.Route };
    }
}
