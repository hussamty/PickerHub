# PickerHub desktop interface

## Reference review

Three interaction studies set the direction of this redesign. They are research material; nothing from them is copied.

- A liquid icon cluster: circular actions that swell under the pointer, press against their neighbours, and name themselves in a pill tooltip. https://x.com/koppkev/status/2120471771602092136
- A spine menu: a slim tab on the window edge that widens into a floating panel, with the current destination shown as one filled accent pill. https://x.com/seanaiux/status/2120222459110789574
- A specimen card deck: a fanned pile of cards, each flipping to a reverse side of measurements, with a small pager below. https://x.com/sanyampunia/status/2120080276022051218

Earlier research into PowerToys Color Picker, Sip, ColorSlurp, and Just Color Picker still informs the feature set: one direct pick action, copy controls next to each format, and palettes as a primary part of the app.

## Audit

The previous interface had a fixed 204-pixel sidebar, 12-pixel white cards for every kind of content, a blue accent, and one row-card per palette color. Navigation used a quarter of a narrow window, palettes read as a settings list, and icon actions gave no feedback beyond the toolkit hover color.

## Design

The brief sets design variance to 7, motion intensity to 6, and visual density to 4.

- Navigation is a spine on the right edge. At rest it is a 60-pixel capsule of icons; under the pointer or keyboard focus it widens to 224 pixels over the page and shows labels. All six tools, the theme switch, and settings stay one click away at the minimum window size.
- Icon actions are liquid buttons: 40-pixel circles that widen to 56 on hover with an overshoot, so a cluster of them shifts like one body. Each has a tooltip and an accessible name.
- Palettes and history are card decks. The top card is the color itself; clicking it, or pressing Space, turns it over to show lightness, HEX, RGB, HSL, CMYK, and its tonal row. Arrow keys and the pager step through the pile, and a sheet of dots beside the deck jumps to any color.
- The color page opens on a specimen: the selected color at full size, labelled in the ink the Rust contrast engine picks for it.
- Image analysis draws each extracted color as a bar whose length is its share of the image.

## System

Tokens and control states live in desktop/Theme.axaml. Controls are built in C# and carry style classes.

- Color: neutral chrome so samples read true. Light canvas #EEF0F2, surface #F8F9FA, ink #15181C. Dark canvas #0D0E10, surface #16181B, ink #F1F3F5. One accent, vermilion #D03A1F, with white text at 4.9:1.
- Shape: interactive controls are pills, surfaces use 24-pixel corners, cards 22, inputs and swatches 12.
- Type: Noto Sans Arabic for the interface at 12/14/16/28 pixels. Codes and measurements use the system monospace face, always left to right.
- Icons: Tabler outline icons drawn in the inherited text color, so they follow their button state.
- Motion: every movement reports a state change (hover, press, page change, card step, card flip). The "تقليل الحركة" setting removes all travel and keeps the state changes.
- Both themes share one token set. The liquid buttons and the spine are plain fills with a border and a soft shadow; no backdrop blur is used, because Avalonia cannot blur in-window content.
