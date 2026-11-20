<script lang="ts">
  // The lens (desktop/Magnifier.cs, MagnifierView): a block of brand black holding the magnified
  // pixels, the pixel under the pointer framed in signal yellow, and its value beneath. Drawn opaque
  // so it reads the same with or without a compositor. The zoom glides instead of jumping.
  import { onMount } from "svelte";
  import { t } from "../lib/i18n";

  export interface LensFrame { pixels: string; hex: string; count: number; recent: string[]; multiple: boolean }
  let { frame, zoom = 12 }: { frame: LensFrame | null; zoom?: number } = $props();

  const W = 232, PAD = 14, SIDE = W - 2 * PAD, R = 12, N = 2 * R + 1;
  const height = $derived(frame?.multiple ? 390 : 314);
  let canvas: HTMLCanvasElement;
  let shown = 12;
  let raf = 0;
  let rgb: Uint8Array = new Uint8Array(N * N * 3);

  $effect(() => {
    if (frame) rgb = Uint8Array.from(atob(frame.pixels), (c) => c.charCodeAt(0));
    void zoom;
    if (!raf) raf = requestAnimationFrame(draw);
  });

  function round(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number) {
    ctx.beginPath(); ctx.roundRect(x, y, w, h, r);
  }
  function draw() {
    raf = 0;
    const ctx = canvas?.getContext("2d");
    if (!ctx || !frame) return;
    if (Math.abs(zoom - shown) > 0.02) { shown += (zoom - shown) * 0.28; raf = requestAnimationFrame(draw); } else shown = zoom;
    const dpr = devicePixelRatio || 1;
    if (canvas.width !== W * dpr || canvas.height !== height * dpr) { canvas.width = W * dpr; canvas.height = height * dpr; }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, W, height);
    round(ctx, 0, 0, W, height, 24); ctx.fillStyle = "#111111"; ctx.fill();
    round(ctx, 0.5, 0.5, W - 1, height - 1, 23.5); ctx.strokeStyle = "rgba(255,255,255,0.24)"; ctx.lineWidth = 1; ctx.stroke();

    // The number of pixels across is fractional while the zoom glides, so cells are laid out from the centre.
    const across = 25 - shown / 2, cell = SIDE / across, c = PAD + SIDE / 2;
    ctx.save();
    round(ctx, PAD, PAD, SIDE, SIDE, 14); ctx.clip();
    ctx.fillStyle = "#1C1C1A"; ctx.fillRect(PAD, PAD, SIDE, SIDE);
    for (let j = -R; j <= R; j++) for (let i = -R; i <= R; i++) {
      const x = c + (i - 0.5) * cell, y = c + (j - 0.5) * cell;
      if (x > PAD + SIDE || y > PAD + SIDE || x + cell < PAD || y + cell < PAD) continue;
      const k = ((j + R) * N + (i + R)) * 3;
      ctx.fillStyle = `rgb(${rgb[k]},${rgb[k + 1]},${rgb[k + 2]})`;
      ctx.fillRect(x, y, cell + 0.5, cell + 0.5);
    }
    if (cell >= 9) {
      ctx.strokeStyle = "rgba(17,17,17,0.15)"; ctx.lineWidth = 1; ctx.beginPath();
      for (let i = -R; i <= R + 1; i++) {
        const at = Math.round(c + (i - 0.5) * cell) + 0.5;
        ctx.moveTo(at, PAD); ctx.lineTo(at, PAD + SIDE); ctx.moveTo(PAD, at); ctx.lineTo(PAD + SIDE, at);
      }
      ctx.stroke();
    }
    ctx.restore();
    round(ctx, PAD + 0.5, PAD + 0.5, SIDE - 1, SIDE - 1, 13.5); ctx.strokeStyle = "rgba(255,255,255,0.24)"; ctx.stroke();
    // The chosen pixel: yellow between two black lines, so it shows on any color.
    const t0 = c - cell / 2;
    round(ctx, t0 - 3, t0 - 3, cell + 6, cell + 6, 4); ctx.strokeStyle = "#000"; ctx.lineWidth = 1; ctx.stroke();
    round(ctx, t0 - 1.5, t0 - 1.5, cell + 3, cell + 3, 3); ctx.strokeStyle = "#FFF000"; ctx.lineWidth = 2; ctx.stroke();
    round(ctx, t0, t0, cell, cell, 1.5); ctx.strokeStyle = "#000"; ctx.lineWidth = 1; ctx.stroke();

    const row = PAD + SIDE + 16, k = (R * N + R) * 3;
    ctx.beginPath(); ctx.arc(PAD + 19, row + 19, 18, 0, Math.PI * 2);
    ctx.fillStyle = `rgb(${rgb[k]},${rgb[k + 1]},${rgb[k + 2]})`; ctx.fill(); ctx.strokeStyle = "#fff"; ctx.lineWidth = 2; ctx.stroke();
    const mono = '"Cascadia Mono","JetBrains Mono","DejaVu Sans Mono",monospace';
    ctx.textBaseline = "top"; ctx.direction = "ltr"; ctx.textAlign = "left";
    ctx.fillStyle = "#fff"; ctx.font = `bold 20px ${mono}`; ctx.fillText(frame.hex, PAD + 50, row - 1);
    ctx.fillStyle = "#ADADA5"; ctx.font = `bold 11px ${mono}`; ctx.fillText(`RGB ${rgb[k]}, ${rgb[k + 1]}, ${rgb[k + 2]}`, PAD + 50, row + 25);
    if (frame.multiple) {
      ctx.strokeStyle = "rgba(255,255,255,0.24)"; ctx.beginPath(); ctx.moveTo(PAD, 283.5); ctx.lineTo(W - PAD, 283.5); ctx.stroke();
      const label = frame.count === 0 ? t("كل نقرة تضيف لوناً إلى اللوحة") : t("أُضيف إلى اللوحة: ") + frame.count;
      ctx.font = `bold 12px "Noto Kufi Arabic", "Plus Jakarta Sans", sans-serif`; ctx.textAlign = "center";
      ctx.fillStyle = frame.count === 0 ? "#ADADA5" : "#FFF000"; ctx.fillText(label, W / 2, 291);
      ctx.textAlign = "left";
      frame.recent.forEach((hex, n) => { round(ctx, PAD + n * 34, 321, 28, 22, 7); ctx.fillStyle = hex; ctx.fill(); ctx.strokeStyle = "rgba(255,255,255,0.24)"; ctx.stroke(); });
    }
    // Keys, as small caps beside what they do.
    const y = height - 30;
    let x = key(ctx, "Esc", t(frame.multiple ? "إنهاء" : "إلغاء"), W - PAD, y);
    key(ctx, "+ −", t("تكبير"), x - 12, y);
  }
  function key(ctx: CanvasRenderingContext2D, cap: string, label: string, right: number, y: number) {
    ctx.font = `11px "Noto Kufi Arabic", "Plus Jakarta Sans", sans-serif`;
    const tw = ctx.measureText(label).width;
    ctx.font = `bold 10px "Plus Jakarta Sans", sans-serif`;
    const cw = ctx.measureText(cap).width + 12, left = right - tw - 6 - cw;
    ctx.fillStyle = "#ADADA5"; ctx.font = `11px "Noto Kufi Arabic", "Plus Jakarta Sans", sans-serif`; ctx.fillText(label, right - tw, y + 2);
    round(ctx, left, y, cw, 20, 6); ctx.fillStyle = "rgba(255,255,255,0.14)"; ctx.fill();
    ctx.fillStyle = "#fff"; ctx.font = `bold 10px "Plus Jakarta Sans", sans-serif`; ctx.fillText(cap, left + 6, y + 4);
    return left;
  }
  onMount(() => () => cancelAnimationFrame(raf));
</script>

<canvas bind:this={canvas} style:width="{W}px" style:height="{height}px" aria-hidden="true"></canvas>
