<script lang="ts">
  // The image guide (desktop/ColorGuide.cs): the picture in a frame of frosted glass, a glass mark on
  // each of its colors, and a leader from every mark out to a glass label naming the color, its code and
  // its share. The leaders draw outward one after another, largest color first, and each label settles
  // in as its leader reaches it. Pointing at a label lights its leader and mark; clicking it opens that
  // color's actions. The picture inside stays the image sampler, so any pixel can still be picked.
  import { onMount } from "svelte";
  import { app } from "../lib/app.svelte";
  import { t } from "../lib/i18n";
  import { GUTTER, layout, path, type Placement } from "../lib/callouts";
  import type { ImageColor } from "../lib/types";

  let { src, width: bw, height: bh, colors, play, cursor = null, onopen, onpixel, onkey }: {
    src: string; width: number; height: number; colors: ImageColor[]; play: number;
    cursor?: { x: number; y: number } | null;
    onopen: (c: ImageColor) => void; onpixel: (fx: number, fy: number) => void; onkey: (e: KeyboardEvent) => void;
  } = $props();

  const PAD = 12, DISC = 11, MAX_H = 460;
  let width = $state(900);
  let canvas: HTMLCanvasElement;
  let hot = $state(-1);
  let started = 0, raf = 0;
  let labelState = $state<{ o: number; dx: number }[]>([]);

  const geo = $derived.by(() => {
    const n = colors.length;
    const twoSided = width >= 780 && n > 1;
    const compact = width < 560;
    const label = compact ? { w: 146, h: 40 } : { w: 196, h: 48 };
    const reserve = label.w + GUTTER + 4;
    const roomW = Math.max(80, width - (twoSided ? 2 : 1) * reserve - 2 * PAD);
    const scale = Math.min(roomW / bw, MAX_H / bh);
    const frame = { w: bw * scale + 2 * PAD, h: bh * scale + 2 * PAD };
    const perSide = twoSided ? n - Math.floor(n / 2) : n;
    const height = Math.max(frame.h, perSide * (label.h + 8)) + 12;
    // One column follows the picture in reading order: on its right in English, on its left in Arabic.
    const labelsRight = app.language !== "ar";
    const x = twoSided ? (width - frame.w) / 2 : Math.max(0, (width - frame.w - reserve) / 2) + (labelsRight ? 0 : reserve);
    const rect = { x, y: (height - frame.h) / 2, w: frame.w, h: frame.h };
    const inner = { x: rect.x + PAD, y: rect.y + PAD, w: rect.w - 2 * PAD, h: rect.h - 2 * PAD };
    const marks = colors.map((c) => ({ x: inner.x + (c.x ?? 0.5) * inner.w, y: inner.y + (c.y ?? 0.5) * inner.h }));
    const placements = layout(marks, rect, label, twoSided, labelsRight, 8, DISC + 5);
    return { height, rect, inner, placements, label, compact };
  });

  // ---- Motion: marks pop in, leaders draw outward, labels settle as their leaders arrive. ----
  const POP = 0.26, DRAW = 0.58, STAGGER = 0.075;
  const popAt = (i: number) => i * STAGGER, drawAt = (i: number) => 0.14 + i * STAGGER;
  let elapsed = Infinity;
  function markScale(i: number) {
    const t = Math.min(1, Math.max(0, (elapsed - popAt(i)) / POP));
    if (t >= 1) return 1;
    const back = 1.7, u = t - 1; // a small overshoot, then rest
    return 1 + u * u * ((back + 1) * u + back);
  }
  const leaderProgress = (i: number) => 1 - Math.pow(1 - Math.min(1, Math.max(0, (elapsed - drawAt(i)) / DRAW)), 3);

  function frameTick(now: number) {
    elapsed = (now - started) / 1000;
    labelState = geo.placements.map((p, i) => {
      const r = Math.min(1, Math.max(0, (elapsed - drawAt(i) - DRAW * 0.7) / 0.24)), e = 1 - Math.pow(1 - r, 3);
      return { o: e, dx: (p.left ? 10 : -10) * (1 - e) }; // each label slides in from the frame's side
    });
    draw();
    if (elapsed > drawAt(Math.max(0, colors.length - 1)) + DRAW + 0.3) { elapsed = Infinity; labelState = []; draw(); raf = 0; return; }
    raf = requestAnimationFrame(frameTick);
  }
  // A new picture, or Show all / Show top five, draws out afresh.
  $effect(() => {
    void play;
    cancelAnimationFrame(raf);
    if (app.motion && play > 0) { started = performance.now(); elapsed = 0; labelState = colors.map(() => ({ o: 0, dx: 0 })); raf = requestAnimationFrame(frameTick); }
    else { elapsed = Infinity; labelState = []; requestAnimationFrame(draw); }
  });
  $effect(() => { void geo; void hot; void app.theme; if (!raf) requestAnimationFrame(draw); });

  function draw() {
    const ctx = canvas?.getContext("2d");
    if (!ctx) return;
    const dpr = devicePixelRatio || 1, H = geo.height;
    if (canvas.width !== Math.round(width * dpr) || canvas.height !== Math.round(H * dpr)) { canvas.width = Math.round(width * dpr); canvas.height = Math.round(H * dpr); }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, width, H);
    const dark = app.theme === "dark";
    // Ink of the theme with a halo of its opposite: a leader reads on any picture and on the page.
    const ink = dark ? "#ffffff" : "#111111", halo = dark ? "rgba(0,0,0,0.59)" : "rgba(255,255,255,0.78)";
    geo.placements.forEach((p, i) => {
      const progress = leaderProgress(i);
      if (progress > 0) leader(ctx, p, progress, hot === i ? "#FFF000" : ink, hot === i ? "rgba(17,17,17,0.75)" : halo, hot === i ? 2.4 : 1.6);
    });
    geo.placements.forEach((p, i) => {
      const s = markScale(i);
      if (s > 0) mark(ctx, p.mark, colors[i].hex, s, dark, hot === i);
    });
  }
  function leader(ctx: CanvasRenderingContext2D, p: Placement, progress: number, ink: string, halo: string, w: number) {
    const pts = path(p).map((q) => ({ ...q }));
    const dx = pts[1].x - pts[0].x, dy = pts[1].y - pts[0].y, reach = Math.hypot(dx, dy);
    if (reach < 1) return;
    // Leave the mark's glass disc clear: the leader starts at its rim, then grows outward along its path.
    const off = Math.min(reach * 0.5, DISC + 1.5);
    pts[0] = { x: pts[0].x + (dx / reach) * off, y: pts[0].y + (dy / reach) * off };
    let total = 0;
    for (let i = 0; i + 1 < pts.length; i++) total += Math.hypot(pts[i + 1].x - pts[i].x, pts[i + 1].y - pts[i].y);
    let along = progress * total;
    ctx.beginPath();
    ctx.moveTo(pts[0].x, pts[0].y);
    for (let i = 0; i + 1 < pts.length && along > 0; i++) {
      const piece = Math.hypot(pts[i + 1].x - pts[i].x, pts[i + 1].y - pts[i].y), k = Math.min(1, along / Math.max(0.001, piece));
      ctx.lineTo(pts[i].x + (pts[i + 1].x - pts[i].x) * k, pts[i].y + (pts[i + 1].y - pts[i].y) * k);
      along -= piece;
    }
    ctx.lineCap = "round"; ctx.lineJoin = "round";
    ctx.strokeStyle = halo; ctx.lineWidth = w + 3; ctx.stroke();
    ctx.strokeStyle = ink; ctx.lineWidth = w; ctx.stroke();
    if (progress < 0.999) return;
    // The arrowhead points into the label.
    const sign = p.left ? -1 : 1, size = 6 + w;
    ctx.beginPath();
    ctx.moveTo(p.tip.x, p.tip.y);
    ctx.lineTo(p.tip.x - sign * size, p.tip.y - size * 0.62);
    ctx.lineTo(p.tip.x - sign * size, p.tip.y + size * 0.62);
    ctx.closePath();
    ctx.fillStyle = ink; ctx.fill(); ctx.strokeStyle = halo; ctx.lineWidth = 1.5; ctx.stroke();
  }
  // A disc of glass with the color's pixel at its centre, framed so it shows on any color.
  function mark(ctx: CanvasRenderingContext2D, at: { x: number; y: number }, hex: string, s: number, dark: boolean, isHot: boolean) {
    const r = DISC * s;
    const shadow = ctx.createRadialGradient(at.x, at.y + 2 * s, 0, at.x, at.y + 2 * s, r + 6 * s);
    shadow.addColorStop(0, "rgba(0,0,0,0.35)"); shadow.addColorStop(1, "rgba(0,0,0,0)");
    ctx.fillStyle = shadow; ctx.beginPath(); ctx.arc(at.x, at.y + 2 * s, r + 6 * s, 0, Math.PI * 2); ctx.fill();
    const rim = ctx.createLinearGradient(0, at.y - r, 0, at.y + r);
    rim.addColorStop(0, "rgba(255,255,255,0.9)"); rim.addColorStop(1, "rgba(255,255,255,0.24)");
    ctx.beginPath(); ctx.arc(at.x, at.y, r, 0, Math.PI * 2);
    ctx.fillStyle = dark ? "rgba(255,255,255,0.27)" : "rgba(255,255,255,0.55)"; ctx.fill();
    ctx.strokeStyle = rim; ctx.lineWidth = 1.2; ctx.stroke();
    if (isHot) { ctx.beginPath(); ctx.arc(at.x, at.y, r + 3, 0, Math.PI * 2); ctx.strokeStyle = "#FFF000"; ctx.lineWidth = 2.2; ctx.stroke(); }
    const half = 4.5 * s;
    ctx.fillStyle = hex; ctx.fillRect(at.x - half, at.y - half, half * 2, half * 2);
    ctx.strokeStyle = "#fff"; ctx.lineWidth = 1.6; ctx.strokeRect(at.x - half, at.y - half, half * 2, half * 2);
    ctx.strokeStyle = "rgba(17,17,17,0.86)"; ctx.lineWidth = 0.9; ctx.strokeRect(at.x - half - 1.3, at.y - half - 1.3, half * 2 + 2.6, half * 2 + 2.6);
  }
  const name = (c: ImageColor) => (app.language === "en" ? c.english : c.name);
  const share = (c: ImageColor) => (c.percent < 10 ? c.percent.toFixed(1) : Math.round(c.percent)) + "%";
  function click(e: MouseEvent) {
    const el = (e.currentTarget as HTMLElement).querySelector("img")!, r = el.getBoundingClientRect();
    onpixel((e.clientX - r.left) / r.width, (e.clientY - r.top) / r.height);
  }
  onMount(() => () => cancelAnimationFrame(raf));
</script>

<div class="guide" dir="ltr" bind:clientWidth={width} style:height="{geo.height}px">
  <div
    class="frame glass"
    role="slider"
    tabindex="0"
    aria-label={t("ألوان الصورة")}
    aria-valuenow={cursor?.x ?? 0}
    style:left="{geo.rect.x}px"
    style:top="{geo.rect.y}px"
    style:width="{geo.rect.w}px"
    style:height="{geo.rect.h}px"
    onclick={click}
    onkeydown={onkey}
  >
    <div class="pic">
      <img {src} alt="" draggable="false" />
      {#if cursor}<span class="cursor" style:left="{((cursor.x + 0.5) / bw) * 100}%" style:top="{((cursor.y + 0.5) / bh) * 100}%"></span>{/if}
    </div>
  </div>
  <canvas bind:this={canvas} style:width="{width}px" style:height="{geo.height}px" aria-hidden="true"></canvas>
  {#each geo.placements as p, i (colors[i].hex + i)}
    {@const c = colors[i]}
    {@const s = labelState[i]}
    <button
      class="label glass"
      class:hot={hot === i}
      dir={app.language === "ar" ? "rtl" : "ltr"}
      style:left="{p.label.x}px"
      style:top="{p.label.y}px"
      style:width="{p.label.w}px"
      style:height="{p.label.h}px"
      style:opacity={s ? s.o : 1}
      style:transform={s ? `translateX(${s.dx}px)` : undefined}
      onpointerenter={() => (hot = i)}
      onpointerleave={() => hot === i && (hot = -1)}
      onfocus={() => (hot = i)}
      onblur={() => hot === i && (hot = -1)}
      onclick={() => onopen(c)}
      title={t("إجراءات اللون")}
      aria-label="{name(c)} {c.hex} {share(c)}"
    >
      <span class="chip" style:background={c.hex}></span>
      <span class="text">
        {#if !geo.compact}<span class="cname">{name(c)}</span>{/if}
        <span class="code meta">{c.hex}  {share(c)}</span>
      </span>
    </button>
  {/each}
</div>

<style>
  .guide {
    position: relative;
    width: 100%;
  }
  canvas {
    position: absolute;
    inset: 0;
    z-index: 2;
    pointer-events: none;
  }
  .frame {
    position: absolute;
    padding: 12px;
    box-sizing: border-box;
    border-radius: 22px;
    background:
      linear-gradient(160deg, rgb(255 255 255 / var(--sheen)) 0%, transparent 42%),
      var(--card-tint);
    cursor: crosshair;
  }
  .pic {
    position: relative;
    width: 100%;
    height: 100%;
  }
  .frame img {
    position: relative;
    display: block;
    width: 100%;
    height: 100%;
    border-radius: 12px;
  }
  .frame:focus-visible {
    outline: 2px solid var(--color-brand);
    outline-offset: 3px;
  }
  .cursor {
    position: absolute;
    width: 18px;
    height: 18px;
    translate: -50% -50%;
    margin: 0;
    border-radius: 999px;
    border: 2px solid var(--color-brand);
    box-shadow: 0 0 0 1px rgb(0 0 0 / 0.6);
    pointer-events: none;
  }
  .label {
    all: unset;
    position: absolute;
    z-index: 3;
    box-sizing: border-box;
    display: flex;
    align-items: center;
    gap: 9px;
    padding: 6px 10px;
    border-radius: 14px;
    background:
      linear-gradient(160deg, rgb(255 255 255 / var(--sheen)) 0%, transparent 42%),
      var(--sheet-tint);
    border: 1px solid var(--glass-rim);
    box-shadow: 0 6px 18px var(--glass-shade);
    cursor: pointer;
    will-change: transform, opacity;
    transition: box-shadow 0.2s, border-color 0.2s;
  }
  .label.hot {
    border-color: var(--color-brand);
    box-shadow: 0 6px 22px rgb(255 240 0 / 0.25);
  }
  .label:focus-visible {
    outline: 2px solid var(--color-brand);
  }
  .chip {
    width: 18px;
    height: 18px;
    flex: none;
    border-radius: 5px;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .text {
    display: flex;
    flex-direction: column;
    min-width: 0;
    gap: 1px;
  }
  /* Clipped for the ellipsis, so the line box must hold the dots under Kufi letters. */
  .cname {
    font-size: var(--text-label);
    font-weight: 600;
    line-height: 1.75;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .meta {
    font-size: var(--text-caption);
    color: var(--muted);
    white-space: pre;
    align-self: flex-start;
  }
</style>
