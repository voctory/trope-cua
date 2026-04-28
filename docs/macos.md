# macOS Operations

The macOS driver is a native Swift package under `native/macos/trope-cua`. It installs an app bundle plus a CLI symlink:

```text
/Applications/TropeCUA.app
~/.local/bin/trope-cua
```

## Permissions

macOS requires Accessibility and Screen Recording grants for `TropeCUA.app`. Start the app once through LaunchServices, then check permissions:

```bash
open -n -g -a TropeCUA --args serve
trope-cua check_permissions
```

Enable `TropeCUA.app` in System Settings > Privacy & Security > Accessibility and Screen Recording. Rerun `trope-cua check_permissions` until both are granted.

Local source builds may be ad-hoc signed. If the app's signature hash changes after a rebuild, macOS can ask for the same grants again.

## Daemon

The daemon keeps AX element indexes, window state, recording state, and cursor palette state alive between calls:

```bash
trope-cua serve
trope-cua status
trope-cua stop
```

It listens on a Unix domain socket under `~/Library/Caches/trope-cua`. Use MCP or the daemon for any multi-step workflow that reuses `element_index` values.

## Browser Navigation

Do not use shell `open`, AppleScript `activate`, Dock clicks, or Command-L as a background automation shortcut. Those are activation paths on macOS.

For browser URL navigation, prefer `launch_app` with a bundle id and URL list:

```bash
trope-cua launch_app '{"bundle_id":"com.google.Chrome","urls":["https://example.com"]}'
```

Then call `list_windows` and `get_window_state` for the browser window you intend to drive.

## Recording

Recording is process-local and works best with a running daemon:

```bash
trope-cua recording start ~/trope-cua-runs/demo
trope-cua recording status
trope-cua recording stop
```

The lower-level MCP tools are `set_recording`, `get_recording_state`, and `replay_trajectory`.

## Cursor Palettes

Parallel MCP and daemon sessions claim distinct cursor palettes automatically. The first live cursor is `default_blue`; later cursors rotate through the alternate palettes. To force a palette on macOS:

```bash
TROPE_CUA_CURSOR_PALETTE=soft_purple trope-cua mcp
```

The colored cursor is a visual overlay only. It does not isolate browser profiles, app state, or global target resources.
