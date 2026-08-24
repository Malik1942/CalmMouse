#!/bin/zsh
# Produces the distributable zip for a GitHub release.
#
# Two modes, picked automatically:
#
#  • Notarized (preferred). Needs a "Developer ID Application" certificate in the keychain
#    (Xcode → Settings → Accounts → Manage Certificates → + → Developer ID Application) and
#    one-time notary credentials:
#        xcrun notarytool store-credentials calmmouse-notary \
#          --apple-id <apple-id> --team-id <team-id> --password <app-specific-password>
#    Signs with the hardened runtime, notarizes, staples. Users download and it just opens.
#
#  • Ad-hoc fallback (no Developer ID cert or no notary profile). macOS treats every release
#    as a new app: users must clear quarantine and re-grant Accessibility after updating —
#    the README says so, and the app has a "Reset grant…" button for exactly that.
set -euo pipefail
cd "$(dirname "$0")"

VERSION=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' Resources/Info.plist)
OUT="dist"
PROFILE="calmmouse-notary"

DEV_ID=$(security find-identity -v -p codesigning 2>/dev/null \
  | grep -E 'Developer ID Application' | head -1 | sed -E 's/.*"(.*)"/\1/' || true)
HAVE_PROFILE=false
if xcrun notarytool history --keychain-profile "$PROFILE" >/dev/null 2>&1; then
  HAVE_PROFILE=true
fi

mkdir -p "$OUT"
rm -f "$OUT/CalmMouse.zip"

zip_app() {
  # --norsrc --noextattr matters: without them ditto stores xattrs as AppleDouble entries, and a
  # plain command-line `unzip` materialises them as ._Info.plist / ._PkgInfo files that invalidate
  # the code signature. Finder's Archive Utility copes; `unzip` does not.
  ditto -c -k --norsrc --noextattr --keepParent build/CalmMouse.app "$1"
}

if [[ -n "$DEV_ID" && "$HAVE_PROFILE" == true ]]; then
  echo "▶ notarized release ($DEV_ID)"
  CALMMOUSE_SIGN_IDENTITY="$DEV_ID" ./build.sh release

  echo "▶ notarizing (waits for Apple; usually a few minutes)"
  zip_app "$OUT/CalmMouse-notarize.zip"
  xcrun notarytool submit "$OUT/CalmMouse-notarize.zip" --keychain-profile "$PROFILE" --wait
  rm -f "$OUT/CalmMouse-notarize.zip"

  echo "▶ stapling ticket"
  xcrun stapler staple build/CalmMouse.app
else
  if [[ -n "$DEV_ID" ]]; then
    echo "⚠️  Developer ID certificate found but no '$PROFILE' notary profile —"
    echo "   run the store-credentials command in this script's header, then re-run for a"
    echo "   notarized release. Falling back to an ad-hoc build."
  else
    echo "▶ ad-hoc release (no Developer ID certificate in the keychain)"
  fi
  CALMMOUSE_SIGN_IDENTITY="-" ./build.sh release
fi

zip_app "$OUT/CalmMouse.zip"

echo
echo "✅ $OUT/CalmMouse.zip  (v$VERSION, $(du -h "$OUT/CalmMouse.zip" | cut -f1))"
# Prove the artifact users will actually download still validates after a plain unzip.
TMP=$(mktemp -d)
( cd "$TMP" && unzip -q "$OLDPWD/$OUT/CalmMouse.zip" && codesign --verify --strict CalmMouse.app ) \
  && echo "   signature verifies after plain unzip ✅" \
  || { echo "   ⚠️  signature broken after unzip"; exit 1; }
if [[ -n "$DEV_ID" && "$HAVE_PROFILE" == true ]]; then
  ( cd "$TMP" && xcrun stapler validate CalmMouse.app >/dev/null ) \
    && echo "   notarization ticket staples through the zip ✅" \
    || { echo "   ⚠️  stapled ticket missing after unzip"; exit 1; }
fi
rm -rf "$TMP"
