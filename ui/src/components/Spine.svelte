<script lang="ts">
  // Edge navigation (desktop/Controls.cs, Spine). One pane of glass grown out of the window's right
  // edge through two concave shoulders. At rest it is a slim rail of icons; under the pointer or
  // keyboard focus it opens over the page and shows labels under a "Menu" header. One pill follows
  // the pointer from row to row and squashes on press; a second, filled pill marks the current tool
  // and slides when it changes.
  import { onMount } from "svelte";
  import { app } from "../lib/app.svelte";
  import { t } from "../lib/i18n";
  import { TOOLS } from "../lib/tools";
  import Icon from "./Icon.svelte";

  const CLOSED = 58, OPEN = 236, LIP = 18, RADIUS = 26;
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

  // Full glass gives the spine the panes' own sheen: CSS linear-gradient(160deg, sheen 0%, transparent 42%).
  const paneSheen = $derived.by(() => {
    const { w, h } = size, dx = Math.sin((160 * Math.PI) / 180), dy = -Math.cos((160 * Math.PI) / 180);
    const half = (Math.abs(w * dx) + Math.abs(h * dy)) / 2;
    return { x1: w / 2 - dx * half, y1: h / 2 - dy * half, x2: w / 2 + dx * half, y2: h / 2 + dy * half };
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
  <!-- The blur and the shadow follow the body of the pane as a rounded rectangle: a path clip or a
       filtered shape would be redrawn in software on every frame of the opening. The shoulders are
       small enough to carry only the tint. -->
  <div class="shade" aria-hidden="true"></div>
  <div class="frost" aria-hidden="true"></div>
  <svg class="pane" width={size.w} height={size.h} aria-hidden="true">
    <defs>
      <linearGradient id="spine-sheen" x1="0" y1="0" x2={size.w * 0.7} y2={size.h * 0.7} gradientUnits="userSpaceOnUse">
        <stop offset="0" stop-color="var(--rim-top)" stop-opacity="0.22" />
        <stop offset="1" stop-color="var(--rim-top)" stop-opacity="0" />
      </linearGradient>
      <linearGradient id="spine-sheen-pane" x1={paneSheen.x1} y1={paneSheen.y1} x2={paneSheen.x2} y2={paneSheen.y2} gradientUnits="userSpaceOnUse">
        <stop offset="0" class="pane-sheen" />
        <stop offset="0.42" stop-color="#fff" stop-opacity="0" />
      </linearGradient>
      <linearGradient id="spine-rim" x1="0" y1="0" x2="0" y2={size.h} gradientUnits="userSpaceOnUse">
        <stop offset="0" stop-color="var(--rim-top)" stop-opacity="var(--rim-a)" />
        <stop offset="1" stop-color="var(--rim-top)" stop-opacity="calc(var(--rim-a) * 0.22)" />
      </linearGradient>
      <clipPath id="spine-clip"><path d={shape} /></clipPath>
    </defs>
    <path d={shape} class="fill" />
    <path d={shape} class="sheen" />
    <path d={shape} class="rim" stroke-width="2" clip-path="url(#spine-clip)" />
  </svg>

  <div class="layers" bind:this={list} dir={app.language === "ar" ? "rtl" : "ltr"} style:--row="{rowH}px">
    <span class="pill active" class:shown={currentShown} class:instant={currentInstant} style:top="{currentY}px"></span>
    <span class="pill hover" class:shown={hoverShown} class:instant={hoverInstant} class:squash style:top="{hoverY}px"></span>
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
    --rim-top: #ffffff;
    --rim-a: 0.34;
    position: absolute;
    right: 0;
    top: 50%;
    translate: 0 calc(-50% - 8px);
    z-index: 20;
    box-sizing: border-box;
    padding: calc(18px + 7px) 7px;
    contain: layout style;
    transition: width 460ms cubic-bezier(0.65, 0, 0.35, 1);
  }
  :global([data-theme="light"]) .spine {
    --rim-a: 1;
  }
  .frost,
  .shade {
    position: absolute;
    inset: 18px 0 18px 0;
    border-radius: 26px 0 0 26px;
  }
  .frost {
    backdrop-filter: blur(20px) saturate(150%);
    -webkit-backdrop-filter: blur(20px) saturate(150%);
  }
  .shade {
    box-shadow: 0 10px 28px var(--glass-shade);
  }
  :global(.reduce-transparency) .frost {
    display: none;
  }
  :global([dir="ltr"]) .frost,
  :global([dir="ltr"]) .shade {
    border-radius: 26px 0 0 26px;
  }
  .pane {
    position: absolute;
    inset: 0;
    overflow: visible;
    pointer-events: none;
  }
  .fill {
    fill: var(--glass-tint);
  }
  .sheen {
    fill: url(#spine-sheen);
  }
  .rim {
    fill: none;
    stroke: url(#spine-rim);
  }
  .pane-sheen {
    stop-color: #fff;
    stop-opacity: var(--sheen);
  }
  /* Full glass: the same material as the panes (app.css, glass): their sheen, an even rim, their blur
     and shadow. */
  :global(.full-glass) .sheen {
    fill: url(#spine-sheen-pane);
  }
  :global(.full-glass) .rim {
    stroke: var(--glass-rim);
  }
  :global(.full-glass) .frost {
    backdrop-filter: blur(18px) saturate(150%);
    -webkit-backdrop-filter: blur(18px) saturate(150%);
  }
  :global(.full-glass) .shade {
    box-shadow: 0 10px 30px var(--glass-shade);
  }
  :global(.reduce-transparency) .fill {
    fill: var(--raised);
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
  .pill {
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
  .pill.instant {
    transition: opacity 110ms;
  }
  .pill.shown {
    opacity: 1;
  }
  .pill.hover {
    background: var(--tint);
    border: 1px solid var(--glass-rim);
  }
  .pill.squash {
    transform: scale(0.97, 0.86);
  }
  .pill.active {
    background: var(--color-brand);
    border: 1px solid rgb(255 255 255 / 0.6);
    box-shadow: 0 8px 26px rgb(255 240 0 / 0.28);
  }
  :global(.reduce-motion) .spine,
  :global(.reduce-motion) .pill,
  :global(.reduce-motion) .label {
    transition: none;
  }
</style>
