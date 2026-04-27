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
- `appbroadcast_input_probe {"probe": "mouse_click", "x": 1600, "y": 870}` sends an absolute click while checking parent cursor and foreground stability. On this machine the parent cursor and foreground window remain unchanged, but Chrome also does not receive the click without an active AppBroadcast capture target.
- `appbroadcast_input_probe {"probe": "keyboard_key", "key": "f24"}` uses `TryCreateForAppBroadcastOnly()` and injects a key down/up pair while checking parent cursor and foreground stability. On this machine the parent cursor and foreground window remain unchanged.
- `appbroadcast_input_probe {"probe": "keyboard_text", "text": "never gonna give you up", "press_enter": true}` injects Unicode text and an optional Enter key while checking parent cursor and foreground stability. On this machine the parent cursor and foreground window remain unchanged, but Chrome does not receive the text without an active AppBroadcast capture target.
- `appbroadcast_input_probe {"probe": "broadcast_status"}` calls `AppBroadcastingUI.GetDefault().GetStatus()` without opening UI. On this machine the call fails with `0x80070490`, even though the `AppBroadcastingUI` and `AppBroadcastServices` WinRT types are present.
- `appbroadcast_input_probe {"probe": "broadcast_plugins"}` calls `AppBroadcastPlugInManager.GetDefault()` and reports provider/plugin availability. This checks whether the machine has a broadcast provider that can be passed to `AppBroadcastServices.EnterBroadcastModeAsync`.
- `appbroadcast_input_probe {"probe": "api_context"}` reports package identity and AppBroadcast/Game Bar type presence from the current driver process. This is useful because the Game Bar widget APIs are package/widget-context APIs, not plain Win32 APIs.
- `appbroadcast_input_probe {"probe": "gamebar_services", "timeout_ms": 10000}` subscribes to `GameBarServicesManager.GameBarServicesCreated` and reports any target metadata exposed by `GameBarServices.TargetInfo`, `AppBroadcastServices`, and `AppCaptureServices`. On this machine the manager can be created, but no event is delivered passively or after opening Game Bar with Chrome foregrounded.
- `appbroadcast_input_probe {"appbroadcast_only": false}` uses generic `TryCreate()` and proves whether generic InputInjector affects the parent cursor. This is diagnostic only and is not a safe background lane.

The AppBroadcast-only probe is not sufficient by itself: production use still needs an active AppBroadcast capture target and provisioned restricted capability behavior before we can treat delivery as reliable.

Deeper Game Bar findings:

- Microsoft documents `TryCreateForAppBroadcastOnly()` as silently dropping injected input when no process is actively captured for broadcast with `AppBroadcastServices`.
- `AppBroadcastServices` is the API surface that can enter/exit broadcast mode and start broadcast/preview, but Microsoft documents it as requiring `appBroadcast`, `appBroadcastServices`, and `appBroadcastSettings`; these capabilities are specially provisioned.
- The Xbox Game Bar widget SDK exposes `XboxGameBarAppTargetTracker`, which can track the app currently targeted by Game Bar. The public NuGet sample uses it only after Game Bar activates a `microsoft.gameBarUIExtension` widget.
- The installed Game Bar package on this machine has a newer `Microsoft.Gaming.XboxGameBar.winmd` whose `XboxGameBarAppTarget` includes an `Hwnd` property, and an internal `XboxGameBarFT.winmd` whose `AppTargetInfo` includes `Hwnd`, `InputHwnd`, process ids, and `IsInputDelegationSupported`. Direct activation of the internal full-trust factory from an ordinary process failed, so this likely requires Game Bar/package context.
- `appbroadcast_input_probe {"probe": "api_context"}` from the plain driver reports `packaged=false`, package identity error `15700`, AUMID error `15703`, AppBroadcast contracts present, and `Microsoft.Gaming.XboxGameBar.*` type presence as unavailable in this process.
- `appbroadcast_input_probe {"probe": "broadcast_plugins"}` reports `is_broadcast_provider_available=false`, no default plugin, and `plugin_count=0` on this machine.
- The installed Game Bar manifest registers `GameBarFTServer.exe` as a packaged COM server (`GbftComFactory`) and hosts the `microsoft.gameBarUIExtension` app-extension point. It also carries the first-party restricted capabilities `appBroadcast`, `appBroadcastServices`, `appBroadcastSettings`, `gameBarServices`, and `runFullTrust`.
- Internal `XboxGameBarFT` metadata exposes target/focus/broadcast helpers (`AppTargetManagerFT`, `InputFocusTrackerFT`, `BcastServicesFT`) and overlay window helpers, but it does not expose a generic keyboard/text injection method. The documented input injection gate remains `InputInjector.TryCreateForAppBroadcastOnly()` plus an active AppBroadcast target.
- Microsoft's widget samples include a full-trust COM server pattern. A plausible next experiment is a packaged Game Bar widget that obtains the current target via `XboxGameBarAppTargetTracker`, then hands target metadata to a packaged full-trust broker that tries the AppBroadcast/InputInjector path. That still depends on restricted capability provisioning for actual input delivery.

References:

- <https://learn.microsoft.com/en-us/uwp/api/windows.ui.input.preview.injection.inputinjector.trycreateforappbroadcastonly>
- <https://learn.microsoft.com/en-us/uwp/api/windows.media.capture.appbroadcastservices>
- <https://learn.microsoft.com/en-us/gaming/game-bar/api/xgb-apptargettracker>
- <https://github.com/microsoft/XboxGameBarSamples>
