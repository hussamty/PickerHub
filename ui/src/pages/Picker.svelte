<script lang="ts">
  import { app } from "../lib/app.svelte";
  import { ipc } from "../lib/ipc";
  import { t, colorName } from "../lib/i18n";
  import { addToPalette, exportColors, useInContrast, perFrame, isHex, normalHex } from "../lib/actions";
  import type { Frame } from "../lib/types";
  import Icon from "../components/Icon.svelte";
  import Select from "../components/Select.svelte";

  const KINDS = ["متجاورة", "متكاملة", "ثلاثية", "متكاملة منقسمة", "أحادية اللون", "عالية التباين"];
  const MAIN = ["HEX", "RGB", "HSL", "OKLCH"] as const;
  const MORE = ["RGBA", "HSV", "HSB", "CMYK"] as const;

  let kind = $state(0);
  let frame = $state<Frame | null>(null);
  let hexText = $state(app.ws!.color);
  let more = $state(false);
  // The spectrum keeps its own hue, saturation and value while dragging, so rounding to 8-bit
  // colors never makes the handle jump.
  let hsv = $state<[number, number, number]>([0, 0, 0]);
  let dragging = false;

  async function load(hex: string, fromSpectrum = false) {
    try {
      const f = await ipc.frame(hex, kind);
      frame = f;
      if (!fromSpectrum) hsv = [...f.hsv];
      if (document.activeElement?.id !== "hex-field") hexText = f.hex;
    } catch (e) { app.fail(e); }
  }
  $effect(() => { load(app.ws!.color); void kind; });

  const preview = perFrame((h: number, s: number, v: number) => ipc.fromHsv(h, s, v), (hex) => load(hex, true));
  function commit(hex: string) {
    if (hex.toUpperCase() !== app.ws!.color.toUpperCase()) app.edit([{ op: "setColor", hex }]);
  }

  function spectrum(e: PointerEvent, el: HTMLElement) {
    const r = el.getBoundingClientRect();
    const s = Math.min(1, Math.max(0, (e.clientX - r.left) / r.width));
    const v = 1 - Math.min(1, Math.max(0, (e.clientY - r.top) / r.height));
    hsv = [hsv[0], s, v];
    preview(hsv[0], s, v);
  }
  function hue(value: number) {
    hsv = [value, hsv[1], hsv[2]];
    preview(value, hsv[1], hsv[2]);
  }
  async function settle() {
    dragging = false;
    if (frame) commit(frame.hex);
  }
  function spectrumKeys(e: KeyboardEvent) {
    const step = e.shiftKey ? 0.1 : 0.01;
    let [h, s, v] = hsv;
    if (e.key === "ArrowLeft") s -= step; else if (e.key === "ArrowRight") s += step;
    else if (e.key === "ArrowUp") v += step; else if (e.key === "ArrowDown") v -= step; else return;
    e.preventDefault();
    hsv = [h, Math.min(1, Math.max(0, s)), Math.min(1, Math.max(0, v))];
    ipc.fromHsv(...hsv).then((hex) => { load(hex, true); commit(hex); });
  }
  function applyHex() {
    if (!isHex(hexText)) { app.fail("أدخل ست خانات HEX."); return; }
    commit(normalHex(hexText));
  }
  async function actions() {
    const hex = app.ws!.color;
    const i = await app.choose("إجراءات اللون", ["نسخ رمز اللون", "استخدام كلون للنص", "استخدام كلون للخلفية", "إضافة إلى لوحة", "تصدير اللون"]);
    if (i === 0) app.copy(hex);
    if (i === 1) useInContrast(hex, "fg");
    if (i === 2) useInContrast(hex, "bg");
    if (i === 3) addToPalette([hex]);
    if (i === 4 && frame) exportColors(colorName(frame), [[colorName(frame), hex]]);
  }
  const allFormats = $derived(frame ? [...MAIN, ...MORE].map((k) => `${k}: ${frame!.formats[k]}`).join("\n") : "");
</script>

{#if frame}
  <div class="grid">
    <div class="col">
      <section class="specimen" style:background={frame.hex} style:color={frame.ink}>
        <div class="face">
          <div class="names">
            <small class="code english">{frame.english.toLowerCase()}</small>
            <h2>{colorName(frame)}</h2>
            <small>{t(frame.base)}{t("، ")}{t(frame.shade)}</small>
          </div>
          <div class="quick" dir="ltr">
            <button class="round on-color" onclick={() => app.copy(frame!.hex)} title={t("نسخ اللون")} aria-label={t("نسخ اللون")}><Icon name="copy" size={17} /></button>
            <button class="round on-color" onclick={() => exportColors(colorName(frame), [[colorName(frame), frame!.hex]])} title={t("تصدير اللون")} aria-label={t("تصدير اللون")}><Icon name="download" size={17} /></button>
            <button class="round on-color" onclick={actions} title={t("إجراءات اللون")} aria-label={t("إجراءات اللون")}><Icon name="dots" size={17} /></button>
          </div>
          <strong class="code hex">{frame.hex}</strong>
        </div>
      </section>

      <section class="glass card">
        <label class="label muted" for="hex-field">HEX</label>
        <form class="hexrow" onsubmit={(e) => { e.preventDefault(); applyHex(); }}>
          <input id="hex-field" class="field code" dir="ltr" bind:value={hexText} maxlength="7" spellcheck="false" aria-label={t("قيمة HEX")} />
          <button class="round" type="submit" title={t("اعتماد HEX")} aria-label={t("اعتماد HEX")}><Icon name="check" size={17} /></button>
        </form>
        <button class="pill pill-accent wide" onclick={() => addToPalette([frame!.hex])}><Icon name="plus" size={16} />{t("إضافة إلى لوحة")}</button>
        <div
          class="spectrum"
          dir="ltr"
          role="slider"
          tabindex="0"
          aria-label={t("تشبع اللون وسطوعه")}
          aria-valuetext={frame.hex}
          aria-valuenow={Math.round(hsv[1] * 100)}
          style:--hue="hsl({hsv[0]} 100% 50%)"
          onpointerdown={(e) => { dragging = true; (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId); spectrum(e, e.currentTarget as HTMLElement); }}
          onpointermove={(e) => dragging && spectrum(e, e.currentTarget as HTMLElement)}
          onpointerup={settle}
          onpointercancel={settle}
          onkeydown={spectrumKeys}
        >
          <span class="handle" style:left="{hsv[1] * 100}%" style:top="{(1 - hsv[2]) * 100}%" style:background={frame.hex}></span>
        </div>
        <input class="hue" dir="ltr" type="range" min="0" max="359" step="1" value={Math.round(hsv[0])} aria-label={t("درجة اللون")}
          oninput={(e) => hue(+(e.currentTarget as HTMLInputElement).value)} onchange={settle} />
      </section>
    </div>

    <div class="col">
      <section class="glass card">
        <header class="head">
          <h3>{t("صيغ اللون")}</h3>
          <button class="round" onclick={() => app.copy(allFormats, true)} title={t("نسخ جميع الصيغ")} aria-label={t("نسخ جميع الصيغ")}><Icon name="copy" size={16} /></button>
        </header>
        <div class="formats">
          {#each more ? [...MAIN, ...MORE] : MAIN as key}
            <button class="format" onclick={() => app.copy(frame!.formats[key], true)} title={t("نسخ ") + key}>
              <span class="key code">{key}</span>
              <span class="code val">{frame.formats[key]}</span>
              <Icon name="copy" size={15} class="muted" />
            </button>
          {/each}
        </div>
        <button class="pill wide between" onclick={() => (more = !more)} aria-expanded={more}>
          {t("المزيد من الصيغ")}
          <Icon name={more ? "chevrons-up" : "chevrons-down"} size={16} />
        </button>
        {#if more}<p class="muted small">{t("sRGB · CMYK تقريبي للطباعة")}</p>{/if}
      </section>

      <section class="glass card">
        <header class="head">
          <h3>{t("تناغم الألوان")}</h3>
          <div class="tools">
            <Select options={KINDS} value={kind} onchange={(i) => (kind = i)} label="تناغم الألوان" width={150} />
            <button class="round" onclick={() => addToPalette(frame!.harmony.map((h) => h.hex))} title={t("إضافة المجموعة إلى لوحة")} aria-label={t("إضافة المجموعة إلى لوحة")}><Icon name="plus" size={16} /></button>
          </div>
        </header>
        <div class="strip" dir="ltr">
          {#each frame.harmony as h}
            <button style:background={h.hex} style:color={h.ink} onclick={() => commit(h.hex)} class="code">{h.hex}</button>
          {/each}
        </div>
      </section>

      {#if app.ws!.history.length}
        <section class="recent">
          <span class="muted small">{t("الألوان الأخيرة")}</span>
          <button class="pill" onclick={() => (app.page = 5)}>{t("عرض السجل")}</button>
          <div class="dots">
            {#each app.ws!.history.slice(0, 6) as hex}
              <button class="dot" style:background={hex} title={hex} aria-label={hex} onclick={() => commit(hex)}></button>
            {/each}
          </div>
        </section>
      {/if}
    </div>
  </div>
{/if}

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
  .card {
    padding: 20px;
    display: flex;
    flex-direction: column;
    gap: 12px;
  }
  .specimen {
    border-radius: 24px;
    padding: 22px;
    min-height: 136px;
    border: 1px solid var(--line);
    transition: background 0.25s, color 0.25s;
  }
  .face {
    display: grid;
    grid-template-columns: 1fr auto;
    grid-template-rows: 1fr auto;
    gap: 6px 12px;
    height: 100%;
  }
  .names {
    display: flex;
    flex-direction: column;
  }
  .names h2 {
    margin: 0;
    font-size: var(--text-feature);
    font-weight: 700;
    line-height: 1.4;
  }
  .english {
    opacity: 0.8;
    align-self: flex-start;
  }
  .quick {
    display: flex;
    gap: 8px;
    align-self: start;
  }
  .on-color {
    background: color-mix(in srgb, currentColor 14%, transparent);
    border-color: color-mix(in srgb, currentColor 22%, transparent);
    color: inherit;
  }
  .hex {
    grid-column: 2;
    align-self: end;
    font-size: var(--text-heading);
  }
  .label {
    font-size: var(--text-label);
  }
  .hexrow {
    display: flex;
    gap: 8px;
  }
  .wide {
    width: 100%;
  }
  .between {
    justify-content: space-between;
  }
  .spectrum {
    position: relative;
    height: 120px;
    border-radius: 14px;
    background:
      linear-gradient(to top, #000, transparent),
      linear-gradient(to right, #fff, var(--hue));
    cursor: crosshair;
    touch-action: none;
  }
  .handle {
    position: absolute;
    width: 14px;
    height: 14px;
    border-radius: 999px;
    border: 2px solid #fff;
    box-shadow: 0 0 0 1px rgb(0 0 0 / 0.5), 0 2px 6px rgb(0 0 0 / 0.4);
    translate: -50% -50%;
    pointer-events: none;
  }
  .hue {
    appearance: none;
    width: 100%;
    height: 4px;
    border-radius: 999px;
    background: linear-gradient(to right, #ff0 0%, #f00 2%, #ff0 17%, #0f0 33%, #0ff 50%, #00f 67%, #f0f 83%, #f00 100%);
    background: linear-gradient(to right, hsl(0 100% 50%), hsl(60 100% 50%), hsl(120 100% 50%), hsl(180 100% 50%), hsl(240 100% 50%), hsl(300 100% 50%), hsl(360 100% 50%));
    margin: 10px 0 4px;
  }
  .hue::-webkit-slider-thumb {
    appearance: none;
    width: 20px;
    height: 20px;
    border-radius: 999px;
    background: var(--color-brand);
    border: 2px solid var(--canvas);
    box-shadow: 0 2px 8px rgb(0 0 0 / 0.35);
    cursor: pointer;
  }
  .head {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 10px;
  }
  .head h3 {
    margin: 0;
    font-size: var(--text-title);
  }
  .tools {
    display: flex;
    gap: 8px;
    align-items: center;
  }
  .formats {
    display: flex;
    flex-direction: column;
  }
  .format {
    all: unset;
    display: grid;
    grid-template-columns: 64px 1fr auto;
    align-items: center;
    gap: 8px;
    padding: 8px 10px;
    border-radius: 10px;
    cursor: pointer;
    direction: ltr;
  }
  .format:hover {
    background: var(--tint);
  }
  .format:focus-visible {
    outline: 2px solid var(--color-brand);
  }
  .key {
    font-size: var(--text-caption);
    color: var(--muted);
  }
  .val {
    font-size: var(--text-control);
  }
  .strip {
    display: flex;
    height: 64px;
    border-radius: 16px;
    overflow: hidden;
  }
  .strip button {
    all: unset;
    flex: 1;
    display: grid;
    place-items: center;
    font-size: var(--text-caption);
    font-weight: 700;
    cursor: pointer;
    transition: flex 0.2s var(--ease-liquid);
  }
  .strip button:hover {
    flex: 1.3;
  }
  .recent {
    display: flex;
    align-items: center;
    gap: 12px;
    flex-wrap: wrap;
    padding-inline: 4px;
  }
  .dots {
    display: flex;
    gap: 8px;
  }
  .dot {
    all: unset;
    width: 34px;
    height: 34px;
    border-radius: 999px;
    box-shadow: inset 0 0 0 1px var(--line);
    cursor: pointer;
    transition: transform 0.18s var(--ease-liquid);
  }
  .dot:hover {
    transform: scale(1.1);
  }
  .small {
    font-size: var(--text-label);
    margin: 0;
  }
</style>
