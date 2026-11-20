// The image guide's leaders, as SelfTests.cs checks them in 1.x: across many random pictures no two
// leaders cross, no labels overlap, and each column's labels are evenly spaced.
import { describe, expect, it } from "vitest";
import { collide, layout, type Point } from "./callouts";

function seeded(seed: number) {
  let s = seed >>> 0;
  return () => (s = (s * 1664525 + 1013904223) >>> 0) / 4294967296;
}

describe("callouts", () => {
  it("never crosses leaders or overlaps labels", () => {
    const rnd = seeded(42);
    for (let run = 0; run < 400; run++) {
      const n = 1 + Math.floor(rnd() * 16);
      const frame = { x: 250, y: 0, w: 400 + rnd() * 200, h: 300 + rnd() * 160 };
      const marks: Point[] = Array.from({ length: n }, () => ({ x: frame.x + 12 + rnd() * (frame.w - 24), y: frame.y + 12 + rnd() * (frame.h - 24) }));
      const placed = layout(marks, frame, { w: 196, h: 48 }, n > 1, false, 8, 16);
      for (let i = 0; i < n; i++)
        for (let j = i + 1; j < n; j++) {
          if (placed[i].left === placed[j].left) {
            expect(collide(placed[i], placed[j]), `run ${run}: leaders ${i} and ${j} cross`).toBe(false);
            const a = placed[i].label, b = placed[j].label;
            expect(a.y + a.h <= b.y + 0.01 || b.y + b.h <= a.y + 0.01, `run ${run}: labels overlap`).toBe(true);
          }
        }
      for (const sideLeft of [true, false]) {
        const ys = placed.filter((p) => p.left === sideLeft).map((p) => p.label.y).sort((a, b) => a - b);
        for (let i = 2; i < ys.length; i++) expect(Math.abs(ys[i] - ys[i - 1] - (ys[1] - ys[0]))).toBeLessThan(0.01);
      }
    }
  });
});
