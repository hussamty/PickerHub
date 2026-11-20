<script lang="ts">
  // Light and dark as one physical switch (desktop/ThemeSwitch.cs). A frosted knob slides in a
  // recessed track; its pixel moon bursts apart and gathers again as a sun while a rim light travels
  // round the knob. Clicking plays the change; dragging the knob scrubs it.
  import { onMount } from "svelte";
  import { app } from "../lib/app.svelte";
  import { t } from "../lib/i18n";

  const W = 80, H = 44, Inset = 4, Knob = H - Inset * 2, Travel = W - Inset * 2 - Knob, Cell = 2;
  const Moon = ["...###...", "..###....", ".###...#.", ".###.....", ".###.....", ".####....", "..######.", "...####..", "........."];
  const Sun = ["....#....", ".#.....#.", "...###...", "..#####..", "#.#####.#", "..#####..", "...###...", ".#.....#.", "....#...."];

  // The same seeded plan as 1.x: each pixel's start in the moon, end in the sun, and outward throw.
  function cells(art: string[]) {
    const out: [number, number][] = [];
    art.forEach((row, y) => [...row].forEach((c, x) => c === "#" && out.push([x, y])));
    return out;
  }
  function seeded(seed: number) {
    let s = seed >>> 0;
    return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296);
  }
  const moon = cells(Moon), sun = cells(Sun), rnd = seeded(7);
  const pixels = Array.from({ length: Math.max(moon.length, sun.length) }, (_, i) => {
    const a = rnd() * Math.PI * 2, reach = 2.5 + rnd() * 4;
    return { from: moon[i % moon.length], to: sun[(i * 5) % sun.length], fling: [Math.cos(a) * reach, Math.sin(a) * reach] };
  });

  let canvas: HTMLCanvasElement;
  let t01 = $state(app.theme === "light" ? 1 : 0);
  let dragging = false, moved = false, pressX = 0, pressT = 0, raf = 0;

  const mix = (a: number[], b: number[], k: number) => a.map((v, i) => v + (b[i] - v) * k);
  const rgba = (c: number[]) => `rgba(${c[0] | 0},${c[1] | 0},${c[2] | 0},${(c[3] ?? 255) / 255})`;
  const hex = (n: number) => [(n >> 16) & 255, (n >> 8) & 255, n & 255, (n >>> 24) & 255];

  function draw() {
    const ctx = canvas?.getContext("2d");
    if (!ctx) return;
    const dpr = window.devicePixelRatio || 1;
    if (canvas.width !== W * dpr) { canvas.width = W * dpr; canvas.height = H * dpr; }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, W, H);
    const k = t01;
    const blend = (d: number, l: number) => rgba(mix(hex(d), hex(l), k));
    // The well: darker at the top lip, a thin catch of light along the bottom.
    let g = ctx.createLinearGradient(0, 0, 0, H);
    g.addColorStop(0, blend(0xff000000, 0xffc9c7be));
    g.addColorStop(1, blend(0xff1d1d1b, 0xffe9e7e0));
    ctx.fillStyle = g;
    ctx.beginPath(); ctx.roundRect(0, 0, W, H, 15); ctx.fill();
    // Knob, with its rim light climbing from beneath (dark) round the right side to the top (light).
    const angle = ((90 - 180 * k) * Math.PI) / 180, tx = Math.cos(angle), ty = Math.sin(angle);
    const x = Inset + Travel * k, y = Inset;
    const glow = mix(hex(0xfff2f2ea), hex(0xfffff000), k);
    ctx.save();
    ctx.shadowColor = rgba([glow[0], glow[1], glow[2], 128 + 102 * k]);
    ctx.shadowBlur = 12; ctx.shadowOffsetX = tx * 4; ctx.shadowOffsetY = ty * 4;
    g = ctx.createLinearGradient(0, y, 0, y + Knob);
    g.addColorStop(0, blend(0xff45464a, 0xffffffff));
    g.addColorStop(1, blend(0xff2b2c2f, 0xffefeee8));
    ctx.fillStyle = g;
    ctx.beginPath(); ctx.roundRect(x, y, Knob, Knob, 11); ctx.fill();
    ctx.restore();
    const cx = x + Knob / 2, cy = y + Knob / 2;
    g = ctx.createLinearGradient(cx + tx * Knob / 2, cy + ty * Knob / 2, cx - tx * Knob / 2, cy - ty * Knob / 2);
    g.addColorStop(0, rgba(glow)); g.addColorStop(0.55, rgba([glow[0], glow[1], glow[2], 0]));
    ctx.strokeStyle = g; ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.roundRect(x + 0.75, y + 0.75, Knob - 1.5, Knob - 1.5, 10.25); ctx.stroke();
    // Each pixel leaves the moon, is thrown outward at the midpoint, and lands in the sun.
    const eased = k * k * (3 - 2 * k), burst = Math.sin(Math.PI * k);
    ctx.fillStyle = blend(0xfff4f4ee, 0xff111111);
    const ox = cx - (Moon.length * Cell) / 2, oy = cy - (Moon.length * Cell) / 2;
    for (const p of pixels) {
      const px = p.from[0] + (p.to[0] - p.from[0]) * eased, py = p.from[1] + (p.to[1] - p.from[1]) * eased;
      const ax = ox + px * Cell + p.fling[0] * burst, ay = oy + py * Cell + p.fling[1] * burst;
      ctx.fillRect(Math.round(ax * 2) / 2, Math.round(ay * 2) / 2, Cell, Cell);
    }
  }

  function set(v: number) {
    const before = t01 >= 0.5;
    t01 = v;
    draw();
    if ((t01 >= 0.5) !== before) app.setting("theme", t01 >= 0.5 ? "light" : "dark");
  }
  function glide(target: number) {
    cancelAnimationFrame(raf);
    if (!app.motion) { set(target); return; }
    const from = t01, start = performance.now(), span = 150 + 230 * Math.abs(target - from);
    const step = (now: number) => {
      const p = Math.min(1, (now - start) / span);
      set(from + (target - from) * (1 - Math.pow(1 - p, 3)));
      if (p < 1) raf = requestAnimationFrame(step);
    };
    raf = requestAnimationFrame(step);
  }

  // Follow the saved theme when it changes elsewhere (settings, system theme).
  $effect(() => {
    const want = app.theme === "light" ? 1 : 0;
    if (!dragging && !raf && Math.round(t01) !== want) { t01 = want; draw(); }
  });
  onMount(() => { draw(); if (app.ws?.followSystemTheme) t01 = app.theme === "light" ? 1 : 0; });

  function down(e: PointerEvent) {
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
    cancelAnimationFrame(raf); raf = 0;
    dragging = true; moved = false; pressX = e.clientX; pressT = t01;
  }
  function move(e: PointerEvent) {
    if (!dragging) return;
    const dx = e.clientX - pressX;
    if (Math.abs(dx) > 3) moved = true;
    if (moved) set(Math.min(1, Math.max(0, pressT + dx / Travel)));
  }
  function up() {
    if (!dragging) return;
    dragging = false;
    if (app.ws?.followSystemTheme) app.setting("followSystemTheme", false);
    glide(moved ? (t01 >= 0.5 ? 1 : 0) : t01 >= 0.5 ? 0 : 1);
    setTimeout(() => (raf = 0), 500);
  }
</script>

<button
  class="switch"
  dir="ltr"
  role="switch"
  aria-checked={t01 >= 0.5}
  aria-label={t("تبديل المظهر بين الفاتح والداكن")}
  onpointerdown={down}
  onpointermove={move}
  onpointerup={up}
  onpointercancel={up}
  onkeydown={(e) => { if (e.key === " " || e.key === "Enter") { e.preventDefault(); glide(t01 >= 0.5 ? 0 : 1); } }}
>
  <canvas bind:this={canvas} style:width="{W}px" style:height="{H}px"></canvas>
</button>

<style>
  .switch {
    all: unset;
    display: block;
    width: 80px;
    height: 44px;
    border-radius: 15px;
    cursor: pointer;
    touch-action: none;
  }
  .switch:focus-visible {
    outline: 2px solid var(--color-brand);
    outline-offset: 3px;
  }
</style>
