#!/usr/bin/env bash
# Build trope-cua and wrap it in a minimal TropeCUA.app bundle with a
# stable CFBundleIdentifier. TCC keys its grants on the identifier so
# grants survive every `swift build` rebuild.
#
# Signing: prefer a Developer ID Application certificate when one is
# available on the machine (looser WindowServer gate on the
# synthetic-input path — relevant to the pixel-click recipe in
# MouseInput.swift). Fall back to ad-hoc otherwise so `swift build`
# still works on contributor machines without a cert.
#
# Bundle ends up at .build/TropeCUA.app -- not production, just for dev
# + integration tests. The real signed cask build lives elsewhere.
set -euo pipefail

cd "$(dirname "$0")/.."

CONFIG="${1:-debug}"
echo "==> swift build ($CONFIG)"
swift build -c "$CONFIG"

BUILD_BIN=".build/$CONFIG/trope-cua"
APP_BUNDLE=".build/TropeCUA.app"
APP_CONTENTS="$APP_BUNDLE/Contents"
APP_MACOS="$APP_CONTENTS/MacOS"
ENTITLEMENTS="scripts/TropeCUA.entitlements"

rm -rf "$APP_BUNDLE"
mkdir -p "$APP_MACOS"
mkdir -p "$APP_CONTENTS/Resources"
cp "$BUILD_BIN" "$APP_MACOS/trope-cua"
cp App/TropeCUA/Info.plist "$APP_CONTENTS/Info.plist"
# App icon — Info.plist references `AppIcon`, which macOS looks up
# at `Contents/Resources/AppIcon.icns`. Regenerate from the .iconset
# with `iconutil -c icns App/TropeCUA/AppIcon.iconset -o
# App/TropeCUA/AppIcon.icns` when the source PNGs change.
if [[ -f "App/TropeCUA/AppIcon.icns" ]]; then
    cp App/TropeCUA/AppIcon.icns "$APP_CONTENTS/Resources/AppIcon.icns"
fi

# Claude Code skill pack. install.sh symlinks ~/.claude/skills/trope-cua
# to this path when a Claude Code install is detected on the user's
# machine. Ships inside the bundle so it survives auto-updates and
# relocations of TropeCUA.app.
if [[ -d "Skills/trope-cua" ]]; then
    mkdir -p "$APP_CONTENTS/Resources/Skills"
    cp -R Skills/trope-cua "$APP_CONTENTS/Resources/Skills/trope-cua"
fi

# Prefer Developer ID Application cert if available; fall back to ad-hoc.
# `security find-identity` prints identities as:
#   1) <SHA> "Developer ID Application: Team Name (TEAMID)"
# We AWK the SHA hash out of the first matching line (not the display
# name — on dev machines where two Developer ID Application certs
# share the same common name, codesign would reject the name as
# "ambiguous", whereas the SHA is always unique). If two or more certs
# are present we just take the first — in practice they're both valid
# for the same team.
#
# --timestamp=none is intentional: Apple's timestamp server needs
# network access, which contributors may not have or may not want on
# every dev build. Signatures without a trusted timestamp are still
# valid locally — they just can't be distributed via notarization
# without re-signing at release time.
CERT_SHA=$(security find-identity -p codesigning -v \
    | awk '/Developer ID Application/ {print $2; exit}')
CERT_NAME=$(security find-identity -p codesigning -v \
    | awk -F'"' '/Developer ID Application/ {print $2; exit}')

if [[ -n "$CERT_SHA" ]]; then
    echo "==> codesign with Developer ID: $CERT_NAME ($CERT_SHA)"
    codesign \
        --force \
        --deep \
        --sign "$CERT_SHA" \
        --identifier com.tropecua.driver \
        --entitlements "$ENTITLEMENTS" \
        --options runtime \
        --timestamp=none \
        "$APP_BUNDLE"
else
    echo "==> codesign (ad-hoc fallback, identifier com.tropecua.driver)"
    codesign \
        --force \
        --sign - \
        --identifier com.tropecua.driver \
        --entitlements "$ENTITLEMENTS" \
        --options runtime \
        --timestamp=none \
        "$APP_BUNDLE"
fi

echo "==> built $APP_BUNDLE"
echo "    binary: $APP_MACOS/trope-cua"
