<script lang="ts">
  import { app } from "../lib/app.svelte";
  import { ipc } from "../lib/ipc";
  import { t } from "../lib/i18n";
  import type { Contrast } from "../lib/types";
  import Icon from "../components/Icon.svelte";
  import ColorChip from "../components/ColorChip.svelte";

  let result = $state<Contrast | null>(null);
  $effect(() => {
    const fg = app.contrastFg, bg = app.contrastBg;
    ipc.contrast(fg, bg).then((r) => (result = r)).catch((e) => app.fail(e));
  });
  const cells = $derived(result ? [
    { level: "AA", size: "نص عادي", pass: result.aa },
    { level: "AA", size: "نص كبير", pass: result.large },
    { level: "AAA", size: "نص عادي", pass: result.aaa },
    { level: "AAA", size: "نص كبير", pass: result.aaaLarge },
  ] : []);

  async function saveRoles() {
    const p = app.palette;
    if (!p) return;
    if ((p.roles.text || p.roles.background) && !(await app.confirm("حفظ النص والخلفية في لوحة", "استبدال دوري النص والخلفية في اللوحة المختارة؟"))) return;
    if (await app.edit([{ op: "setRoles", id: p.id, roles: { ...p.roles, text: app.contrastFg, background: app.contrastBg } }]))
      app.toast("حُفظ دوران في {0}", undefined, [p.name]);
  }
  function swap() {
    [app.contrastFg, app.contrastBg] = [app.contrastBg, app.contrastFg];
  }
</script>

<div class="grid">
  <div class="col">
    <section class="preview" style:background={app.contrastBg} style:color={app.contrastFg}>
      <h2>{t("تصميم واضح للجميع")}</h2>
      <p>{t("النص المقروء يبدأ بتباين مناسب بين اللون والخلفية.")}</p>
    </section>
    <section class="glass card">
      <div class="pair">
        <div class="field-col">
          <span class="muted small">{t("لون النص")}</span>
          <ColorChip hex={app.contrastFg} label="تعديل لون النص" onchange={(h) => (app.contrastFg = h)} />
        </div>
        <button class="round" onclick={swap} title={t("تبديل اللونين")} aria-label={t("تبديل اللونين")}><Icon name="arrows-exchange" size={17} /></button>
        <div class="field-col">
          <span class="muted small">{t("الخلفية")}</span>
          <ColorChip hex={app.contrastBg} label="تعديل لون الخلفية" onchange={(h) => (app.contrastBg = h)} />
        </div>
      </div>
      <div class="row">
        <button class="pill" onclick={() => (app.contrastFg = app.ws!.color)}>{t("استخدام اللون المحدد للنص")}</button>
        <button class="pill" onclick={() => (app.contrastBg = app.ws!.color)}>{t("استخدام اللون المحدد للخلفية")}</button>
      </div>
      <div class="row">
        <button class="pill" onclick={saveRoles}><Icon name="device-floppy" size={16} />{t("حفظ دوري النص والخلفية")}</button>
        {#if result && !result.aa}
          <button class="pill pill-accent" onclick={() => (app.contrastFg = result!.suggestion)}>{t("تحسين لون النص")}</button>
        {/if}
      </div>
    </section>
  </div>

  <section class="glass card result">
    <h3>{t("نتيجة الفحص")}</h3>
    {#if result}
      <strong class="ratio code">{result.ratio.toFixed(2)}:1</strong>
      <span class="muted small">{t("نسبة التباين")}</span>
      <div class="cells">
        {#each cells as c}
          <div class="cell" class:fail={!c.pass}>
            <span class="level code">{#if c.pass}<Icon name="check" size={15} />{:else}<Icon name="x" size={15} />{/if}{c.level}</span>
            <span>{t(c.size)}</span>
            <span class="muted small">{t(c.pass ? "ناجح" : "راسب")}</span>
          </div>
        {/each}
      </div>
    {/if}
  </section>
</div>

<style>
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(340px, 1fr));
    gap: 18px;
    align-items: start;
  }
  .col {
    display: flex;
    flex-direction: column;
    gap: 18px;
  }
  .preview {
    border-radius: 24px;
    padding: 32px 28px;
    border: 1px solid var(--line);
    transition: background 0.25s, color 0.25s;
  }
  .preview h2 {
    margin: 0 0 10px;
    font-size: var(--text-display);
  }
  .preview p {
    margin: 0;
  }
  .card {
    padding: 20px;
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
  .pair {
    display: flex;
    align-items: flex-end;
    gap: 10px;
    flex-wrap: wrap;
  }
  .field-col {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .row {
    display: flex;
    gap: 8px;
    flex-wrap: wrap;
  }
  h3 {
    margin: 0;
    font-size: var(--text-title);
  }
  .ratio {
    font-size: 56px;
    line-height: 1.1;
    text-align: end;
    margin-top: 20px;
  }
  .result .muted.small {
    text-align: start;
  }
  .cells {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 12px;
  }
  .cell {
    display: flex;
    flex-direction: column;
    gap: 2px;
    padding: 14px 16px;
    border-radius: 16px;
    background: var(--tint);
    border: 1px solid var(--line);
  }
  .level {
    display: flex;
    align-items: center;
    gap: 4px;
    font-weight: 700;
    color: var(--accent-text);
    align-self: flex-start;
  }
  .fail .level {
    color: var(--danger);
  }
  .small {
    font-size: var(--text-label);
  }
</style>
