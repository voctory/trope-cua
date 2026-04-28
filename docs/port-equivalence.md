# Routing notes

## Windows

The Windows driver uses semantic UIA actions first and pixel routes second:

1. Element-indexed actions are UIA pattern calls, not synthetic mouse input.
2. Pixel actions first run a UIA hit-test at the screenshot coordinate. Accessible buttons/links are activated semantically, and text targets are remembered for later `type_text`.
3. Pixel actions try browser-native CDP when a Chromium debugging port is supplied.
4. Pixel actions then use targeted `HWND` messages for classic native controls.
5. Browser web content without UIA/CDP returns an explicit failure instead of claiming a blind `PostMessage` click landed.
6. Parent-session hardware input is refused by default.
7. Parent-session launches are refused by default; generic Windows launch APIs can foreground the target. Callers must use a child-session/AppBroadcast lane, reuse an existing window, or explicitly pass the last-resort `unsafe_allow_foreground=true` escape hatch, which remains unsafe and attempts to restore the previous foreground while pushing launched windows behind the current stack.
8. Raw-input-only targets are escalated to the child-session lane.

## macOS

The macOS driver uses AX semantics first and only falls back to lower-level event paths when the target and route can preserve the user's active session:

1. Element-indexed actions come from a fresh AX snapshot.
2. Browser navigation should use `launch_app` with `bundle_id` and `urls` instead of omnibox focus shortcuts.
3. `trope-cua serve` relaunches through `TropeCUA.app` when needed so TCC attributes permissions to the app bundle.
4. `open`, AppleScript `activate`, Dock clicks, and focus shortcuts are not background-safe launch routes.
5. Recording, cursor overlay, and element caches are process-local; use MCP or the daemon for multi-step workflows.

This routing policy is deliberately strict. It avoids the common regressions: moving the user's cursor, changing the foreground window, switching desktops or Spaces, or typing into the wrong app.
