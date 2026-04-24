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
