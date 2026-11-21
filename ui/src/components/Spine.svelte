<script lang="ts">
  // Edge navigation (desktop/Controls.cs, Spine). One pane of glass grown out of the window's right
  // edge through two concave shoulders. At rest it is a slim rail of icons; under the pointer or
  // keyboard focus it opens over the page and shows labels under a "Menu" header. One marker follows
  // the pointer from row to row and squashes on press; a second, filled marker shows the current tool
  // and slides when it changes. (Not .pill: that is the app's button utility, which adds a border.)
  import { onMount } from "svelte";
  import { app } from "../lib/app.svelte";
  import { t } from "../lib/i18n";
  import { TOOLS } from "../lib/tools";
  import Icon from "./Icon.svelte";

  const CLOSED = 58, OPEN = 236, LIP = 18, RADIUS = 22; // RADIUS matches the panes (--radius-pane)
  let el: HTMLElement;
  let list: HTMLElement;
  let size = $state({ w: CLOSED, h: 400 });
  let expanded = $state(false);
  let keyboard = false;
  let over = false;
  let closing = 0;
  let rowH = $state(44);
  let hoverY = $state(0), hoverShown = $state(false), hoverInstant = $state(true), squash = $state(false);
  let currentY = $state(0), currentShown = $state(false), currentInstant = $state(true);

  // The pane outline, as Glass.Shape() draws it: concave shoulders where it meets the edge.
  const shape = $derived.by(() => {
    const { w, h } = size, s = LIP, r = Math.max(0, Math.min(RADIUS, Math.min(w, h - 2 * s) / 2));
    return `M ${w} 0 A ${s} ${s} 0 0 1 ${w - s} ${s} L ${r} ${s} A ${r} ${r} 0 0 0 0 ${s + r} L 0 ${h - s - r} A ${r} ${r} 0 0 0 ${r} ${h - s} L ${w - s} ${h - s} A ${s} ${s} 0 0 1 ${w} ${h} Z`;
  });

  function expand(open: boolean) {
    if (expanded === open) return;
    expanded = open;
  }
  function enter() {
    over = true;
    clearTimeout(closing);
    expand(true);
  }
  function leave() {
    over = false;
    hoverShown = false;
    clearTimeout(closing);
    if (!keyboard) closing = window.setTimeout(() => { if (!over && !keyboard) expand(false); }, app.motion ? 160 : 0);
  }
  // A pill that is not showing appears in place; one that is showing travels.
  function follow(row: HTMLElement) {
    hoverInstant = !hoverShown;
    hoverY = row.offsetTop;
    hoverShown = true;
  }
  function mark() {
    const row = list?.querySelector<HTMLElement>(`[data-tool="${app.page}"]`);
    if (!row) return;
    currentInstant = !currentShown;
    currentY = row.offsetTop;
    currentShown = true;
  }
  $effect(() => { void app.page; void rowH; requestAnimationFrame(mark); });

  // Every row stays on screen: in a short window the rows shrink, down to 30 px, before anything is cut off.
  function fit() {
    const rows = TOOLS.length + 2, rules = 2;
    const chrome = 16 + 2 * (LIP + 7) + 3 * (rows + rules - 1) + rules * 11;
    rowH = Math.max(30, Math.min(44, Math.floor((window.innerHeight - 64 - chrome) / rows)));
  }

  onMount(() => {
    const ro = new ResizeObserver(([e]) => (size = { w: e.borderBoxSize[0].inlineSize, h: e.borderBoxSize[0].blockSize }));
    ro.observe(el);
    fit();
    addEventListener("resize", fit);
    return () => { ro.disconnect(); removeEventListener("resize", fit); };
  });
</script>

<!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
<nav
  class="spine"
  class:open={expanded}
  bind:this={el}
  aria-label={t("التنقل")}
  style:width="{expanded ? OPEN : CLOSED}px"
  onpointerenter={enter}
  onpointerleave={leave}
  onpointerdown={() => (squash = true)}
  onpointerup={() => (squash = false)}
  onfocusin={(e) => { if ((e.target as HTMLElement).matches(":focus-visible")) { keyboard = true; expand(true); follow(e.target as HTMLElement); } }}
  onfocusout={(e) => { if (!el.contains(e.relatedTarget as Node)) { keyboard = false; if (!over) { hoverShown = false; expand(false); } } }}
>
  <!-- One body of the panes' glass, built from shapes the GPU composites without repainting them on
       every frame of the opening: a rounded rectangle for the body (tint, sheen, blur, shadow) and
       two fixed shoulder pieces at the window edge with the same glass, cut to their curve by a mask.
       One rim runs round the whole outline. A path clip or a filtered shadow here dropped frames at 2x. -->
  <div class="body" aria-hidden="true"></div>
  <span class="shoulder top" aria-hidden="true"></span>
  <span class="shoulder bottom" aria-hidden="true"></span>
  <svg class="rim" width={size.w} height={size.h} aria-hidden="true"><path d={shape} /></svg>

  <div class="layers" bind:this={list} dir={app.language === "ar" ? "rtl" : "ltr"} style:--row="{rowH}px">
    <span class="marker active" class:shown={currentShown} class:instant={currentInstant} style:top="{currentY}px"></span>
    <span class="marker hover" class:shown={hoverShown} class:instant={hoverInstant} class:squash style:top="{hoverY}px"></span>
    <!-- A header, not a control: the nav's own label names it for assistive technology. -->
    <div class="row title" aria-hidden="true" onpointerenter={() => (hoverShown = false)}>
      <span class="ico"><Icon name="dots" size={20} /></span><span class="label">{t("القائمة")}</span>
    </div>
    <span class="rule"></span>
    {#each TOOLS as tool, i}
      <button class="row" class:active={app.page === i} data-tool={i} onpointerenter={(e) => follow(e.currentTarget)} onclick={() => (app.page = i)} aria-current={app.page === i ? "page" : undefined} aria-label={t(tool.title)}>
        <span class="ico"><Icon name={tool.icon} size={20} /></span><span class="label">{t(tool.title)}</span>
      </button>
    {/each}
    <span class="rule"></span>
    <button class="row" onpointerenter={(e) => follow(e.currentTarget)} onclick={() => (app.settingsOpen = true)} aria-label={t("الإعدادات")}>
      <span class="ico"><Icon name="settings" size={20} /></span><span class="label">{t("الإعدادات")}</span>
    </button>
  </div>
</nav>

<style>
  .spine {
    position: absolute;
    right: 0;
    top: 50%;
    translate: 0 calc(-50% - 8px);
    z-index: var(--z-spine);
    box-sizing: border-box;
    padding: calc(18px + 7px) 7px;
    contain: layout style;
    transition: width 460ms cubic-bezier(0.65, 0, 0.35, 1);
  }
  .body,
  .shoulder,
  .rim {
    position: absolute;
    pointer-events: none;
  }
  .body {
    inset: 18px 0;
    border-radius: 22px 0 0 22px;
    background: var(--glass-sheen), var(--glass-tint);
    box-shadow: var(--glass-shadow);
  }
  .shoulder {
    right: 0;
    width: 18px;
    height: 18px;
    background: var(--glass-tint);
  }
  /* The concave curve: everything outside a circle of the shoulder's radius around its inner corner. */
  .shoulder.top {
    top: 0;
    mask-image: radial-gradient(circle 18px at 0 0, transparent 17.5px, #000 18px);
    -webkit-mask-image: radial-gradient(circle 18px at 0 0, transparent 17.5px, #000 18px);
  }
  .shoulder.bottom {
    bottom: 0;
    mask-image: radial-gradient(circle 18px at 0 100%, transparent 17.5px, #000 18px);
    -webkit-mask-image: radial-gradient(circle 18px at 0 100%, transparent 17.5px, #000 18px);
  }
  .body,
  .shoulder {
    backdrop-filter: var(--glass-blur);
    -webkit-backdrop-filter: var(--glass-blur);
  }
  .rim {
    inset: 0;
    overflow: visible;
  }
  .rim path {
    fill: none;
    stroke: var(--glass-rim);
    stroke-width: 1;
  }
  :global(.reduce-transparency) .body,
  :global(.reduce-transparency) .shoulder {
    background: var(--raised);
    backdrop-filter: none;
    -webkit-backdrop-filter: none;
  }
  .layers {
    position: relative;
    display: flex;
    flex-direction: column;
    gap: 3px;
    overflow: hidden;
  }
  .row {
    all: unset;
    position: relative;
    z-index: 1;
    display: grid;
    grid-template-columns: 44px 1fr;
    align-items: center;
    height: var(--row);
    color: var(--muted);
    cursor: pointer;
    white-space: nowrap;
    transition: color 0.18s, transform 0.18s;
  }
  .row:hover {
    color: var(--ink);
  }
  .row.title {
    cursor: default;
    --hand: idle;
  }
  .row.title,
  .row.title:hover {
    color: var(--muted);
  }
  .row.title:active {
    transform: none;
  }
  .row.title .label {
    font-weight: 600;
  }
  .row:active {
    transform: scale(0.97, 0.94);
  }
  .row.active {
    color: var(--color-brand-ink);
  }
  .row:focus-visible .ico {
    outline: 2px solid var(--color-brand);
    outline-offset: -4px;
    border-radius: 999px;
  }
  .ico {
    display: grid;
    place-items: center;
    height: 100%;
  }
  .label {
    font-size: var(--text-control);
    font-weight: 500;
    margin-inline: 2px 14px;
    opacity: 0;
    transition: opacity 220ms cubic-bezier(0.33, 1, 0.68, 1);
  }
  .open .label {
    opacity: 1;
  }
  .rule {
    height: 1px;
    margin: 5px 12px;
    background: var(--line);
    flex: none;
  }
  .marker {
    position: absolute;
    left: 0;
    right: 0;
    height: var(--row);
    border-radius: 22px;
    opacity: 0;
    pointer-events: none;
    box-sizing: border-box;
    transition:
      top 230ms cubic-bezier(0.34, 1.56, 0.64, 1),
      opacity 110ms,
      transform 220ms cubic-bezier(0.34, 1.56, 0.64, 1);
  }
  .marker.instant {
    transition: opacity 110ms;
  }
  .marker.shown {
    opacity: 1;
  }
  /* Flat: a tint under the pointer and a plain yellow fill for the current tool, no rims or shadows. */
  .marker.hover {
    background: var(--tint);
  }
  .marker.squash {
    transform: scale(0.97, 0.86);
  }
  .marker.active {
    background: var(--color-brand);
  }
  :global(.reduce-motion) .spine,
  :global(.reduce-motion) .marker,
  :global(.reduce-motion) .label {
    transition: none;
  }
</style>
