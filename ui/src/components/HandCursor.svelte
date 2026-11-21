<!-- The hand cursor: a brand-black glass hand with a white rim that reads on any ground and any
     sampled color, and yellow only where it sits on the black (the fingertip, the resting rim, the
     reticle between two black lines). It follows the mouse in a fixed layer, one transform per frame,
     with the index fingertip exactly on the pointer. The system cursor is hidden only while a mouse is
     in use (html.hand-cursor, app.css); touch, pens, forced colors and text fields keep the system one. -->
<script lang="ts">
  import { onMount } from "svelte";
  import { HOTSPOT, MARGIN, mode, want, type Mode, type Want } from "../lib/hand";

  let hand: HTMLDivElement;
  // Pose and visibility change a few times a second at most; the position, every frame, bypasses them.
  let current = $state<Mode>("idle");
  let visible = $state(false);
  const ripples: HTMLSpanElement[] = [];

  onMount(() => {
    const root = document.documentElement;
    const fine = matchMedia("(pointer: fine)");
    const forced = matchMedia("(forced-colors: active)");
    let x = 0;
    let y = 0;
    let frame = 0;
    let wanted: Want = "idle";
    let pressed = false;
    let ring = 0;

    const read = (target: EventTarget | null): Want =>
      target instanceof Element ? want(getComputedStyle(target).getPropertyValue("--hand")) : "idle";
    const pose = () => (current = mode(wanted, pressed));
    const paint = () => {
      frame = 0;
      hand.style.transform = `translate3d(${x - HOTSPOT.x - MARGIN}px, ${y - HOTSPOT.y - MARGIN}px, 0)`;
    };
    const hide = () => (visible = false);
    const use = (on: boolean) => {
      root.classList.toggle("hand-cursor", on);
      if (!on) hide();
    };
    // Only a mouse gets the hand; a touch or a pen gives the system cursor back until the mouse returns.
    const mouse = (e: PointerEvent) => {
      if (e.pointerType === "mouse" && fine.matches && !forced.matches) return true;
      use(false);
      return false;
    };

    const move = (e: PointerEvent) => {
      if (!mouse(e)) return;
      x = e.clientX;
      y = e.clientY;
      // A button released outside the window, or after a native window drag, sends no pointerup here.
      if (pressed && !(e.buttons & 1)) {
        pressed = false;
        pose();
      }
      if (!root.classList.contains("hand-cursor")) use(true);
      if (!visible) {
        paint(); // appear under the pointer rather than glide in from where it left
        visible = true;
      } else if (!frame) {
        frame = requestAnimationFrame(paint);
      }
    };
    const over = (e: PointerEvent) => {
      if (!mouse(e)) return;
      wanted = read(e.target);
      pose();
    };
    const down = (e: PointerEvent) => {
      if (!mouse(e) || e.button !== 0) return;
      pressed = true;
      wanted = read(e.target);
      if (pose() === "hidden") return;
      // A ring of yellow between black lines, centred on the exact point of the click.
      const el = ripples[ring++ % ripples.length];
      el.style.left = `${e.clientX}px`;
      el.style.top = `${e.clientY}px`;
      el.classList.remove("go");
      void el.offsetWidth; // restart the animation on a reused ring
      el.classList.add("go");
    };
    const up = (e: PointerEvent) => {
      pressed = false;
      wanted = read(e.target);
      pose();
    };
    const out = (e: MouseEvent) => {
      if (!e.relatedTarget) hide(); // left the window
    };
    const blur = () => {
      pressed = false;
      pose();
      hide();
    };

    const options = { capture: true, passive: true };
    window.addEventListener("pointermove", move, options);
    window.addEventListener("pointerover", over, options);
    window.addEventListener("pointerdown", down, options);
    window.addEventListener("pointerup", up, options);
    window.addEventListener("pointercancel", up, options);
    window.addEventListener("mouseout", out, options);
    window.addEventListener("dragover", hide, options); // the system draws the drag
    window.addEventListener("blur", blur);
    const media = () => !fine.matches || forced.matches ? use(false) : undefined;
    fine.addEventListener("change", media);
    forced.addEventListener("change", media);
    pose();

    return () => {
      cancelAnimationFrame(frame);
      window.removeEventListener("pointermove", move, options);
      window.removeEventListener("pointerover", over, options);
      window.removeEventListener("pointerdown", down, options);
      window.removeEventListener("pointerup", up, options);
      window.removeEventListener("pointercancel", up, options);
      window.removeEventListener("mouseout", out, options);
      window.removeEventListener("dragover", hide, options);
      window.removeEventListener("blur", blur);
      fine.removeEventListener("change", media);
      forced.removeEventListener("change", media);
      use(false);
    };
  });
</script>

<div class="hand" bind:this={hand} data-mode={current} data-visible={visible} aria-hidden="true">
  <svg viewBox="{-MARGIN} {-MARGIN} {28 + 2 * MARGIN} {28 + 2 * MARGIN}" width={28 + 2 * MARGIN} height={28 + 2 * MARGIN}>
    <defs>
      <linearGradient id="hand-sheen" gradientUnits="userSpaceOnUse" x1="5" y1="2" x2="17" y2="20">
        <stop offset="0" stop-color="#fff" stop-opacity="0.26" />
        <stop offset="0.55" stop-color="#fff" stop-opacity="0" />
      </linearGradient>
      <!-- Pointing: index finger up, three fingers folded, thumb tucked. -->
      <g id="hand-point">
        <rect x="7.5" y="2.7" width="4" height="13" rx="2" />
        <rect x="11.2" y="9.4" width="3.8" height="8" rx="1.9" />
        <rect x="14.7" y="10.4" width="3.7" height="8" rx="1.85" />
        <rect x="18.1" y="11.8" width="3.4" height="7" rx="1.7" />
        <rect x="4.3" y="11.6" width="3.8" height="8.6" rx="1.9" transform="rotate(-34 6.2 15.9)" />
        <path d="M7.5 13.2H21.5V19.6C21.5 23.1 18.9 25.6 15.4 25.6H13.4C10 25.6 7.5 23.1 7.5 19.6Z" />
      </g>
      <!-- Pressing: the tip stays on the point and the hand rises to it, so the finger shortens. -->
      <g id="hand-press">
        <rect x="7.5" y="2.7" width="4" height="10.8" rx="2" />
        <g transform="translate(0 -2.4)">
          <rect x="11.2" y="9.4" width="3.8" height="8" rx="1.9" />
          <rect x="14.7" y="10.4" width="3.7" height="8" rx="1.85" />
          <rect x="18.1" y="11.8" width="3.4" height="7" rx="1.7" />
          <rect x="4.3" y="11.6" width="3.8" height="8.6" rx="1.9" transform="rotate(-34 6.2 15.9)" />
          <path d="M7.5 13.2H21.5V19.6C21.5 23.1 18.9 25.6 15.4 25.6H13.4C10 25.6 7.5 23.1 7.5 19.6Z" />
        </g>
      </g>
      <!-- Open hand, fingers spread; the index tip keeps the point. -->
      <g id="hand-grab">
        <rect x="7.5" y="2.7" width="3.9" height="13" rx="1.95" />
        <rect x="11.5" y="2.2" width="3.9" height="13" rx="1.95" transform="rotate(4 13.4 15)" />
        <rect x="15.4" y="3.6" width="3.7" height="12" rx="1.85" transform="rotate(11 17.2 15)" />
        <rect x="19" y="6.4" width="3.3" height="10" rx="1.65" transform="rotate(19 20.6 15.5)" />
        <rect x="3.6" y="11.4" width="3.8" height="9.4" rx="1.9" transform="rotate(-48 5.5 16)" />
        <path d="M7.5 12.8H22V19.6C22 23.1 19.3 25.6 15.8 25.6H13.4C10 25.6 7.5 23.1 7.5 19.6Z" />
      </g>
      <!-- Closed fist: the index knuckle under the point, the thumb across the front. -->
      <g id="hand-fist">
        <rect x="7.5" y="2.4" width="3.9" height="8.4" rx="1.95" />
        <rect x="11.2" y="2.6" width="3.8" height="8.4" rx="1.9" />
        <rect x="14.8" y="3.2" width="3.7" height="8.2" rx="1.85" />
        <rect x="18.3" y="4.4" width="3.3" height="7.6" rx="1.65" />
        <path d="M7.5 6.8H21.6V13.4C21.6 16.9 19 19.4 15.5 19.4H13.4C10 19.4 7.5 16.9 7.5 13.4Z" />
      </g>
    </defs>

    <g class="figure">
      {#each [["point", "M11.25 10.6V13.4M14.75 11.6V14M18.15 13V15"], ["press", "M11.25 8.2V11M14.75 9.2V11.6M18.15 10.6V12.6M7.9 11.3H11.1"], ["grab", "M11.4 12.4V13.6M15.5 12.6V13.8M19 13.2V14.2"], ["fist", "M11.2 4.2V8M14.8 4.6V8.4M18.3 5.6V9M7.4 12.2C9.5 11 12.4 10.6 15.2 11.2"]] as [name, creases] (name)}
        <!-- One silhouette drawn four times: a white outline, the yellow rim, the black body, the sheen. -->
        <g class="pose {name}">
          <use href="#hand-{name}" class="outline" />
          <use href="#hand-{name}" class="rim" />
          <use href="#hand-{name}" class="body" />
          <use href="#hand-{name}" class="sheen" />
          <path class="crease" d={creases} />
          {#if name === "point" || name === "press"}<path class="tip" d="M7.5 6.1V4.7A2 2 0 0 1 11.5 4.7V6.1Z" />{/if}
        </g>
      {/each}
    </g>
    <!-- Precision: a hollow ring on the point, so the pixel under it stays in view. -->
    <g class="reticle">
      <circle cx={HOTSPOT.x} cy={HOTSPOT.y} r="3.4" stroke="#fff" stroke-width="2.6" />
      <circle cx={HOTSPOT.x} cy={HOTSPOT.y} r="3.4" stroke="#111" stroke-width="1.8" />
      <circle cx={HOTSPOT.x} cy={HOTSPOT.y} r="3.4" stroke="#fff000" stroke-width="0.9" />
    </g>
  </svg>
</div>
{#each [0, 1, 2] as i (i)}
  <span class="ripple" bind:this={ripples[i]} aria-hidden="true"></span>
{/each}

<style>
  .hand,
  .ripple {
    position: fixed;
    top: 0;
    left: 0;
    z-index: var(--z-cursor);
    pointer-events: none;
  }
  .hand {
    width: 40px;
    height: 40px;
    will-change: transform;
    contain: layout style size;
    filter: drop-shadow(0 2px 3px rgb(0 0 0 / 0.35));
    opacity: 0;
    transition: opacity 0.12s;
  }
  .hand[data-visible="true"] {
    opacity: 1;
  }
  .hand[data-mode="hidden"] {
    visibility: hidden;
  }
  svg {
    display: block;
    overflow: visible;
  }
  .outline {
    fill: none;
    stroke: #fff;
    stroke-width: 2.4;
    stroke-linejoin: round;
  }
  .rim {
    fill: none;
    stroke: var(--color-brand);
    stroke-width: 0.9;
    stroke-linejoin: round;
    opacity: 0;
    transition: opacity 0.18s;
  }
  .body {
    fill: #111111;
  }
  .sheen {
    fill: url(#hand-sheen);
  }
  .crease {
    fill: none;
    stroke: #4a4a44;
    stroke-width: 0.7;
    stroke-linecap: round;
  }
  .tip {
    fill: var(--color-brand);
    transition: opacity 0.18s;
  }
  .pose {
    opacity: 0;
    transition: opacity 0.12s;
  }
  .reticle {
    fill: none;
    opacity: 0;
    transition: opacity 0.15s;
  }
  /* Every pose turns and scales about the fingertip, so the point never moves. With a view-box
     reference, the origin is in drawing units: (0, 0) is the drawing's corner, not the margin's. */
  .figure {
    transform-box: view-box;
    transform-origin: 9.5px 1.5px;
    transition: transform 0.22s var(--ease-spring);
  }

  /* Idle: the pointing hand at rest, leaning back, with a thin yellow rim. */
  [data-mode="idle"] .figure {
    transform: rotate(-14deg);
  }
  [data-mode="idle"] .rim {
    opacity: 0.85;
  }
  [data-mode="idle"] .tip {
    opacity: 0;
  }
  [data-mode="idle"] .point,
  [data-mode="pointer"] .point,
  [data-mode="precision"] .point {
    opacity: 1;
  }
  /* Something to click: upright, a little larger, the fingertip lit. */
  [data-mode="pointer"] .figure {
    transform: scale(1.08);
  }
  [data-mode="press"] .press {
    opacity: 1;
  }
  [data-mode="press"] .figure {
    transform: scale(0.9);
    transition-duration: 0.08s;
  }
  [data-mode="grab"] .grab {
    opacity: 1;
  }
  [data-mode="grabbing"] .fist {
    opacity: 1;
  }
  [data-mode="grabbing"] .figure {
    transform: scale(0.94);
  }
  /* Picking a pixel: the hand steps aside and a ring marks the point. */
  [data-mode="precision"] .figure {
    transform: translate(5px, 6px) scale(0.62);
  }
  [data-mode="precision"] .reticle {
    opacity: 1;
  }

  .ripple {
    width: 26px;
    height: 26px;
    margin: -13px 0 0 -13px;
    border-radius: 50%;
    border: 2px solid var(--color-brand);
    box-shadow:
      0 0 0 1px #111111,
      inset 0 0 0 1px #111111;
    opacity: 0;
  }
  .ripple:global(.go) {
    animation: ripple 0.42s var(--ease-liquid);
  }
  @keyframes ripple {
    from {
      opacity: 0.9;
      transform: scale(0.3);
    }
    to {
      opacity: 0;
      transform: scale(1.5);
    }
  }
</style>
