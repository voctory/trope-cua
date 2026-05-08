# Safety

Trope CUA is a desktop automation driver for agents. It is not a sandbox.

Same-session tools operate on the real user desktop. Do not use Trope CUA for financial, medical, credential, destructive, or high-impact workflows without explicit human approval at the point of action.

## Background-Safe Means Receipt-Safe

Treat a mutating action as background-safe only when the returned receipt says:

```json
{
  "background_safe": true,
  "cursor_moved": false,
  "foreground_changed": false
}
```

`ok=true` means the tool completed its requested route. It does not by itself mean the route preserved the user's foreground work.

## Default Same-Session Rules

The default lane is designed to:

- Avoid moving the user's real cursor.
- Avoid stealing foreground focus.
- Avoid parent-session `SendInput`.
- Refuse blind browser or hardware-style input when delivery cannot be verified.
- Report unsafe fallbacks directly in the receipt.

If a target requires hardware-style input, use a child-session, AppBroadcast-style lane, VM, or other isolation boundary. Do not treat a colored visual cursor as process isolation.

## Agent Authority

Only give agents access to windows and authenticated apps you are willing for them to inspect and operate. Use explicit `(pid, window_id)` targets, refresh snapshots before acting, and read every action receipt before assuming the action was safe.

## Known Risk Areas

- Browser profiles shared by multiple agents.
- Authenticated business, admin, cloud, banking, or messaging apps.
- Apps that virtualize controls or expose incomplete accessibility trees.
- Games, DirectX, Unity, Unreal, raw-input, and canvas-heavy surfaces.
- Any workflow that can send messages, delete data, buy items, change permissions, or publish content.

See [Known limits](docs/limits.md) and [Threat model](docs/threat-model.md).
