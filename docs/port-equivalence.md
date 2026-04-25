# Routing equivalence notes

The Windows driver uses semantic UIA actions first and pixel routes second:

1. Element-indexed actions are UIA pattern calls, not synthetic mouse input.
2. Pixel actions first run a UIA hit-test at the screenshot coordinate. Accessible buttons/links are activated semantically, and text targets are remembered for later `type_text`.
3. Pixel actions try browser-native CDP when a Chromium debugging port is supplied.
4. Pixel actions then use targeted `HWND` messages for classic native controls.
5. Browser web content without UIA/CDP returns an explicit failure instead of claiming a blind `PostMessage` click landed.
6. Parent-session hardware input is refused by default.
7. Parent-session launches are refused by default; generic Windows launch APIs can foreground the target. Callers must pass `unsafe_allow_foreground=true` or use a child-session/AppBroadcast lane.
8. Raw-input-only targets are escalated to the child-session lane.

This is deliberately stricter than a naive Windows port. It avoids the common regressions: moving the user's cursor, changing the foreground window, switching desktops, or typing into the wrong app.
