# API map

| macOS cua-driver shape | Windows port route |
|---|---|
| App/window enumeration | `EnumWindows`, `GetWindowThreadProcessId`, `GetWindowTextW`, `GetClassNameW`, `DwmGetWindowAttribute` |
| AX tree | `System.Windows.Automation` / UI Automation |
| AX action | UIA control patterns (`InvokePattern`, `ValuePattern`, etc.) |
| Window screenshot | WGC seam; current fallback uses `PrintWindow`/GDI and reports screen-copy fallback when `PrintWindow` fails |
| Pixel click | CDP for Chromium, targeted `HWND` messages for classic controls, child-session hardware input for raw surfaces |
| Per-process trusted input | AppBroadcast + `InputInjector.TryCreateForAppBroadcastOnly` when provisioned |
| Focus without raise | Prefer not needed; avoid `SetForegroundWindow`; use semantic UIA routes |
| Raw-input/canvas/games | RDP child session / PiP lane probe exists; full child action-agent routing is still pending |
