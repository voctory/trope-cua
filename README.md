# cua-driver-win

Windows background computer-use driver that gives agents an AX/UIA tree, a target-window image, and cursor-safe actions without stealing the user's foreground work.

This source drop is organized around three computer-use modalities:

- `ax`: Windows UI Automation tree only.
- `vision`: target-window image only.
- `som`: UI Automation tree plus screenshot.

The Windows driver intentionally does **not** treat `SendInput` as the normal automation lane. The default lane is public, same-session, no-cursor/no-foreground automation using UIA patterns, targeted `HWND` messages for classic controls, and Chromium DevTools Protocol where available. Hardware-style input is only allowed inside an isolated child session or an explicitly opted-in parent-session escape hatch.

## What is included

- .NET 10 Windows console/MCP server project.
- Tool names for app/window discovery, target-window state, screenshots, zoom, clicks, text entry, keys, scrolling, value setting, permissions, config, a click-through visual agent cursor overlay, trajectory recording, and replay.
- UIA element-index snapshots with an in-memory `(pid, window_id) -> element_index -> AutomationElement` cache for MCP/daemon usage.
- Cursor/foreground no-regression guard around mutating actions.
- Pixel clicks try UIA hit-test first, so accessible links/buttons use semantic actions instead of blind mouse messages.
- `zoom` returns native-resolution crops from resized screenshots and stores crop context for `click(..., from_zoom=true)`.
- CDP lane for Chromium-page pixel clicks and `Runtime.evaluate(... userGesture: true)`.
- Classic Win32 targeted message fallback for native controls, typing, keys, and scroll; browser web content without UIA/CDP now returns an explicit failure instead of reporting unverified delivery.
- Visual agent cursor overlay drawn in a click-through, no-activate WinForms window. It does not move the user's hardware cursor.
- Trajectory recording writes `turn-NNNNN` folders with `action.json`, `app_state.json`, `screenshot.png`, and click markers, plus `replay_trajectory` for re-driving recorded action calls.
- `launch_app` refuses parent-session launches by default because Windows ShellExecute/CreateProcess can foreground the target. Use `unsafe_allow_foreground=true` only when that is intentional, or run launches in the child-session/AppBroadcast lane.
- GDI/PrintWindow screenshot fallback plus a WGC integration seam. Production WGC capture is documented in `docs/capture.md` because it needs Windows-only WinRT/D3D plumbing and validation on the target OS.
- The named-pipe daemon is single-instance per daemon instance id, not globally single-instance. Use `serve --instance <id>` and `call --instance <id>` when running isolated parallel background agents.
- Child-session broker scaffolding for the hard-case lane.
- AppBroadcast/InputInjector lane documentation and disabled source hook for Microsoft-provisioned builds.

## Build

On Windows 10 1903+ or Windows 11 with the .NET 10 SDK or newer:

```powershell
cd cua-driver-win
.\scripts\build.ps1
```

The build script defaults to the current Windows architecture, for example `win-arm64` on Windows on ARM. Use `-SelfContained` when the published executable should not depend on the user's `DOTNET_ROOT` or installed runtime layout. The install script defaults to self-contained output for daemon reliability; pass `-FrameworkDependent` only when the target runtime layout is intentionally managed outside the repo.

The published executable is written to:

```text
artifacts\publish\cua-driver-win.exe
```

## Use from shell

```powershell
cua-driver-win list_windows
cua-driver-win get_window_state '{"pid":1234,"window_id":123456}'
cua-driver-win click '{"pid":1234,"window_id":123456,"element_index":14}'
```

For persistent element-index caches across shell calls, start the daemon first:

```powershell
cua-driver-win serve
# in another terminal
cua-driver-win call get_window_state '{"pid":1234,"window_id":123456}'
cua-driver-win call click '{"pid":1234,"window_id":123456,"element_index":14}'
```

For isolated parallel agents, give each daemon its own instance id:

```powershell
cua-driver-win serve --instance agent-a
cua-driver-win call --instance agent-a get_window_state '{"pid":1234,"window_id":123456}'
```

Set `CUA_DRIVER_CONFIG_DIR` when an agent or test run should use an isolated config and daemon registry directory instead of `%LOCALAPPDATA%\cua-driver-win`.

## Use as MCP

Install a self-contained user-level build:

```powershell
.\scripts\install.ps1
```

Then register it with Codex:

```powershell
codex mcp add cua-driver-win -- "$env:LOCALAPPDATA\Programs\CuaDriverWin\cua-driver-win.exe" mcp
```

```json
{
  "mcpServers": {
    "cua-driver-win": {
      "command": "C:\\Users\\YOU\\AppData\\Local\\Programs\\CuaDriverWin\\cua-driver-win.exe",
      "args": ["mcp"]
    }
  }
}
```

## Safety contract

Every mutating action returns a receipt that names the actual route and lane:

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

The default build refuses to use parent-session `SendInput`. If a target requires hardware-style input, the action returns `requires_child_session_or_appbroadcast` instead of silently stealing the user's cursor/focus.

Agent routing guidance is maintained in `docs/agent-routing.md`. The MCP server also returns the same core rules in its initialize instructions so clients can steer tool selection toward background-safe routes.

Refactoring guidance is maintained in `docs/refactoring-rubric.md`. It captures the staged build, analyzer, packaging, testing, and module-boundary policy for reshaping this driver without weakening the background-safety contract.

## Important limitations in this source drop

This is a source package, not a signed binary. The AppBroadcast/InputInjector lane requires Microsoft restricted capabilities and is therefore present as a documented integration seam rather than a generally buildable default. The child-session lane includes the Windows Terminal Services broker scaffolding, but packaging a full Picture-in-Picture host requires an RDP ActiveX UI host or equivalent wrapper.

For UIA-addressable apps and Chromium pages with CDP access, the default same-session lane is the intended Windows equivalent. For canvas, raw-input, DirectX/Unity/Unreal, and other hardware-input-only surfaces, run the target plus this driver inside the child-session lane.

## License and attribution

This project is released under the MIT License. See [LICENSE](LICENSE).

This Windows driver was informed by the Cua Driver Mac implementation in [trycua/cua](https://github.com/trycua/cua), but does not include upstream source code from that repository. See [NOTICE](NOTICE). This project is not affiliated with or endorsed by Cua AI, Inc.
