namespace CuaDriver.Win.Tools;

internal static class ToolDescriptions
{
    public const string AgentInstructions = """
        You are controlling Windows through cua-driver-win. Treat this as a background-safe computer-use driver, not as a foreground mouse and keyboard driver.

        Default workflow:
        1. Reuse an existing window when possible. Call list_windows, choose an explicit pid and window_id, then call get_window_state for that exact pair.
        2. Prefer element_index actions from the latest get_window_state snapshot for that same pid/window_id. Use pixels only for canvas, custom, or non-UIA surfaces.
        3. Read every action receipt. A successful background action must report background_safe=true, cursor_moved=false, and foreground_changed=false.
        4. If a route refuses with requires_cdp, requires_child_session, requires_appbroadcast, or requires_background_launch_lane, switch to that lane or report the blocker. Do not work around it with blind parent-session mouse or keyboard input.
        5. For Chromium/Electron browser surfaces, first reuse an existing browser window from list_windows. Do not launch a separate debugging-profile browser just to get CDP unless the user explicitly asked for a new CDP-controlled window.
        6. Browser text workflow: call get_window_state, choose the edit/search/address element, call type_text with that element_index, then press_key enter/return with the same pid/window_id/element_index. Do not use set_value for browser search/address fields unless the tool itself reports success. If a CDP port is already configured for that same existing browser window and address/search submission is blocked, use browser_eval with a user-gesture navigation expression instead of launching or shelling out to the browser.
        7. For Chromium/Electron browser surfaces, use cdp_port or configured chromium_debugging_port only when that exact existing browser window is already CDP-enabled. If a normal user browser refuses a browser route, use child-session/AppBroadcast or report the blocker.
        8. If an element-indexed browser action fails because the element exposes no supported action, or the page may have changed after ads, loading, animation, or navigation, call get_window_state again for the same pid/window_id and retry with a fresh element_index.
        9. Existing-window actions may use transient foreground/focus by default when the verified background route fails; read the receipt because these actions report background_safe=false when a foreground blip occurs. Do not pass unsafe_allow_foreground for launches, and do not use allow_parent_sendinput or allow_parent_cursor for routine automation.

        The visual agent cursor is an overlay that shows intent above the target window. It must not be treated as the real Windows cursor.
        """;

    public const string GetWindowState = """
        Walk a running app's UI Automation tree and return a Markdown rendering of its UI, tagging actionable elements with [element_index N]. Pass those indices to click, double_click, right_click, type_text, press_key, hotkey, scroll, or set_value.

        Invariant: call get_window_state once per turn per (pid, window_id) before any element-indexed action against that window. The element_index cache is scoped to the same (pid, window_id) and is replaced by the next snapshot for that window.

        Use list_windows to choose a window_id. The driver does not pick a different window implicitly: window_id must belong to pid. Window-local screenshot coordinates returned here are the coordinate space used by pixel click, right_click, double_click, zoom, and debug_image_out.

        Response shape is controlled by persistent capture_mode (default som):
        - som: screenshot plus UIA tree. Prefer element_index actions, using the image only to disambiguate.
        - vision: screenshot only. No UIA cache is updated, so element-indexed actions will fail until a non-vision snapshot runs.
        - ax: UIA tree only. No screen capture.

        Set query to a case-insensitive substring to trim the rendered Markdown to matching lines plus ancestors. Indices still resolve against the full cached tree.
        """;

    public const string Click = """
        Left-click against a target pid. Two addressing modes:

        - element_index + window_id from the last get_window_state snapshot performs a semantic UIA action on the cached element. Default action is press; other action names include show_menu, pick, confirm, cancel, and open. This is the preferred path when an element_index exists: it is background-safe, does not move the real cursor, and should not foreground the target. Requires a prior get_window_state(pid, window_id) in this MCP or daemon process.

        - x and y are window-local screenshot pixels, top-left origin, in the same pixel space as get_window_state returned. Use this for canvas, WebGL, custom surfaces, or when there is no useful element_index. The driver maps resized screenshot pixels back to native window pixels internally. count: 2 posts a double-click. modifier/modifiers holds ctrl, shift, alt/option, or win/cmd during the pixel click.

        allow_transient_foreground controls the existing-window foreground fallback. It defaults true: when no verified background route exists, the driver may briefly foreground/focus the target and then restore the previous cursor and foreground. Treat any receipt with background_safe=false, foreground_changed=true, or cursor_moved=true as a transient foreground route even when ok=true.

        Agent rule: after get_window_state exposes an element_index, use it instead of approximating a pixel click. Pixel clicks are for surfaces that do not expose a useful UIA element. Exactly one of element_index or (x and y) must be provided. window_id is required for element_index and recommended for pixel clicks. action is only valid with element_index; count and modifier only affect the pixel route. For Chromium web content, provide cdp_port or configure chromium_debugging_port; blind browser PostMessage clicks are refused to avoid reporting an unsafe foreground-only route as success. If a browser element_index action reports that the element exposes no supported action, refresh get_window_state and retry with the new element_index before falling back to pixels.
        """;

    public const string DoubleClick = """
        Double-click against a target pid. Two addressing modes:

        - element_index + window_id from the last get_window_state snapshot targets the cached UIA element. Prefer this when the tree exposes the target; it preserves the same element-indexed workflow as click.

        - x and y are window-local screenshot pixels, top-left origin, in the same pixel space as get_window_state returned. This routes through the same pixel engine as click(count=2). modifier/modifiers holds ctrl, shift, alt/option, or win/cmd during the gesture.

        Agent rule: do not double-click a browser or custom surface just to force focus. Use the same background-safe routing rules as click and trust the returned receipt. Exactly one of element_index or (x and y) must be provided. pid is required. window_id is required for element_index and recommended for pixel double-clicks.
        """;

    public const string RightClick = """
        Right-click against a target pid. Two addressing modes:

        - element_index + window_id from the last get_window_state snapshot performs the semantic menu action on the cached element where possible. Prefer this path for UIA-addressable controls because it is background-safe and does not move the real cursor.

        - x and y are window-local screenshot pixels, top-left origin, in the same pixel space as get_window_state returned. Use this for non-UIA surfaces. modifier/modifiers holds ctrl, shift, alt/option, or win/cmd during the pixel right-click and forces the pixel route rather than a semantic UIA action.

        Agent rule: do not use right-click as a foregrounding workaround. Exactly one of element_index or (x and y) must be provided. pid is required. window_id is required for element_index and recommended for pixel right-clicks. For browser web content, configure cdp_port when possible; unsafe browser fallback routes are refused instead of stealing focus.
        """;

    public const string Scroll = """
        Scroll a target window without parent-session SendInput. Prefer direction mode: direction up/down/left/right, amount repetitions, and by line/page. This sends background-safe key messages such as Down, PageDown, or PageUp.

        Optional element_index + window_id from the last get_window_state snapshot targets that element's native HWND when available, falling back to the root target window. Skip it when a prior click already established the target's internal focus.

        Windows wheel mode remains available when direction is omitted: x/y identify the region to scroll in window-local screenshot pixels, delta controls wheel amount, and the target window center is used when x/y are omitted. Prefer direction mode for browser-like surfaces because wheel messages are commonly filtered or foreground-prone.

        Browser providers often expose partial UIA trees and foreground-prone routes. The Windows driver refuses unsafe browser scroll fallbacks unless a background-safe UIA or CDP route is available. When scrolling fails with an unsafe-route receipt, configure CDP or a child-session lane instead of foregrounding the browser.
        """;

    public const string TypeText = """
        Insert text into a target pid/window. Use element_index + window_id from the last get_window_state snapshot when filling a specific field; type_text writes the complete text atomically through IA2/UIA text setters where possible. Without element_index, type_text targets the last UIA text target, then Chromium CDP Input.insertText when cdp_port is configured, then a native child HWND text route for classic controls.

        For Chromium or Electron inputs, pass the edit/search/address element_index directly to type_text. The browser route first uses a background HWND click to establish Chromium's internal focus, then writes through IA2 editable text and can fall back to focused HWND text messages when IA2 is unavailable. Follow with press_key enter/return using the same element_index when submitting a search or address. Prefer this over set_value for browser fields because set_value is an atomic ValuePattern operation and browser providers often expose it without a background-safe setter. If the browser route has no safe target and no cdp_port, the tool refuses blind WM_CHAR delivery rather than typing into the wrong foreground app.

        delay_ms is ignored by type_text because background text entry should be atomic. Use type_text_chars when a caller explicitly needs character-by-character entry.

        allow_transient_foreground controls the existing-window foreground fallback. It defaults true: when no verified background text route exists, the driver may briefly foreground/focus the target and then restore the previous cursor and foreground. Treat any receipt with background_safe=false, foreground_changed=true, or cursor_moved=true as a transient foreground route even when ok=true.

        Agent rule: use element_index when filling a known field. Do not click the page and then blindly type unless the receipt proves a background-safe text target was established. Special keys such as Return, Escape, arrows, and shortcuts go through press_key or hotkey, not type_text.
        """;

    public const string TypeTextChars = """
        Compatibility surface for type_text. It accepts the same pid, window_id, element_index, text, cdp_port, and allow_transient_foreground arguments and routes through the same background-safe text insertion logic.

        delay_ms spaces character delivery. Default is 30 ms, clamped to 0-200. Pass delay_ms=0 when an instant/bulk write is explicitly desired.

        Use this name when a caller expects character-by-character entry; the implementation intentionally shares the safer type_text routing where possible. Do not use this to bypass a type_text refusal.
        """;

    public const string PressKey = """
        Press and release a single key against a target pid/window without parent-session SendInput. The target does not need to be foreground if the target HWND accepts posted key messages.

        Optional element_index + window_id from the last get_window_state snapshot targets that element's native HWND when available, falling back to the root target window. This preserves control-specific key routing while avoiding parent-session SendInput.

        For Chromium browser content, press_key uses cdp_port/configured chromium_debugging_port when available. Without CDP, pass the same element_index used for type_text; the driver tries a background HWND click to establish Chromium's internal focus, then posts the key to the browser HWND. If no background key target is available and allow_transient_foreground is true, the existing browser window may be temporarily foregrounded for keyboard input, then foreground is restored. Treat any receipt with background_safe=false, foreground_changed=true, or cursor_moved=true as a transient foreground route even when ok=true. If address/search submission is still blocked and CDP is configured for this exact browser window, use browser_eval for user-gesture navigation instead of launching or shelling out to the browser.

        Agent rule: pass window_id whenever possible and prefer element_index when targeting a specific control. Key vocabulary: enter/return, tab, escape/esc, arrows, space, backspace, delete, home, end, pageup, pagedown, f1-f24, plus any letter or digit. modifiers can hold ctrl, shift, alt/option, or win/cmd. For true key combinations such as ctrl+c, use hotkey.
        """;

    public const string Hotkey = """
        Press a modifier combination against a target pid/window without parent-session SendInput. Prefer the keys array, for example ["ctrl", "c"]. The key plus modifiers shape remains accepted.

        Recognized modifiers: ctrl/control, shift, alt/option, win/cmd/meta. Non-modifier keys use the same vocabulary as press_key. Pass window_id when available; otherwise the driver's current main-window heuristic is used. Do not use hotkeys to compensate for a missing safe text/click route.
        """;

    public const string LaunchApp = """
        Launch an app for background automation. Parent-session Windows launches can foreground the target, so this tool refuses those launches by default. Do not pass unsafe_allow_foreground for routine automation. When unsafe_allow_foreground=true is explicitly used, the driver still treats the route as unsafe, attempts to restore the previous foreground window, and sends launched windows behind the current stack with no activation.

        Prefer list_windows to reuse an already running app before launching a new one. For Chromium browsers, do not create a separate --remote-debugging-port profile unless the user explicitly asked for a CDP-controlled window; use an existing browser window, a configured CDP port for that exact window, or an isolated child-session/AppBroadcast lane. Provide path for an executable or name for an app/executable name. Use list_apps to discover installed apps and list_windows after launch to choose the target window_id for get_window_state. Normal workflow: launch or identify an app, enumerate windows, snapshot a specific (pid, window_id), then act by element_index whenever possible.
        """;

    public const string ListApps = """
        List installed and running Windows apps with process identity when available.

        Use this for app discovery ("is X installed or running?"). For window-level reasoning such as which window to inspect, whether a window is minimized, where it is on screen, or which HWND to pass as window_id, call list_windows. list_apps is not a prerequisite for launch_app when you already know a path or executable name.
        """;

    public const string ListWindows = """
        List top-level Windows HWNDs with owning pid/app, title, class name, bounds, visibility, minimized state, z-order, and DPI.

        Use this, not list_apps, for any window-level reasoning: choosing the window_id for get_window_state, deciding which of a pid's windows is main, checking whether a target is visible/minimized, or comparing stacking order. Pass pid to restrict the result to one process. on_screen_only=false is useful for automation because background/minimized windows can still be addressable by UIA or HWND routes.
        """;

    public const string Screenshot = """
        Capture a screenshot and return it as native MCP image content. Without window_id, captures the full virtual desktop. With window_id, captures that specific target window and can validate pid.

        format accepts png or jpeg (default png). quality is JPEG quality 1-95 and is ignored for png. This is a raw screenshot tool; it does not walk UIA and does not populate the element_index cache.

        For normal agent interaction, prefer get_window_state because it returns the screenshot plus the actionable UIA tree and keeps coordinates/cached indices aligned. Use screenshot when you only need pixels, want a full-desktop overview, or want to write an image file with out.
        """;

    public const string Zoom = """
        Zoom into a rectangular region of a window screenshot at native resolution. Use this when get_window_state returned a resized image and you need to read small text, identify icons, or verify details.

        Coordinates x1, y1, x2, y2 are in the same pixel space as the screenshot returned by get_window_state. Pass window_id when the pid has multiple windows so the zoom uses the exact prior screenshot context. The tool maps the region back through the stored resize ratio, captures at native resolution, adds 20 percent padding, and stores a zoom context. To click something in the returned zoom image, call click with from_zoom=true and x/y in the zoom image coordinate space.

        Requires get_window_state(pid, window_id) earlier in this session so the resize ratio is known.
        """;

    public const string SetValue = """
        Directly set a UIA element's value. Use element_index + window_id from the last get_window_state snapshot for controls that expose a settable ValuePattern or RangeValuePattern, such as sliders, steppers, and native editable combo boxes.

        For Chromium/Electron address, search, and page text fields, prefer type_text with element_index, then press_key enter/return if submission is needed. Browser ValuePattern setters are often exposed but not background-safe, so set_value may return use_type_text_for_browser_text instead of attempting an unsafe UIA setter.

        allow_transient_foreground controls the existing-window foreground fallback. It defaults true: when no verified background value route exists, the driver may briefly foreground/focus the target and then restore the previous cursor and foreground. Treat any receipt with background_safe=false, foreground_changed=true, or cursor_moved=true as a transient foreground route even when ok=true.

        For free-form text entry where cursor position matters, prefer type_text. set_value replaces or sets the element value directly.
        """;
}
