// Boundary labeling for the image guide (desktop/ColorGuide.cs, Callouts): one label per marked color
// in a column beside the frame, and a leader from each mark out to its label.
//
// The marks are split at their median x: the left half is labeled on the left and the right half on the
// right, so a leader never crosses to the other side and no left leader can meet a right one. Each column
// has one evenly spaced slot per label. Marks are matched to slots by minimum total leader length (the
// Hungarian method); a matching of least total length has no two straight leaders crossing, because
// swapping the ends of any crossing pair would shorten it. A leader that would graze another color's
// mark is penalised, and a final pass swaps any pair that then crosses, until none does. Each leader runs
// straight from its mark to an elbow just outside the frame, then level to its label, so the short
// outer tails, which share one x, never meet either.
export interface Point { x: number; y: number }
export interface Rect { x: number; y: number; w: number; h: number }
export interface Placement { index: number; label: Rect; mark: Point; elbow: Point; tip: Point; left: boolean; via?: Point }

export const GUTTER = 36;

export const path = (p: Placement): Point[] => (p.via ? [p.mark, p.via, p.elbow, p.tip] : [p.mark, p.elbow, p.tip]);
const dist = (a: Point, b: Point) => Math.hypot(a.x - b.x, a.y - b.y);

export function segmentDistance(p: Point, a: Point, b: Point) {
  const dx = b.x - a.x, dy = b.y - a.y, len = dx * dx + dy * dy;
  const t = len === 0 ? 0 : Math.min(1, Math.max(0, ((p.x - a.x) * dx + (p.y - a.y) * dy) / len));
  return dist(p, { x: a.x + t * dx, y: a.y + t * dy });
}
function foot(p: Point, a: Point, b: Point): Point {
  const dx = b.x - a.x, dy = b.y - a.y, len = dx * dx + dy * dy;
  const t = len === 0 ? 0 : Math.min(1, Math.max(0, ((p.x - a.x) * dx + (p.y - a.y) * dy) / len));
  return { x: a.x + t * dx, y: a.y + t * dy };
}
/** Whether two segments cross at a point inside both (touching ends do not count). */
export function cross(a: Point, b: Point, c: Point, d: Point) {
  const turn = (p: Point, q: Point, r: Point) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
  const d1 = turn(c, d, a), d2 = turn(c, d, b), d3 = turn(a, b, c), d4 = turn(a, b, d);
  return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
}
export function collide(a: Placement, b: Placement) {
  const pa = path(a), pb = path(b);
  for (let i = 0; i + 1 < pa.length; i++) for (let j = 0; j + 1 < pb.length; j++) if (cross(pa[i], pa[i + 1], pb[j], pb[j + 1])) return true;
  return false;
}
export function clearance(p: Placement, mark: Point) {
  const q = path(p);
  let least = Infinity;
  for (let i = 0; i + 1 < q.length; i++) least = Math.min(least, segmentDistance(mark, q[i], q[i + 1]));
  return least;
}

/** Minimum-cost assignment of rows to columns of a square matrix; returns the column for each row. */
export function hungarian(a: number[][]): number[] {
  const n = a.length;
  const u = new Array(n + 1).fill(0), v = new Array(n + 1).fill(0), p = new Array(n + 1).fill(0), way = new Array(n + 1).fill(0);
  for (let i = 1; i <= n; i++) {
    p[0] = i;
    let j0 = 0;
    const minv = new Array(n + 1).fill(Infinity), used = new Array(n + 1).fill(false);
    do {
      used[j0] = true;
      const i0 = p[j0];
      let j1 = 0, delta = Infinity;
      for (let j = 1; j <= n; j++) {
        if (used[j]) continue;
        const cur = a[i0 - 1][j - 1] - u[i0] - v[j];
        if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
        if (minv[j] < delta) { delta = minv[j]; j1 = j; }
      }
      for (let j = 0; j <= n; j++) {
        if (used[j]) { u[p[j]] += delta; v[j] -= delta; } else minv[j] -= delta;
      }
      j0 = j1;
    } while (p[j0] !== 0);
    do { const j1 = way[j0]; p[j0] = p[j1]; j0 = j1; } while (j0 !== 0);
  }
  const assign = new Array(n).fill(0);
  for (let j = 1; j <= n; j++) assign[p[j] - 1] = j - 1;
  return assign;
}

export function layout(marks: Point[], frame: Rect, label: { w: number; h: number }, twoSided: boolean, oneSideRight: boolean, spacing: number, clear: number): Placement[] {
  const n = marks.length;
  const out: Placement[] = new Array(n);
  if (!n) return out;
  const byX = [...marks.keys()].sort((a, b) => marks[a].x - marks[b].x || a - b);
  const left = twoSided ? Math.floor(n / 2) : oneSideRight ? 0 : n;
  side(byX.slice(0, left), true);
  side(byX.slice(left), false);
  return out;

  function grazes(from: Point, to: Point, self: number) {
    for (let i = 0; i < n; i++) if (i !== self && segmentDistance(marks[i], from, to) < clear) return true;
    return false;
  }
  function side(ids: number[], isLeft: boolean) {
    const k = ids.length;
    if (!k) return;
    // Evenly spread over the frame's height when there is room; packed and centred on it when not.
    const pitch = Math.max(label.h + spacing, frame.h / k), top = frame.y + frame.h / 2 - (pitch * k) / 2;
    const elbowX = isLeft ? frame.x - GUTTER / 2 : frame.x + frame.w + GUTTER / 2;
    const tipX = isLeft ? frame.x - GUTTER : frame.x + frame.w + GUTTER;
    const slots = ids.map((_, s) => ({ x: elbowX, y: top + pitch * (s + 0.5) }));
    const cost = ids.map((id) => slots.map((slot) => dist(marks[id], slot) + (grazes(marks[id], slot, id) ? 10_000 : 0)));
    const assign = hungarian(cost);
    for (let pass = 0, swaps = 1; pass < 64 && swaps > 0; pass++) {
      swaps = 0;
      for (let i = 0; i < k; i++) for (let j = i + 1; j < k; j++)
        if (cross(marks[ids[i]], slots[assign[i]], marks[ids[j]], slots[assign[j]])) { [assign[i], assign[j]] = [assign[j], assign[i]]; swaps++; }
    }
    ids.forEach((id, a) => {
      const slot = slots[assign[a]];
      out[id] = { index: id, label: { x: isLeft ? tipX - label.w : tipX, y: slot.y - label.h / 2, w: label.w, h: label.h }, mark: marks[id], elbow: slot, tip: { x: tipX, y: slot.y }, left: isLeft };
    });
    // Where straight leaders cannot keep clear of every mark (marks in a line, say), a leader bends once to
    // pass the mark it grazes at a safe distance, on the side it was already passing. The bend is kept only
    // if the new path crosses no other leader and grazes no other mark.
    for (const id of ids) {
      const p = out[id];
      let grazed = -1, worst = clear;
      for (let j = 0; j < n; j++) {
        if (j === id) continue;
        const d = segmentDistance(marks[j], p.mark, p.elbow);
        if (d < worst) { worst = d; grazed = j; }
      }
      if (grazed < 0) continue;
      const obstacle = marks[grazed], f = foot(obstacle, p.mark, p.elbow);
      let ax = f.x - obstacle.x, ay = f.y - obstacle.y;
      const len = Math.hypot(ax, ay);
      if (len < 0.01) { ax = 0; ay = p.elbow.y < p.mark.y ? -1 : 1; } else { ax /= len; ay /= len; }
      const bent: Placement = { ...p, via: { x: obstacle.x + ax * (clear + 2), y: obstacle.y + ay * (clear + 2) } };
      const clean = ids.every((o) => o === id || !collide(bent, out[o])) && marks.every((m, j) => j === id || clearance(bent, m) >= clear - 0.5);
      if (clean) out[id] = bent;
    }
  }
}
