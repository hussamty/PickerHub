#!/bin/sh
# Run only inside the dedicated Xvfb display, never on the user's desktop.
set -eu
wm_pid=''
cleanup() { if [ -n "$wm_pid" ]; then kill "$wm_pid" 2>/dev/null || true; fi; }
trap cleanup EXIT INT TERM
openbox --sm-disable >/tmp/pickerhub-ui-wm.log 2>&1 &
wm_pid=$!
ready=0
while ! xprop -root _NET_SUPPORTING_WM_CHECK 2>/dev/null | grep -q 'window id'; do
    ready=$((ready + 1))
    if [ "$ready" -ge 50 ]; then cat /tmp/pickerhub-ui-wm.log; exit 1; fi
    sleep .1
done
PICKERHUB_TEST_WM=1 /usr/bin/python3 -u tests/ui_visual.py
