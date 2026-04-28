# Platform API map

| Driver surface | Windows route | macOS route |
|---|---|---|
| App/window enumeration | `EnumWindows`, `GetWindowThreadProcessId`, `GetWindowTextW`, `GetClassNameW`, `DwmGetWindowAttribute` | AppKit/CoreGraphics process and window enumeration |
| Accessibility tree | `System.Windows.Automation` / UI Automation plus MSAA/IA2 where needed | macOS Accessibility AX tree |
| Accessibility action | UIA control patterns (`InvokePattern`, `ValuePattern`, etc.) | AX actions and value setters |
| Window screenshot | WGC seam; current fallback uses `PrintWindow`/GDI and reports screen-copy fallback when `PrintWindow` fails | CoreGraphics/window capture through the app's Screen Recording grant |
| Pixel click | CDP for Chromium, targeted `HWND` messages for classic controls, child-session hardware input for raw surfaces | AX-targeted click path, browser/page helpers, and signed event posting where supported |
| Per-process trusted input | AppBroadcast + `InputInjector.TryCreateForAppBroadcastOnly` when provisioned | TCC-granted app bundle plus AX/event-post paths |
| Focus without raise | Prefer not needed; avoid `SetForegroundWindow`; use semantic UIA routes | Focus-without-raise and focus-restore guards for supported app/browser paths |
| Raw-input/canvas/games | RDP child session / PiP lane probe exists; full child action-agent routing is still pending | Best-effort pixel/drag paths; use isolation when the target ignores AX/event delivery |
