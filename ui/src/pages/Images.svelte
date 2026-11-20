<script lang="ts">
  import { app } from "../lib/app.svelte";
  import { ipc } from "../lib/ipc";
  import { t } from "../lib/i18n";
  import { addToPalette, exportColors, useInContrast } from "../lib/actions";
  import type { ImageColor, ImageResult } from "../lib/types";
  import Icon from "../components/Icon.svelte";
  import { images } from "../lib/images.svelte";
  import ImageGuide from "../components/ImageGuide.svelte";

  const COMPACT = 5;
  let busy = $state(false);
  let over = $state(false);
  let all = $state(false);
  let cursor = $state<{ x: number; y: number; hex: string } | null>(null);
  let canvas: HTMLCanvasElement | null = null;

  const result = $derived(images.result);
  // Most dominant first; equal shares keep the engine's order, so the choice never shuffles.
  const shown = $derived.by(() => {
    if (!result) return [];
    const ordered = result.colors.map((c, i) => ({ c, i })).sort((a, b) => b.c.percent - a.c.percent || a.i - b.i).map((x) => x.c);
    return all ? ordered : ordered.slice(0, COMPACT);
  });
  // Leaders draw out once for each new picture and for each Show all / Show top five; a page that is
  // only revisited shows them at rest (desktop/Studio.cs).
  let play = $state(images.drawn ? 0 : 1);
  const name = (c: ImageColor) => (app.language === "en" ? c.english : c.name);

  async function take(run: () => Promise<ImageResult | null>) {
    busy = true;
    const started = performance.now();
    try {
      const r = await run();
      if (r) { app.timed("image-to-colors", started); images.result = r; all = false; cursor = null; images.drawn = true; play++; app.toast("استُخرجت ألوان الصورة"); }
    } catch (e) { app.fail(e); }
    busy = false;
  }
  async function fromFile(file: File) {
    if (!/^image\/(png|jpeg|webp)$/.test(file.type)) { app.fail("اختر صورة واحدة بصيغة PNG أو JPG أو WEBP."); return; }
    if (file.size > 32 << 20) { app.fail("اختر صورة أصغر من 32 ميغابايت."); return; }
    take(async () => ipc.analyzeImage(new Uint8Array(await file.arrayBuffer()), file.name));
  }
  function paste(e: ClipboardEvent) {
    if (e.target instanceof HTMLInputElement) return;
    const file = [...(e.clipboardData?.files ?? [])][0];
    e.preventDefault();
    if (file) fromFile(file); else take(() => ipc.pasteImage());
  }
  function drop(e: DragEvent) {
    e.preventDefault();
    over = false;
    const file = e.dataTransfer?.files[0];
    if (file) fromFile(file);
  }

  // The preview drawn once on an offscreen canvas, so a click reads the exact pixel under it.
  $effect(() => {
    if (!result) return;
    const img = new Image();
    img.onload = () => {
      canvas = document.createElement("canvas");
      canvas.width = result.previewWidth; canvas.height = result.previewHeight;
      canvas.getContext("2d", { willReadFrequently: true })!.drawImage(img, 0, 0);
    };
    img.src = result.preview;
  });
  function sample(x: number, y: number) {
    if (!canvas || !result) return;
    x = Math.max(0, Math.min(result.previewWidth - 1, Math.round(x)));
    y = Math.max(0, Math.min(result.previewHeight - 1, Math.round(y)));
    const [r, g, b] = canvas.getContext("2d")!.getImageData(x, y, 1, 1).data;
    cursor = { x, y, hex: "#" + [r, g, b].map((v) => v.toString(16).padStart(2, "0")).join("").toUpperCase() };
  }
  function keys(e: KeyboardEvent) {
    if (!result) return;
    const c = cursor ?? { x: result.previewWidth / 2, y: result.previewHeight / 2, hex: "" };
    const step = e.shiftKey ? 10 : 1;
    const moves: Record<string, [number, number]> = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
    if (moves[e.key]) { e.preventDefault(); sample(c.x + moves[e.key][0], c.y + moves[e.key][1]); }
    if (e.key === "Enter" && cursor) { app.copy(cursor.hex); app.edit([{ op: "remember", hex: cursor.hex }]); }
  }

  async function colorActions(c: ImageColor) {
    const i = await app.choose(name(c), ["نسخ اللون", "فتح في الملتقط", "إضافة إلى لوحة", "استخدام كلون للنص", "استخدام كلون للخلفية"]);
    if (i === 0) app.copy(c.hex);
    if (i === 1) { app.edit([{ op: "setColor", hex: c.hex }]); app.page = 0; }
    if (i === 2) addToPalette([c.hex]);
    if (i === 3) useInContrast(c.hex, "fg");
    if (i === 4) useInContrast(c.hex, "bg");
  }
  async function savePalette() {
    const n = await app.prompt("لوحة جديدة من صورة", result!.name, "اسم اللوحة");
    if (n && (await app.edit([{ op: "addPalette", name: n, colors: shown.map((c) => c.hex) }]))) {
      app.paletteId = app.ws!.palettes.at(-1)!.id;
      app.toast("حُفظت الباقة كلوحة جديدة");
    }
  }
</script>

<svelte:window onpaste={paste} />

<section
  class="glass drop"
  class:over
  aria-label={t("تحليل الصورة")}
  ondragover={(e) => { e.preventDefault(); over = true; }}
  ondragleave={() => (over = false)}
  ondrop={drop}
>
  <p class="muted">{t("اسحب صورة هنا أو الصقها باستخدام ")}<span class="code">Ctrl+V</span> · PNG / JPG / WEBP · 32 MB</p>
  <div class="row">
    <button class="pill pill-accent" disabled={busy} onclick={() => take(() => ipc.analyzeImageFile())}><Icon name="upload" size={16} />{t(result ? "تغيير الصورة" : "اختيار صورة")}</button>
    <button class="pill" disabled={busy} onclick={() => take(() => ipc.pasteImage())}><Icon name="copy" size={16} />{t("لصق صورة")}</button>
    {#if busy}<span class="muted">{t("جارٍ تحليل الصورة…")}</span>{/if}
  </div>
  <p class="muted small">{t("الصورة تبقى على جهازك. تُستخرج الألوان محلياً.")}</p>
</section>

{#if result}
  <section class="glass result">
    <header class="head">
      <h2 dir="auto">{result.name}</h2>
      <div class="row">
        <button class="pill pill-accent" onclick={savePalette} title={t("حفظ الألوان المعروضة كلوحة في مكتبتك")}><Icon name="device-floppy" size={16} />{t("حفظ اللوحة")}</button>
        <button class="round" onclick={() => exportColors(result!.name, shown.map((c) => [name(c), c.hex]))} title={t("تصدير الألوان المعروضة")} aria-label={t("تصدير الألوان المعروضة")}><Icon name="download" size={16} /></button>
        <button class="round" onclick={() => addToPalette(shown.map((c) => c.hex))} title={t("إضافة إلى لوحة")} aria-label={t("إضافة إلى لوحة")}><Icon name="plus" size={16} /></button>
      </div>
    </header>

    <ImageGuide
      src={result.preview}
      width={result.previewWidth}
      height={result.previewHeight}
      colors={shown}
      {play}
      {cursor}
      onopen={colorActions}
      onpixel={(fx, fy) => sample(fx * result!.previewWidth, fy * result!.previewHeight)}
      onkey={keys}
    />

    {#if result.colors.length > COMPACT}
      <div class="row">
        <button class="pill" onclick={() => { all = !all; play++; }}><Icon name={all ? "chevrons-up" : "chevrons-down"} size={16} />{all ? t("عرض أبرز {0}", COMPACT) : t("عرض الكل")}</button>
        <span class="muted small">{all ? t("تُعرض الألوان كلها ({0}).", result.colors.length) : t("عرض مختصر: أبرز {0} من {1} ألوان.", COMPACT, result.colors.length)}</span>
      </div>
    {/if}
    <p class="muted small">{t("انقر لافتة لون لإجراءاته.")}</p>
    {#if cursor}
      <div class="pixel row">
        <span class="chip big" style:background={cursor.hex}></span>
        <span class="code">{cursor.x}, {cursor.y} · {cursor.hex}</span>
        <button class="pill" onclick={() => app.copy(cursor!.hex)}>{t("نسخ اللون")}</button>
        <button class="pill" onclick={() => { app.edit([{ op: "setColor", hex: cursor!.hex }]); app.page = 0; }}>{t("فتح في الملتقط")}</button>
        <button class="pill" onclick={() => addToPalette([cursor!.hex])}>{t("إضافة إلى لوحة")}</button>
      </div>
    {/if}
  </section>
{/if}

<style>
  .drop {
    padding: 20px;
    display: flex;
    flex-direction: column;
    gap: 12px;
    transition: border-color 0.2s, background 0.2s;
  }
  .drop.over {
    border-color: var(--color-brand);
    border-style: dashed;
  }
  .drop p {
    margin: 0;
  }
  .row {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 8px;
  }
  .small {
    font-size: var(--text-label);
  }
  .result {
    padding: 20px;
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
  .result p {
    margin: 0;
  }
  .head {
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 12px;
    flex-wrap: wrap;
  }
  .head h2 {
    margin: 0;
    font-size: var(--text-title);
  }
  .chip {
    width: 18px;
    height: 18px;
    flex: none;
    border-radius: 5px;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .chip.big {
    width: 28px;
    height: 28px;
    border-radius: 8px;
  }
</style>
