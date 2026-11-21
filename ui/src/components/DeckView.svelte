<script lang="ts" module>
  export interface Specimen { hex: string; name: string }
</script>

<script lang="ts">
  // A fanned stack of cards (desktop/Controls.cs, Deck; desktop/Library.cs, DeckView). The pile leans
  // away from the reading edge and opens a little under the pointer. The top card flips to its
  // reverse; stepping deals the next one forward, and the one that leaves slides away to the side.
  import type { Snippet } from "svelte";
  import { app } from "../lib/app.svelte";
  import { ipc } from "../lib/ipc";
  import { t } from "../lib/i18n";
  import type { Frame, NameRow } from "../lib/types";
  import Icon from "./Icon.svelte";

  let { items, index = $bindable(0), title, actions, extra, below }: {
    items: Specimen[]; index?: number; title: string; actions?: Snippet; extra?: Snippet; below?: Snippet;
  } = $props();

  const W = 264, H = 372, DEPTH = 4;
  let spread = $state(1);
  let flipped = $state(false);
  let flip = $state(1);
  let busy = $state(false);
  let step = 0; // +1 after moving forward, -1 after moving back
  let rows = $state<Record<string, NameRow>>({});
  let back = $state<Frame | null>(null);

  const count = $derived(items.length);
  $effect(() => { if (index >= count) index = Math.max(0, count - 1); });
  const pile = $derived(Array.from({ length: Math.min(DEPTH, count) }, (_, d) => ({ d, i: (index + d) % count })));
  $effect(() => {
    const need = items.map((s) => s.hex).filter((h) => !rows[h]);
    if (need.length) ipc.names([...new Set(need)]).then((r) => { const next = { ...rows }; r.forEach((x) => (next[x.hex] = x)); rows = next; });
  });
  $effect(() => {
    const top = items[index];
    if (flipped && top) ipc.frame(top.hex, 4).then((f) => (back = f)).catch((e) => app.fail(e));
  });

  const pose = (d: number, f = 1) => `translate(${-d * 30 * spread}px, ${d * 3}px) rotate(${-d * 5 * spread}deg) scale(${(1 - d * 0.03) * f}, ${1 - d * 0.03})`;
  const AWAY = "translate(150px, 10px) rotate(9deg) scale(0.97, 0.97)";

  // A card that joins the pile starts where the step brings it from, then travels to its place.
  function enter(el: HTMLElement, d: number) {
    if (!app.motion || step === 0) return;
    const from = step < 0 && d === 0 ? AWAY : pose(d + step);
    const final = el.style.transform;
    el.style.transition = "none";
    el.style.transform = from;
    if (step < 0 && d === 0) el.style.opacity = "0";
    void el.offsetWidth;
    requestAnimationFrame(() => { el.style.transition = ""; el.style.transform = final; el.style.opacity = ""; });
  }
  // The card that leaves the top slides away to the side; one that falls off the back just goes.
  function leave(el: HTMLElement, { d }: { d: number }) {
    const moving = app.motion && step > 0 && d === 0;
    if (moving) {
      el.style.zIndex = "11";
      requestAnimationFrame(() => { el.style.transform = AWAY; el.style.opacity = "0"; });
    }
    return { duration: moving ? 230 : 0 };
  }

  function go(by: number) {
    if (count < 2) return;
    step = by;
    flipped = false;
    flip = 1;
    index = (index + by + count) % count;
  }
  export function show(i: number) {
    if (i === index || i < 0 || i >= count) return;
    step = 1;
    flipped = false;
    index = i;
  }
  async function turn() {
    if (busy || !count) return;
    if (!app.motion) { flipped = !flipped; return; }
    busy = true;
    flip = 0.02;
    await new Promise((r) => setTimeout(r, 125));
    flipped = !flipped;
    flip = 1;
    await new Promise((r) => setTimeout(r, 125));
    busy = false;
  }
  function keys(e: KeyboardEvent) {
    if (e.key === "ArrowLeft") go(1);
    else if (e.key === "ArrowRight") go(-1);
    else if (e.key === " " || e.key === "Enter") turn();
    else return;
    e.preventDefault();
  }
  const arabic = $derived(app.language === "ar");
  const name = (s: Specimen) => s.name;
  const family = (hex: string) => (rows[hex] ? (app.language === "en" ? rows[hex].family : rows[hex].base) : "");
  const strip = (v: string) => v.replace("rgb", "").replace("hsl", "").replace(/^\(|\)$/g, "");
</script>

<div class="pair">
  <div class="view">
    <!-- svelte-ignore a11y_no_noninteractive_tabindex, a11y_no_noninteractive_element_interactions -->
    <div
      class="deck"
      role="group"
      tabindex="0"
      aria-label={t("بطاقات الألوان")}
      onpointerenter={() => (spread = 1.35)}
      onpointerleave={() => (spread = 1)}
      onkeydown={keys}
    >
      {#each [...pile].reverse() as card (card.i)}
        {@const item = items[card.i]}
        {@const ink = rows[item.hex]?.ink ?? "#111111"}
        <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
        <div
          class="card"
          class:top={card.d === 0}
          style:transform={pose(card.d, card.d === 0 ? flip : 1)}
          style:z-index={10 - card.d}
          style:transition-duration={card.d === 0 && busy ? "120ms" : undefined}
          style:transition-timing-function={card.d === 0 && busy ? (flip < 1 ? "cubic-bezier(.32,0,.67,0)" : "cubic-bezier(.33,1,.68,1)") : undefined}
          use:enter={card.d}
          out:leave={{ d: card.d }}
          onclick={() => card.d === 0 && turn()}
        >
          {#if card.d === 0 && flipped}
            <div class="reverse" dir={arabic ? "rtl" : "ltr"}>
              <div class="rhead" dir="ltr">
                <span class="code muted caption">no. {String(card.i + 1).padStart(2, "0")} / {(back?.english ?? "").toLowerCase()}</span>
                <span class="dot" style:background={item.hex}></span>
              </div>
              <strong class="rname">{name(item)}</strong>
              {#if back}
                {@const light = Math.round(back.hsl[2] * 100)}
                <div class="light">
                  <div class="lrow"><span class="muted label">{t("السطوع")}</span><span class="code caption">{light}%</span></div>
                  <div class="scale" dir="ltr">{#each Array(10) as _, s}<span class:fill={s < Math.round(light / 10)}></span>{/each}</div>
                </div>
                <span class="hair"></span>
                <div class="rows" dir="ltr">
                  {#each ["HEX", "RGB", "HSL", "CMYK"] as const as key}
                    <div class="frow"><span class="code muted caption">{key.toLowerCase()}</span><span class="code">{strip(back.formats[key])}</span></div>
                  {/each}
                </div>
                <div class="shades" dir="ltr">{#each back.harmony as h}<span style:background={h.hex}></span>{/each}</div>
                <span class="muted label">{app.language === "en" ? back.family : back.base}{t("، ")}{t(back.shade)}</span>
              {/if}
            </div>
          {:else}
            <div class="front" style:background={item.hex} style:color={ink} style:border-color="color-mix(in srgb, {ink} 18%, transparent)">
              <div class="fhead" dir="ltr">
                <span class="code serial">{String(card.i + 1).padStart(2, "0")}</span>
                <strong class="code">{item.hex}</strong>
              </div>
              <div class="caption-block" dir={arabic ? "rtl" : "ltr"}>
                <strong class="fname">{name(item)}</strong>
                <span class="family">{family(item.hex)}</span>
              </div>
            </div>
          {/if}
        </div>
      {/each}
    </div>

    <div class="pager">
      <button class="round" onclick={() => go(-1)} title={t("اللون السابق")} aria-label={t("اللون السابق")}><Icon name={arabic ? "chevron-right" : "chevron-left"} size={17} /></button>
      <div class="pcaption">
        <strong>{items[index] ? name(items[index]) : ""}</strong>
        <span class="code muted caption">{String(index + 1).padStart(2, "0")} / {String(count).padStart(2, "0")}</span>
      </div>
      <button class="round" onclick={() => go(1)} title={t("اللون التالي")} aria-label={t("اللون التالي")}><Icon name={arabic ? "chevron-left" : "chevron-right"} size={17} /></button>
    </div>
    <div class="tools">
      <button class="round" onclick={turn} title={t("قلب البطاقة")} aria-label={t("قلب البطاقة")}><Icon name="refresh" size={16} /></button>
      {@render actions?.()}
    </div>
  </div>

  <div class="side">
    <section class="glass sheet">
      <header>
        <h2 dir="auto">{title}</h2>
        {@render extra?.()}
      </header>
      <div class="dots">
        {#each items as s, i}
          <button class="pick" class:on={i === index} style:background={s.hex} title="{s.name}  {s.hex}" aria-label="{s.name} {s.hex}" onclick={() => show(i)}></button>
        {/each}
      </div>
    </section>
    {@render below?.()}
  </div>
</div>

<style>
  .pair {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 18px;
    align-items: start;
  }
  @media (max-width: 1000px) {
    .pair {
      grid-template-columns: 1fr;
    }
  }
  .view {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 12px;
  }
  .deck {
    position: relative;
    width: 414px;
    height: 412px;
    direction: ltr;
    outline: none;
    cursor: pointer;
  }
  .deck:focus-visible {
    outline: 2px solid var(--color-brand);
    outline-offset: 6px;
    border-radius: 26px;
  }
  .card {
    position: absolute;
    top: 0;
    right: 0;
    width: 264px;
    height: 372px;
    transform-origin: 50% 100%;
    transition:
      transform 210ms cubic-bezier(0.33, 1, 0.68, 1),
      opacity 180ms;
    will-change: transform;
  }
  .front,
  .reverse {
    position: absolute;
    inset: 0;
    border-radius: 26px;
    box-sizing: border-box;
  }
  .front {
    padding: 22px;
    border: 1px solid;
    display: flex;
    flex-direction: column;
    justify-content: space-between;
    box-shadow: 0 18px 40px rgb(0 0 0 / 0.28);
  }
  .fhead {
    display: flex;
    justify-content: space-between;
    align-items: baseline;
  }
  .serial {
    font-size: var(--text-label);
  }
  .fhead strong {
    font-size: 14px;
  }
  .caption-block {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .fname {
    font-size: var(--text-feature);
    line-height: 1.45;
    display: -webkit-box;
    -webkit-line-clamp: 3;
    line-clamp: 3;
    -webkit-box-orient: vertical;
    overflow: hidden;
  }
  .family {
    font-size: 13px;
  }
  .reverse {
    padding: 18px 20px;
    border-radius: 26px;
    /* Dense enough that the card beneath never reads through: WebKit does not blur the backdrop of
       an element inside a transformed card, which 1.x's frosted reverse relied on. */
    background:
      var(--glass-sheen),
      color-mix(in srgb, var(--raised) 96%, transparent);
    border: 1px solid var(--glass-rim);
    box-shadow: 0 18px 40px rgb(0 0 0 / 0.28);
    color: var(--ink);
    display: flex;
    flex-direction: column;
    gap: 12px;
  }
  .rhead {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }
  .caption {
    font-size: var(--text-caption);
  }
  .label {
    font-size: var(--text-label);
  }
  .dot {
    width: 12px;
    height: 12px;
    border-radius: 6px;
  }
  .rname {
    font-size: var(--text-heading);
    line-height: 1.4;
  }
  .light {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .lrow {
    display: flex;
    justify-content: space-between;
  }
  .scale {
    display: grid;
    grid-template-columns: repeat(10, 1fr);
    gap: 3px;
    height: 5px;
  }
  .scale span {
    border-radius: 2px;
    background: var(--line);
  }
  .scale span.fill {
    background: var(--ink);
  }
  .hair {
    height: 1px;
    background: var(--line);
  }
  .rows {
    display: flex;
    flex-direction: column;
    gap: 5px;
  }
  .frow {
    display: flex;
    justify-content: space-between;
    font-size: 11.5px;
  }
  .shades {
    display: flex;
    height: 40px;
    border-radius: 10px;
    overflow: hidden;
  }
  .shades span {
    flex: 1;
  }
  .pager {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .pcaption {
    width: 150px;
    display: flex;
    flex-direction: column;
    align-items: center;
    min-width: 0;
  }
  .pcaption strong {
    font-size: var(--text-lead);
    max-width: 100%;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    line-height: 1.7;
  }
  .tools {
    display: flex;
    gap: 6px;
    flex-wrap: wrap;
    justify-content: center;
  }
  .side {
    display: flex;
    flex-direction: column;
    gap: 18px;
  }
  .sheet {
    padding: 20px;
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  .sheet header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 10px;
  }
  .sheet h2 {
    margin: 0;
    font-size: var(--text-title);
  }
  .dots {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
  }
  .pick {
    all: unset;
    width: 38px;
    height: 38px;
    border-radius: 999px;
    box-shadow: inset 0 0 0 1px var(--line);
    cursor: pointer;
    transition: transform 0.18s var(--ease-liquid);
  }
  .pick:hover {
    transform: scale(1.08);
  }
  .pick.on {
    outline: 2px solid var(--color-brand);
    outline-offset: 2px;
  }
  .pick:focus-visible {
    outline: 2px solid var(--color-brand);
    outline-offset: 2px;
  }
</style>
