# Palette of the day

One nature-inspired palette every 24 hours of use, opened from a sealed pack on its own page. It runs entirely on the device: no network, no account, no model.

## Where it lives

- `src/daily.rs` holds everything that decides anything: the catalog of 36 themes, the dark and light variants, the contrast check, the choice of theme, the cooldown, clock checks and the state file.
- `src/lib.rs` exposes it as one C function, `daily(request, len)`, taking JSON `{dir, action, value}` and returning JSON through the same output buffer as the other calls. Actions: `status`, `reveal`, `save`, `apply`, `dismiss`, `adjust` (value `lighter`, `darker`, `muted` or `vivid`).
- `desktop/Daily.cs` is the page and `desktop/PackStage.cs` its envelope-and-cards scene. It shows what the core returns and reports what the user did. It never sends a time: the core reads the clock itself.
- State is `daily_palette_state.json` in the application data folder, beside `workspace.json`.

## Palettes

A theme is a name, a one-line story, two mood tags, a primary and an accent color, and the hue and tint of its neutrals. From that the core builds seven roles (background, surface, primary, accent, text, secondary text, border) twice: a dark variant and a light one. The variants are built, not inverted: neutrals keep the theme's hue, primary and accent keep theirs, and lightness moves in HSL until each role reaches its contrast target against the ground it is used on.

Every variant must pass before it can be shown: text on background and surface at 7:1, secondary text at 4.5:1, primary at 4.5:1 with readable ink on it, accent at 3:1 with readable ink on it. A test checks all 36 themes in both variants at every adjustment step.

## Choosing

The choice depends only on the installation ID, the number of reveals so far, the recent history and the mood scores, so the same state always gives the same theme. The last 12 themes are excluded. Mood scores (save +2, apply +3, dismiss -2, each once per palette, clamped to ±6) pull toward what the user keeps; a per-reveal jitter separates equal candidates; and one reveal in five ignores the scores, so preference never ends variety. The four adjust buttons reshape the current palette and are remembered: the next palette starts from half of the learned direction.

## Cooldown and the clock

A reveal records `last_used_at_utc` and sets `next_available_at_utc` 86,400 seconds later. Until then every request returns the same palette. Opening the app or the page is not a use. When a new pack is due, the previous palette stays viewable and usable, and saving it does not spend the new pack.

Times are stored in UTC. While the process runs, a monotonic `Instant` gives a floor under the wall clock, so setting the clock back cannot stall or rewind the count and is reported. The file also keeps `last_seen_at_utc`, the latest time it has been open at. If the wall clock is more than two minutes behind either, the page shows an invalid-clock state with the earliest time to retry, and no pack opens.

Writes go to a temporary file, are flushed, then renamed over the old file, so an interrupted write leaves the old state or the new one. The file carries a keyed checksum; a file that fails it, or cannot be parsed, is replaced by a fresh state locked for a full 24 hours. A file written by a newer build is left untouched and reported.

### What this does not prevent

A local application cannot defend its state against the owner of the machine. Deleting the state file gives a fresh start. The checksum key is in this source, so anyone who reads it can forge a file. Restoring an old copy of the file restores its cooldown.

Moving the clock forward does open a pack early: across a restart there is nothing but the wall clock to go by. It cannot be repeated for free, though. The file then remembers the faked time, so returning to the real time is a rollback, and the feature stays locked until the real clock reaches the faked time plus 24 hours.

## Tests

```
cargo test --locked
```

`daily::tests` covers: catalog size, unique IDs and contrast; variant identity; time formatting; first reveal; same palette for a full day across restarts; a new palette at exactly 86,400 seconds; the previous palette staying available; clock set back; clock set forward then back; deterministic and non-repeating selection; preference scoring with variety; feedback counted once; adjustments; an edited or damaged file; atomic saves with a stray temporary file; migration of an old file and refusal of a newer one.

### Manual checklist for the page

1. First visit shows a sealed foil envelope in the middle of the page under "اكتشاف اليوم". It breathes slowly and leans toward the pointer. No state file is written until it is opened.
2. Clicking the envelope, pressing Enter on it, or "افتح الباقة" plays the opening once: the envelope shakes, the flap swings back with a small overshoot, the seven cards are pushed out one after another into a fan held in the pocket, confetti in six colors bursts from the mouth, the envelope falls away, and the fan opens into a row and settles with a soft bounce. The story pane then arrives beneath.
3. With "تقليل الحركة" on, the cards appear in their row at once, with no envelope motion and no confetti.
4. Leave the page and return, and restart the app: the same cards show at rest, without the opening, and the countdown continues.
5. A card under the pointer lifts, tilts toward the pointer and catches a band of light; its neighbours step aside. Clicking it, or Enter on a focused card, copies its code and the status line names what was copied.
6. The format select switches the code on every card between HEX, RGB and HSL; the variant select switches dark and light.
7. "حفظ كلوحة" adds a palette to the library and then reads "محفوظة في لوحاتك". "تطبيق على اللوحة الحالية" adds the colors to the selected palette once.
8. Set the system clock back an hour and reopen the page: the invalid-clock message appears with a retry time, and the cards are still usable.
9. Narrow the window: the cards overlap rather than shrink, and the row stays centred.

## The opening scene

`desktop/PackStage.cs` holds the scene. The envelope is drawn in two layers (back with lining, front pocket with the flap) so the cards can sit between them. Cards are ordinary controls posed with a perspective matrix, so the cards that fly out are the same ones that stay on the page. Every motion is a damped spring: a cue sets a target or gives a push, and the spring supplies the ease and the bounce, which is why nothing snaps or overshoots unnaturally at any frame rate. Confetti is drawn in two depth layers: far pieces are small and soft, a few near pieces are large, out of focus and drawn over everything.
