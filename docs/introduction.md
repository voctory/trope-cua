# What is Trope CUA?

Trope CUA is a native computer-use driver for agent harnesses. It lets an agent inspect and operate real desktop applications while protecting the user's active session from accidental cursor movement, focus steals, and blind keyboard delivery.

On Windows, Trope CUA installs as `trope-cua.exe`. On macOS, it installs as `trope-cua`.

```powershell
trope-cua list_windows
trope-cua get_window_state '{"pid":1234,"window_id":456789}'
trope-cua click '{"pid":1234,"window_id":456789,"element_index":14}'
```

You can run it three ways:

- `trope-cua mcp` as an MCP stdio server.
- `trope-cua serve` as a long-running daemon with persistent element caches.
- `trope-cua <tool> '{...}'` as a direct CLI tool call.

## Background-safety contract

Trope CUA is designed around an explicit receipt contract. A mutating action is safe for background automation only when the returned receipt says all three are true:

```json
{
  "background_safe": true,
  "cursor_moved": false,
  "foreground_changed": false
}
```

`ok=true` means the route completed. It does not by itself mean the route preserved the user's foreground work. Unsafe or fallback routes report that directly instead of hiding the risk.

The default same-session lane follows these rules:

- Do not move the user's real cursor.
- Do not steal foreground focus.
- Do not use parent-session `SendInput`.
- Do not report blind browser or hardware-style input as delivered.

If a target requires a route that cannot satisfy those rules, the tool returns a structured refusal such as `requires_cdp_or_child_session`, `requires_background_launch_lane`, or `requires_child_session_or_appbroadcast`.

## Three capture modes

`capture_mode` controls what `get_window_state` returns:

- `som` (default): screenshot plus UIA tree. Use this for normal agent loops because it gives both visual grounding and stable `element_index` actions.
- `ax`: UIA tree only. Use this for deterministic structured automation when pixels are not needed.
- `vision`: screenshot only. Use this for vision-first models or custom surfaces where the UIA tree is not useful.

Set the mode persistently:

```powershell
trope-cua set_config '{"key":"capture_mode","value":"som"}'
```

## How it works

Trope CUA chooses the safest route available for the target:

- UIA/MSAA on Windows and AX on macOS for accessible controls. `get_window_state` walks the tree, tags actionable nodes with `element_index`, and stores that cache in the current MCP or daemon process.
- Targeted `HWND` messages for classic controls, text entry, keys, and scrolling when a background-safe child window target exists.
- Chromium DevTools Protocol for Chromium and Electron windows when a matching `cdp_port` or configured `chromium_debugging_port` is available.
- Child-session or AppBroadcast lanes for hard cases that require hardware-style input.

The visual agent cursor is a click-through overlay. It shows agent intent and can use distinct colors for parallel agent sessions, but it is not the user's hardware cursor.

## Who it is for

- Agent harnesses that need to control native desktop apps through MCP or CLI tools.
- Local development loops where the user keeps working while an agent inspects, clicks, types, and verifies a target app.
- Trajectory collection and replay where the visible cursor should be an overlay, not the user's actual pointer.
- Experiments with isolated hard-case lanes such as child sessions and AppBroadcast.

## What it is not

- It is not a VM or sandbox by itself. Same-session tools operate on the real user desktop.
- It is not a parent-session hardware-input driver. Parent-session `SendInput` is intentionally not the normal lane.
- It is not a signed Microsoft-provisioned AppBroadcast package. Restricted AppBroadcast/InputInjector support is documented as an integration seam.
- It does not guarantee background-safe control for raw-input-only surfaces such as games, DirectX, Unity, Unreal, and many canvas-heavy apps. Use an isolated child-session lane for those.

Next: [install Trope CUA](installation.md), then run the [quickstart](quickstart.md).
