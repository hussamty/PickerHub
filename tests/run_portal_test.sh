#!/bin/sh
# Run under dbus-run-session and Xvfb. All generated video stays on this server.
set -eu
runtime=$(mktemp -d)
export XDG_RUNTIME_DIR="$runtime" XDG_CONFIG_HOME="$runtime/config" PIPEWIRE_REMOTE=pipewire-0
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
# A private server with no device monitors or host portal access. Explicit
# modules work with both Ubuntu 22.04 PipeWire and recent developer systems.
# Access is restricted by the private runtime directory, not host policy.
cat > "$runtime/pipewire-test.conf" <<'CONF'
context.properties = { core.daemon = true core.name = pipewire-0 }
context.spa-libs = {
    support.* = support/libspa-support
    audio.convert.* = audioconvert/libspa-audioconvert
    video.convert.* = videoconvert/libspa-videoconvert
}
context.modules = [
    { name = libpipewire-module-protocol-native }
    { name = libpipewire-module-client-node }
    { name = libpipewire-module-client-device }
    { name = libpipewire-module-adapter }
    { name = libpipewire-module-link-factory }
    { name = libpipewire-module-metadata }
    { name = libpipewire-module-access args = { access.socket = { pipewire-0 = unrestricted } } }
    { name = libpipewire-module-session-manager }
]
CONF
pipewire -c "$runtime/pipewire-test.conf" > "$runtime/pipewire.log" 2>&1 &
jobs="$!"
sleep .3
wireplumber > "$runtime/wireplumber.log" 2>&1 &
jobs="$! $jobs"
/usr/bin/python3 tests/portal_fixture.py > "$runtime/fixture.log" 2>&1 &
jobs="$! $jobs"
gdbus wait --session --timeout 8 org.freedesktop.portal.Desktop
for live in 0 1; do
    PICKERHUB_CAPTURE_TRACE=1 GST_DEBUG=2 PICKERHUB_TEST_PORTAL=1 PICKERHUB_TEST_LIVE="$live" /usr/bin/python3 tests/native_x11.py || { cat "$runtime/fixture.log" "$runtime/pipewire.log" "$runtime/wireplumber.log"; exit 1; }
done
