#!/bin/sh
# Makes the minisign key pair that signs PickerHub updates. Run it once, yourself, on a machine you
# trust; nothing here is sent anywhere. Keep the private key and its password out of the repository.
#
#   sh scripts/updater_key.sh [path]      (default ~/.tauri/pickerhub-updater.key)
#
# Then, in the GitHub repository's settings:
#   Variables  PICKERHUB_UPDATER_PUBKEY            the contents of <path>.pub
#   Secrets    TAURI_SIGNING_PRIVATE_KEY           the contents of <path>
#              TAURI_SIGNING_PRIVATE_KEY_PASSWORD  the password you choose below
# Losing the private key means installed copies can no longer be updated: back it up offline.
set -eu
key="${1:-$HOME/.tauri/pickerhub-updater.key}"
if [ -e "$key" ]; then
  echo "$key already exists; refusing to replace a signing key." >&2
  exit 1
fi
mkdir -p "$(dirname "$key")"
"$(dirname "$0")/../ui/node_modules/.bin/tauri" signer generate -w "$key"
chmod 600 "$key"
echo
echo "Public key (repository variable PICKERHUB_UPDATER_PUBKEY):"
cat "$key.pub"
echo
