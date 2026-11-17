Native color picker for Windows, Linux and macOS. Download the archive matching your computer below; you do not need .NET or Rust installed.

**Windows 11 x64:** download `PickerHub-0.7.0-win-x64.zip`, choose Extract All, then open `PickerHub/PickerHub.exe`. Keep all files together. This is an **unsigned testing release**; Windows may display an unknown-publisher notice. No paid certificate or trusted publisher identity is bundled. Do not disable Windows security protections.

**Linux:** use the `.deb` on Ubuntu/Debian or extract the `linux-x64.tar.gz` archive. **macOS:** choose `osx-arm64` for Apple Silicon or `osx-x64` for Intel. Mac preview builds are ad-hoc signed, not notarized.

New in 0.7.0:

- New typography: Plus Jakarta Sans for Latin and Noto Kufi Arabic for Arabic on one type scale, with refined weights, line heights and spacing throughout.
- Maximized windows now show a Restore button beside Minimize; Restore returns the window to its previous size.
- A redesigned palette-of-the-day envelope with exact corners and edges, a perspective flap, a lined interior and the PickerHub seal.
- `--preview-envelope` opens the envelope in a temporary library so it can be replayed at will (Linux launchers also offer it in the app's context menu).
- A new PickerHub mark, an eyedropper written as one fluid stroke in the shape of a P, with a drop falling from its tip, and a single-row top bar.
- Arabic reads from the right and English from the left throughout, including after switching language in Settings, and every daily palette has an English story.
- Status messages appear as frosted-glass toasts in the bottom corner where reading starts, and leave on their own.
- Short windows keep every navigation tool on screen; the minimum window size is 560 × 480.

Image paste/drop, full-color cards and responsive scrolling from 0.6.0 are included.

Included from 0.5.0: instant multi-pick count and swatches, responsive layout, single-instance activation, nonblocking clipboard completion, and optional local diagnostics. Arabic and English are included.

F8 starts a single pick. Use Multi-pick from the palette page or tray; each click adds a color and Esc finishes. Quick Copy and copy format apply immediately in Settings.

[Windows test guide](https://github.com/hussamty/PickerHub/blob/main/docs/windows-test.md) · [Install](https://github.com/hussamty/PickerHub/blob/main/docs/install.md)

Automated checks do not replace testing on real Windows 11 hardware, mixed-DPI monitors, GPU drivers or assistive technology. Automatic installation of updates is not enabled; backups and manual rollback are supported. SHA-256 files detect download corruption and do not replace a publisher signature.
