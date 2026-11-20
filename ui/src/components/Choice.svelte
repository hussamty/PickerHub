<script lang="ts">
  // A segmented choice: every option visible, the chosen one in brand yellow.
  import { t } from "../lib/i18n";
  let { options, value, onchange, raw = false }: { options: readonly string[]; value: number; onchange: (i: number) => void; raw?: boolean } = $props();
  let group: HTMLElement;
  // A radio group is one tab stop; the arrow keys move the choice, in reading order.
  function key(e: KeyboardEvent) {
    const rtl = getComputedStyle(group).direction === "rtl";
    const step = { ArrowDown: 1, ArrowUp: -1, ArrowRight: rtl ? -1 : 1, ArrowLeft: rtl ? 1 : -1 }[e.key];
    let next = step === undefined ? (e.key === "Home" ? 0 : e.key === "End" ? options.length - 1 : -1) : (value + step + options.length) % options.length;
    if (next < 0) return;
    e.preventDefault();
    onchange(next);
    requestAnimationFrame(() => group.querySelectorAll<HTMLElement>("[role=radio]")[next]?.focus());
  }
</script>

<div class="choice" role="radiogroup" bind:this={group}>
  {#each options as option, i}
    <button role="radio" aria-checked={i === value} tabindex={i === value ? 0 : -1} class:on={i === value} onclick={() => onchange(i)} onkeydown={key}>{raw ? option : t(option)}</button>
  {/each}
</div>

<style>
  .choice {
    display: inline-flex;
    padding: 3px;
    gap: 2px;
    border-radius: 999px;
    background: var(--tint);
    border: 1px solid var(--line);
  }
  button {
    all: unset;
    padding: 6px 14px;
    border-radius: 999px;
    font-size: var(--text-control);
    font-weight: 600;
    cursor: pointer;
    white-space: nowrap;
    transition: background 0.18s, color 0.18s;
  }
  button:hover {
    background: var(--tint-strong);
  }
  button.on {
    background: var(--color-brand);
    color: #111;
  }
  button:focus-visible {
    outline: 2px solid var(--color-brand);
    outline-offset: 1px;
  }
</style>
