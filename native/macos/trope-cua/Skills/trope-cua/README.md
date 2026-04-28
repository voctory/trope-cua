# trope-cua — Claude Code skill

A [Claude Code](https://code.claude.com) skill that teaches Claude to
drive native macOS apps via the
[`trope-cua`](https://github.com/voctory/trope-cua)
CLI — snapshot an app's accessibility tree, click/type/scroll by
`element_index`, and verify via re-snapshot. Backgrounded-first: no
focus steal, no cursor warp, no Space follow.

## What the skill covers

- The snapshot-before-AND-after invariant that keeps the agent honest
  about whether an action actually landed.
- The backgrounded-click recipe (yabai focus-without-raise + stamped
  SLEventPostToPid) that lets synthetic clicks land on Chrome web
  content without raising the window or pulling the user across Spaces.
- Web-app quirks (`WEB_APPS.md`) — Chromium/WebKit/Electron/Tauri,
  including the minimized-Chrome keyboard-commit caveat and the
  `set_value` workaround.
- Trajectory recording (`RECORDING.md`) — optional per-session
  recording + replay for demos and regressions.
- Canvas/viewport apps (Blender, Unity, GHOST, Qt, wxWidgets) —
  HID-tap fallback when AX is empty.

See `SKILL.md` for the main body.

## Prerequisites

1. **macOS 14 or newer** — the driver depends on SkyLight private SPIs
   that were stabilized in Sonoma.
2. **`trope-cua` CLI + `TropeCUA.app`** — installable one-liner:
   ```bash
   /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/voctory/trope-cua/main/native/macos/trope-cua/scripts/install.sh)"
   ```
   Or from a clone of `voctory/trope-cua`:
   ```bash
   cd native/macos/trope-cua
   scripts/install-local.sh   # builds + installs + symlinks for dev use
   ```
   The driver runs as an `.app` bundle because macOS TCC grants are
   tied to a stable bundle id (`com.tropecua.driver`). The CLI symlink
   lets Claude invoke tools via plain shell.
3. **TCC grants on `TropeCUA.app`** — **Accessibility** and
   **Screen Recording** in System Settings → Privacy & Security.
   Verify with:
   ```bash
   trope-cua check_permissions
   ```
   Both fields must be `true`. If not, the app appears in the
   relevant panes of System Settings after first use; toggle it on
   there.

## Install

The skill is two drop-in directories.

**Personal scope** (all Claude Code sessions on your machine):

```bash
mkdir -p ~/.claude/skills
cp -R Skills/trope-cua ~/.claude/skills/
```

Or symlink if you want edits-in-place:

```bash
ln -s "$PWD/Skills/trope-cua" ~/.claude/skills/trope-cua
```

**Project scope** (committed alongside a specific repo):

```bash
mkdir -p .claude/skills
cp -R /path/to/trope-cua/native/macos/trope-cua/Skills/trope-cua .claude/skills/
```

## Invoking the skill

Claude Code auto-invokes the skill when you ask for macOS GUI
automation — e.g. "open the Downloads folder in Finder", "click the
Save button in Numbers", "navigate to trycua.com in Chrome". You can
also invoke it explicitly:

```
/trope-cua
```

## Files

- `SKILL.md` — the main skill body (~500 lines). Loaded on first
  invocation; stays in context for the session.
- `WEB_APPS.md` — browsers, Electron, Tauri (Chromium + WebKit). Loaded
  on demand when SKILL.md's pointer is followed.
- `RECORDING.md` — trajectory recording / replay. Loaded on demand.
- `TESTS.md` — manual test scripts for end-to-end skill verification.

## Troubleshooting

- `trope-cua: command not found` → re-run the installer or add
  `.build/TropeCUA.app/Contents/MacOS/` to `$PATH`.
- `No cached AX state for pid X window_id W` → element_index was
  reused across turns, or across different windows of the same app.
  Call `get_window_state({pid, window_id})` first in the same turn,
  with the same window_id you're about to act against.
- Empty `tree_markdown` → `capture_mode` is set to `vision`, which
  skips the AX walk by design. Flip back to the default `som`
  (`trope-cua config set capture_mode som`) to get the tree.
  Tiny screenshot → likely a stale window capture. See "Behavior
  matrix" in SKILL.md for the full mode table.
- System-alert beep when pressing Return on a minimized Chrome
  omnibox → the keyboard-commit-on-minimized limitation. Use
  `set_value` on the field instead, or AX-click a Go/Submit button.
  See `WEB_APPS.md`.

## Updates

The skill evolves alongside the driver. To update:

```bash
cd /path/to/trope-cua && git pull
# if you copied: re-copy
cp -R native/macos/trope-cua/Skills/trope-cua ~/.claude/skills/
# if you symlinked: nothing needed
```

## License

MIT. Same license as the parent `voctory/trope-cua` repo.
