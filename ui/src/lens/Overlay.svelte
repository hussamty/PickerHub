<script lang="ts">
  // Wayland live lens: a full-screen surface that receives the pointer (the compositor gives events
  // only to a visible surface) and draws the lens beside it (desktop/PickerInput.cs). WebKitGTK
  // cannot make the surface see-through on Wayland, so it shows the screen itself: a frame taken
  // from the shared stream as the eyedropper opened. The lens reads that same frame, so what is
  // seen is exactly what is picked.
  import { onMount } from "svelte";
  import { invoke } from "@tauri-apps/api/core";
  import Lens, { type LensFrame } from "./Lens.svelte";

  const W = 232, GAP = 24;
  let backdrop = $state<string | null>(null);
  let frame = $state<LensFrame | null>(null);
  let zoom = $state(12);
  let at = $state({ x: -1000, y: -1000 });
  let pending = false;
  const height = $derived(frame?.multiple ? 390 : 314);
  const place = (pos: number, size: number, end: number) => (pos + GAP + size <= end ? pos + GAP : Math.max(0, pos - GAP - size));
  const lens = $derived({ x: place(at.x, W, innerWidth), y: place(at.y, height, innerHeight) });

  // One request per frame at most; the newest pointer position wins.
  async function sample() {
    pending = false;
    try {
      const r = await invoke<{ frame: LensFrame | null }>("lens_sample", { fx: at.x / innerWidth, fy: at.y / innerHeight });
      if (r.frame) frame = r.frame;
    } catch (e) {
      console.warn(e);
      invoke("lens_done", { hex: null });
    }
  }
  function move(x: number, y: number) {
    at = { x: Math.max(0, Math.min(innerWidth - 1, x)), y: Math.max(0, Math.min(innerHeight - 1, y)) };
    if (!pending) { pending = true; requestAnimationFrame(sample); }
  }
  function pick() {
    if (!frame) return;
    invoke("lens_done", { hex: frame.hex });
    if (frame.multiple) move(at.x, at.y);
  }
  function keys(e: KeyboardEvent) {
    const nudge: Record<string, [number, number]> = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
    if (e.key === "Escape") invoke("lens_done", { hex: null });
    else if (e.key === "Enter") pick();
    else if (nudge[e.key]) move(at.x + nudge[e.key][0], at.y + nudge[e.key][1]);
    else if (e.key === "+" || e.key === "=") zoom = Math.min(24, zoom + 2);
    else if (e.key === "-") zoom = Math.max(6, zoom - 2);
    else return;
    e.preventDefault();
  }
  onMount(async () => {
    backdrop = await invoke<string | null>("lens_backdrop");
    move(innerWidth / 2, innerHeight / 2);
  });
</script>

<svelte:window onkeydown={keys} />

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
  class="surface"
  onpointermove={(e) => move(e.clientX, e.clientY)}
  onpointerdown={(e) => { e.preventDefault(); if (e.button === 2) invoke("lens_done", { hex: null }); else if (e.button === 0) { move(e.clientX, e.clientY); pick(); } }}
  oncontextmenu={(e) => e.preventDefault()}
  onwheel={(e) => { zoom = Math.max(6, Math.min(24, zoom + (e.deltaY < 0 ? 2 : -2))); }}
>
  {#if backdrop}<img class="backdrop" src={backdrop} alt="" draggable="false" />{/if}
  {#if frame}
    <div class="lens" style:transform="translate({lens.x}px, {lens.y}px)"><Lens {frame} {zoom} /></div>
  {/if}
</div>

<style>
  .surface {
    position: fixed;
    inset: 0;
    cursor: crosshair;
    background: #000;
    overflow: hidden;
  }
  /* The frame covers the surface edge to edge, at the monitor's own pixels. */
  .backdrop {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    object-fit: fill;
    image-rendering: pixelated;
    pointer-events: none;
    user-select: none;
  }
  .lens {
    position: absolute;
    left: 0;
    top: 0;
    pointer-events: none;
    will-change: transform;
  }
</style>
