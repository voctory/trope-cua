---
name: trope-cua
description: Drive native Windows apps with Trope CUA via the cua-driver-win CLI or MCP server. Use when the user asks to operate, inspect, automate, or test a real Windows GUI app while preserving the user's foreground app and real cursor whenever possible.
---

# Trope CUA

Use Trope CUA for Windows GUI automation. It snapshots a target window, exposes a UI Automation/MSAA tree with `element_index` targets, and routes actions through background-safe lanes before any foreground-prone fallback.

## Ground Rules

- Prefer `element_index` actions from `get_window_state` over pixel coordinates.
- Do not move the user's real cursor unless the user explicitly asked for an unsafe local experiment.
- Do not launch a new browser/debug profile unless the user explicitly asked for it. Reuse existing windows, configured CDP for that exact window, or an isolated child-session/AppBroadcast lane.
- Treat receipts with `background_safe=false`, `foreground_changed=true`, or `cursor_moved=true` as visible/transient routes and report that fact.
- Re-snapshot after actions when correctness matters. A changed tree, value, window, or screenshot is the evidence that the action landed.

## CLI

The installed binary is normally:

```powershell
$env:LOCALAPPDATA\Programs\CuaDriverWin\cua-driver-win.exe
```

Common commands:

```powershell
cua-driver-win list_windows
cua-driver-win get_window_state '{"pid":1234,"window_id":456789}'
cua-driver-win click '{"pid":1234,"window_id":456789,"element_index":14}'
```

Use the daemon when actions need to share the same element cache:

```powershell
cua-driver-win serve --instance demo
cua-driver-win call --instance demo get_window_state '{"pid":1234,"window_id":456789}'
cua-driver-win call --instance demo click '{"pid":1234,"window_id":456789,"element_index":14}'
cua-driver-win daemon-stop --instance demo
```

## Canonical Loop

1. `list_apps` or `list_windows` to find the target process/window.
2. `get_window_state` with `pid` and `window_id`.
3. Act with `element_index` when available.
4. Re-run `get_window_state` to verify.

## Tool Choices

| Intent | Prefer |
|---|---|
| Enumerate apps/windows | `list_apps`, `list_windows` |
| Snapshot a window | `get_window_state` |
| Click a UIA/MSAA element | `click` with `element_index` |
| Fill text | `type_text` with `element_index` |
| Set atomic value | `set_value` |
| Key press / shortcut | `press_key`, `hotkey` |
| Scroll | `scroll` |
| Screenshot only | `screenshot` |
| Small text or icon detail | `zoom` |
| Record/replay | `set_recording`, `get_recording_state`, `replay_trajectory` |

## Browser Notes

Chromium/Electron can expose partial UIA trees and can contend under parallel automation. If text/click actions report a browser contention or stale target:

1. Retry the same action up to 3 times after refreshing `get_window_state`.
2. Use the same `pid`, `window_id`, and refreshed `element_index`.
3. Use `cdp_port` or configured `chromium_debugging_port` only when it belongs to that exact existing window.
4. If normal browser routes are unsafe or blocked, use child-session/AppBroadcast or report the blocker.

## Agent Cursor

The visual cursor is a click-through overlay. It shows intent but does not deliver input. Parallel sessions get distinct palettes by default; the first/default cursor remains blue.

Useful controls:

```powershell
cua-driver-win set_agent_cursor_enabled '{"enabled":true}'
cua-driver-win get_agent_cursor_state
cua-driver-win set_agent_cursor_motion '{"idle_hide_ms":3000}'
```

## Recording

Enable trajectory recording only when the user asks for it:

```powershell
cua-driver-win set_recording '{"enabled":true,"output_dir":"C:\\temp\\trope-run"}'
cua-driver-win get_recording_state
cua-driver-win set_recording '{"enabled":false}'
```

Replay a captured run:

```powershell
cua-driver-win replay_trajectory '{"dir":"C:\\temp\\trope-run"}'
```

## Diagnostics

```powershell
cua-driver-win check_permissions
cua-driver-win get_config
cua-driver-win daemon-list
cua-driver-win daemon-status --instance demo
```

If a call fails because an `element_index` is invalid or uncached, re-run `get_window_state` for the same `pid` and `window_id` and use the fresh index.
