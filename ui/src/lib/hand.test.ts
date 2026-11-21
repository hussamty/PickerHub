import { describe, expect, it } from "vitest";
import { mode, want } from "./hand";

describe("hand cursor", () => {
  it("reads --hand, treating unknown and unset values as idle", () => {
    expect(want(" pointer")).toBe("pointer");
    expect(want("grab ")).toBe("grab");
    expect(want("")).toBe("idle");
    expect(want(null)).toBe("idle");
    expect(want("crosshair")).toBe("idle");
  });

  it("presses, grips and keeps precision while the button is down", () => {
    expect(mode("idle", false)).toBe("idle");
    expect(mode("idle", true)).toBe("press");
    expect(mode("pointer", false)).toBe("pointer");
    expect(mode("pointer", true)).toBe("press");
    expect(mode("grab", false)).toBe("grab");
    expect(mode("grab", true)).toBe("grabbing");
    expect(mode("precision", true)).toBe("precision");
  });

  it("gives text fields back to the system I-beam", () => {
    expect(mode("text", false)).toBe("hidden");
    expect(mode("text", true)).toBe("hidden");
  });
});
