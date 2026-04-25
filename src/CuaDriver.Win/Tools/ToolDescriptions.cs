namespace CuaDriver.Win.Tools;

internal static class ToolDescriptions
{
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

        Exactly one of element_index or (x and y) must be provided. window_id is required for element_index and recommended for pixel clicks. action is only valid with element_index; count and modifier only affect the pixel route. For Chromium web content, provide cdp_port or configure chromium_debugging_port; blind browser PostMessage clicks are refused to avoid reporting an unsafe foreground-only route as success.
        """;

    public const string DoubleClick = """
        Double-click against a target pid. Two addressing modes:

        - element_index + window_id from the last get_window_state snapshot targets the cached UIA element. Prefer this when the tree exposes the target; it preserves the same element-indexed workflow as click.

        - x and y are window-local screenshot pixels, top-left origin, in the same pixel space as get_window_state returned. This routes through the same pixel engine as click(count=2). modifier/modifiers holds ctrl, shift, alt/option, or win/cmd during the gesture.

        Exactly one of element_index or (x and y) must be provided. pid is required. window_id is required for element_index and recommended for pixel double-clicks.
        """;

    public const string RightClick = """
        Right-click against a target pid. Two addressing modes:

        - element_index + window_id from the last get_window_state snapshot performs the semantic menu action on the cached element where possible. Prefer this path for UIA-addressable controls because it is background-safe and does not move the real cursor.

        - x and y are window-local screenshot pixels, top-left origin, in the same pixel space as get_window_state returned. Use this for non-UIA surfaces. modifier/modifiers holds ctrl, shift, alt/option, or win/cmd during the pixel right-click and forces the pixel route rather than a semantic UIA action.

        Exactly one of element_index or (x and y) must be provided. pid is required. window_id is required for element_index and recommended for pixel right-clicks. For browser web content, configure cdp_port when possible; unsafe browser fallback routes are refused instead of stealing focus.
        """;

    public const string Scroll = """
        Scroll a target window without parent-session SendInput. Prefer the Mac-compatible direction mode: direction up/down/left/right, amount repetitions, and by line/page. This sends background-safe key messages such as Down, PageDown, or PageUp.

        Optional element_index + window_id from the last get_window_state snapshot targets that element's native HWND when available, falling back to the root target window. Skip it when a prior click already established the target's internal focus.

        Windows wheel mode remains available when direction is omitted: x/y identify the region to scroll in window-local screenshot pixels, delta controls wheel amount, and the target window center is used when x/y are omitted. Prefer direction mode for browser-like surfaces because wheel messages are commonly filtered or foreground-prone.

        Browser providers often expose partial UIA trees and foreground-prone routes. The Windows driver refuses unsafe browser scroll fallbacks unless a background-safe UIA or CDP route is available.
        """;

    public const string TypeText = """
        Insert text into a target pid/window. Use element_index + window_id from the last get_window_state snapshot when filling a specific field; this streams the visible value through IA2/UIA text setters by default. Without element_index, type_text targets the last UIA text target, then Chromium CDP Input.insertText when cdp_port is configured, then a native child HWND text route for classic controls.

        For Chromium or Electron inputs, first click the input or pass element_index so the driver has a text target. If the browser route has no safe target and no cdp_port, the tool refuses blind WM_CHAR delivery rather than typing into the wrong foreground app.

        delay_ms spaces streamed text chunks so autocomplete and reactive inputs can keep up. Default is 30 ms, matching the Mac type_text_chars pacing. Pass delay_ms=0 for the old instant/bulk behavior. For an explicitly atomic write, use set_value.

        Special keys such as Return, Escape, arrows, and shortcuts go through press_key or hotkey, not type_text.
        """;

    public const string TypeTextChars = """
        Compatibility surface for type_text. It accepts the same pid, window_id, element_index, text, and cdp_port arguments and routes through the same background-safe text insertion logic.

        delay_ms spaces character delivery, matching the Mac pacing knob. Default is 30 ms, clamped to 0-200. Pass delay_ms=0 when an instant/bulk write is explicitly desired.

        Use this name when a Mac-oriented caller expects type_text_chars for character-by-character entry; on Windows the implementation intentionally shares the safer type_text routing where possible.
        """;

    public const string PressKey = """
        Press and release a single key against a target pid/window without parent-session SendInput. The target does not need to be foreground if the target HWND accepts posted key messages.

        Optional element_index + window_id from the last get_window_state snapshot targets that element's native HWND when available, falling back to the root target window. This is the Windows equivalent of the Mac focus-then-key route while avoiding parent-session SendInput.

        Pass window_id when you know the target window; otherwise the driver's current main-window heuristic is used. Key vocabulary: enter/return, tab, escape/esc, arrows, space, backspace, delete, home, end, pageup, pagedown, f1-f24, plus any letter or digit. modifiers can hold ctrl, shift, alt/option, or win/cmd. For true key combinations such as ctrl+c, use hotkey.
        """;

    public const string Hotkey = """
        Press a modifier combination against a target pid/window without parent-session SendInput. Prefer the Mac-compatible keys array, for example ["ctrl", "c"]. The Windows-compatible key plus modifiers shape remains accepted.

        Recognized modifiers: ctrl/control, shift, alt/option, win/cmd/meta. Non-modifier keys use the same vocabulary as press_key. Pass window_id when available; otherwise the driver's current main-window heuristic is used.
        """;

    public const string LaunchApp = """
        Launch an app for background automation. By default the Windows port refuses parent-session foreground launch behavior; pass allow_foreground=true only when the user explicitly wants the app to appear or take focus.

        Provide path for an executable or name for an app/executable name. Use list_apps to discover installed apps and list_windows after launch to choose the target window_id for get_window_state. This mirrors the Mac workflow: launch or identify an app, enumerate windows, snapshot a specific (pid, window_id), then act by element_index whenever possible.
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

        Coordinates x1, y1, x2, y2 are in the same pixel space as the screenshot returned by get_window_state. The tool maps the region back through the stored resize ratio, captures at native resolution, adds 20 percent padding, and stores a zoom context. To click something in the returned zoom image, call click with from_zoom=true and x/y in the zoom image coordinate space.

        Requires get_window_state(pid, window_id) earlier in this session so the resize ratio is known.
        """;

    public const string SetValue = """
        Directly set a UIA element's value. Use element_index + window_id from the last get_window_state snapshot for controls that expose a settable ValuePattern or RangeValuePattern, such as text fields, sliders, steppers, and editable combo boxes.

        For free-form text entry where cursor position matters, prefer type_text. set_value replaces or sets the element value directly.
        """;
}
