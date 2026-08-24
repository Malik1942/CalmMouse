#!/bin/zsh
# Build, replace /Applications/CalmMouse.app, relaunch, and report what the running app thinks.
set -euo pipefail
cd "$(dirname "$0")"

./build.sh "${1:-release}"

echo "▶ replacing /Applications/CalmMouse.app"
pkill -x CalmMouse 2>/dev/null || true
sleep 1
rm -rf /Applications/CalmMouse.app
cp -R build/CalmMouse.app /Applications/
open /Applications/CalmMouse.app
sleep 3

echo
echo "▶ runtime status"
/Applications/CalmMouse.app/Contents/MacOS/CalmMouse --status || true
echo
if /Applications/CalmMouse.app/Contents/MacOS/CalmMouse --status 2>/dev/null | grep -q '"accessibilityTrusted" : false'; then
  cat <<'MSG'
⚠️  Accessibility access is NOT granted, so CalmMouse can't see mouse events yet.
   System Settings → Privacy & Security → Accessibility → enable CalmMouse.
   (If a stale "CalmMouse" entry is already listed, remove it with “−” and add
   /Applications/CalmMouse.app again — an old entry from an ad-hoc build won't match.)
MSG
fi
