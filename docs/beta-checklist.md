# PickerHub 2 beta checklist

These checks need real machines and a person. Everything automated already runs in CI
(`.github/workflows/tauri.yml`): the Rust tests, the 1.x library fixtures, X11 picking under Xvfb, the
end-to-end pick, the WebView checks (final yeh, content security policy, every page in both
languages and themes) and the accessibility audit. Run this list on each release candidate and note
the build (`pickerhub --version`), the machine and the result beside every line.

Use a copy of a real 1.x library (`~/.local/share/PickerHub`, `%APPDATA%\PickerHub` or
`~/Library/Application Support/PickerHub`) for the upgrade checks. Keep the original.

Mark each line **pass**, **fail** (with what you saw) or **n/a**.

## 1. Install, upgrade and remove

| Check | Linux deb | Linux rpm | AppImage | Windows | macOS |
| --- | --- | --- | --- | --- | --- |
| Fresh install starts and shows the picker in Arabic, dark | | | | | |
| Unsigned builds only: the documented way past SmartScreen (Windows) or Gatekeeper (macOS) works, and later updates install without asking again | n/a | n/a | n/a | | |
| Installing over 1.x (`picker-hub`) replaces it, and the 1.x library opens with every palette, gradient, history entry and discovery | | | | | |
| The library is still readable by 1.2 after 2.x has saved it once (open it in 1.2 from the copy) | | | | | |
| A second launch only brings the first window forward | | | | | |
| Closing and reopening keeps the window's size and place | | | | | |
| Uninstalling leaves the library folder in place | | | | | |
| The update check (Settings) finds a newer test release, verifies its signature and installs it | | | | | |
| An update whose signature does not match is refused | | | | | |

## 2. Eyedropper

| Check | GNOME Wayland | KDE Wayland | X11 (any) | Windows | macOS |
| --- | --- | --- | --- | --- | --- |
| F8 and the title-bar button pick the pixel under the pointer, exactly (compare with a known swatch) | | | | | |
| The live lens follows the pointer with no visible lag and never covers the pixel being picked | | | | | |
| Arrow keys nudge one pixel, the wheel zooms, Escape cancels, Enter picks | | | | | |
| Multiple pick (Library) keeps picking until Escape and adds each color once | | | | | |
| The global shortcut picks while another app has focus | | | | | |
| Tray: pick, pick and edit, show and quit all work | | | | | |
| Wayland: the capture-mode button switches between the system picker and the live lens | | | | n/a | n/a |
| Wayland: screen sharing asks consent once, the lens opens on every monitor, and Stop sharing ends it (the compositor's indicator goes away) | | | | n/a | n/a |
| Wayland: declining consent leaves a clear message and the system picker still works | | | | n/a | n/a |
| macOS: the first pick asks for screen recording permission and works after it is granted | n/a | n/a | n/a | n/a | |

## 3. Displays

| Check | Result |
| --- | --- |
| One monitor at 100 %, then at 125 %, 150 % and 200 % scaling: the picked color is exact and the lens sits beside the pointer | |
| Two monitors with different scaling (for example 100 % and 200 %): pick on each, and drag the window between them; text stays crisp | |
| A monitor placed left of or above the primary one (negative coordinates) | |
| Unplug a monitor while the app is open, then pick | |
| A colour-managed or HDR display: the picked value matches the system's own picker | |
| Rotate a monitor to portrait and pick | |

## 4. Sleep, sessions and long use

| Check | Result |
| --- | --- |
| Suspend with the app open, resume: the window, the tray and the global shortcut still work | |
| Suspend while screen sharing is active, resume, pick: sharing restarts or explains itself | |
| Lock and unlock the session, then pick | |
| Switch user and back | |
| Leave the app open for a working day: memory stays steady (note the start and end figures) | |
| Change the system's dark or light mode with "follow system" on | |
| Change the system's reduce-motion and high-contrast settings | |
| Remove the network: nothing changes (the app is fully local) | |
| Fill the disk, then edit: a clear error, and the library is intact afterwards | |

## 5. Keyboard and assistive technology

| Check | Orca (Linux) | Narrator (Windows) | VoiceOver (macOS) |
| --- | --- | --- | --- |
| Every page can be reached and used with Tab, Shift+Tab, the arrows, Enter and Escape alone | | | |
| A sheet keeps focus inside until it closes, and focus returns to what opened it | | | |
| Buttons with icons only are announced by name | | | |
| The palette of the day can be opened, and each card copied, from the keyboard | | | |
| The image guide: the arrows move the pixel cursor and Enter copies it | | | |
| Arabic text is read in Arabic, English in English | | | |

## 6. Look and language

| Check | Result |
| --- | --- |
| Arabic: final yeh keeps its dots (بني, not بنى), word spaces are even, numbers and codes read left to right | |
| English: every string is translated, nothing is cut off at the window's smallest size | |
| Full glass on, then reduce transparency on: both readable on a busy wallpaper | |
| The palette-of-the-day envelope opens smoothly; with reduce motion on, the cards simply appear | |
| The image guide draws its leaders once per picture and the labels never overlap | |

## 7. Support

| Check | Result |
| --- | --- |
| With the local log on, picks and searches add lines to `diagnostics.jsonl` in the library folder; it holds no colors, names or paths | |
| With the log off, the file is not written | |
| The support report saves, and holds the build, the platform, counts and (when on) this session's timings only | |

## When a check fails

Write down the build, the system and its version, the session type and display setup, what you did,
what you expected and what happened. Attach the support report (Settings, Support) with the local log
turned on, and a screenshot where it helps. Never attach a library file: it holds your colors.
