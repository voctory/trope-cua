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

See `src/CuaDriver.Win/HardCases/AppBroadcastInputInjector.cs` for the integration seam.

Current probe:

- `appbroadcast_input_probe {"appbroadcast_only": true}` uses `TryCreateForAppBroadcastOnly()` and sends a tiny relative mouse move. On this machine the parent cursor does not move, which is consistent with a target-scoped or dropped AppBroadcast-only event.
- `appbroadcast_input_probe {"appbroadcast_only": false}` uses generic `TryCreate()` and proves whether generic InputInjector affects the parent cursor. This is diagnostic only and is not a safe background lane.

The AppBroadcast-only probe is not sufficient by itself: production use still needs an active AppBroadcast capture target and provisioned restricted capability behavior before we can treat delivery as reliable.
