# Agent routing guide

The driver should present itself to agents as a background-safe native automation surface.

Default loop:

1. Call `list_windows` and choose an explicit `pid` and `window_id`.
2. Call `get_window_state` for that exact window before element-indexed actions.
3. Prefer `element_index` actions. Use pixels only for canvas, custom, or non-accessibility surfaces.
4. Read the action receipt. Treat the action as background-safe only when `background_safe=true`, `cursor_moved=false`, and `foreground_changed=false`.
5. If a tool returns a route such as `requires_cdp_or_child_session`, `requires_child_session_or_appbroadcast`, or `requires_background_launch_lane`, switch lanes or report the blocker. Do not replace it with parent-session mouse or keyboard input.

Unsafe flags are explicit human opt-ins:

- `unsafe_allow_foreground` is a last-resort parent-session launch escape hatch and is not for routine automation. Its receipt remains unsafe; the driver attempts to restore the previous foreground window and push launched windows behind the current stack.
- `allow_parent_sendinput` is a local experiment escape hatch, not a production input lane.
- `allow_parent_cursor` moves the real parent-session cursor and should not be used for background automation.

Browser rules:

- Use UIA/MSAA element actions when they are available and the receipt is background-safe.
- Use `cdp_port` or configured `chromium_debugging_port` for Chromium/Electron surfaces when UIA/MSAA cannot safely act.
- Refusals are intentional. They prevent the agent from stealing focus or typing into the user's foreground app.

macOS browser rules:

- Use `launch_app` with `bundle_id` and `urls` to navigate Chrome-style browsers without an omnibox focus steal.
- Do not use shell `open`, AppleScript `activate`, or browser focus shortcuts such as Command-L unless the user explicitly asked for frontmost behavior.
- For Safari and JavaScript-from-Apple-Events workflows, expect a separate browser setting and possible confirmation dialogs. Prefer normal accessibility actions unless the user explicitly asks for script execution.

Cursor rules:

- The visual agent cursor is an overlay that communicates intent.
- It must not be treated as the user's hardware cursor.
- Each daemon or MCP session owns its own overlay, so parallel agents should use separate `--instance` values for daemon workflows and separate MCP sessions for harness workflows.
- `move_cursor` is for display only unless a human explicitly authorizes `allow_parent_cursor`.
