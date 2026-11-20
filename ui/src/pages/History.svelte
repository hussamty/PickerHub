<script lang="ts">
  // The history as a deck of cards beside a sheet of every color (desktop/Library.cs, History).
  import { app } from "../lib/app.svelte";
  import { ipc } from "../lib/ipc";
  import { t } from "../lib/i18n";
  import { addToPalette, useInContrast } from "../lib/actions";
  import type { NameRow } from "../lib/types";
  import Icon from "../components/Icon.svelte";
  import DeckView from "../components/DeckView.svelte";

  let index = $state(0);
  let rows = $state<Record<string, NameRow>>({});
  let limit = $state(app.ws!.limit);
  $effect(() => { ipc.names(app.ws!.history).then((r) => (rows = Object.fromEntries(r.map((x) => [x.hex, x])))).catch((e) => app.fail(e)); });
  const name = (hex: string) => (rows[hex] ? (app.language === "en" ? rows[hex].english : rows[hex].name) : hex);
  const items = $derived(app.ws!.history.map((hex) => ({ hex, name: name(hex) })));
  const top = $derived(app.ws!.history[index] ?? "");

  async function clear() {
    if (await app.confirm("مسح السجل", "مسح سجل الألوان؟", true)) { await app.edit([{ op: "clearHistory" }]); index = 0; }
  }
  async function actions() {
    const i = await app.choose("إجراءات اللون", ["نسخ رمز اللون", "استخدام كلون للنص", "استخدام كلون للخلفية"]);
    if (i === 0) app.copy(top);
    if (i === 1) useInContrast(top, "fg");
    if (i === 2) useInContrast(top, "bg");
  }
</script>

{#if app.ws!.history.length === 0}
  <section class="glass empty">
    <Icon name="history" size={36} />
    <h2>{t("سجلك يبدأ بلون")}</h2>
    <p class="muted">{t("اضغط القطّارة لاختيار لون من شاشتك.")}</p>
  </section>
{:else}
  <DeckView {items} bind:index title={t("كل الألوان")}>
    {#snippet actions()}
      <button class="round" onclick={() => app.copy(top)} title={t("نسخ اللون")} aria-label={t("نسخ اللون")}><Icon name="copy" size={16} /></button>
      <button class="round" onclick={() => { app.edit([{ op: "setColor", hex: top }]); app.page = 0; }} title={t("فتح في الملتقط")} aria-label={t("فتح في الملتقط")}><Icon name="color-picker" size={16} /></button>
      <button class="round" onclick={() => addToPalette([top])} title={t("إضافة إلى لوحة")} aria-label={t("إضافة إلى لوحة")}><Icon name="plus" size={16} /></button>
      <button class="round" onclick={actions} title={t("إجراءات اللون")} aria-label={t("إجراءات اللون")}><Icon name="dots" size={16} /></button>
      <button class="round" onclick={() => app.edit([{ op: "forget", hex: top }])} title={t("حذف من السجل")} aria-label={t("حذف من السجل")}><Icon name="trash" size={16} /></button>
    {/snippet}
    {#snippet extra()}
      <button class="round" onclick={clear} title={t("مسح السجل")} aria-label={t("مسح السجل")}><Icon name="trash" size={16} /></button>
    {/snippet}
    {#snippet below()}
      <section class="glass limit">
        <span class="muted small">{t("عدد العناصر المحفوظة")}</span>
        <div class="row">
          <input class="field code" type="number" min="1" max="256" bind:value={limit} aria-label={t("عدد العناصر المحفوظة")} />
          <button class="pill" onclick={() => app.setting("limit", Math.max(1, Math.min(256, Math.round(limit))))}>{t("تطبيق")}</button>
        </div>
      </section>
    {/snippet}
  </DeckView>
{/if}

<style>
  .empty {
    display: grid;
    justify-items: center;
    gap: 6px;
    padding: 48px 24px;
    text-align: center;
  }
  .empty h2 {
    margin: 8px 0 0;
    font-size: var(--text-heading);
  }
  .empty p {
    margin: 0;
  }
  .limit {
    padding: 20px;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .row {
    display: flex;
    gap: 10px;
    align-items: center;
  }
  .row input {
    width: 150px;
  }
  .small {
    font-size: var(--text-label);
  }
</style>
