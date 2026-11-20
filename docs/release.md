# Releasing PickerHub 2

A release is a tag. `.github/workflows/release.yml` builds installers on Linux, Windows and macOS, gives the Debian package its final name, writes the updater manifest and opens a **draft**
GitHub release. A person checks it (docs/beta-checklist.md) and publishes it; installed copies see
the update only after that.

## What a release build is

`src-tauri/tauri.release.conf.json` is merged over `tauri.conf.json`:

| | Development and CI builds | Release builds |
| --- | --- | --- |
| Product name | PickerHub 2 | PickerHub |
| Program | `pickerhub2` | `pickerhub` |
| Debian package | `picker-hub-2` | `pickerhub` (replaces and conflicts with `picker-hub-2`) |
| RPM package | `picker-hub-2` | `picker-hub`, provides `pickerhub`, obsoletes `picker-hub-2` |
| Updater | absent | present, with the project's public key |

The library folder does not depend on the product name, so alphas, betas and 1.x all open the same
library. 1.x shipped one Debian package, `pickerhub`, with `/usr/bin/pickerhub`. Tauri would name the
2.x package `picker-hub` (the product name in kebab case), which would install beside 1.x and collide
on that path. `scripts/release_deb.py` renames it to `pickerhub`, so installing 2.x upgrades 1.x in
place, and it signs the rebuilt archive again for the updater.

## Updates

* The updater is compiled in only with the `updater` feature **and** a public key in
  `PICKERHUB_UPDATER_PUBKEY` at build time (`src-tauri/src/update.rs`). Other builds say in Settings
  that they do not update themselves, and never go online.
* It checks only when the user presses **Check for updates** in Settings. The manifest comes over TLS
  from `https://github.com/hussamty/PickerHub/releases/latest/download/latest.json`.
* An installer is accepted only if its minisign signature matches the built-in key **and** names the
  version the manifest announces (`requireSignedVersion`), so a forged manifest cannot pair a new
  version number with an old, genuinely signed installer.
* The library is flushed to disk before the installer runs; the app restarts afterwards.
* Supported: AppImage, `.deb`, `.rpm`, the Windows NSIS installer and the macOS app.

## One-time setup (the maintainer)

Nothing here is in the repository, and nothing should be.

1. **Updater key.** On a machine you trust:

   ```bash
   sh scripts/updater_key.sh
   ```

   It writes `~/.tauri/pickerhub-updater.key` (private, password-protected) and `.pub`. Back up the
   private key and its password offline: without them, installed copies can never be updated again.
2. **Repository settings → Secrets and variables → Actions:**

   | Kind | Name | Value |
   | --- | --- | --- |
   | Variable | `PICKERHUB_UPDATER_PUBKEY` | contents of `pickerhub-updater.key.pub` |
   | Secret | `TAURI_SIGNING_PRIVATE_KEY` | contents of `pickerhub-updater.key` |
   | Secret | `TAURI_SIGNING_PRIVATE_KEY_PASSWORD` | its password |
   | Secret, optional | `WINDOWS_CERTIFICATE` | the code-signing certificate as a base64 `.pfx` |
   | Secret, optional | `WINDOWS_CERTIFICATE_PASSWORD` | the `.pfx` password |
   | Secret, optional | `APPLE_CERTIFICATE` | Developer ID Application certificate, base64 `.p12` |
   | Secret, optional | `APPLE_CERTIFICATE_PASSWORD` | the `.p12` password |
   | Secret, optional | `APPLE_SIGNING_IDENTITY` | e.g. `Developer ID Application: Name (TEAMID)` |
   | Secret, optional | `APPLE_ID`, `APPLE_PASSWORD`, `APPLE_TEAM_ID` | notarization: Apple ID, app-specific password, team |

   The updater key and its public half are required: the workflow stops early, with a message,
   without them. If the key was made without a password, leave
   `TAURI_SIGNING_PRIVATE_KEY_PASSWORD` unset; if it has one, the secret must hold it or signing fails.

### Without Windows or Apple certificates

The certificates can be added later; until then a release is still complete and safe to update:

| | With a certificate | Without (the workflow's warning says so) |
| --- | --- | --- |
| Windows installer | Authenticode-signed and verified | unsigned: SmartScreen shows "Windows protected your PC" on first run; **More info → Run anyway** |
| macOS app | Developer ID-signed, notarized and stapled | ad-hoc signed, not notarized: the first open is refused; **System Settings → Privacy & Security → Open Anyway**, or right-click the app and choose **Open** |
| Updates | verified with the updater key | verified with the updater key, exactly the same |

The updater's minisign signature is what proves an update came from you, on every system, so
unsigned installers do not weaken updates. Say in the release notes that the Windows and macOS
installers are unsigned and how to open them. Once a certificate is added, the next release is
signed with no other change.

## Cutting a release

1. Set the app's version in `src-tauri/Cargo.toml`, `src-tauri/tauri.conf.json` and `ui/package.json`
   (the root `Cargo.toml` is the color core, versioned on its own), and add the entry to
   `CHANGELOG.md`.
2. Run the full checks locally (`cargo test --workspace`, `npm --prefix ui test`,
   `python3 tests/ui_tauri.py --pages`) and on CI.
3. Tag and push: `git tag v2.0.0 && git push origin v2.0.0`.
4. When the workflow finishes, open the draft release. It holds every installer, its `.sig`,
   `latest.json` and `SHA256SUMS`. Install each on a real machine and run docs/beta-checklist.md,
   including **the update check from the previous release**.
5. Replace the draft's notes with the changelog entry, and publish. `latest.json` is live from that
   moment.

To withdraw a bad release, publish a newer one; never re-use a version. Deleting a release's
`latest.json` stops new offers but cannot recall installs already made.

## Building a release locally

Without the keys you can still check the packaging (the result is unsigned and has no updater):

```bash
ui/node_modules/.bin/tauri build --config src-tauri/tauri.release.conf.json --config '{"bundle":{"createUpdaterArtifacts":false}}' --bundles deb
```

```bash
python3 scripts/release_deb.py target/release/bundle/deb
```

```bash
dpkg-deb -f target/release/bundle/deb/pickerhub_*.deb
```

The bundler finds the tray library through pkg-config, so a Linux build machine needs the
`libayatana-appindicator3-dev` package (CI installs it with the other build dependencies).
