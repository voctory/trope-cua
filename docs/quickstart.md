# Quickstart

This quickstart uses Windows Notepad to get one agent-controlled text entry into an existing desktop app. It uses the daemon so `element_index` values from the window snapshot remain valid for the follow-up action.

## 1. Install

From the repository root on Windows:

```powershell
.\scripts\install-windows.ps1 -SelfContained
```

Verify the command:

```powershell
trope-cua --help
trope-cua check_permissions
```

For macOS setup, install with `./scripts/install-macos.sh`, start the app once with `open -n -g -a TropeCUA --args serve`, then run `trope-cua check_permissions` and grant Accessibility and Screen Recording permissions for `TropeCUA.app`. The command pattern is the same, but use a macOS target app such as TextEdit. See [Installation](installation.md) and [macOS operations](macos.md) for platform setup.

## 2. Open A Target App

Open Notepad yourself and leave it at least partly visible. You can keep another app in the foreground while Trope CUA targets the Notepad window.

Trope CUA prefers reusing an existing window because launching a new app can foreground it.

## 3. Start The Daemon

In terminal 1:

```powershell
trope-cua serve --instance quickstart
```

Leave this process running.

## 4. Find Notepad

In terminal 2:

```powershell
trope-cua call --instance quickstart list_windows '{}'
```

Find the Notepad line and copy its `pid` and `window_id`:

```text
- Notepad pid=1234 window_id=567890 title="Untitled - Notepad"
```

In the commands below, replace `1234` and `567890` with your values.

## 5. Snapshot The Text Area

Ask for an accessibility-only snapshot filtered to likely text controls:

```powershell
trope-cua call --instance quickstart get_window_state '{"pid":1234,"window_id":567890,"capture_mode":"ax","query":"text"}'
```

Look for an editable control in the output and copy its `element_index`. It appears as `[eN]`, for example:

```text
[e14] document "Text Editor"
```

If the filtered snapshot does not show an editable element, rerun without `query`:

```powershell
trope-cua call --instance quickstart get_window_state '{"pid":1234,"window_id":567890,"capture_mode":"ax"}'
```

## 6. Type Text

Replace `14` with the `element_index` from your snapshot:

```powershell
trope-cua call --instance quickstart type_text '{"pid":1234,"window_id":567890,"element_index":14,"text":"hello from Trope CUA"}'
```

Read the receipt. Treat the action as background-safe only when:

```json
{
  "background_safe": true,
  "cursor_moved": false,
  "foreground_changed": false
}
```

`ok=true` means the route completed. It does not by itself mean the action preserved the user's foreground work.

## 7. Stop The Daemon

```powershell
trope-cua stop --instance quickstart
```

## Next Steps

- Register an MCP client with `trope-cua mcp-config`.
- Read [Workflows](workflows.md) for MCP, browser, visual cursor, and recording workflows.
- Read [Known limits](limits.md) before targeting authenticated apps or browser profiles.
