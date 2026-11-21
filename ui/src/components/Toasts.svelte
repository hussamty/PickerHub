<script lang="ts">
  // News arrives as a toast: a small pane of frosted glass rising from the bottom corner where
  // reading starts. A message that carries a color shows it. Pointing at the toast holds it.
  import { app } from "../lib/app.svelte";
  import { t } from "../lib/i18n";
  import { fly } from "svelte/transition";
</script>

<div class="toasts" role="status" aria-live="polite">
  {#each app.toasts as toast (toast.id)}
    <div class="toast glass glass-dense" class:error={toast.tone === "error"} transition:fly={{ y: 24, duration: app.motion ? 260 : 0 }}>
      {#if toast.color}<span class="chip" style:background={toast.color}></span>{/if}
      <span>{t(toast.text, ...(toast.args ?? []))}</span>
    </div>
  {/each}
</div>

<style>
  .toasts {
    position: fixed;
    inset-inline-start: 76px;
    margin-inline-start: 0;
    bottom: 20px;
    z-index: var(--z-toast);
    display: flex;
    flex-direction: column;
    gap: 8px;
    pointer-events: none;
  }
  .toast {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 10px 16px;
    border-radius: 16px;
    font-size: var(--text-control);
    font-weight: 600;
    max-width: 420px;
    pointer-events: auto;
  }
  .toast.error {
    border-color: var(--danger);
    color: var(--danger);
  }
  .chip {
    width: 18px;
    height: 18px;
    border-radius: 6px;
    box-shadow: inset 0 0 0 1px var(--line);
    flex: none;
  }
</style>
