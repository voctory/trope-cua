# No-regression contract

Mutating tools must report the actual route used.

Required fields:

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

Default same-session rules:

- Do not call `SendInput`.
- Do not call `SetForegroundWindow`.
- Do not move the real cursor.
- Do not switch virtual desktops.
- Do not hide a hardware-input fallback behind a successful result.

If a route would need parent-session hardware input, return:

```json
{
  "ok": false,
  "route": "requires_child_session_or_appbroadcast",
  "lane": "same_session",
  "background_safe": false,
  "reason": "Target requires hardware-style input; refusing parent-session SendInput."
}
```

Escape hatches are explicit and opt-in:

- `allow_parent_sendinput=true` for local experiments only.
- Child-session lane for production hardware-style input.
- AppBroadcast/InputInjector lane only when provisioned.
