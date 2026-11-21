// The hand cursor's state: what the element under the pointer asks for, and what the hand shows.
//
// Elements ask through the inherited custom property `--hand` (set in app.css by role, and by a few
// components): `pointer` for anything that acts on a click, `grab` for what is dragged, `precision`
// for surfaces that pick a pixel, `text` where the system I-beam stays, and nothing (idle) elsewhere.
// A custom property, not `cursor`, because the native cursor is hidden by setting `cursor: none`.

export type Want = "idle" | "pointer" | "grab" | "precision" | "text";
export type Mode = "idle" | "pointer" | "press" | "grab" | "grabbing" | "precision" | "hidden";

/** The fingertip, in the hand's 28-unit drawing at 1 unit per CSS pixel: the top of the index finger's rim. */
export const HOTSPOT = { x: 9.5, y: 1.5 } as const;
/** Room around the drawing (the precision ring and the poses reach past it): the view box starts at -MARGIN. */
export const MARGIN = 6;

const WANTS: readonly string[] = ["idle", "pointer", "grab", "precision", "text"];

/** Reads `--hand` as computed for an element; anything unknown or unset is idle. */
export function want(value: string | null | undefined): Want {
  const v = (value ?? "").trim();
  return (WANTS.includes(v) ? v : "idle") as Want;
}

/** The pose for what the element wants and whether the main button is down. */
export function mode(wanted: Want, pressed: boolean): Mode {
  switch (wanted) {
    case "text":
      return "hidden"; // the system I-beam shows instead
    case "precision":
      return "precision";
    case "grab":
      return pressed ? "grabbing" : "grab";
    default:
      return pressed ? "press" : wanted;
  }
}
