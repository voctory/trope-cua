# AppBroadcast/InputInjector lane

The Microsoft-shaped candidate for same-session, process-bounded input is:

```text
Windows.UI.Input.Preview.Injection.InputInjector.TryCreateForAppBroadcastOnly()
```

This route is intentionally not compiled in the default project. It requires restricted capabilities that ordinary desktop apps do not receive by default.

Integration plan for a provisioned build:

1. Package a broker with the required restricted capabilities.
2. Start an AppBroadcast capture for the target process.
3. Create an `InputInjector` with `TryCreateForAppBroadcastOnly`.
4. Inject mouse/keyboard/touch/gamepad events.
5. Keep the same receipt contract: report `route="appbroadcast.inputinjector"`, `lane="appbroadcast"`, no parent cursor or foreground change.

See `src/CuaDriver.Win/Automation/AppBroadcastInputInjector.cs` for the disabled integration seam.
