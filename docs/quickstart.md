# Quickstart

This quickstart drives an existing desktop app without treating the user's real cursor or foreground app as the automation lane.

On macOS, grant permissions before the first real automation loop:

```bash
open -n -g -a TropeCUA --args serve
trope-cua check_permissions
```

Allow `TropeCUA.app` in System Settings > Privacy & Security > Accessibility and Screen Recording, then rerun `trope-cua check_permissions` until both are granted.

## 1. List windows

Start by identifying a target `pid` and `window_id`:

```powershell
trope-cua list_windows
```

Pick a visible target window from the output. Most action tools accept both `pid` and `window_id`; pass both whenever you have them.

## 2. Snapshot the target

Call `get_window_state` for that exact window:

```powershell
trope-cua get_window_state '{"pid":1234,"window_id":456789}'
```

The default `som` mode returns a screenshot and a UIA tree. Actionable controls are tagged with `element_index` values.

Element indexes are cached in the current process. If you use one-shot CLI commands, the cache disappears after each command. Use MCP or the daemon for element-indexed workflows.

## 3. Act by element index

Prefer element-indexed actions over approximate pixels:

```powershell
trope-cua click '{"pid":1234,"window_id":456789,"element_index":14}'
```

For text fields:

```powershell
trope-cua type_text '{"pid":1234,"window_id":456789,"element_index":16,"text":"hello from Trope CUA"}'
trope-cua press_key '{"pid":1234,"window_id":456789,"element_index":16,"key":"enter"}'
```

Read every action receipt. Treat the action as background-safe only when:

```json
{
  "background_safe": true,
  "cursor_moved": false,
  "foreground_changed": false
}
```

## 4. Use the daemon for shell workflows

Start a persistent daemon:

```powershell
trope-cua serve --instance demo
```

Use `call --instance` from another terminal:

```powershell
trope-cua call --instance demo get_window_state '{"pid":1234,"window_id":456789}'
trope-cua call --instance demo click '{"pid":1234,"window_id":456789,"element_index":14}'
```

The daemon keeps the UIA element cache alive between calls.

## 5. Use MCP for agent harnesses

Register `trope-cua.exe mcp` on Windows or `trope-cua mcp` on macOS with your MCP client. The harness will start the stdio process and call tools directly.

Normal agent loop:

1. `list_windows`
2. `get_window_state` for one explicit `(pid, window_id)`
3. `click`, `type_text`, `press_key`, `scroll`, or `set_value` using `element_index` where possible
4. Inspect the receipt before assuming the action was background-safe
5. Refresh `get_window_state` after navigation, animation, failed element actions, or major UI changes

Parallel MCP sessions automatically get different visual cursor colors. This only changes the overlay; it does not isolate browser profiles or make shared target apps concurrency-safe by itself.

## 6. Browser pages

For Chromium or Electron targets:

- Use `element_index` for address bars, search fields, buttons, and links when UIA exposes them.
- Use `cdp_port` or `chromium_debugging_port` when the exact target browser was launched with remote debugging and CDP is the intended lane.
- If Chromium fallback routes report contention, retry the same action up to 3 times. Persistent contention means multiple agents are driving the same browser process; use separate browser profiles with separate CDP ports for true parallel browser work.

On macOS, prefer `launch_app` for URL navigation instead of Command-L or shell `open`:

```bash
trope-cua launch_app '{"bundle_id":"com.google.Chrome","urls":["https://example.com"]}'
```

Set a persistent CDP port:

```powershell
trope-cua set_config '{"key":"chromium_debugging_port","value":9222}'
```

Only use a port that belongs to the same browser window you intend to drive.

## 7. Visual cursor

The visual cursor is a click-through overlay. It shows where the agent intends to act:

```powershell
trope-cua move_cursor '{"x":400,"y":300}'
trope-cua get_agent_cursor_state
```

It fades after `agent_cursor.motion.idle_hide_ms`. Tune motion:

```powershell
trope-cua set_agent_cursor_motion '{"idle_hide_ms":20000,"glide_duration_ms":160}'
```

Disable the overlay:

```powershell
trope-cua set_agent_cursor_enabled '{"enabled":false}'
```

## 8. Record and replay trajectories

Turn on recording:

```powershell
trope-cua set_recording '{"enabled":true,"output_dir":"C:\\temp\\trope-cua-run"}'
```

On macOS with the daemon running:

```bash
trope-cua recording start ~/trope-cua-runs/demo
```

Subsequent mutating actions write turn folders with `action.json`, `app_state.json`, screenshots, and click markers when applicable.

Replay:

```powershell
trope-cua replay_trajectory '{"dir":"C:\\temp\\trope-cua-run"}'
```

Recording is useful for debugging route choices and building regression cases. It is not a video recorder.
