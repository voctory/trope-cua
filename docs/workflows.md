# Workflows

This guide covers common Trope CUA workflows after the quickstart: MCP registration, browser targets, visual cursor controls, and trajectory recording.

For first-time setup, start with the [quickstart](quickstart.md).

## Agent Loop

Normal loop:

1. `list_windows`
2. `get_window_state` for one explicit `(pid, window_id)`
3. `click`, `type_text`, `press_key`, `scroll`, or `set_value` using `element_index` where possible
4. Inspect the receipt before assuming the action was background-safe
5. Refresh `get_window_state` after navigation, animation, failed element actions, or major UI changes

Treat a mutating action as background-safe only when:

```json
{
  "background_safe": true,
  "cursor_moved": false,
  "foreground_changed": false
}
```

`ok=true` means the requested route completed. It does not by itself mean the route preserved the user's foreground work.

## Direct CLI Calls

Direct CLI calls work well for stateless read-only checks:

```powershell
trope-cua list_windows
trope-cua get_config
trope-cua check_permissions
```

Use MCP or the daemon when an action depends on an `element_index` from `get_window_state`. Element indexes are cached in the running process that created the snapshot.

## Daemon Workflows

Start a persistent daemon:

```powershell
trope-cua serve --instance demo
```

Use `call --instance` from another terminal:

```powershell
trope-cua call --instance demo list_windows '{}'
trope-cua call --instance demo get_window_state '{"pid":1234,"window_id":456789}'
trope-cua call --instance demo click '{"pid":1234,"window_id":456789,"element_index":14}'
```

The daemon keeps the UIA or AX element cache alive between calls.

For parallel daemon-based agents, give each daemon an instance id:

```powershell
trope-cua serve --instance agent-a
trope-cua serve --instance agent-b

trope-cua call --instance agent-a list_windows '{}'
trope-cua call --instance agent-b list_windows '{}'
```

Parallel sessions automatically get different visual cursor colors. Cursor color only distinguishes intent visually; it does not isolate browser profiles or shared app state.

## MCP Clients

Register `trope-cua.exe mcp` on Windows or `trope-cua mcp` on macOS with your MCP client.

Print a generic MCP config:

```powershell
trope-cua mcp-config
```

Print a client-specific config when supported:

```powershell
trope-cua mcp-config --client cursor
```

The MCP client starts the stdio process and calls tools directly. Use the same loop: list windows, snapshot one explicit target, act by `element_index`, and inspect receipts.

## Browser Targets

For Chromium or Electron targets:

- Use `element_index` for address bars, search fields, buttons, and links when UIA exposes them.
- Use `cdp_port` or `chromium_debugging_port` when the exact target browser was launched with remote debugging and CDP is the intended lane.
- If Chromium fallback routes report contention, retry the same action up to 3 times. Persistent contention means multiple agents are driving the same browser process; use separate browser profiles with separate CDP ports for true parallel browser work.

On macOS, prefer `launch_app` for URL navigation instead of Command-L or shell `open`:

```bash
trope-cua launch_app '{"bundle_id":"com.google.Chrome","urls":["https://example.com"]}'
```

Set a persistent Chromium CDP port only after launching the target Chromium or Electron app with remote debugging on the same port:

```powershell
trope-cua set_config '{"key":"chromium_debugging_port","value":9222}'
```

Setting this config does not add CDP to an already-running browser. Only use a port that belongs to the same browser window you intend to drive.

## Visual Cursor

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

## Record And Replay Trajectories

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
