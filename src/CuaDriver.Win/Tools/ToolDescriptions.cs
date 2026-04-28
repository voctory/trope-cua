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
        Snapshot one explicit (pid, window_id): screenshot plus compact UIA Markdown with actionable [eN] entries; pass N as element_index.

        Use list_windows to choose window_id. The driver never picks a different window implicitly. Call this before element-indexed actions; the cache is scoped to this exact pid/window_id and replaced by the next snapshot for that pair.

        capture_mode override: som=screenshot+tree, vision=screenshot only, ax=tree only. Use ax for cheap UIA refreshes and som when pixels are needed. Use query to trim Markdown to matches plus ancestors; indices still resolve against the full cached tree. tree_markdown is text-only by default; pass include_structured_tree=true only if JSON duplication is needed.
        """;

    public const string Click = """
        Left-click a target pid. Prefer element_index + window_id from get_window_state; this performs semantic UIA/MSAA action and is background-safe when the receipt says so. action may be press, show_menu, pick, confirm, cancel, or open.

        Pixel fallback: x/y are window-local screenshot pixels for canvas, WebGL, custom surfaces, or when no useful element_index exists. count=2 double-clicks; modifier/modifiers holds ctrl, shift, alt/option, or win/cmd.

        allow_transient_foreground defaults true for existing windows; inspect background_safe, foreground_changed, and cursor_moved. For Chromium, provide cdp_port/config when available; blind browser PostMessage clicks are refused. If a browser element_index action fails because no supported action is exposed, refresh get_window_state and retry with the new element_index before falling back to pixels.

        Exactly one of element_index or x/y is required. window_id is required for element_index and recommended for pixels.
        """;

    public const string DoubleClick = """
        Double-click a target pid. Use element_index + window_id from the latest get_window_state snapshot when possible; use x/y window-local screenshot pixels only for canvas/custom surfaces. modifier/modifiers holds ctrl, shift, alt/option, or win/cmd.

        Do not double-click a browser or custom surface just to force focus. Trust the receipt. Exactly one of element_index or x/y is required; window_id is required for element_index and recommended for pixels.
        """;

    public const string RightClick = """
        Right-click a target pid. Prefer element_index + window_id from get_window_state for semantic menu actions; use x/y window-local screenshot pixels for non-UIA surfaces. modifier/modifiers holds ctrl, shift, alt/option, or win/cmd.

        Do not use right-click as a foregrounding workaround. Exactly one of element_index or x/y is required. For browser web content, configure cdp_port when possible; unsafe fallback routes are refused instead of stealing focus.
        """;

    public const string Scroll = """
        Scroll a target window without parent-session SendInput. Prefer direction mode: direction up/down/left/right, amount repetitions, by line/page. Optional element_index + window_id targets that element's native HWND; omit it when prior input already established internal focus.

        Wheel mode is available when direction is omitted: x/y are window-local screenshot pixels and delta controls wheel amount. Browser wheel routes are often filtered or foreground-prone, so unsafe browser scroll fallbacks are refused unless UIA or CDP can handle them; use CDP or child-session when receipt says so.
        """;

    public const string TypeText = """
        Insert text into a target pid/window. Use element_index + window_id from get_window_state for a known field; writes are atomic through IA2/UIA when possible. Without element_index, the tool tries the last UIA text target, configured Chromium CDP, then classic child HWND routes.

        For Chromium/Electron, pass the edit/search/address element_index directly to type_text. The browser route establishes internal focus in the background, writes through IA2 or focused HWND messages, and refuses blind WM_CHAR when no safe target exists. Follow with press_key enter/return using the same element_index when submitting search/address text. Prefer this over set_value for browser fields.

        delay_ms is ignored here; use type_text_chars only when character-by-character entry is required.

        allow_transient_foreground controls the existing-window fallback. It defaults true; inspect receipts for background_safe=false, foreground_changed=true, or cursor_moved=true.

        Special keys such as Return, Escape, arrows, and shortcuts go through press_key or hotkey, not type_text.
        """;

    public const string TypeTextChars = """
        Character-by-character compatibility surface for type_text. It accepts the same target arguments and uses the same safe text routing. delay_ms defaults to 30 ms and is clamped to 0-200. Do not use this to bypass a type_text refusal.
        """;

    public const string PressKey = """
        Press and release one key without parent-session SendInput when a background route exists. Optional element_index + window_id targets that element's native HWND; without it, native apps can reuse the last background-click child HWND.

        For Chromium, use CDP when configured; otherwise pass the same element_index used for type_text so the driver can establish internal focus before posting the key. If address/search submission is still blocked and CDP is configured for this exact browser window, use browser_eval for user-gesture navigation instead of launching or shelling out to the browser.

        Keys: enter/return, tab, escape/esc, arrows, space, backspace, delete, home, end, pageup, pagedown, f1-f24, letters, digits. modifiers can hold ctrl, shift, alt/option, or win/cmd. Use hotkey for combinations like ctrl+c.
        """;

    public const string Hotkey = """
        Press a modifier combination without parent-session SendInput. Prefer keys, for example ["ctrl", "c"]; key plus modifiers is accepted. Optional element_index + window_id targets that element's native HWND, otherwise native apps can reuse the last background-click child HWND.

        Chromium uses CDP when available, else the same browser target and posted-key chain as press_key. Modifiers: ctrl/control, shift, alt/option, win/cmd/meta. Non-modifier keys use press_key vocabulary. Do not use hotkeys to compensate for a missing safe text/click route.
        """;

    public const string LaunchApp = """
        Launch an app only when a safe lane is available. Parent-session Windows launches can foreground the target, so this tool refuses them by default. Do not pass unsafe_allow_foreground for routine automation; when explicitly set, the receipt remains unsafe and the driver tries to restore foreground.

        Prefer list_windows to reuse a running app. For Chromium, do not create a separate --remote-debugging-port profile unless the user asked for a CDP-controlled window; reuse an existing browser, a configured CDP port for that exact window, or child-session/AppBroadcast. Provide path, exe/name, or app_id.
        """;

    public const string ListApps = """
        List installed and running Windows apps with process identity when available.

        Use query to search installed apps. Default output is compact: running apps plus a small installed sample. Pass verbose=true only when every matched app and expanded fields are needed. For window-level reasoning and window_id, call list_windows.
        """;

    public const string ListWindows = """
        List top-level Windows HWNDs with owning pid/app, title, class name, bounds, visibility, minimized state, z-order, and DPI.

        Use this, not list_apps, for window-level reasoning: choosing window_id for get_window_state, deciding a pid's main window, checking visible/minimized state, or comparing stacking order. Pass pid to restrict one process. on_screen_only=false is useful because background/minimized windows can still be addressable. Text and structured windows are compact by default; pass verbose=true only when every window and expanded fields are needed.
        """;

    public const string Screenshot = """
        Capture native MCP image content. Without window_id, captures the virtual desktop; with window_id, captures that target and can validate pid. format is png or jpeg; quality is JPEG 1-95.

        This does not walk UIA or populate element_index cache. Prefer get_window_state for normal interaction; use screenshot for pixels only, full-desktop overview, or writing an image file with out.
        """;

    public const string Zoom = """
        Zoom a rectangular region of a prior window screenshot at native resolution. Use for small text, icons, or detail checks after get_window_state returned a resized image.

        x1/y1/x2/y2 use get_window_state screenshot pixels. Pass window_id when pid has multiple windows. The tool maps through the stored resize ratio, adds 20 percent padding, and stores a zoom context. To click the zoom image, call click with from_zoom=true and zoom-image x/y. Requires earlier get_window_state(pid, window_id).
        """;

    public const string SetValue = """
        Directly set a UIA element value. Use element_index + window_id from get_window_state for settable ValuePattern or RangeValuePattern controls such as sliders, steppers, and native editable combo boxes.

        For Chromium/Electron address, search, and page text fields, prefer type_text with element_index, then press_key enter/return if submission is needed. Browser ValuePattern setters are often exposed but not background-safe, so set_value may return use_type_text_for_browser_text instead of attempting an unsafe UIA setter.

        allow_transient_foreground defaults true for the existing-window fallback; treat receipts with background_safe=false, foreground_changed=true, or cursor_moved=true as transient foreground routes even when ok=true.

        For free-form text entry where cursor position matters, prefer type_text. set_value replaces or sets the element value directly.
        """;
}
