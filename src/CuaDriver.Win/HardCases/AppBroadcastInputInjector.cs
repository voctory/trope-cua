using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
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
    public sealed record ProbeResult(string Text, JsonObject StructuredContent, bool IsError);

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

    public static ProbeResult ProbeMouseDelta(bool appBroadcastOnly)
    {
        var route = appBroadcastOnly ? "inputinjector.trycreateforappbroadcastonly" : "inputinjector.trycreate";
        try
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.Input.Preview.Injection.InputInjector"))
                return Failure(appBroadcastOnly, route, "InputInjector WinRT type is not present on this OS.");

            var injector = appBroadcastOnly
                ? InputInjector.TryCreateForAppBroadcastOnly()
                : InputInjector.TryCreate();
            if (injector is null)
                return Failure(appBroadcastOnly, route, "InputInjector factory returned null.");

            if (!NativeMethods.GetCursorPos(out var before))
                return Failure(appBroadcastOnly, "user32.getcursorpos", "GetCursorPos failed before probe injection.");

            var forward = new InjectedInputMouseInfo
            {
                MouseOptions = InjectedInputMouseOptions.Move,
                DeltaX = 8,
                DeltaY = 0
            };
            injector.InjectMouseInput([forward]);
            if (!TryWaitForCursor(point => point.X != before.X || point.Y != before.Y, TimeSpan.FromMilliseconds(50), out var afterForward))
                return Failure(appBroadcastOnly, "user32.getcursorpos", "GetCursorPos failed after forward probe injection.");

            var back = new InjectedInputMouseInfo
            {
                MouseOptions = InjectedInputMouseOptions.Move,
                DeltaX = -8,
                DeltaY = 0
            };
            injector.InjectMouseInput([back]);
            if (!TryWaitForCursor(point => point.X == before.X && point.Y == before.Y, TimeSpan.FromMilliseconds(50), out var afterBack))
                return Failure(appBroadcastOnly, "user32.getcursorpos", "GetCursorPos failed after restore probe injection.");

            var movedOnForward = before.X != afterForward.X || before.Y != afterForward.Y;
            var restoredByInjector = before.X == afterBack.X && before.Y == afterBack.Y;
            var restoredByProbe = false;
            if (!restoredByInjector)
            {
                var restoreRequested = NativeMethods.SetCursorPos(before.X, before.Y);
                if (!TryWaitForCursor(point => point.X == before.X && point.Y == before.Y, TimeSpan.FromMilliseconds(50), out afterBack))
                    return Failure(appBroadcastOnly, "user32.getcursorpos", "GetCursorPos failed after parent cursor restoration attempt.");
                restoredByProbe = before.X == afterBack.X && before.Y == afterBack.Y;
                if (!restoreRequested || !restoredByProbe)
                {
                    var restoreFailureText = $"ok=false appbroadcast_only={appBroadcastOnly} route=\"{route}\" reason=\"Probe could not restore the parent cursor.\" cursor_before={before} cursor_after_back={afterBack}";
                    return new ProbeResult(restoreFailureText, new JsonObject
                    {
                        ["ok"] = false,
                        ["appbroadcast_only"] = appBroadcastOnly,
                        ["route"] = route,
                        ["reason"] = "Probe could not restore the parent cursor.",
                        ["cursor_before"] = PointObject(before),
                        ["cursor_after_back"] = PointObject(afterBack),
                        ["cursor_restored_by_probe"] = restoredByProbe
                    }, IsError: true);
                }
            }

            var text = string.Join(Environment.NewLine, [
                "ok=true",
                $"appbroadcast_only={appBroadcastOnly}",
                $"cursor_before={before}",
                $"cursor_after_forward={afterForward}",
                $"cursor_after_back={afterBack}",
                $"cursor_moved_on_forward={movedOnForward}",
                $"cursor_restored_by_injector={restoredByInjector}",
                $"cursor_restored_by_probe={restoredByProbe}",
                $"background_safe={!movedOnForward}",
                $"route={route}"
            ]);
            return new ProbeResult(text, new JsonObject
            {
                ["ok"] = true,
                ["appbroadcast_only"] = appBroadcastOnly,
                ["route"] = route,
                ["cursor_before"] = PointObject(before),
                ["cursor_after_forward"] = PointObject(afterForward),
                ["cursor_after_back"] = PointObject(afterBack),
                ["cursor_position_verified"] = true,
                ["cursor_moved_on_forward"] = movedOnForward,
                ["cursor_restored_by_injector"] = restoredByInjector,
                ["cursor_restored_by_probe"] = restoredByProbe,
                ["background_safe"] = !movedOnForward
            }, IsError: false);
        }
        catch (Exception ex)
        {
            var message = $"{ex.GetType().Name}: {ex.Message}";
            return new ProbeResult(
                $"ok=false appbroadcast_only={appBroadcastOnly} route=\"{route}\" hresult=0x{Marshal.GetHRForException(ex):X8} message=\"{ex.Message}\"",
                new JsonObject
                {
                    ["ok"] = false,
                    ["appbroadcast_only"] = appBroadcastOnly,
                    ["route"] = route,
                    ["hresult"] = $"0x{Marshal.GetHRForException(ex):X8}",
                    ["reason"] = message
                },
                IsError: true);
        }
    }

    private static ProbeResult Failure(bool appBroadcastOnly, string route, string reason)
    {
        var text = $"ok=false appbroadcast_only={appBroadcastOnly} route=\"{route}\" reason=\"{reason}\"";
        return new ProbeResult(text, new JsonObject
        {
            ["ok"] = false,
            ["appbroadcast_only"] = appBroadcastOnly,
            ["route"] = route,
            ["reason"] = reason
        }, IsError: true);
    }

    private static bool TryWaitForCursor(Func<POINT, bool> predicate, TimeSpan timeout, out POINT current)
    {
        if (!NativeMethods.GetCursorPos(out current))
            return false;

        var observed = current;
        SpinWait.SpinUntil(() =>
        {
            if (!NativeMethods.GetCursorPos(out observed))
                return false;
            return predicate(observed);
        }, timeout);
        current = observed;
        return true;
    }

    private static JsonObject PointObject(POINT point) => new()
    {
        ["x"] = point.X,
        ["y"] = point.Y
    };

#if CUA_ENABLE_APPBROADCAST
    // Intentionally left as an integration seam. The provisioned broker should
    // return ActionReceipt(route: "appbroadcast.inputinjector", lane: "appbroadcast").
#endif
}
