// Paints the room (app.css, .room) at device resolution with a true dither: every pixel's color is
// computed in floating point, triangular noise of about one 8-bit step is added, and only then is it
// rounded. Wide faint gradients then show neither bands nor a visible texture, which a noise image
// laid over finished 8-bit pixels cannot achieve. Runs off the main thread; see Backdrop.svelte.

/** A color as 0..255 channels and 0..1 alpha. */
export type Rgba = [number, number, number, number];
/** One pool of light: center and radius in device pixels, color, and its falloff as [t, factor] stops. */
export type Pool = { x: number; y: number; r: number; color: Rgba; stops: [number, number][] };
export type Job = { id: number; width: number; height: number; canvas: Rgba; pools: Pool[] };

/** The pool's strength at t (distance / radius): smooth between the same stops the CSS uses. */
function falloff(stops: [number, number][], t: number) {
  if (t <= stops[0][0]) return stops[0][1];
  for (let i = 1; i < stops.length; i++) {
    const [t1, f1] = stops[i];
    if (t <= t1) {
      const [t0, f0] = stops[i - 1];
      const u = (t - t0) / (t1 - t0), s = u * u * (3 - 2 * u); // C1 between stops: no Mach bands
      return f0 + (f1 - f0) * s;
    }
  }
  return stops[stops.length - 1][1];
}

self.onmessage = (e: MessageEvent<Job>) => {
  const { id, width, height, canvas, pools } = e.data;
  const out = new Uint8ClampedArray(width * height * 4);
  // Bottom to top, as CSS stacks background layers: the last pool in the list is painted first.
  const order = [...pools].reverse();
  let seed = 0x9e3779b9;
  const rand = () => {
    seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5;
    return (seed >>> 0) / 4294967296;
  };
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      let r = canvas[0], g = canvas[1], b = canvas[2];
      for (const p of order) {
        const dx = x - p.x, dy = y - p.y;
        const t = Math.sqrt(dx * dx + dy * dy) / p.r;
        const a = p.color[3] * falloff(p.stops, t);
        if (a <= 0) continue;
        r += (p.color[0] - r) * a; g += (p.color[1] - g) * a; b += (p.color[2] - b) * a;
      }
      // Triangular-PDF dither of +-1 step, one draw shared by the channels so it adds no color noise.
      const n = rand() + rand() - 1;
      const i = (y * width + x) * 4;
      // Uint8ClampedArray rounds to the nearest step on its own.
      out[i] = r + n; out[i + 1] = g + n; out[i + 2] = b + n; out[i + 3] = 255;
    }
  }
  (self as unknown as Worker).postMessage({ id, width, height, pixels: out }, [out.buffer]);
};
