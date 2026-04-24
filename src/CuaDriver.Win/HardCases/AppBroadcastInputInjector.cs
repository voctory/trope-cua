namespace CuaDriver.Win.HardCases;

/// <summary>
/// Disabled source seam for the restricted AppBroadcast/InputInjector lane.
/// Define CUA_ENABLE_APPBROADCAST in a provisioned UWP/desktop-bridge broker
/// with the required Microsoft restricted capabilities, then replace the
/// placeholder with calls to:
///
/// Windows.UI.Input.Preview.Injection.InputInjector.TryCreateForAppBroadcastOnly()
///
/// The default desktop console build intentionally leaves this disabled.
/// </summary>
public static class AppBroadcastInputInjector
{
    public static string Status() =>
        "Not enabled in the default build. Requires Microsoft-provisioned restricted capabilities.";

#if CUA_ENABLE_APPBROADCAST
    // Intentionally left as an integration seam. The provisioned broker should
    // return ActionReceipt(route: "appbroadcast.inputinjector", lane: "appbroadcast").
#endif
}
