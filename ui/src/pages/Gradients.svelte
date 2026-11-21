<script lang="ts">
  import { app } from "../lib/app.svelte";
  import { ipc } from "../lib/ipc";
  import { t } from "../lib/i18n";
  import { isHex, normalHex } from "../lib/actions";
  import type { Gradient, Stop } from "../lib/types";
  import Icon from "../components/Icon.svelte";
  import Choice from "../components/Choice.svelte";

  const fresh = (): Gradient => ({ id: "", name: t("تدرجي"), type: "linear", angle: 90, stops: [{ hex: "#2856B4", pos: 0 }, { hex: "#87CEEB", pos: 100 }] });
  let draft = $state<Gradient>(app.ws!.gradients[0] ? structuredClone($state.snapshot(app.ws!.gradients[0])) : fresh());
  let active = $state(0);
  let bar: HTMLElement;

  const sorted = $derived([...draft.stops].sort((a, b) => a.pos - b.pos));
  const css = $derived(
    draft.type === "linear"
      ? `linear-gradient(${draft.angle}deg, ${sorted.map((s) => `${s.hex} ${s.pos}%`).join(", ")})`
      : `radial-gradient(circle, ${sorted.map((s) => `${s.hex} ${s.pos}%`).join(", ")})`,
  );
  const stop = $derived<Stop | undefined>(draft.stops[active]);

  function open(g: Gradient) { draft = structuredClone($state.snapshot(g)); active = 0; }
  function addStop(e?: MouseEvent) {
    if (draft.stops.length >= 32) return;
    let pos = 50;
    if (e) { const r = bar.getBoundingClientRect(); pos = Math.round(((e.clientX - r.left) / r.width) * 100); }
    draft.stops.push({ hex: app.ws!.color, pos: Math.max(0, Math.min(100, pos)) });
    active = draft.stops.length - 1;
  }
  function drag(e: PointerEvent, i: number) {
    active = i;
    const el = e.currentTarget as HTMLElement;
    el.setPointerCapture(e.pointerId);
    const move = (ev: PointerEvent) => {
      const r = bar.getBoundingClientRect();
      draft.stops[i].pos = Math.max(0, Math.min(100, Math.round(((ev.clientX - r.left) / r.width) * 100)));
    };
    el.onpointermove = move;
    el.onpointerup = () => { el.onpointermove = null; el.onpointerup = null; };
  }
  async function save() {
    const name = draft.name.trim() || (await app.prompt("اسم التدرج", t("تدرجي")));
    if (!name) return;
    const id = draft.id || crypto.randomUUID().replaceAll("-", "");
    const gradient = { ...$state.snapshot(draft), id, name };
    if (await app.edit([{ op: "saveGradient", gradient }], "حُفظ التدرج")) draft.id = id;
  }
  async function remove(g: Gradient) {
    if (!(await app.confirm("حذف التدرج", "حذف التدرج؟", true))) return;
    await app.edit([{ op: "deleteGradient", id: g.id }]);
    if (draft.id === g.id) draft = fresh();
  }
  async function exportCss() {
    try { if (await ipc.exportText(`${draft.name.replace(/[\\/:*?"<>|]/g, "-") || "gradient"}.css`, `.gradient {\n  background: ${css};\n}\n`)) app.toast("حُفظ الملف"); }
    catch (e) { app.fail(e); }
  }
</script>

<div class="grid">
  <section class="glass card editor">
    <div class="preview" style:background={css}></div>
    <div class="line">
      <input class="field" dir="auto" bind:value={draft.name} maxlength="100" aria-label={t("اسم التدرج")} placeholder={t("اسم التدرج")} />
    </div>
    <div class="line">
      <span class="muted">{t("النوع")}</span>
      <Choice options={["خطي", "دائري"]} value={draft.type === "linear" ? 0 : 1} onchange={(i) => (draft.type = i ? "radial" : "linear")} />
      {#if draft.type === "linear"}
        <span class="muted">{t("الزاوية")}</span>
        <input class="angle" dir="ltr" type="range" min="0" max="360" bind:value={draft.angle} aria-label={t("الزاوية")} />
        <span class="code">{draft.angle}°</span>
      {/if}
    </div>
    <div>
      <span class="muted small">{t("نقاط التدرج")}</span>
      <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
      <div class="bar" dir="ltr" bind:this={bar} style:background={`linear-gradient(90deg, ${sorted.map((s) => `${s.hex} ${s.pos}%`).join(", ")})`} ondblclick={(e) => addStop(e)}>
        {#each draft.stops as s, i}
          <button class="knob" class:on={i === active} style:left="{s.pos}%" style:background={s.hex}
            aria-label={t("موضع النقطة") + " " + s.pos}
            onpointerdown={(e) => drag(e, i)}
            onkeydown={(e) => { if (e.key === "ArrowLeft") s.pos = Math.max(0, s.pos - 1); if (e.key === "ArrowRight") s.pos = Math.min(100, s.pos + 1); }}
          ></button>
        {/each}
      </div>
    </div>
    {#if stop}
      <div class="line">
        <span class="muted">{t("لون النقطة")}</span>
        <input class="field code hex" dir="ltr" value={stop.hex} maxlength="7" aria-label={t("لون النقطة")}
          onchange={(e) => { const v = (e.currentTarget as HTMLInputElement).value; if (isHex(v)) stop.hex = normalHex(v); else app.fail("أدخل ست خانات HEX."); }} />
        <span class="muted">{t("موضع النقطة")}</span>
        <input class="field code pos" dir="ltr" type="number" min="0" max="100" bind:value={stop.pos} aria-label={t("موضع النقطة")} />
        <button class="pill" onclick={() => (stop.hex = app.ws!.color)}>{t("استخدام اللون المحدد")}</button>
      </div>
    {/if}
    <div class="row">
      <button class="pill" onclick={() => addStop()} disabled={draft.stops.length >= 32}><Icon name="plus" size={16} />{t("إضافة نقطة")}</button>
      <button class="pill" disabled={draft.stops.length <= 2} onclick={() => { draft.stops.splice(active, 1); active = 0; }}><Icon name="minus" size={16} />{t("إزالة النقطة")}</button>
    </div>
    <code class="css code">{css}</code>
    <div class="row">
      <button class="pill pill-accent" onclick={save}><Icon name="device-floppy" size={16} />{t("حفظ التدرج")}</button>
      <button class="pill" onclick={() => app.copy(`background: ${css};`, true)}><Icon name="copy" size={16} />{t("نسخ CSS")}</button>
      <button class="pill" onclick={exportCss}><Icon name="download" size={16} />{t("تصدير CSS")}</button>
    </div>
  </section>

  <section class="glass card">
    <header class="head">
      <h2>{t("مكتبة التدرجات")}</h2>
      <button class="round" onclick={() => (draft = fresh())} title={t("تدرج جديد")} aria-label={t("تدرج جديد")}><Icon name="plus" size={16} /></button>
    </header>
    {#if app.ws!.gradients.length === 0}
      <p class="muted">{t("احفظ تدرجك للعودة إليه في أي وقت.")}</p>
    {/if}
    <ul>
      {#each app.ws!.gradients as g (g.id)}
        {@const sw = [...g.stops].sort((a, b) => a.pos - b.pos).map((s) => `${s.hex} ${s.pos}%`).join(", ")}
        <li class:on={g.id === draft.id}>
          <button class="item" onclick={() => open(g)}>
            <span class="thumb" style:background={g.type === "linear" ? `linear-gradient(${g.angle}deg, ${sw})` : `radial-gradient(circle, ${sw})`}></span>
            <span dir="auto">{g.name}</span>
          </button>
          <button class="round small-round" onclick={() => remove(g)} title={t("حذف التدرج")} aria-label={t("حذف التدرج")}><Icon name="trash" size={15} /></button>
        </li>
      {/each}
    </ul>
  </section>
</div>

<style>
  .grid {
    display: grid;
    grid-template-columns: minmax(0, 2fr) minmax(260px, 1fr);
    gap: 18px;
    align-items: start;
  }
  @media (max-width: 1060px) {
    .grid {
      grid-template-columns: 1fr;
    }
  }
  .card {
    padding: 20px;
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
  .preview {
    height: 200px;
    border-radius: 18px;
    border: 1px solid var(--line);
  }
  .line,
  .row {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 10px;
  }
  .angle {
    flex: 1;
    min-width: 120px;
    accent-color: var(--color-brand);
  }
  .bar {
    position: relative;
    height: 36px;
    margin-top: 8px;
    border-radius: 12px;
    border: 1px solid var(--line);
  }
  .knob {
    all: unset;
    position: absolute;
    top: 50%;
    width: 18px;
    height: 46px;
    translate: -50% -50%;
    border-radius: 9px;
    border: 3px solid #fff;
    box-shadow: 0 0 0 1px rgb(0 0 0 / 0.5), 0 3px 8px rgb(0 0 0 / 0.35);
    cursor: ew-resize;
    --hand: grab;
    touch-action: none;
  }
  .knob.on {
    border-color: var(--color-brand);
  }
  .knob:focus-visible {
    outline: 2px solid var(--color-brand);
    outline-offset: 2px;
  }
  .hex {
    width: 120px;
  }
  .pos {
    width: 86px;
  }
  .css {
    display: block;
    padding: 12px;
    border-radius: 12px;
    background: var(--tint);
    font-size: var(--text-label);
    overflow-wrap: anywhere;
    user-select: text;
  }
  .head {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }
  .head h2 {
    margin: 0;
    font-size: var(--text-title);
  }
  ul {
    list-style: none;
    margin: 0;
    padding: 0;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  li {
    display: flex;
    align-items: center;
    gap: 6px;
    border-radius: 12px;
  }
  li.on {
    background: var(--tint-strong);
  }
  .item {
    all: unset;
    flex: 1;
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 6px 8px;
    border-radius: 12px;
    cursor: pointer;
  }
  .item:hover {
    background: var(--tint);
  }
  .thumb {
    width: 44px;
    height: 28px;
    border-radius: 8px;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .small-round {
    width: 34px;
    height: 34px;
  }
  .small {
    font-size: var(--text-label);
  }
</style>
