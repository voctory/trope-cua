using System.Runtime.InteropServices;
using CuaDriver.Win.Win32;
using Windows.Foundation.Metadata;
using Windows.UI.Input.Preview.Injection;

namespace CuaDriver.Win.HardCases;

/// <summary>
/// Probe and integration seam for Windows.UI.Input.Preview.Injection.
/// The API can be present/callable on some desktop builds, but it injects
/// parent-session input and is not a pid-addressed background lane by itself.
/// </summary>
public static class AppBroadcastInputInjector
{
    public static string Status()
    {
        try
        {
            var typePresent = ApiInformation.IsTypePresent("Windows.UI.Input.Preview.Injection.InputInjector");
            if (!typePresent)
                return "InputInjector WinRT type is not present on this OS.";

            var injector = InputInjector.TryCreate();
            var appBroadcastInjector = InputInjector.TryCreateForAppBroadcastOnly();
            return $"type_present=true try_create={(injector is not null)} try_create_appbroadcast={(appBroadcastInjector is not null)} generic_lane=parent_session_global appbroadcast_lane=requires_active_broadcast_validation";
        }
        catch (Exception ex)
        {
            return $"probe_failed type={ex.GetType().Name} hresult=0x{Marshal.GetHRForException(ex):X8} message=\"{ex.Message}\"";
        }
    }

    public static string ProbeMouseDelta(bool appBroadcastOnly)
    {
        try
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.Input.Preview.Injection.InputInjector"))
                return "ok=false reason=\"InputInjector WinRT type is not present on this OS.\"";

            var injector = appBroadcastOnly
                ? InputInjector.TryCreateForAppBroadcastOnly()
                : InputInjector.TryCreate();
            if (injector is null)
                return $"ok=false appbroadcast_only={appBroadcastOnly} reason=\"InputInjector factory returned null.\"";

            NativeMethods.GetCursorPos(out var before);
            var forward = new InjectedInputMouseInfo
            {
                MouseOptions = InjectedInputMouseOptions.Move,
                DeltaX = 8,
                DeltaY = 0
            };
            injector.InjectMouseInput([forward]);
            var afterForward = WaitForCursor(point => point.X != before.X || point.Y != before.Y, TimeSpan.FromMilliseconds(50));

            var back = new InjectedInputMouseInfo
            {
                MouseOptions = InjectedInputMouseOptions.Move,
                DeltaX = -8,
                DeltaY = 0
            };
            injector.InjectMouseInput([back]);
            var afterBack = WaitForCursor(point => point.X == before.X && point.Y == before.Y, TimeSpan.FromMilliseconds(50));

            var movedOnForward = before.X != afterForward.X || before.Y != afterForward.Y;
            var restoredByInjector = before.X == afterBack.X && before.Y == afterBack.Y;
            var restoredByProbe = false;
            if (!restoredByInjector)
            {
                _ = NativeMethods.SetCursorPos(before.X, before.Y);
                afterBack = WaitForCursor(point => point.X == before.X && point.Y == before.Y, TimeSpan.FromMilliseconds(50));
                restoredByProbe = before.X == afterBack.X && before.Y == afterBack.Y;
            }

            return string.Join(Environment.NewLine, [
                "ok=true",
                $"appbroadcast_only={appBroadcastOnly}",
                $"cursor_before={before}",
                $"cursor_after_forward={afterForward}",
                $"cursor_after_back={afterBack}",
                $"cursor_moved_on_forward={movedOnForward}",
                $"cursor_restored_by_injector={restoredByInjector}",
                $"cursor_restored_by_probe={restoredByProbe}",
                $"background_safe={!movedOnForward}",
                $"route={(appBroadcastOnly ? "inputinjector.trycreateforappbroadcastonly" : "inputinjector.trycreate")}"
            ]);
        }
        catch (Exception ex)
        {
            return $"ok=false appbroadcast_only={appBroadcastOnly} type={ex.GetType().Name} hresult=0x{Marshal.GetHRForException(ex):X8} message=\"{ex.Message}\"";
        }
    }

    private static POINT WaitForCursor(Func<POINT, bool> predicate, TimeSpan timeout)
    {
        NativeMethods.GetCursorPos(out var current);
        SpinWait.SpinUntil(() =>
        {
            NativeMethods.GetCursorPos(out current);
            return predicate(current);
        }, timeout);
        return current;
    }

#if CUA_ENABLE_APPBROADCAST
    // Intentionally left as an integration seam. The provisioned broker should
    // return ActionReceipt(route: "appbroadcast.inputinjector", lane: "appbroadcast").
#endif
}
