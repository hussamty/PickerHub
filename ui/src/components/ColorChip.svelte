<script lang="ts">
  // A color as a pill: its swatch and code. Clicking opens a small editor for the code.
  import { app } from "../lib/app.svelte";
  import { t } from "../lib/i18n";
  import { isHex, normalHex } from "../lib/actions";
  let { hex, label, onchange }: { hex: string; label: string; onchange: (hex: string) => void } = $props();
  let editing = $state(false);
  let text = $state("");
  function open() { text = hex; editing = true; }
  function save() {
    if (!isHex(text)) { app.fail("أدخل ست خانات HEX."); return; }
    onchange(normalHex(text));
    editing = false;
  }
</script>

{#if editing}
  <form class="edit" onsubmit={(e) => { e.preventDefault(); save(); }}>
    <!-- svelte-ignore a11y_autofocus -->
    <input class="field code" dir="ltr" bind:value={text} maxlength="7" autofocus aria-label={t(label)} onblur={save} onkeydown={(e) => e.key === "Escape" && (editing = false)} />
  </form>
{:else}
  <button class="pill chip" onclick={open} title={t(label)} aria-label={t(label)}>
    <span class="dot" style:background={hex}></span>
    <span class="code">{hex}</span>
  </button>
{/if}

<style>
  .chip {
    gap: 10px;
    padding-inline: 10px 16px;
  }
  .dot {
    width: 20px;
    height: 20px;
    border-radius: 999px;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .edit input {
    width: 130px;
    min-height: 40px;
  }
</style>
