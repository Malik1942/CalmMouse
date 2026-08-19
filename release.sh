#!/bin/zsh
# Produces the distributable zip for a GitHub release.
#
# Release builds are deliberately signed ad-hoc rather than with a personal certificate:
# an "Apple Development" signature embeds the developer's name and won't validate on anyone
# else's Mac anyway. The cost is that macOS treats every release as a new app, so users have
# to re-grant Accessibility after updating — the README says so, and the app has a
# "Reset grant…" button for exactly that.
set -euo pipefail
cd "$(dirname "$0")"

VERSION=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' Resources/Info.plist)
OUT="dist"

GODMOUSE_SIGN_IDENTITY="-" ./build.sh release

mkdir -p "$OUT"
rm -f "$OUT/Godmouse.zip"
# --norsrc --noextattr matters: without them ditto stores xattrs as AppleDouble entries, and a
# plain command-line `unzip` materialises them as ._Info.plist / ._PkgInfo files that invalidate
# the code signature. Finder's Archive Utility copes; `unzip` does not.
ditto -c -k --norsrc --noextattr --keepParent build/Godmouse.app "$OUT/Godmouse.zip"

echo
echo "✅ $OUT/Godmouse.zip  (v$VERSION, $(du -h "$OUT/Godmouse.zip" | cut -f1))"
# Prove the artifact users will actually download still validates after a plain unzip.
TMP=$(mktemp -d)
( cd "$TMP" && unzip -q "$OLDPWD/$OUT/Godmouse.zip" && codesign --verify --strict Godmouse.app ) \
  && echo "   signature verifies after plain unzip ✅" \
  || { echo "   ⚠️  signature broken after unzip"; exit 1; }
rm -rf "$TMP"
