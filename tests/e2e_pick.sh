#!/bin/sh
# End to end on X11, in the real app on an empty library:
#   1. F8 starts the eyedropper; a fixture window of a known color is clicked through XTEST.
#   2. The global shortcut is enabled, Ctrl+Shift+P is pressed through XTEST, and a second color is clicked.
# Both colors must reach the saved history.
#   dbus-run-session -- xvfb-run -a -s "-screen 0 1400x900x24" sh tests/e2e_pick.sh
set -e
root=$(cd "$(dirname "$0")/.." && pwd)
unset WAYLAND_DISPLAY
export XDG_SESSION_TYPE=x11 GDK_BACKEND=x11

run() { # <script> <color> [shortcut]
  data=$(mktemp -d)
  PICKERHUB_DATA_DIR="$data" PICKERHUB_EVAL="$1" "$root/target/debug/pickerhub" &
  app=$!
  "$root/target/debug/examples/x11_click" "$2" 4500 $3
  sleep 2
  kill $app 2>/dev/null || true
  wait $app 2>/dev/null || true
  first=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['history'][0])" "$data/PickerHub/workspace.json")
  rm -rf "$data"
  echo "$first"
}

a=$(run 'window.dispatchEvent(new KeyboardEvent("keydown", {key: "F8"}))' 0x6A2CB0)
echo "F8 picked: $a"
b=$(run '__pickerhub.setting("globalShortcut", true)' 0x1E8F6B shortcut)
echo "shortcut picked: $b"
test "$a" = "#6A2CB0" && test "$b" = "#1E8F6B"
