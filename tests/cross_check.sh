#!/bin/sh
# Type-checks the whole app for Windows and both macOS architectures from Linux, without their SDKs.
# `cargo check` links nothing, so the native resource compiler, C compiler and archiver are stubbed
# (tests/stubs). This catches every type and API error in code that only builds on those systems;
# it does not replace running the app there (CI builds and tests natively on all three).
#   rustup target add x86_64-pc-windows-gnullvm aarch64-apple-darwin x86_64-apple-darwin
#   sh tests/cross_check.sh
set -e
root=$(cd "$(dirname "$0")/.." && pwd)
stubs="$root/tests/stubs"
cd "$root"
PATH="$stubs:$PATH" cargo check -p pickerhub-app --target x86_64-pc-windows-gnullvm
for t in aarch64-apple-darwin x86_64-apple-darwin; do
  T=$(echo "$t" | tr - _)
  env "CC_$T=$stubs/cc-stub" "AR_$T=$stubs/ar-stub" cargo check -p pickerhub-app --target "$t"
done
echo "cross check: ok"
