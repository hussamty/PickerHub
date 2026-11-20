// Damped springs as CSS easing (desktop/PackStage.cs moves everything with springs). A spring is
// sampled once into a `linear()` easing, so the browser runs it on the compositor at any frame rate.
export function spring(stiffness = 170, damping = 16, mass = 1): { easing: string; duration: number } {
  const dt = 1 / 120;
  let x = 0, v = 0, t = 0;
  const points: number[] = [];
  // Simulate until the spring is at rest within a hair of its target.
  while (t < 3) {
    const a = (-stiffness * (x - 1) - damping * v) / mass;
    v += a * dt;
    x += v * dt;
    t += dt;
    points.push(x);
    if (Math.abs(x - 1) < 0.0008 && Math.abs(v) < 0.01) break;
  }
  const step = Math.max(1, Math.floor(points.length / 60));
  const sampled = points.filter((_, i) => i % step === 0);
  sampled[sampled.length - 1] = 1;
  return { easing: `linear(0, ${sampled.map((p) => p.toFixed(4)).join(", ")})`, duration: Math.round(t * 1000) };
}

/** Animates one element with a spring; resolves when it lands. */
export function springTo(el: Element, keyframes: Keyframe[], opts: { stiffness?: number; damping?: number; delay?: number } = {}) {
  const s = spring(opts.stiffness, opts.damping);
  const anim = el.animate(keyframes, { duration: s.duration, easing: s.easing, delay: opts.delay ?? 0, fill: "forwards" });
  return anim.finished.then(() => { anim.commitStyles?.(); anim.cancel(); });
}
