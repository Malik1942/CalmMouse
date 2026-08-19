#!/bin/zsh
# Build, replace /Applications/Godmouse.app, relaunch, and report what the running app thinks.
set -euo pipefail
cd "$(dirname "$0")"

./build.sh "${1:-release}"

echo "▶ replacing /Applications/Godmouse.app"
pkill -x Godmouse 2>/dev/null || true
sleep 1
rm -rf /Applications/Godmouse.app
cp -R build/Godmouse.app /Applications/
open /Applications/Godmouse.app
sleep 3

echo
echo "▶ runtime status"
/Applications/Godmouse.app/Contents/MacOS/Godmouse --status || true
echo
if /Applications/Godmouse.app/Contents/MacOS/Godmouse --status 2>/dev/null | grep -q '"accessibilityTrusted" : false'; then
  cat <<'MSG'
⚠️  Accessibility access is NOT granted, so Godmouse can't see mouse events yet.
   System Settings → Privacy & Security → Accessibility → enable Godmouse.
   (If a stale "Godmouse" entry is already listed, remove it with “−” and add
   /Applications/Godmouse.app again — an old entry from an ad-hoc build won't match.)
MSG
fi
