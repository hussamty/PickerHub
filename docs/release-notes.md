Native color picker for Windows, Linux and macOS. Download the archive matching your computer below; you do not need .NET or Rust installed.

**Windows 11 x64:** download `PickerHub-0.6.0-win-x64.zip`, choose Extract All, then open `PickerHub/PickerHub.exe`. Keep all files together. This is an **unsigned testing release**; Windows may display an unknown-publisher notice. No paid certificate or trusted publisher identity is bundled. Do not disable Windows security protections.

**Linux:** use the `.deb` on Ubuntu/Debian or extract the `linux-x64.tar.gz` archive. **macOS:** choose `osx-arm64` for Apple Silicon or `osx-x64` for Intel. Mac preview builds are ad-hoc signed, not notarized.

New in 0.6.0:

- Paste image pixels or image files with Ctrl+V / Cmd+V, or drag an image onto Image Analysis.
- Full-color palette and history cards with readable contrast-aware labels.
- Responsive card navigation, consistent glass tooltips/menus/dialogs, and overlays that fit after resizing.
- Reachable bottom controls and custom draggable scrollbars, tested at compact, medium and large window sizes in Arabic and English.
- Branded default Linux/Windows titlebar with minimize, maximize/restore, close, dragging and edge resizing. macOS retains native window controls.

Included from 0.5.0: instant multi-pick count and swatches, responsive layout, single-instance activation, nonblocking clipboard completion, and optional local diagnostics. Arabic and English are included.

F8 starts a single pick. Use Multi-pick from the palette page or tray; each click adds a color and Esc finishes. Quick Copy and copy format apply immediately in Settings.

[Windows test guide](https://github.com/hussamty/PickerHub/blob/main/docs/windows-test.md) · [Install](https://github.com/hussamty/PickerHub/blob/main/docs/install.md)

Automated checks do not replace testing on real Windows 11 hardware, mixed-DPI monitors, GPU drivers or assistive technology. Automatic installation of updates is not enabled; backups and manual rollback are supported. SHA-256 files detect download corruption and do not replace a publisher signature.
