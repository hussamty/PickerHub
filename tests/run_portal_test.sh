#!/bin/sh
# Run under dbus-run-session and Xvfb. All generated video stays on this server.
set -eu
runtime=$(mktemp -d)
export XDG_RUNTIME_DIR="$runtime" XDG_CONFIG_HOME="$runtime/config" PIPEWIRE_REMOTE=pipewire-0-manager
mkdir -p "$XDG_CONFIG_HOME/wireplumber/wireplumber.conf.d"
cat > "$XDG_CONFIG_HOME/wireplumber/wireplumber.conf.d/50-test.conf" <<'CONF'
wireplumber.profiles = { main = {
    monitor.alsa = disabled
    monitor.alsa-midi = disabled
    monitor.bluez = disabled
    monitor.bluez-midi = disabled
    monitor.v4l2 = disabled
    monitor.libcamera = disabled
} }
CONF
jobs=""
cleanup() {
    for pid in $jobs; do kill "$pid" 2>/dev/null || true; done
    wait || true
    python3 -c 'import shutil, sys; shutil.rmtree(sys.argv[1])' "$runtime"
}
trap cleanup EXIT
pipewire > "$runtime/pipewire.log" 2>&1 &
jobs="$!"
sleep .3
wireplumber > "$runtime/wireplumber.log" 2>&1 &
jobs="$! $jobs"
python3 tests/portal_fixture.py > "$runtime/fixture.log" 2>&1 &
jobs="$! $jobs"
gdbus wait --session --timeout 8 org.freedesktop.portal.Desktop
for live in 0 1; do
    PICKERHUB_TEST_PORTAL=1 PICKERHUB_TEST_LIVE="$live" python3 tests/native_x11.py || { cat "$runtime/fixture.log"; exit 1; }
done
