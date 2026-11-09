#!/bin/zsh
# Builds CalmMouse.app into ./build and signs it.
#
# Signing matters more than it looks: macOS keys the Accessibility (TCC) grant to the app's
# code-signing identity. An ad-hoc signature's designated requirement is the *binary hash*, so
# every rebuild looks like a different app and the permission silently stops applying. Signing
# with a real (even free "Apple Development") identity gives a stable identifier-based
# requirement, so you grant access once.
set -euo pipefail
cd "$(dirname "$0")"

CONFIG=${1:-release}
APP=build/CalmMouse.app

# The repo lives in an iCloud-synced folder (~/Documents) and the account's quota is full: the
# sync daemon fighting over the build directory corrupts SwiftPM's build database ("disk I/O
# error") and leaves stale products behind. Empirically, an in-repo scratch dir keeps failing
# (even *.nosync) while any location outside ~/Documents builds fine — so the scratch lives in
# Caches, which is never synced.
SCRATCH="$HOME/Library/Caches/CalmMouse/build"

echo "▶ swift build -c $CONFIG"
swift build -c "$CONFIG" --scratch-path "$SCRATCH" >/dev/null
BIN="$(swift build -c "$CONFIG" --scratch-path "$SCRATCH" --show-bin-path)/CalmMouse"
[[ -x "$BIN" ]] || { echo "binary missing: $BIN"; exit 1; }

echo "▶ assembling $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/CalmMouse"
# The linked binary records its deployment target (13.0) as the SDK it was built with, and macOS
# draws an app from an old SDK in the old style: small switches, outlined groups, a grey window.
# Stamp the SDK it was really built against so it gets the current system controls. This changes
# the binary, so it has to come before signing.
MIN_OS=$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' Resources/Info.plist)
vtool -set-build-version macos "$MIN_OS" "$(xcrun --show-sdk-version)" -replace \
  -output "$APP/Contents/MacOS/CalmMouse.stamped" "$APP/Contents/MacOS/CalmMouse" 2>/dev/null
mv "$APP/Contents/MacOS/CalmMouse.stamped" "$APP/Contents/MacOS/CalmMouse"
cp Resources/Info.plist "$APP/Contents/Info.plist"
cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
# Localizations: the app picks the .lproj matching the system language (English fallback).
cp -R Resources/*.lproj "$APP/Contents/Resources/"
printf 'APPL????' > "$APP/Contents/PkgInfo"

# This folder is synced by iCloud, which tags what lands in it with Finder and sync attributes;
# codesign refuses a bundle that carries them.
xattr -cr "$APP"

# Prefer a real identity (stable TCC grant); fall back to ad-hoc with a warning.
IDENTITY="${CALMMOUSE_SIGN_IDENTITY:-}"
if [[ -z "$IDENTITY" ]]; then
  IDENTITY=$(security find-identity -v -p codesigning 2>/dev/null \
    | grep -E 'Developer ID Application' | head -1 | sed -E 's/.*"(.*)"/\1/' || true)
fi
if [[ -z "$IDENTITY" ]]; then
  IDENTITY=$(security find-identity -v -p codesigning 2>/dev/null \
    | grep -E 'Apple Development' | head -1 | sed -E 's/.*"(.*)"/\1/' || true)
fi

if [[ -n "$IDENTITY" ]]; then
  echo "▶ codesign with: $IDENTITY"
  codesign --force --options runtime --sign "$IDENTITY" --identifier com.calmmouse.app "$APP"
else
  echo "▶ codesign ad-hoc (no signing identity found)"
  echo "  ⚠️  macOS will forget the Accessibility grant on every rebuild."
  codesign --force --sign - --identifier com.calmmouse.app "$APP"
fi
codesign --verify --verbose=1 "$APP" 2>&1 | tail -1

echo
echo "✅ $APP"
echo "   install: ./install.sh"
