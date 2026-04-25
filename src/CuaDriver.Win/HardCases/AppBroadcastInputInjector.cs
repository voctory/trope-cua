using System.Runtime.InteropServices;
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
            return $"type_present=true try_create={(injector is not null)} try_create_appbroadcast={(appBroadcastInjector is not null)} lane=parent_session_global background_safe=false";
        }
        catch (Exception ex)
        {
            return $"probe_failed type={ex.GetType().Name} hresult=0x{Marshal.GetHRForException(ex):X8} message=\"{ex.Message}\"";
        }
    }

#if CUA_ENABLE_APPBROADCAST
    // Intentionally left as an integration seam. The provisioned broker should
    // return ActionReceipt(route: "appbroadcast.inputinjector", lane: "appbroadcast").
#endif
}
