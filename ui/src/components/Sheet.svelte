<script lang="ts">
  // Everything that opens over the page lives in one layer inside the window (desktop/Overlay.cs),
  // so it can be real glass over the page and follow the theme.
  import type { Snippet } from "svelte";
  import { app } from "../lib/app.svelte";
  import { scale, fade } from "svelte/transition";
  import { t } from "../lib/i18n";
  import Icon from "./Icon.svelte";

  let { title, onclose, width = 460, children }: { title: string; onclose: () => void; width?: number; children: Snippet } = $props();
  let panel: HTMLElement;
  const id = `sheet-${Math.random().toString(36).slice(2, 8)}`;
  // The first control takes focus; on close, focus goes back to what opened the sheet.
  $effect(() => {
    const opener = document.activeElement as HTMLElement | null;
    const focusable = panel?.querySelector<HTMLElement>("input, button:not(.close), select, textarea");
    focusable?.focus();
    return () => { if (opener?.isConnected) opener.focus({ preventScroll: true }); };
  });
  // Tab and Shift+Tab stay inside the sheet while it is open.
  function keys(e: KeyboardEvent) {
    if (e.key === "Escape") { e.stopPropagation(); onclose(); return; }
    if (e.key !== "Tab") return;
    const list = [...panel.querySelectorAll<HTMLElement>("button, input, select, textarea, [tabindex]:not([tabindex='-1'])")].filter((el) => !el.hasAttribute("disabled") && el.offsetParent !== null);
    if (!list.length) return;
    const first = list[0], last = list[list.length - 1];
    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  }
</script>

<div class="scrim" transition:fade={{ duration: app.motion ? 140 : 0 }} onclick={onclose} aria-hidden="true"></div>
<div
  class="sheet glass"
  role="dialog"
  aria-modal="true"
  aria-labelledby={id}
  tabindex="-1"
  bind:this={panel}
  style:width="min({width}px, calc(100vw - 48px))"
  transition:scale={{ start: 0.96, duration: app.motion ? 180 : 0, easing: (t: number) => 1 - Math.pow(1 - t, 3) }}
  onkeydown={keys}
>
  <div class="head">
    <h2 {id}>{title}</h2>
    <button class="round close" onclick={onclose} aria-label={t("إغلاق")}><Icon name="x" size={16} /></button>
  </div>
  <div class="body scroll">{@render children()}</div>
</div>

<style>
  .scrim {
    position: fixed;
    inset: 0;
    z-index: var(--z-scrim);
    background: var(--scrim);
    will-change: opacity;
  }
  .sheet {
    position: fixed;
    z-index: var(--z-sheet);
    top: 50%;
    left: 50%;
    translate: -50% -50%;
    max-height: calc(100vh - 80px);
    display: flex;
    flex-direction: column;
    background: var(--glass-sheen), var(--sheet-tint);
    outline: none;
    will-change: transform, opacity;
    backdrop-filter: var(--glass-blur);
    -webkit-backdrop-filter: var(--glass-blur);
  }
  .head {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    padding: 18px 20px 8px;
  }
  h2 {
    margin: 0;
    font-size: var(--text-heading);
    font-weight: 700;
  }
  .body {
    padding: 8px 20px 20px;
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
</style>
