# Threat Model

Trope CUA is designed to make desktop automation explicit and inspectable. It is not a sandbox, VM, policy engine, or permission manager.

## Can It Move My Real Cursor?

Default same-session mutating routes are expected not to move the user's hardware cursor. Every mutating action reports whether the real cursor moved:

```json
{
  "cursor_moved": false
}
```

If `cursor_moved=true`, do not treat the action as background-safe.

The visual agent cursor is a click-through overlay. It shows intent; it is not the user's hardware cursor and it is not isolation.

## Can It Steal Focus?

Some OS and app routes can activate or foreground a target. Trope CUA tracks this in action receipts:

```json
{
  "foreground_changed": false
}
```

If a route cannot preserve foreground focus, treat the action as unsafe unless the receipt explicitly reports `foreground_changed=false` and `background_safe=true`.

## Can An Agent Type Into The Wrong App?

It can if the harness gives it too much authority or ignores receipts. The intended workflow is:

1. Use `list_windows` to choose a specific target.
2. Snapshot the exact `(pid, window_id)` with `get_window_state`.
3. Use fresh `element_index` values from that snapshot.
4. Read the action receipt.
5. Refresh the snapshot after navigation, animation, or failed actions.

Avoid blind typing. Do not treat `ok=true` as proof that the route was safe.

## Can It Access Windows I Did Not Intend?

Trope CUA can enumerate local windows and inspect a window when a caller provides a valid target. The driver is not an authorization layer. Limit access at the agent harness, MCP client, OS account, session, VM, or child-session boundary when a workflow needs stronger isolation.

## What Happens When A Route Is Unsafe?

Unsafe routes refuse or report unsafe receipt fields. Common refusals include:

- `requires_background_launch_lane`
- `requires_cdp_or_child_session`
- `requires_cdp_or_uia_text_target`
- `requires_child_session`
- `requires_child_session_or_appbroadcast`

A refusal means the driver did not have a route it could report as background-safe for that target and action.

## Main Risks

- An agent with broad access can inspect sensitive windows.
- Shared browser profiles can leak state between agents.
- Authenticated apps can expose private or high-impact actions.
- Accessibility providers can behave differently across app versions.
- Raw-input and GPU-heavy apps may require hardware-style input that is not same-session safe.

Use Trope CUA with the same care you would use for any local automation tool that can inspect and operate your desktop.
