# Child-session / PiP lane

The child-session lane is the production answer for surfaces that cannot be controlled by UIA, CDP, or classic `HWND` messages.

Shape:

```text
parent session broker
  -> enable Windows child sessions
  -> host RDP ActiveX with ConnectToChildSession=true
  -> start cua-driver-win-child.exe inside the child session
  -> communicate over named pipe / websocket / virtual channel

child session agent
  -> UIA/WGC/CDP as usual
  -> hardware-style SendInput allowed inside child only
  -> parent cursor, foreground app, and desktop remain untouched
```

The included `ChildSessionBroker` exposes the Windows Terminal Services calls needed for the broker layer. A full UI host still needs an RDP ActiveX container or equivalent PiP wrapper.

Current implementation:

- `child_session_status` reports parent/console session ids, whether child sessions are enabled, host state, and connected child session id.
- `child_session_start` enables child sessions, starts a no-activate RDP ActiveX host, sets `IMsRdpExtendedSettings.Property("ConnectToChildSession") = true`, and waits for `WTSGetChildSessionId`.
- `child_session_stop` closes the host.

Observed setup requirement:

- On this machine, `WTSEnableChildSessions(TRUE)` returns `Access is denied` from a non-elevated token. The normal background daemon should remain non-elevated, but child sessions must be enabled once from an elevated process before the no-activate host can connect.
