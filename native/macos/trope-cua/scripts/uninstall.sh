#!/usr/bin/env bash
# trope-cua uninstaller. Removes everything install.sh laid down:
#
#   - ~/.local/bin/trope-cua symlink
#   - /Applications/TropeCUA.app bundle
#   - ~/.trope-cua/ (telemetry id + install marker)
#   - ~/Library/Application Support/Trope CUA/ (config.json)
#
# Does NOT revoke TCC grants (Accessibility + Screen Recording).
#
# Usage:
#   /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/voctory/trope-cua/main/native/macos/trope-cua/scripts/uninstall.sh)"
set -euo pipefail

USER_BIN_LINK="$HOME/.local/bin/trope-cua"
SYSTEM_BIN_LINK="/usr/local/bin/trope-cua"
APP_BUNDLE="/Applications/TropeCUA.app"
USER_DATA="$HOME/.trope-cua"
CONFIG_DIR="$HOME/Library/Application Support/Trope CUA"
log() { printf '==> %s\n' "$*"; }

# CLI symlinks. Try the user-bin first (no sudo), then the legacy
# /usr/local/bin path (needs sudo on default macOS).
for BIN_LINK in "$USER_BIN_LINK" "$SYSTEM_BIN_LINK"; do
    if [[ -L "$BIN_LINK" ]] || [[ -e "$BIN_LINK" ]]; then
        SUDO=""
        [[ ! -w "$(dirname "$BIN_LINK")" ]] && SUDO="sudo"
        $SUDO rm -f "$BIN_LINK"
        log "removed $BIN_LINK"
    fi
done

# .app bundle (in /Applications, usually writable by the user).
if [[ -d "$APP_BUNDLE" ]]; then
    SUDO=""
    if [[ ! -w "$(dirname "$APP_BUNDLE")" ]]; then
        SUDO="sudo"
    fi
    $SUDO rm -rf "$APP_BUNDLE"
    log "removed $APP_BUNDLE"
else
    log "no app bundle at $APP_BUNDLE (skipping)"
fi

# User-data directory (telemetry id + install marker).
if [[ -d "$USER_DATA" ]]; then
    rm -rf "$USER_DATA"
    log "removed $USER_DATA"
else
    log "no user data at $USER_DATA (skipping)"
fi

# Persisted config.
if [[ -d "$CONFIG_DIR" ]]; then
    rm -rf "$CONFIG_DIR"
    log "removed $CONFIG_DIR"
else
    log "no config at $CONFIG_DIR (skipping)"
fi

# Agent skill symlinks (Claude Code + Codex). Only remove when the link
# is ours — a dev user pointing the symlink at a working copy of the repo
# keeps theirs untouched.
SKILL_TARGET_EXPECTED="$APP_BUNDLE/Contents/Resources/Skills/trope-cua"
for SKILL_LINK in \
    "$HOME/.claude/skills/trope-cua" \
    "$HOME/.agents/skills/trope-cua" \
    "$HOME/.openclaw/skills/trope-cua" \
    "$HOME/.config/opencode/skills/trope-cua"; do
    if [[ -L "$SKILL_LINK" ]] && [[ "$(readlink "$SKILL_LINK")" == "$SKILL_TARGET_EXPECTED" ]]; then
        rm -f "$SKILL_LINK"
        log "removed $SKILL_LINK"
    else
        log "no install-created skill symlink at $SKILL_LINK (skipping)"
    fi
done

cat << 'FINALUNMSG'

trope-cua uninstalled.

TCC grants (Accessibility + Screen Recording) remain in System
Settings > Privacy & Security. Reset them explicitly if you want a
clean re-install flow:

  tccutil reset Accessibility com.tropecua.driver
  tccutil reset ScreenCapture com.tropecua.driver
FINALUNMSG
