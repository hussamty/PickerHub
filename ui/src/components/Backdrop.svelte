<!-- The room, dithered. app.css draws the same three pools of light as CSS gradients; this canvas
     paints them again at device resolution in a worker (lib/room.worker.ts), with a dither applied
     before the colors are rounded to 8 bits, and fades in over the CSS version. The CSS version shows
     on its own until the first paint and for the moment a theme change takes to repaint. -->
<script lang="ts">
  import { onMount } from "svelte";
  import { app } from "../lib/app.svelte";
  import type { Job, Pool, Rgba } from "../lib/room.worker";

  // The same pools as .room in app.css: center as a fraction of the window, radius in vmax, and the
  // falloff stops of its color-mix() ramp.
  const POOLS: { name: string; x: number; y: number; r: number; stops: [number, number][] }[] = [
    { name: "--pool-yellow", x: 0.12, y: -0.05, r: 72, stops: [[0, 1], [0.18, 0.72], [0.36, 0.42], [0.54, 0.18], [0.7, 0.05], [0.84, 0]] },
    { name: "--pool-neutral", x: 0.95, y: 1.05, r: 80, stops: [[0, 1], [0.24, 0.6], [0.48, 0.24], [0.68, 0.06], [0.84, 0]] },
    { name: "--pool-core", x: 0.62, y: 0.6, r: 42, stops: [[0, 1], [0.32, 0.5], [0.58, 0.14], [0.8, 0]] },
  ];

  let canvas: HTMLCanvasElement;
  let shown = $state(false);
  let job = 0;
  let worker: Worker | null = null;
  let waiting = 0;

  // Any CSS color as 0..255 channels and alpha, through the canvas's own color parser.
  const probe = document.createElement("canvas").getContext("2d")!;
  function rgba(css: string): Rgba {
    probe.fillStyle = "#000";
    probe.fillStyle = css.trim();
    const v = String(probe.fillStyle);
    if (v.startsWith("#")) return [parseInt(v.slice(1, 3), 16), parseInt(v.slice(3, 5), 16), parseInt(v.slice(5, 7), 16), 1];
    const [r, g, b, a = "1"] = v.slice(v.indexOf("(") + 1, -1).split(",");
    return [+r, +g, +b, +a];
  }

  function paint() {
    if (!worker) return;
    const style = getComputedStyle(document.documentElement);
    const dpr = devicePixelRatio || 1, w = Math.round(innerWidth * dpr), h = Math.round(innerHeight * dpr);
    const vmax = Math.max(w, h) / 100;
    const pools: Pool[] = POOLS.map((p) => ({ x: p.x * w, y: p.y * h, r: p.r * vmax, color: rgba(style.getPropertyValue(p.name)), stops: p.stops }));
    const message: Job = { id: ++job, width: w, height: h, canvas: rgba(style.getPropertyValue("--canvas")), pools };
    worker.postMessage(message);
  }
  /** Repaints after things settle. A new theme hides the canvas at once, so the CSS room shows meanwhile. */
  function schedule(hide: boolean, delay: number) {
    if (hide) shown = false;
    clearTimeout(waiting);
    // After the frame that applies the new theme's variables.
    waiting = window.setTimeout(() => requestAnimationFrame(paint), delay);
  }

  onMount(() => {
    worker = new Worker(new URL("../lib/room.worker.ts", import.meta.url), { type: "module" });
    worker.onmessage = (e: MessageEvent<{ id: number; width: number; height: number; pixels: Uint8ClampedArray<ArrayBuffer> }>) => {
      const { id, width, height, pixels } = e.data;
      if (id !== job) return; // a newer paint is on its way
      canvas.width = width;
      canvas.height = height;
      canvas.getContext("2d")!.putImageData(new ImageData(pixels, width, height), 0, 0);
      shown = true;
    };
    const resized = () => schedule(false, 160); // stretched meanwhile: a smooth gradient survives that
    addEventListener("resize", resized);
    return () => { removeEventListener("resize", resized); clearTimeout(waiting); worker?.terminate(); worker = null; };
  });

  $effect(() => {
    void app.theme;
    schedule(true, 0);
  });
</script>

<canvas class="backdrop" class:shown bind:this={canvas} aria-hidden="true"></canvas>

<style>
  .backdrop {
    position: fixed;
    inset: 0;
    width: 100%;
    height: 100%;
    z-index: -1;
    pointer-events: none;
    opacity: 0;
    transition: opacity 0.25s;
  }
  .backdrop.shown {
    opacity: 1;
  }
</style>
