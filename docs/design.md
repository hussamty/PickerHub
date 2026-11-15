# PickerHub desktop interface

## Reference review

Four interaction studies set the direction of this redesign. They are research material; nothing from them is copied.

- A liquid interface: circular actions that swell under the pointer, flow into their neighbours, squash when pressed, and name themselves in a pill tooltip; menus that grow out of the control that opens them. https://x.com/koppkev/status/2120471771602092136
- A spine menu: a pane of frosted glass attached to the window edge through concave shoulders, opening into a panel where a highlight follows the pointer from row to row. https://x.com/seanaiux/status/2120222459110789574
- A specimen card deck: a fanned pile of frosted cards, each flipping to a reverse side of measurements, with a small pager below. https://x.com/sanyampunia/status/2120080276022051218
- A light and dark switch built as one physical object: a frosted knob in a recessed track, a pixel moon that bursts and gathers again as a sun, a rim light that travels round the knob, and a drag that scrubs the change. https://www.scrolltide.co/#c-ember-tide

Earlier research into PowerToys Color Picker, Sip, ColorSlurp, and Just Color Picker still informs the feature set: one direct pick action, copy controls next to each format, and palettes as a primary part of the app.

## Design

The brief sets design variance to 7, motion intensity to 6, and visual density to 4.

- Navigation is a spine grown out of the right edge. At rest it is a 58-pixel rail of icons, so every tool stays one click away; under the pointer or keyboard focus it opens to 236 pixels over the page and shows labels. The grip at the top pins it open until a click elsewhere or Esc. A glass pill follows the pointer between rows and squashes on press; a yellow pill marks the current tool and slides when the tool changes.
- Icon actions are liquid buttons: 40-pixel drops that widen on hover with an overshoot and flatten on press. Grouped actions share one fluid body, so a swelling button flows into its neighbours. Each has a tooltip and an accessible name.
- Palettes and history are decks of glass cards. The front holds the color as one flat disc, never tinted or lit, so it reads true; the cards beneath show through the frost. Hovering opens the fan, clicking or Space turns the top card over to lightness, HEX, RGB, HSL, CMYK, and its tonal row. Arrow keys and the pager step through the pile, and a sheet of dots beside the deck jumps to any color.
- The theme switch sits in the header. Clicking plays the change; dragging the knob scrubs it. The room behind the glass follows the knob continuously and the theme itself turns at the midpoint.
- Everything that opens over the page is in-window glass with one motion: settings, prompts and confirmations are sheets over a dimmed page, and select buttons drop a glass list that grows out of the button. No separate system windows or stock drop-downs remain.
- Pages fit their viewport: the title and its description share one row, the content column is capped at 1300 pixels, and the color page shows every tool without scrolling at the default window size.
- Palette of the day is a small sealed envelope in glossy foil: brand yellow under an iridescent wash, leaning toward the pointer in 3D. Opening it is the one orchestrated moment in the app: the flap swings back, the cards are pushed out into a fan, confetti bursts from the mouth, the envelope falls away and the fan opens into a row. Afterwards the page is quiet: the seven role cards in a shallow arc, each lifting and tilting under the pointer, and one pane beneath with the story, the countdown and the actions. Reference for the opening: https://x.com/insporadesign/status/2120404687291584780
- The color page opens on a specimen: the selected color at full size, labelled in the ink the Rust contrast engine picks for it.
- Image analysis lists up to sixteen colors, each as a bar whose length is its share of the image. Shades that look alike count as one color and small distinct accents are kept, so the list reads as a person would describe the picture.
- The eyedropper lens is a block of brand black: magnified pixels in a rounded well, the chosen pixel framed in yellow between two black lines so it shows on any color, and the value beneath. It refreshes every frame and its zoom glides between levels.

## System

Tokens and control states live in desktop/Theme.axaml. Glass, the fluid button body, and the backdrop are in desktop/Glass.cs; the theme switch is in desktop/ThemeSwitch.cs. Controls are built in C# and carry style classes.

- Color: the TrineHub palette. Black #111111, yellow #FFF000, white #FFFFFF, and nothing else in the chrome, so samples read true. Dark is the home theme: black room, white ink, yellow for the one filled action, the current tool, and focus. Light inverts black and white and keeps yellow fixed, as the brand's reverse rule does for the logo. Yellow never appears as text or a thin mark on a light ground: yellow fills always carry black ink (17.5:1), and where the dark theme writes in yellow (the H of the wordmark, pass marks, focus rings, slider and checkbox accents) the light theme writes in black.
- Backdrop: brand black or its light inverse with three pools of light. Each pool fades along an eased curve and is dithered, and a fine fixed grain covers the room, so wide faint gradients show no steps or edges on 8-bit screens.
- Glass: a pane is a translucent tint, a rim lit from above, a faint sheen from its top-left corner, and a soft shadow. Panes that overlap other content (the spine and the cards) also blur what is behind them. Avalonia has no backdrop blur, so the pane copies the pixels already painted beneath it and draws them back through a Skia blur; it keeps its own copy of that backdrop so a partial repaint cannot blur the finished glass into itself. The copy is kept at a quarter of the size, on the GPU when there is one, so a blurred pane costs about a sixteenth of a full-size blur. Panes that sit directly on the backdrop skip the blur, because it would show nothing.
- Shape: interactive controls are pills, glass panes use 24 to 26-pixel corners, inputs and swatches 12 to 14.
- Type: IBM Plex Sans Arabic for the whole interface, Arabic and Latin, at 12/14/16/26 pixels: open counters and even stroke weight keep it clear at small sizes. Codes and measurements use the system monospace face, always left to right.
- Icons: Tabler outline icons drawn in the inherited text color, so they follow their button state.
- Motion: every movement reports a state change (hover, press, page change, card step, card flip, theme change). Liquid motion means an overshoot on release and a squash on press. Responses start on the frame of the input and finish within 300 ms: the tool marker moves before the page is built, and the theme turns by restyling what is on screen, without rebuilding it. The "تقليل الحركة" setting removes all travel and keeps the state changes.
- Tooltips are separate windows and cannot see the page behind them, so they use an opaque raised fill with the same rim.
