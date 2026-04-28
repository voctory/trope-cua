# Tools and command modes

Trope CUA exposes the same tool registry through direct CLI calls, MCP stdio, and the named-pipe daemon.

## Command modes

```powershell
cua-driver-win <tool> [json]
cua-driver-win mcp
cua-driver-win serve [--instance <id>]
cua-driver-win call [--instance <id>] <tool> [json]
cua-driver-win daemon-status [--instance <id>]
cua-driver-win daemon-list
cua-driver-win daemon-stop [--instance <id>|--all]
```

Use direct CLI calls for stateless read-only checks and simple actions. Use MCP or the daemon when you need element indexes from `get_window_state` to remain valid across subsequent actions.

## Core action loop

1. Choose a target with `list_windows`.
2. Snapshot it with `get_window_state`.
3. Prefer `element_index` actions.
4. Use pixels only for canvas, custom, or non-UIA surfaces.
5. Read the receipt and route. Do not infer background safety from `ok=true`.

## Receipt fields

Mutating tools return a structured action receipt:

```json
{
  "ok": true,
  "route": "uia.invoke",
  "lane": "same_session",
  "background_safe": true,
  "cursor_moved": false,
  "foreground_changed": false,
  "session": "parent"
}
```

Important fields:

- `route`: the actual dispatch route used.
- `lane`: same-session, child-session, AppBroadcast, or another explicit lane.
- `background_safe`: whether the route itself is considered safe for background automation.
- `cursor_moved`: whether the real Windows cursor moved.
- `foreground_changed`: whether foreground focus changed.
- `reason`: present on refusals and failures.

## Tool groups

### Discovery and state

| Tool | Purpose |
| --- | --- |
| `list_apps` | List installed and running apps. |
| `list_windows` | List top-level windows with pid, title, class, bounds, visibility, and z-order. |
| `get_window_state` | Return a screenshot and/or UIA tree with `element_index` tags. |
| `get_accessibility_tree` | Alias of `get_window_state` in `ax` mode for one call. |
| `screenshot` | Capture the virtual desktop or one target window. |
| `zoom` | Capture a native-resolution crop from the last target-window screenshot. |
| `get_screen_size` | Return virtual desktop bounds. |
| `get_cursor_position` | Return the real cursor position for diagnostics. |

### Input and values

| Tool | Purpose |
| --- | --- |
| `click` | Element-indexed or pixel-addressed left click. |
| `double_click` | Element-indexed or pixel-addressed double click. |
| `right_click` | Element-indexed or pixel-addressed right click. |
| `type_text` | Atomic text insertion through IA2/UIA/CDP/HWND routes. |
| `type_text_chars` | Compatibility wrapper for character-spaced text entry. |
| `press_key` | Single key press scoped to the target when possible. |
| `hotkey` | Modifier combination such as `["ctrl","c"]`. |
| `scroll` | Background-safe direction or wheel scroll where supported. |
| `set_value` | Direct UIA value/range setting for controls that expose safe setters. |

### Browser and hard-case lanes

| Tool | Purpose |
| --- | --- |
| `browser_eval` | Chromium CDP `Runtime.evaluate` with `userGesture=true`. |
| `child_session_start` | Start the no-activate child-session host probe. |
| `child_session_status` | Report child-session support and host state. |
| `child_session_stop` | Stop the child-session host. |
| `appbroadcast_input_probe` | Probe restricted AppBroadcast/InputInjector behavior. |

### Cursor, recording, and config

| Tool | Purpose |
| --- | --- |
| `move_cursor` | Move the visual overlay cursor without moving the real cursor. |
| `get_agent_cursor_state` | Return overlay visibility, palette, target, and motion state. |
| `set_agent_cursor_enabled` | Enable or disable the visual overlay. |
| `set_agent_cursor_motion` | Tune overlay glide, press, dwell, and idle-hide timing. |
| `set_recording` | Enable or disable trajectory recording. |
| `get_recording_state` | Return recorder state. |
| `replay_trajectory` | Replay recorded `action.json` calls in lexical order. |
| `get_config` | Return persistent config. |
| `set_config` | Set persistent config keys. |
| `check_permissions` | Report local automation and capture capability diagnostics. |

## Capture modes

Set with `set_config`:

```powershell
cua-driver-win set_config '{"key":"capture_mode","value":"ax"}'
```

Values:

- `som`: screenshot plus UIA tree.
- `ax`: UIA tree only.
- `vision`: screenshot only.

## Config keys

Supported keys:

- `capture_mode`
- `max_image_dimension`
- `chromium_debugging_port`
- `allow_parent_sendinput`
- `agent_cursor.enabled`
- `agent_cursor.motion.*`

Use `get_config` to inspect the current values.

## Parallel cursor palettes

Plain MCP sessions without an explicit instance id claim a runtime identity and a palette. The first live session uses `default_blue`; later sessions rotate through:

```text
soft_purple
rose_gold
mint_lime
amber
aqua
orchid
crimson
chartreuse
cobalt
```

Daemon sessions also claim palettes through the same registry. Cursor color only helps distinguish agents visually; it does not isolate target app state.
