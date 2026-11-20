<script lang="ts">
  import { app } from "../lib/app.svelte";
  import { t } from "../lib/i18n";
  import Sheet from "./Sheet.svelte";

  let value = $state("");
  $effect(() => { if (app.dialog?.kind === "prompt") value = app.dialog.value; });

  function close(result: unknown) {
    const d = app.dialog;
    app.dialog = null;
    if (!d) return;
    if (d.kind === "confirm") d.resolve(result === true);
    else if (d.kind === "prompt") d.resolve(typeof result === "string" ? result : null);
    else d.resolve(typeof result === "number" ? result : null);
  }
</script>

{#if app.dialog}
  {@const d = app.dialog}
  <Sheet title={t(d.title)} onclose={() => close(null)} width={420}>
    {#if d.kind === "confirm"}
      {#if d.body}<p class="muted">{t(d.body)}</p>{/if}
      <div class="row">
        <button class="pill" class:danger={d.danger} class:pill-accent={!d.danger} onclick={() => close(true)}>{t(d.danger ? "حذف" : "متابعة")}</button>
        <button class="pill" onclick={() => close(false)}>{t("إلغاء")}</button>
      </div>
    {:else if d.kind === "prompt"}
      <form onsubmit={(e) => { e.preventDefault(); if (value.trim()) close(value.trim()); }}>
        <input class="field" dir="auto" bind:value placeholder={d.placeholder ? t(d.placeholder) : ""} maxlength="100" />
        <div class="row">
          <button class="pill pill-accent" type="submit" disabled={!value.trim()}>{t("حفظ")}</button>
          <button class="pill" type="button" onclick={() => close(null)}>{t("إلغاء")}</button>
        </div>
      </form>
    {:else}
      <div class="options">
        {#each d.options as option, i}
          <button class="pill option" onclick={() => close(i)}>{t(option)}</button>
        {/each}
      </div>
    {/if}
  </Sheet>
{/if}

<style>
  .row {
    display: flex;
    gap: 8px;
    margin-top: 6px;
  }
  form {
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  p {
    margin: 0;
  }
  .danger {
    background: var(--danger);
    color: var(--canvas);
    border-color: transparent;
  }
  .options {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
    gap: 8px;
  }
  .option {
    justify-content: flex-start;
  }
</style>
