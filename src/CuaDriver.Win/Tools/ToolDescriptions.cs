namespace CuaDriver.Win.Tools;

internal static class ToolDescriptions
{
    public const string AgentInstructions = """
        Trope CUA is a background-safe Windows computer-use driver. Treat receipts as authoritative: a safe action reports background_safe=true, cursor_moved=false, and foreground_changed=false.

        Workflow: call list_windows, choose an explicit pid/window_id, then call get_window_state for that exact pair. Prefer element_index actions from the latest snapshot. Use pixels only for canvas/custom surfaces or when no useful element exists.

        If a route refuses with requires_cdp, requires_child_session, requires_appbroadcast, or requires_background_launch_lane, switch lanes or report the blocker. Do not bypass refusals with blind parent-session input, allow_parent_sendinput, or allow_parent_cursor.

        Browser rules: reuse an existing browser window. Do not launch a separate debugging-profile browser just to get CDP unless the user requested one. Browser text workflow: get_window_state, choose the edit/search/address element, type_text with that element_index, then press_key enter/return with the same pid/window_id/element_index. If configured CDP is already available for that same window and submission is blocked, use browser_eval with a user-gesture navigation expression.

        If browser state changed after ads/loading/navigation, call get_window_state again and retry with a fresh element_index. Do not pass unsafe_allow_foreground for launches. Existing-window transient foreground fallbacks may be allowed by default; read the receipt because ok=true can still be unsafe.

        The visual agent cursor is only an overlay, not the real cursor.
        """;

    public const string GetWindowState = """
        Snapshot one explicit (pid, window_id): screenshot plus compact UIA Markdown with actionable [element_index N] entries.

        Use list_windows to choose window_id. The driver never picks a different window implicitly. Call this before element-indexed actions; the cache is scoped to this exact pid/window_id and replaced by the next snapshot for that pair.

        capture_mode: som=screenshot+tree, vision=screenshot only, ax=tree only. Use query to trim Markdown to matches plus ancestors; indices still resolve against the full cached tree. tree_markdown is text-only by default; pass include_structured_tree=true only if JSON duplication is needed.
        """;

    public const string Click = """
        Left-click a target pid.

        Preferred: element_index + window_id from the last get_window_state snapshot. This performs a semantic UIA/MSAA action on the cached element and is background-safe when the receipt says so. action may be press, show_menu, pick, confirm, cancel, or open.

        Pixel fallback: x/y are window-local screenshot pixels from get_window_state. Use pixels for canvas, WebGL, custom surfaces, or when no useful element_index exists. count=2 double-clicks; modifier/modifiers holds ctrl, shift, alt/option, or win/cmd.

        allow_transient_foreground defaults true for existing windows; receipts expose background_safe, foreground_changed, and cursor_moved. For Chromium web content, provide cdp_port/config when available; blind browser PostMessage clicks are refused. If a browser element_index action fails because no supported action is exposed, refresh get_window_state and retry with the new element_index before falling back to pixels.

        Exactly one of element_index or x/y is required. window_id is required for element_index and recommended for pixels.
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
        Insert text into a target pid/window. Use element_index + window_id from the last get_window_state snapshot for a known field; the write is atomic through IA2/UIA when possible. Without element_index, the tool tries the last UIA text target, configured Chromium CDP, then classic child HWND text routes.

        For Chromium/Electron, pass the edit/search/address element_index directly to type_text. The browser route establishes internal focus in the background, writes through IA2 or focused HWND messages, and refuses blind WM_CHAR when no safe target exists. Follow with press_key enter/return using the same element_index when submitting search/address text. Prefer this over set_value for browser fields.

        delay_ms is ignored here; use type_text_chars only when character-by-character entry is required.

        allow_transient_foreground controls the existing-window fallback. It defaults true; inspect receipts for background_safe=false, foreground_changed=true, or cursor_moved=true.

        Special keys such as Return, Escape, arrows, and shortcuts go through press_key or hotkey, not type_text.
        """;

    public const string TypeTextChars = """
        Compatibility surface for type_text. It accepts the same pid, window_id, element_index, text, cdp_port, and allow_transient_foreground arguments and routes through the same background-safe text insertion logic.

        delay_ms spaces character delivery. Default is 30 ms, clamped to 0-200. Pass delay_ms=0 when an instant/bulk write is explicitly desired.

        Use this name when a caller expects character-by-character entry; the implementation intentionally shares the safer type_text routing where possible. Do not use this to bypass a type_text refusal.
        """;

    public const string PressKey = """
        Press and release one key against a target pid/window without parent-session SendInput when a background route exists.

        Optional element_index + window_id targets that element's native HWND when available, else the root window. Without element_index, native apps can reuse the child HWND from the last successful background click.

        For Chromium, use CDP when configured; otherwise pass the same element_index used for type_text so the driver can establish background internal focus before posting the key. If address/search submission is still blocked and CDP is configured for this exact browser window, use browser_eval for user-gesture navigation instead of launching or shelling out to the browser.

        Keys: enter/return, tab, escape/esc, arrows, space, backspace, delete, home, end, pageup, pagedown, f1-f24, letters, digits. modifiers can hold ctrl, shift, alt/option, or win/cmd. Use hotkey for combinations like ctrl+c.
        """;

    public const string Hotkey = """
        Press a modifier combination against a target pid/window without parent-session SendInput. Prefer the keys array, for example ["ctrl", "c"]. The key plus modifiers shape remains accepted. Optional element_index + window_id targets that element's native HWND when available; otherwise native apps reuse the child HWND from the last successful background click for that window when available.

        For Chromium browser content, hotkey uses CDP when available. Without CDP, it can reuse the last browser text target established by click/type_text and applies the same background HWND focus-click plus posted-key chain as press_key.

        Recognized modifiers: ctrl/control, shift, alt/option, win/cmd/meta. Non-modifier keys use the same vocabulary as press_key. Pass window_id when available; otherwise the driver's current main-window heuristic is used. Do not use hotkeys to compensate for a missing safe text/click route.
        """;

    public const string LaunchApp = """
        Launch an app only when a safe lane is available. Parent-session Windows launches can foreground the target, so this tool refuses them by default. Do not pass unsafe_allow_foreground for routine automation; when explicitly set, the receipt remains unsafe and the driver tries to restore foreground.

        Prefer list_windows to reuse a running app. For Chromium, do not create a separate --remote-debugging-port profile unless the user asked for a CDP-controlled window; reuse an existing browser, a configured CDP port for that exact window, or child-session/AppBroadcast. Provide path, exe/name, or app_id.
        """;

    public const string ListApps = """
        List installed and running Windows apps with process identity when available.

        Use this for app discovery ("is X installed or running?"). For window-level reasoning such as which window to inspect, whether a window is minimized, where it is on screen, or which HWND to pass as window_id, call list_windows. list_apps is not a prerequisite for launch_app when you already know a path or executable name.
        """;

    public const string ListWindows = """
        List top-level Windows HWNDs with owning pid/app, title, class name, bounds, visibility, minimized state, z-order, and DPI.

        Use this, not list_apps, for any window-level reasoning: choosing the window_id for get_window_state, deciding which of a pid's windows is main, checking whether a target is visible/minimized, or comparing stacking order. Pass pid to restrict the result to one process. on_screen_only=false is useful for automation because background/minimized windows can still be addressable by UIA or HWND routes. Text output is compact by default; structuredContent.windows always contains the full list. Pass verbose=true only when the expanded text form is useful.
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
