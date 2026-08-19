#!/bin/zsh
# Builds Godmouse.app into ./build and signs it.
#
# Signing matters more than it looks: macOS keys the Accessibility (TCC) grant to the app's
# code-signing identity. An ad-hoc signature's designated requirement is the *binary hash*, so
# every rebuild looks like a different app and the permission silently stops applying. Signing
# with a real (even free "Apple Development") identity gives a stable identifier-based
# requirement, so you grant access once.
set -euo pipefail
cd "$(dirname "$0")"

CONFIG=${1:-release}
APP=build/Godmouse.app

echo "▶ swift build -c $CONFIG"
swift build -c "$CONFIG" >/dev/null
BIN="$(swift build -c "$CONFIG" --show-bin-path)/Godmouse"
[[ -x "$BIN" ]] || { echo "binary missing: $BIN"; exit 1; }

echo "▶ assembling $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Godmouse"
cp Resources/Info.plist "$APP/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"

# Prefer a real identity (stable TCC grant); fall back to ad-hoc with a warning.
IDENTITY="${GODMOUSE_SIGN_IDENTITY:-}"
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
  codesign --force --options runtime --sign "$IDENTITY" --identifier com.godmouse.app "$APP"
else
  echo "▶ codesign ad-hoc (no signing identity found)"
  echo "  ⚠️  macOS will forget the Accessibility grant on every rebuild."
  codesign --force --sign - --identifier com.godmouse.app "$APP"
fi
codesign --verify --verbose=1 "$APP" 2>&1 | tail -1

echo
echo "✅ $APP"
echo "   install: ./install.sh"
