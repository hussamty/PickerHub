<script lang="ts">
  // A select button whose list drops in the window's own layer, as glass, following the theme.
  import { t } from "../lib/i18n";
  import Icon from "./Icon.svelte";
  import { scale } from "svelte/transition";
  import { app } from "../lib/app.svelte";

  let { options, value, onchange, raw = false, label = "", width = 0 }: {
    options: readonly string[]; value: number; onchange: (i: number) => void; raw?: boolean; label?: string; width?: number;
  } = $props();
  let open = $state(false);
  let root: HTMLElement;
  const text = (o: string) => (raw ? o : t(o));

  function outside(e: PointerEvent) {
    if (open && !root.contains(e.target as Node)) open = false;
  }
  function key(e: KeyboardEvent) {
    // Closed, the arrow keys open the list; open, they move the choice.
    if (!open) {
      if (e.key === "ArrowDown" || e.key === "ArrowUp") { e.preventDefault(); open = true; }
      return;
    }
    if (e.key === "Escape") { open = false; e.stopPropagation(); }
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      const next = (value + (e.key === "ArrowDown" ? 1 : options.length - 1)) % options.length;
      onchange(next);
    }
    if (e.key === "Enter") open = false;
  }
</script>

<svelte:window onpointerdown={outside} />

<div class="select" bind:this={root} style:min-width={width ? width + "px" : undefined}>
  <button class="pill trigger" aria-haspopup="listbox" aria-expanded={open} aria-label={label ? t(label) : undefined} onclick={() => (open = !open)} onkeydown={key}>
    <span class="value">{text(options[value] ?? "")}</span>
    <Icon name="chevron-left" size={14} class="chev {open ? 'up' : ''}" />
  </button>
  {#if open}
    <ul class="menu glass glass-live" role="listbox" transition:scale={{ start: 0.96, duration: app.motion ? 140 : 0, easing: (t: number) => 1 - Math.pow(1 - t, 3) }}>
      {#each options as option, i}
        <li>
          <button role="option" aria-selected={i === value} class:on={i === value} onclick={() => { onchange(i); open = false; }}>
            {text(option)}
            {#if i === value}<Icon name="check" size={14} />{/if}
          </button>
        </li>
      {/each}
    </ul>
  {/if}
</div>

<style>
  .select {
    position: relative;
    display: inline-block;
  }
  .trigger {
    width: 100%;
    justify-content: space-between;
  }
  .trigger :global(.chev) {
    rotate: -90deg;
    transition: rotate 0.2s var(--ease-liquid);
  }
  .trigger :global(.chev.up) {
    rotate: 90deg;
  }
  .menu {
    position: absolute;
    z-index: 30;
    top: calc(100% + 6px);
    inset-inline-start: 0;
    min-width: 100%;
    margin: 0;
    padding: 6px;
    list-style: none;
    border-radius: 16px;
    background: var(--sheet-tint);
    transform-origin: top;
    will-change: transform, opacity;
  }
  .menu button {
    all: unset;
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    width: 100%;
    box-sizing: border-box;
    padding: 8px 12px;
    border-radius: 10px;
    font-size: var(--text-control);
    font-weight: 600;
    white-space: nowrap;
    cursor: pointer;
  }
  .menu button:hover,
  .menu button:focus-visible {
    background: var(--tint-strong);
  }
  .menu button.on {
    color: var(--accent-text);
  }
</style>
