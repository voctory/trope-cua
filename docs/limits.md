# Known limits

Trope CUA refuses or marks unsafe routes when Windows or a target app cannot satisfy the background-safety contract. This page names the common cases and the intended workarounds.

## Parent-session launches can foreground the target

Symptom: `launch_app` returns `requires_background_launch_lane`.

Cause: Windows ShellExecute/CreateProcess launch paths can foreground or activate the new app. Trope CUA does not treat that as a background-safe default.

Workarounds:

1. Reuse an existing window from `list_windows`.
2. Use a child-session or AppBroadcast lane for isolated launch-and-drive workflows.
3. If a human explicitly accepts a visible foreground launch, pass `unsafe_allow_foreground=true`. The receipt remains unsafe and reports any foreground change.

## UIA providers can foreground native apps

Symptom: `click`, `set_value`, or `type_text` refuses with a route such as `requires_child_session`, `requires_browser_semantic_route`, or `requires_cdp_or_child_session`.

Cause: Some UIA providers expose actions or setters that are not background-safe. Invoking them can focus or foreground the app, especially for virtualized WinUI controls.

Workarounds:

1. Prefer element-indexed MSAA/UIA routes that return `background_safe=true`.
2. Use semantic buttons or direct setters instead of keyboard commits where possible.
3. Keep `allow_transient_foreground` enabled only when a foreground blip is acceptable for that existing window.
4. Move hard cases to child-session or AppBroadcast isolation.

## Chromium without CDP is not a true parallel browser lane

Symptom: Parallel agents targeting the same Chrome/Edge/Brave process receive `browser.chromium_fallback.contended`, or a text route fails because the element does not expose `IAccessibleEditableText`.

Cause: Chromium UIA/IA2 fallback routes are process-global and can block inside Windows accessibility calls. Trope CUA serializes non-CDP Chromium fallback paths per browser process so one blocked action does not make every parallel agent hang.

Workarounds:

1. Retry the same action up to 3 times after the active browser action completes.
2. Use a `cdp_port` or configured `chromium_debugging_port` for the exact browser window when CDP is intentionally available.
3. For true parallel browser work, launch separate browser profiles with separate CDP ports and target different browser processes.

## Browser web content may not expose a safe click target

Symptom: Pixel click on browser content returns `requires_cdp_or_uia_hit_test`, or text input returns `requires_cdp_or_uia_text_target`.

Cause: Browser providers often expose partial UIA trees. Blind `PostMessage` mouse or keyboard delivery is not reported as success because it can target the wrong app or fail silently.

Workarounds:

1. Refresh `get_window_state` and use a current `element_index`.
2. Use CDP for Chromium/Electron when the target window is CDP-enabled.
3. Use `browser_eval` for activation-gated browser flows where CDP is the configured route.
4. Use child-session isolation for browser surfaces that require hardware-style input.

## Canvas, games, DirectX, Unity, and Unreal need isolation

Symptom: `get_window_state` returns a small or unhelpful tree; clicks or keys are refused or no-op.

Cause: Raw-input and game-style surfaces often ignore UIA, targeted `HWND` messages, and browser CDP. They require hardware-style input, which Trope CUA will not send into the parent user session by default.

Workarounds:

1. Run the target and driver inside a child session.
2. Use an AppBroadcast/InputInjector package if you have the restricted Microsoft capability.
3. Treat parent-session hardware input flags as local experiments only.

## AppBroadcast/InputInjector requires restricted capabilities

Symptom: AppBroadcast probes report unavailable APIs, denied creation, or dropped input.

Cause: `Windows.UI.Input.Preview.Injection.InputInjector.TryCreateForAppBroadcastOnly()` is a restricted-capability lane. The default open-source build includes the integration seam and probes, not a generally provisioned production package.

Workarounds:

1. Use the child-session lane for production hard-case automation.
2. Use `appbroadcast_input_probe` only to diagnose provisioned builds.
3. See [AppBroadcast/InputInjector lane](appbroadcast-inputinjector.md).

## Child sessions need host setup

Symptom: `child_session_status` reports that child sessions are unsupported, disabled, or require elevation.

Cause: Windows child-session support depends on OS policy, Terminal Services behavior, and enabling child sessions from an elevated context on some machines.

Workarounds:

1. Run `child_session_status` to see the exact state.
2. Enable child sessions once from an elevated process when required.
3. Start the no-activate host with `child_session_start`.
4. See [Child-session lane](child-session.md).

## Screenshots depend on target and desktop state

Symptom: `screenshot` or `get_window_state` returns a blank, stale, black, or partially rendered image for a target.

Cause: The default capture path uses GDI/PrintWindow-style capture plus fallback behavior. Some GPU, protected, minimized, or off-desktop surfaces do not render correctly through that path.

Workarounds:

1. Prefer visible, non-minimized target windows.
2. Use `ax` mode when the UIA tree is sufficient.
3. Use child-session isolation for hard-case apps where capture and input must be controlled together.
4. See [Capture](capture.md).

## Element indexes are process-local

Symptom: An action reports that no cached UIA state exists, or an `element_index` no longer acts on the expected control.

Cause: Element indexes are cached in memory for the current MCP or daemon process and are replaced by the next `get_window_state` snapshot for the same `(pid, window_id)`.

Workarounds:

1. Call `get_window_state` before element-indexed actions in the same MCP or daemon process.
2. Pass the same `pid` and `window_id` to the action.
3. Use the daemon for shell workflows that span multiple commands.
4. Refresh the snapshot after navigation, animation, reloads, or failed element actions.

## Visual cursor color is not execution isolation

Symptom: Multiple agents show different colored overlay cursors but still interfere with the same app or browser process.

Cause: Cursor palettes distinguish intent visually. They do not isolate target process state, UIA providers, browser profiles, or global app resources.

Workarounds:

1. Use separate daemon/MCP sessions for separate cursor identity.
2. Use separate browser profiles and CDP ports for parallel Chromium work.
3. Use separate Windows sessions, child sessions, or virtual machines when the target app itself is not concurrency-safe.
