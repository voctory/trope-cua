#!/usr/bin/env bash
# trope-cua installer — download the latest signed + notarized tarball
# from GitHub Releases, move TropeCUA.app to /Applications, and symlink
# the `trope-cua` binary into ~/.local/bin so shell users can invoke
# it without typing the bundle path. Sudo-free.
#
# Usage (from README + release body):
#   /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/voctory/trope-cua/main/native/macos/trope-cua/scripts/install.sh)"
#
# Flags:
#   --bin-dir <path>     install the trope-cua wrapper to <path> instead of
#                        ~/.local/bin (e.g. /usr/local/bin — that target needs sudo)
#   --no-modify-path     skip auto-appending an `export PATH=...` line to your
#                        shell rc when ~/.local/bin is missing from PATH
#
# Env overrides:
#   TROPE_CUA_VERSION=0.1.0   pin a specific release tag
#   TROPE_CUA_BIN_DIR=PATH    same as --bin-dir
#   TROPE_CUA_NO_MODIFY_PATH=1  same as --no-modify-path
#
# Uninstall:
#   /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/voctory/trope-cua/main/native/macos/trope-cua/scripts/uninstall.sh)"
set -euo pipefail

REPO="voctory/trope-cua"
APP_NAME="TropeCUA.app"
BINARY_NAME="trope-cua"
TAG_PREFIX="trope-cua-v"
APP_DEST="/Applications/$APP_NAME"
BIN_DIR="${TROPE_CUA_BIN_DIR:-$HOME/.local/bin}"
NO_MODIFY_PATH="${TROPE_CUA_NO_MODIFY_PATH:-0}"

# Lightweight flag parsing (avoid getopt; macOS getopt is GNU-incompatible).
while [[ $# -gt 0 ]]; do
    case "$1" in
        --bin-dir) BIN_DIR="$2"; shift 2 ;;
        --bin-dir=*) BIN_DIR="${1#*=}"; shift ;;
        --no-modify-path) NO_MODIFY_PATH=1; shift ;;
        *) shift ;;
    esac
done

BIN_LINK="$BIN_DIR/$BINARY_NAME"
TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

log() { printf '==> %s\n' "$*"; }
err() { printf 'error: %s\n' "$*" >&2; }

# --- Sanity checks ------------------------------------------------------

if [[ "$(uname -s)" != "Darwin" ]]; then
    err "trope-cua is macOS-only; uname reports $(uname -s)"
    exit 1
fi

for cmd in curl tar; do
    if ! command -v "$cmd" >/dev/null 2>&1; then
        err "$cmd not found on PATH"
        exit 1
    fi
done

# --- Resolve release tag ------------------------------------------------

if [[ -n "${TROPE_CUA_VERSION:-}" ]]; then
    TAG="${TAG_PREFIX}${TROPE_CUA_VERSION#v}"
    log "using version from TROPE_CUA_VERSION: $TAG"
else
    log "resolving latest $TAG_PREFIX* release via GitHub API"
    TAG=$(curl -fsSL "https://api.github.com/repos/$REPO/releases?per_page=40" \
        | grep -Eo '"tag_name":[[:space:]]*"'"${TAG_PREFIX}"'[^"]+"' \
        | sed -E 's/.*"'"${TAG_PREFIX}"'([0-9]+[.][0-9]+[.][0-9]+)"/\1/' \
        | sort -t. -k1,1nr -k2,2nr -k3,3nr \
        | head -n 1 \
        | sed -E 's/^/'"${TAG_PREFIX}"'/')
    if [[ -z "$TAG" ]]; then
        err "no release matching ${TAG_PREFIX}* found on $REPO"
        exit 1
    fi
    log "latest release: $TAG"
fi

# --- Download tarball ---------------------------------------------------

ARCH=$(uname -m)
VERSION="${TAG#${TAG_PREFIX}}"
TARBALL="trope-cua-${VERSION}-darwin-${ARCH}.tar.gz"
URL="https://github.com/$REPO/releases/download/$TAG/$TARBALL"

log "downloading $URL"
if ! curl -fsSL -o "$TMP_DIR/$TARBALL" "$URL"; then
    err "download failed; try TROPE_CUA_VERSION=<version> to pin a specific release"
    exit 1
fi

log "extracting"
tar -xzf "$TMP_DIR/$TARBALL" -C "$TMP_DIR"

if [[ ! -d "$TMP_DIR/$APP_NAME" ]]; then
    err "$APP_NAME not found inside $TARBALL (tarball layout may have changed)"
    exit 1
fi

# --- Install .app bundle ------------------------------------------------

if [[ -e "$APP_DEST" ]]; then
    log "removing existing $APP_DEST"
    rm -rf "$APP_DEST"
fi

log "installing $APP_DEST"
ditto "$TMP_DIR/$APP_NAME" "$APP_DEST"

# --- Wrapper / symlink for CLI ------------------------------------------
#
# Default install location is ~/.local/bin/trope-cua — no sudo. Power
# users can override via --bin-dir or $TROPE_CUA_BIN_DIR. We refuse to
# write to root-owned dirs (e.g. /usr/local/bin) without an explicit opt-in.

APP_BINARY="$APP_DEST/Contents/MacOS/$BINARY_NAME"
if [[ ! -x "$APP_BINARY" ]]; then
    err "binary missing at $APP_BINARY (refusing to create broken symlink)"
    exit 1
fi

mkdir -p "$BIN_DIR"
if [[ ! -w "$BIN_DIR" ]]; then
    err "$BIN_DIR is not writable. Pick a user-writable --bin-dir, or pre-create the dir with sudo."
    exit 1
fi
ln -sf "$APP_BINARY" "$BIN_LINK"
log "symlinked $BIN_LINK -> $APP_BINARY"

# --- Install agent skill pack -------------------------------------------
#
# Drop a symlink for each detected agent that auto-loads Anthropic-format
# SKILL.md skills from a folder. Auto-updates atomically replace
# /Applications/TropeCUA.app so the symlinks stay valid across releases.
# We never overwrite an existing link or directory — dev users with a
# symlink pointing at a working copy of the repo keep theirs.
#
# Supported (folder-of-skills, frontmatter compatible):
#   - Claude Code: scans ~/.claude/skills/ on startup
#   - Codex     : scans ~/.agents/skills/ on startup
#   - OpenClaw  : scans ~/.openclaw/skills/
#   - OpenCode  : scans ~/.config/opencode/skills/ (also reads ~/.claude/skills/
#                 natively, so the Claude Code symlink covers OpenCode for users
#                 who have both)
#
# Not auto-wired (different file format / would clobber user state):
#   - Cursor: rules use a different frontmatter shape (description/globs/
#             alwaysApply) — paste manually into ~/.cursor/rules/.
#   - Hermes: SOUL.md replaces the system prompt — overwriting would destroy
#             user customisations.
#   - Pi    : SYSTEM.md / AGENTS.md are single-file replacements; same risk.

SKILL_TARGET="$APP_DEST/Contents/Resources/Skills/trope-cua"

link_skill_into() {
    local parent_dir="$1"        # e.g. $HOME/.claude/skills
    local label="$2"             # e.g. "Claude Code"
    local link_path="$parent_dir/trope-cua"

    if [[ ! -d "$parent_dir" ]]; then
        return 0
    fi
    if [[ -e "$link_path" ]] || [[ -L "$link_path" ]]; then
        log "$label skill link already exists at $link_path (skipping)"
        return 0
    fi
    if [[ ! -d "$SKILL_TARGET" ]]; then
        log "skill pack missing at $SKILL_TARGET (skipping; older release?)"
        return 0
    fi
    ln -s "$SKILL_TARGET" "$link_path"
    log "symlinked $label skill at $link_path"
}

# Claude Code — only when ~/.claude/skills already exists (Claude installed).
link_skill_into "$HOME/.claude/skills" "Claude Code"

# Codex — create ~/.agents/skills if Codex is installed (~/.codex present)
# but the agents skills dir hasn't been initialized yet, then link.
if [[ -d "$HOME/.codex" ]] && [[ ! -d "$HOME/.agents/skills" ]]; then
    mkdir -p "$HOME/.agents/skills"
fi
link_skill_into "$HOME/.agents/skills" "Codex"

# OpenClaw — create ~/.openclaw/skills if OpenClaw is installed but the
# skills dir hasn't been initialized yet, then link.
if [[ -d "$HOME/.openclaw" ]] && [[ ! -d "$HOME/.openclaw/skills" ]]; then
    mkdir -p "$HOME/.openclaw/skills"
fi
link_skill_into "$HOME/.openclaw/skills" "OpenClaw"

# OpenCode (sst/opencode) — create ~/.config/opencode/skills if OpenCode is
# installed but the skills dir hasn't been initialized yet, then link.
if [[ -d "$HOME/.config/opencode" ]] && [[ ! -d "$HOME/.config/opencode/skills" ]]; then
    mkdir -p "$HOME/.config/opencode/skills"
fi
link_skill_into "$HOME/.config/opencode/skills" "OpenCode"

# --- PATH setup ---------------------------------------------------------
#
# If $BIN_DIR is not on the user's PATH (common for ~/.local/bin on macOS,
# which is not on the system default), append an export line to the right
# shell rc file unless --no-modify-path was passed. We never edit a file
# without the user's $SHELL pointing at it (avoids surprise edits to bash
# rc files for a zsh user, etc.).

PATH_NEEDS_FIX=1
case ":$PATH:" in
    *":$BIN_DIR:"*) PATH_NEEDS_FIX=0 ;;
esac

if [[ "$PATH_NEEDS_FIX" == "1" ]]; then
    if [[ "$NO_MODIFY_PATH" == "1" ]]; then
        log "$BIN_DIR is not on PATH (skipping rc edit; --no-modify-path set)"
    else
        # Pick the rc file for the user's login shell.
        SHELL_NAME="$(basename "${SHELL:-/bin/zsh}")"
        case "$SHELL_NAME" in
            zsh)  RC_FILE="$HOME/.zshrc" ;;
            bash) RC_FILE="$HOME/.bash_profile" ;;
            fish) RC_FILE="$HOME/.config/fish/config.fish" ;;
            *)    RC_FILE="" ;;
        esac

        if [[ -n "$RC_FILE" ]]; then
            mkdir -p "$(dirname "$RC_FILE")"
            EXPORT_LINE='export PATH="$HOME/.local/bin:$PATH"'
            [[ "$SHELL_NAME" == "fish" ]] && EXPORT_LINE='set -gx PATH $HOME/.local/bin $PATH'

            if [[ -f "$RC_FILE" ]] && grep -qF "$BIN_DIR" "$RC_FILE"; then
                log "$BIN_DIR already referenced in $RC_FILE (skipping rc edit)"
            else
                {
                    printf '\n# Added by trope-cua installer -- see https://github.com/voctory/trope-cua\n'
                    printf '%s\n' "$EXPORT_LINE"
                } >> "$RC_FILE"
                log "appended PATH entry to $RC_FILE — restart your shell or run: source $RC_FILE"
            fi
        else
            log "unrecognised shell '$SHELL_NAME' — add $BIN_DIR to PATH manually"
        fi
    fi
fi

# --- Done ---------------------------------------------------------------

log "trope-cua $VERSION installed"
cat <<FINALEOF

Next steps:

  1. Grant macOS permissions (required either way):
       open -n -g -a TropeCUA --args serve
       trope-cua check_permissions
     macOS raises the Accessibility + Screen Recording dialogs.
     Grant both, then re-run check_permissions to confirm.

  2. Pick how you want to use trope-cua — pick ONE, both, or switch later:

     A. As a CLI from the shell (no extra config needed):
          trope-cua list_apps
          trope-cua --help

     B. As an MCP server — run the one matching your client. Each is also
        available via 'trope-cua mcp-config --client <name>':

        • Claude Code:
            claude mcp add --transport stdio trope-cua -- $BIN_LINK mcp

        • Codex (OpenAI):
            codex mcp add trope-cua -- $BIN_LINK mcp

        • OpenClaw:
            trope-cua mcp-config --client openclaw

        • GitHub Copilot CLI (paste into ~/.copilot/mcp-config.json):
            {
              "mcpServers": {
                "trope-cua": {
                  "type": "local",
                  "command": "$BIN_LINK",
                  "args": ["mcp"],
                  "tools": ["*"]
                }
              }
            }
            Or inside gh copilot chat: /mcp add → type=STDIO, command=$BIN_LINK, args=mcp

        • Cursor / OpenCode / Hermes (no add CLI — paste config):
            trope-cua mcp-config --client cursor     # JSON for ~/.cursor/mcp.json
            trope-cua mcp-config --client opencode   # JSON for opencode.json
            trope-cua mcp-config --client hermes     # YAML for ~/.hermes/config.yaml

        For other clients accepting the generic mcpServers shape:
            trope-cua mcp-config

Docs: https://github.com/voctory/trope-cua
FINALEOF
