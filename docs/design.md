# Windows design

Trope CUA is built as a route stack rather than a single API substitution.

## Lane A: same-session background-safe lane

Default for ordinary Windows applications.

Routes:

1. UI Automation patterns: `Invoke`, `Toggle`, `SelectionItem`, `ExpandCollapse`, `Value`, `RangeValue`, `Scroll`, `ScrollItem`.
2. Chromium DevTools Protocol when a tab is explicitly available through a debugging port.
3. Classic Win32 targeted messages to child `HWND`s: `BM_CLICK`, mouse-button messages, `WM_CHAR`, `WM_KEY*`, `WM_MOUSEWHEEL`.
4. Refuse hardware-style input in the parent session.

The lane is guarded by before/after cursor and foreground checks.

## Lane B: AppBroadcast/InputInjector lane

This is the closest Microsoft-shaped process-bounded input lane. It is not enabled in the default project because it requires restricted Microsoft capabilities. See `docs/appbroadcast-inputinjector.md` and `src/CuaDriver.Win/Automation/AppBroadcastInputInjector.cs`.

## Lane C: child-session / Picture-in-Picture lane

For canvas, raw-input, DirectX, Unity/Unreal, games, and apps that accept only hardware-style input, the driver should run in an isolated Windows child session and use real input only there. The parent user session remains untouched. See `docs/child-session.md`.

## Tool behavior

`get_window_state` returns the current mode:

- `som`: tree plus screenshot.
- `ax`: tree only.
- `vision`: screenshot only.

Element-indexed actions require a previous `get_window_state` call in the same long-lived process. Use MCP or `serve` for persistent caches.
