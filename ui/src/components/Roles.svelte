<script lang="ts">
  // Semantic roles saved with a palette, independent of its swatches, with CSS and DTCG token export.
  import { app } from "../lib/app.svelte";
  import { ipc } from "../lib/ipc";
  import { t } from "../lib/i18n";
  import { isHex, normalHex } from "../lib/actions";
  import { ROLES, ROLE_LABELS, type Palette } from "../lib/types";
  import Sheet from "./Sheet.svelte";
  import Choice from "./Choice.svelte";

  let { palette, onclose }: { palette: Palette; onclose: () => void } = $props();
  let values = $state<Record<string, string>>(Object.fromEntries(ROLES.map((r) => [r, palette.roles[r] ?? ""])));
  let dark = $state(app.theme === "dark");
  let feedback = $state("");

  function read(): Record<string, string> | null {
    const out: Record<string, string> = {};
    for (const r of ROLES) {
      const v = values[r].trim();
      if (!v) continue;
      if (!isHex(v)) { app.fail("أدخل ست خانات HEX."); return null; }
      out[r] = normalHex(v);
    }
    return out;
  }
  async function save() {
    const roles = read();
    if (roles && (await app.edit([{ op: "setRoles", id: palette.id, roles }]))) onclose();
  }
  async function exportAs(kind: "css" | "tokens") {
    const roles = read();
    if (!roles) return;
    try {
      const [name, text] = await ipc.semantic(kind, { [dark ? "dark" : "light"]: roles });
      if (await ipc.exportText(name, text)) app.toast("حُفظ الملف");
    } catch (e) { app.fail(e); }
  }
  async function check() {
    const roles = read();
    if (!roles) return;
    const rows = await ipc.contrastChecks(roles);
    feedback = rows.length === 0 ? t("عيّن النص والخلفية لفحص التباين.")
      : rows.map((c) => `${c.foreground} / ${c.background}: ${c.ratio.toFixed(2)}:1 · AA ${c.aa ? "✓" : "×"}`).join("\n");
  }
</script>

<Sheet title={t("أدوار الألوان")} {onclose} width={520}>
  <p class="muted small">{t("أدوار محفوظة مستقلة. اترك الدور فارغاً لاستبعاده.")}</p>
  <Choice options={["فاتح", "داكن"]} value={dark ? 1 : 0} onchange={(i) => (dark = i === 1)} />
  <div class="rows">
    {#each ROLES as r}
      <label class="role">
        <span class="swatch" style:background={isHex(values[r]) ? normalHex(values[r]) : "transparent"}></span>
        <span class="label">{t(ROLE_LABELS[r])}</span>
        <input class="field code" dir="ltr" bind:value={values[r]} placeholder="#RRGGBB" maxlength="7" />
        <button class="pill" type="button" onclick={() => (values[r] = app.ws!.color)}>{t("استخدام اللون المحدد")}</button>
      </label>
    {/each}
  </div>
  {#if feedback}<pre class="feedback code">{feedback}</pre>{/if}
  <div class="row">
    <button class="pill pill-accent" onclick={save}>{t("حفظ")}</button>
    <button class="pill" onclick={() => exportAs("css")}>CSS</button>
    <button class="pill" onclick={() => exportAs("tokens")}>Design tokens</button>
    <button class="pill" onclick={check}>{t("فحص التباين")}</button>
  </div>
</Sheet>

<style>
  .rows {
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .role {
    display: grid;
    grid-template-columns: 24px 110px 1fr auto;
    align-items: center;
    gap: 10px;
  }
  .swatch {
    width: 24px;
    height: 24px;
    border-radius: 8px;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .label {
    font-weight: 600;
  }
  .row {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
  }
  .feedback {
    margin: 0;
    padding: 12px;
    border-radius: 12px;
    background: var(--tint);
    font-size: var(--text-label);
    white-space: pre-wrap;
  }
  .small {
    font-size: var(--text-label);
    margin: 0;
  }
</style>
