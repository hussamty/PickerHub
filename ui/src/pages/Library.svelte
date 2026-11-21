<script lang="ts">
  import { app } from "../lib/app.svelte";
  import { ipc } from "../lib/ipc";
  import { t } from "../lib/i18n";
  import { FORMATS, useInContrast } from "../lib/actions";
  import { pickMany } from "../lib/pick";
  import type { Swatch } from "../lib/types";
  import Icon from "../components/Icon.svelte";
  import Select from "../components/Select.svelte";
  import Roles from "../components/Roles.svelte";
  import Matrix from "../components/Matrix.svelte";
  import ProjectSearch from "../components/ProjectSearch.svelte";
  import DeckView from "../components/DeckView.svelte";

  const PAGE = 48;
  const VIEWS = ["grid", "list", "deck"] as const;
  const VIEW_LABELS = ["شبكة", "قائمة", "بطاقات"];

  let query = $state("");
  let favorites = $state(false);
  let searching = $state(false);
  let pageIndex = $state(0);
  let selected = $state<Set<string>>(new Set());
  let inks = $state<Record<string, string>>({});
  let sheet = $state<"" | "roles" | "matrix" | "search">("");
  let card = $state(0);

  const palette = $derived(app.palette!);
  const view = $derived(app.ws!.libraryView);
  const filtered = $derived.by(() => {
    const q = query.trim().toLowerCase().replace(/^#/, "");
    return palette.colors.filter((c) =>
      (!favorites || c.favorite) && (!q || c.name.toLowerCase().includes(q) || c.hex.toLowerCase().includes(q)));
  });
  const pages = $derived(Math.max(1, Math.ceil(filtered.length / PAGE)));
  const shown = $derived(filtered.slice(pageIndex * PAGE, pageIndex * PAGE + PAGE));

  $effect(() => { void palette.id; pageIndex = 0; card = 0; selected = new Set(); });
  $effect(() => { if (pageIndex >= pages) pageIndex = pages - 1; });
  $effect(() => {
    const need = shown.map((c) => c.hex).filter((h) => !inks[h]);
    if (need.length) ipc.names(need).then((rows) => { const next = { ...inks }; rows.forEach((r) => (next[r.hex] = r.ink)); inks = next; });
  });

  function toggle(id: string) {
    const next = new Set(selected);
    next.has(id) ? next.delete(id) : next.add(id);
    selected = next;
  }
  const picked = $derived(palette.colors.filter((c) => selected.has(c.id)));

  async function newPalette() {
    const name = await app.prompt("لوحة جديدة", "", "اسم اللوحة");
    if (!name) return;
    if (await app.edit([{ op: "addPalette", name, colors: [] }])) app.paletteId = app.ws!.palettes.at(-1)!.id;
  }
  async function exportPalette(colors?: Swatch[]) {
    const choice = await app.choose(colors ? "تصدير المحدد" : "تصدير اللوحة", [...FORMATS]);
    if (choice == null) return;
    try {
      const saved = colors
        ? await ipc.exportColors(palette.name, colors.map((c) => [c.name, c.hex]), FORMATS[choice])
        : await ipc.exportPalette(palette.id, FORMATS[choice]);
      if (saved) app.toast("حُفظ الملف");
    } catch (e) { app.fail(e); }
  }
  async function importPalette() {
    try {
      const preview = await ipc.importPalette(palette.id);
      if (!preview) return;
      const i = await app.choose(
        t("{0} لون في الملف", preview.palette.colors.length) + " · " + t("الدمج في {0}: إضافة {1} وتجاوز {2} مكررة", palette.name, preview.unique, preview.duplicates),
        ["إنشاء لوحة جديدة", "دمج في اللوحة الحالية"],
      );
      if (i == null) return;
      if (await app.edit([{ op: "import", palette: preview.palette, into: i === 1 ? palette.id : null }])) {
        if (i === 0) app.paletteId = app.ws!.palettes.at(-1)!.id;
        app.toast("استُوردت اللوحة.");
      }
    } catch (e) { app.fail(e); }
  }
  async function movePicked(ids: string[]) {
    const others = app.ws!.palettes.filter((p) => p.id !== palette.id);
    if (!others.length) { app.fail("أنشئ لوحة ثانية أولاً."); return; }
    const i = await app.choose("نقل إلى لوحة", others.map((p) => p.name));
    if (i == null) return;
    if (await app.edit([{ op: "moveColorsTo", from: palette.id, to: others[i].id, ids }])) selected = new Set();
  }
  async function deletePicked() {
    if (!(await app.confirm("حذف المحدد", "حذف الألوان المحددة؟ يمكنك التراجع.", true))) return;
    if (await app.edit([{ op: "deleteColors", palette: palette.id, ids: [...selected] }])) selected = new Set();
  }
  async function tools() {
    const actions = ["تسمية اللوحة", "اسم المشروع", "أدوار الألوان", "تصدير اللوحة", "استيراد لوحة JSON", "مصفوفة التباين", "إزالة المكرر", "البحث في المشاريع", "حذف اللوحة"];
    const i = await app.choose("أدوات اللوحة", actions);
    if (i === 0) { const n = await app.prompt("تسمية اللوحة", palette.name, "اسم اللوحة"); if (n) app.edit([{ op: "renamePalette", id: palette.id, name: n }]); }
    if (i === 1) { const n = await app.prompt("اسم المشروع", palette.collection, "اسم المشروع"); if (n != null) app.edit([{ op: "setCollection", id: palette.id, collection: n }]); }
    if (i === 2) sheet = "roles";
    if (i === 3) exportPalette();
    if (i === 4) importPalette();
    if (i === 5) sheet = "matrix";
    if (i === 6) {
      const before = palette.colors.length;
      if (await app.edit([{ op: "dedupe", palette: palette.id }])) app.toast("أزيلت الألوان المكررة: {0}", undefined, [before - app.palette!.colors.length]);
    }
    if (i === 7) sheet = "search";
    if (i === 8 && (await app.confirm("حذف اللوحة", t("حذف «") + palette.name + t("» بكل ألوانها؟"), true))) app.edit([{ op: "deletePalette", id: palette.id }]);
  }
  async function swatchMenu(c: Swatch) {
    const i = await app.choose(c.name, ["اسم اللون", "نقل اللون إلى", "فتح في الملتقط", "استخدام اللون المحدد للنص", "استخدام اللون المحدد للخلفية", "تقديم", "تأخير", "حذف اللون"]);
    const at = palette.colors.findIndex((x) => x.id === c.id);
    if (i === 0) { const n = await app.prompt("اسم اللون", c.name); if (n) app.edit([{ op: "renameColor", palette: palette.id, id: c.id, name: n }]); }
    if (i === 1) movePicked([c.id]);
    if (i === 2) { app.edit([{ op: "setColor", hex: c.hex }]); app.page = 0; }
    if (i === 3) useInContrast(c.hex, "fg");
    if (i === 4) useInContrast(c.hex, "bg");
    if (i === 5 && at > 0) app.edit([{ op: "moveColor", palette: palette.id, id: c.id, to: at - 1 }]);
    if (i === 6) app.edit([{ op: "moveColor", palette: palette.id, id: c.id, to: at + 1 }]);
    if (i === 7) app.edit([{ op: "deleteColors", palette: palette.id, ids: [c.id] }]);
  }
  const paletteNames = $derived(app.ws!.palettes.map((p) => (p.collection ? `${p.collection} · ${p.name}` : p.name)));
</script>

<div class="toolbar">
  <Select options={paletteNames} raw value={app.ws!.palettes.findIndex((p) => p.id === palette.id)} onchange={(i) => (app.paletteId = app.ws!.palettes[i].id)} label="لوحات الألوان" width={180} />
  <button class="round" onclick={newPalette} title={t("لوحة جديدة")} aria-label={t("لوحة جديدة")}><Icon name="plus" size={17} /></button>
  <button class="pill pill-accent" onclick={() => app.edit([{ op: "addColors", palette: palette.id, hexes: [app.ws!.color] }], "أُضيفت إلى اللوحة الحالية")}>
    <Icon name="plus" size={16} />{t("إضافة اللون المحدد")}
  </button>
  <button class="pill" onclick={() => pickMany(palette.id)}>{t("التقاط متعدد")}</button>
  <Select options={VIEW_LABELS} value={VIEWS.indexOf(view)} onchange={(i) => app.setting("libraryView", VIEWS[i])} width={120} />
  <button class="round" class:on={searching} onclick={() => (searching = !searching)} title={t("البحث في ألوان اللوحة")} aria-label={t("البحث في ألوان اللوحة")}><Icon name="search" size={17} /></button>
  <button class="round" onclick={tools} title={t("أدوات اللوحة")} aria-label={t("أدوات اللوحة")}><Icon name="dots" size={17} /></button>
</div>

{#if view === "deck" && palette.colors.length}
  <DeckView items={palette.colors.map((c) => ({ hex: c.hex, name: c.name }))} bind:index={card} title={palette.name}>
    {#snippet actions()}
      {@const c = palette.colors[card]}
      {#if c}
        <button class="round" onclick={() => app.copy(c.hex)} title={t("نسخ اللون")} aria-label={t("نسخ اللون")}><Icon name="copy" size={16} /></button>
        <button class="round" onclick={() => { app.edit([{ op: "setColor", hex: c.hex }]); app.page = 0; }} title={t("فتح في الملتقط")} aria-label={t("فتح في الملتقط")}><Icon name="color-picker" size={16} /></button>
        <button class="round" onclick={() => swatchMenu(c)} title={t("إجراءات اللون")} aria-label={t("إجراءات اللون")}><Icon name="dots" size={16} /></button>
        <button class="round" disabled={card === 0} onclick={async () => { if (await app.edit([{ op: "moveColor", palette: palette.id, id: c.id, to: card - 1 }])) card--; }} title={t("تقديم")} aria-label={t("تقديم")}><Icon name="arrow-up" size={16} /></button>
        <button class="round" disabled={card >= palette.colors.length - 1} onclick={async () => { if (await app.edit([{ op: "moveColor", palette: palette.id, id: c.id, to: card + 1 }])) card++; }} title={t("تأخير")} aria-label={t("تأخير")}><Icon name="arrow-down" size={16} /></button>
        <button class="round" onclick={() => movePicked([c.id])} title={t("نقل إلى لوحة")} aria-label={t("نقل إلى لوحة")}><Icon name="folder" size={16} /></button>
        <button class="round" onclick={() => app.edit([{ op: "deleteColors", palette: palette.id, ids: [c.id] }])} title={t("حذف اللون")} aria-label={t("حذف اللون")}><Icon name="trash" size={16} /></button>
      {/if}
    {/snippet}
  </DeckView>
{:else}
<section class="glass pane">
  <div class="filters">
    {#if searching || query}
      <!-- svelte-ignore a11y_autofocus -->
      <input class="field" dir="auto" bind:value={query} placeholder={t("ابحث بالاسم أو HEX")} autofocus aria-label={t("البحث في ألوان اللوحة")} />
    {/if}
    <label class="fav"><input type="checkbox" bind:checked={favorites} /><span>{t("المفضلة فقط")}</span></label>
    <span class="muted code count">{palette.colors.length}/2048</span>
  </div>

  {#if palette.colors.length === 0}
    <div class="empty">
      <Icon name="palette" size={34} />
      <h2>{t("ابدأ لوحتك الأولى")}</h2>
      <p class="muted">{t("أضف اللون المحدد أو احفظ مجموعة من الملتقط.")}</p>
    </div>
  {:else if filtered.length === 0}
    <p class="muted empty">{t("لا توجد ألوان مطابقة. جرّب بحثاً آخر.")}</p>
  {:else}
    <div class="pager">
      {#if selected.size}
        <span class="muted">{t("المحدد: ")}<span class="code">{selected.size}</span></span>
        <button class="pill" onclick={() => app.copy(picked.map((c) => c.hex).join("\n"), true)}>{t("نسخ المحدد")}</button>
        <button class="pill" onclick={() => exportPalette(picked)}>{t("تصدير المحدد")}</button>
        <button class="pill" onclick={() => movePicked([...selected])}>{t("نقل المحدد")}</button>
        <button class="pill" onclick={deletePicked}>{t("حذف المحدد")}</button>
        <button class="pill" onclick={() => (selected = new Set())}>{t("إلغاء التحديد")}</button>
      {:else}
        <button class="pill" onclick={() => (selected = new Set(shown.map((c) => c.id)))}>{t("تحديد الصفحة")}</button>
      {/if}
      {#if pages > 1}
        <span class="grow"></span>
        <button class="pill" disabled={pageIndex === 0} onclick={() => pageIndex--}>{t("السابق")}</button>
        <span class="code">{pageIndex + 1} / {pages}</span>
        <button class="pill" disabled={pageIndex >= pages - 1} onclick={() => pageIndex++}>{t("التالي")}</button>
      {/if}
    </div>

    <div class="swatches {view}">
      {#each shown as c (c.id)}
        <article class="swatch" class:picked={selected.has(c.id)} style:background={c.hex} style:color={inks[c.hex] ?? "#111"}>
          <strong class="code hex">{c.hex}</strong>
          <label class="name">
            <input type="checkbox" checked={selected.has(c.id)} onchange={() => toggle(c.id)} aria-label={t("تحديد ") + c.name} />
            <span class="ring" aria-hidden="true"></span>
            <span>{c.name}</span>
          </label>
          <div class="actions" dir="ltr">
            <button class="round on-color" onclick={() => swatchMenu(c)} title={t("إجراءات اللون")} aria-label={t("إجراءات اللون")}><Icon name="dots" size={15} /></button>
            <button class="round on-color" onclick={() => app.copy(c.hex)} title={t("نسخ اللون")} aria-label={t("نسخ اللون")}><Icon name="copy" size={15} /></button>
            <button class="round on-color star" class:fav={c.favorite} onclick={() => app.edit([{ op: "favorite", palette: palette.id, id: c.id, value: !c.favorite }])} title={t("تبديل المفضلة: ") + c.name} aria-pressed={c.favorite}>
              {c.favorite ? "★" : "☆"}
            </button>
          </div>
        </article>
      {/each}
    </div>
  {/if}
</section>

{/if}

{#if sheet === "roles"}<Roles palette={palette} onclose={() => (sheet = "")} />{/if}
{#if sheet === "matrix"}<Matrix colors={(picked.length ? picked : palette.colors).slice(0, 12)} onclose={() => (sheet = "")} />{/if}
{#if sheet === "search"}<ProjectSearch onclose={() => (sheet = "")} />{/if}

<style>
  .toolbar {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    align-items: center;
  }
  .round.on {
    background: var(--color-brand);
    color: #111;
  }
  .pane {
    padding: 20px;
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
  .filters,
  .pager {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 10px;
  }
  .filters input.field {
    flex: 1;
    min-width: 220px;
  }
  .fav {
    display: flex;
    align-items: center;
    gap: 8px;
    cursor: pointer;
  }
  .fav input {
    accent-color: var(--color-brand);
    width: 18px;
    height: 18px;
  }
  .count {
    margin-inline-start: auto;
  }
  .grow {
    flex: 1;
  }
  .empty {
    display: grid;
    justify-items: center;
    text-align: center;
    gap: 6px;
    padding: 40px 0;
  }
  .empty h2 {
    margin: 6px 0 0;
  }
  .empty p {
    margin: 0;
  }
  .swatches.grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
    gap: 12px;
  }
  .swatches.deck {
    display: flex;
    gap: 14px;
    overflow-x: auto;
    padding-bottom: 8px;
    scroll-snap-type: x mandatory;
  }
  .swatches.deck .swatch {
    flex: 0 0 240px;
    min-height: 300px;
    scroll-snap-align: center;
  }
  .swatches.list {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .swatch {
    position: relative;
    display: flex;
    flex-direction: column;
    gap: 8px;
    min-height: 186px;
    padding: 16px;
    border-radius: 18px;
    border: 1px solid var(--line);
    transition: transform 0.2s var(--ease-liquid), box-shadow 0.2s;
  }
  .swatch:hover {
    transform: translateY(-3px);
    box-shadow: var(--glass-shadow);
  }
  .swatch.picked {
    outline: 3px solid var(--color-brand);
    outline-offset: 2px;
  }
  .list .swatch {
    flex-direction: row;
    align-items: center;
    min-height: 0;
    padding: 8px 12px;
    border-radius: 14px;
  }
  .list .swatch:hover {
    transform: none;
  }
  .list .hex {
    width: 90px;
  }
  .list .name {
    flex: 1;
    margin: 0;
  }
  .list .actions {
    margin: 0;
  }
  .hex {
    text-align: center;
    margin-top: 18px;
  }
  .list .hex {
    margin: 0;
    text-align: start;
  }
  .name {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-top: auto;
    font-weight: 600;
    cursor: pointer;
  }
  .name input {
    position: absolute;
    opacity: 0;
    pointer-events: none;
  }
  .ring {
    width: 18px;
    height: 18px;
    flex: none;
    border-radius: 999px;
    border: 1.5px solid currentColor;
  }
  .name input:checked + .ring {
    background: currentColor;
    box-shadow: inset 0 0 0 3px var(--canvas);
  }
  .name input:focus-visible + .ring {
    outline: 2px solid var(--color-brand);
    outline-offset: 2px;
  }
  .actions {
    display: flex;
    gap: 6px;
    justify-content: center;
  }
  .actions .round {
    width: 36px;
    height: 36px;
  }
  .on-color {
    background: color-mix(in srgb, currentColor 14%, transparent);
    border-color: color-mix(in srgb, currentColor 22%, transparent);
    color: inherit;
  }
  .star {
    font-size: 15px;
  }
</style>
