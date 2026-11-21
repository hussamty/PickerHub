<script lang="ts">
  // Palette of the day. The Rust core owns the catalog, the choice, the cooldown and the state file;
  // this page only shows what it returns and reports what the user did with it.
  import { onMount } from "svelte";
  import { app } from "../lib/app.svelte";
  import { ipc } from "../lib/ipc";
  import { t } from "../lib/i18n";
  import { exportColors, useInContrast, addToPalette } from "../lib/actions";
  import { ROLES, ROLE_LABELS, type Discovery, type Palette } from "../lib/types";
  import Icon from "../components/Icon.svelte";
  import Select from "../components/Select.svelte";
  import Sheet from "../components/Sheet.svelte";
  import DailyStage, { type Card } from "../components/DailyStage.svelte";
  import { fade } from "svelte/transition";

  interface Role { role: string; label: string; hex: string; ink: string; name: string; rgb: string; hsl: string }
  interface DailyPalette { id: string; name: string; english: string; story: string; storyEnglish: string; serial: number; createdAt: string; moods: { tag: string; label: string }[]; dark: Role[]; light: Role[]; accessible: boolean }
  interface Status { state: "sealed" | "revealed" | "waiting" | "clock"; palette: DailyPalette | null; remaining: number; retryAt: string | null; nextAt: string | null; serial: number; altered: boolean; fresh: boolean; flags: { saved: boolean; applied: boolean; dismissed: boolean } }

  const FORMATS = ["HEX", "RGB", "HSL"];
  let d = $state<Status | null>(null);
  let dark = $state(app.theme === "dark");
  let format = $state(0);
  let remaining = $state(0);
  let names = $state<Record<string, string>>({});
  let sheet = $state<"" | "archive" | "locks">("");
  let arrived = $state(true);
  let stage = $state<DailyStage | null>(null);
  // The stage that opened the envelope stays on the page: its cards are the ones that flew out.
  let opening = $state(false);
  let sealSerial = $state(1);

  async function load(action = "status", value?: string) {
    try {
      d = await ipc.daily(action, value);
      remaining = d!.remaining;
      if (d!.state === "sealed") { sealSerial = d!.serial + 1; opening = false; }
      if (d!.palette && d!.state !== "sealed") remember(d!.palette);
    } catch (e) { app.fail(e); }
  }
  onMount(() => {
    load();
    const timer = setInterval(() => {
      if (!d || d.state === "sealed" || d.state === "clock") return;
      remaining = Math.max(0, remaining - 1);
      if (remaining === 0) load();
    }, 1000);
    const keys = (e: KeyboardEvent) => { if (app.preview && e.key.toLowerCase() === "r" && !(e.target instanceof HTMLInputElement)) replay(); };
    addEventListener("keydown", keys);
    return () => { clearInterval(timer); removeEventListener("keydown", keys); };
  });

  const p = $derived(d?.palette ?? null);
  const roles = $derived(p ? (dark ? p.dark : p.light) : []);
  $effect(() => {
    if (app.language !== "en" || !roles.length) return;
    ipc.names(roles.map((r) => r.hex)).then((rows) => (names = Object.fromEntries(rows.map((r) => [r.hex, r.english]))));
  });
  const code = (r: Role) => (format === 0 ? r.hex : format === 1 ? r.rgb : r.hsl);
  const cards = $derived<Card[]>(roles.map((r) => ({ hex: r.hex, ink: r.ink, label: t(r.label), code: code(r), name: app.language === "en" ? (names[r.hex] ?? r.name) : r.name })));
  const title = $derived(p ? (app.language === "en" ? p.english : p.name) + t(dark ? " (داكن)" : " (فاتح)") : "");
  const story = $derived(p ? (app.language === "en" ? p.storyEnglish : p.story) : "");
  const span = (s: number) => [Math.floor(s / 3600), Math.floor((s % 3600) / 60), s % 60].map((v) => String(v).padStart(2, "0")).join(":");
  const roleMap = (list: Role[]) => Object.fromEntries(list.map((r) => [r.role, r.hex]));
  const withLocks = (m: Record<string, string>) => {
    const out = { ...m };
    for (const [k, v] of Object.entries(app.ws!.brandLocks)) if (k in out) out[k] = v;
    return out;
  };
  const themes = $derived(p ? { light: withLocks(roleMap(p.light)), dark: withLocks(roleMap(p.dark)) } : null);
  let allAA = $state(true);
  $effect(() => {
    if (!themes) return;
    Promise.all([ipc.contrastChecks(themes.light), ipc.contrastChecks(themes.dark)]).then(([a, b]) => (allAA = [...a, ...b].every((c) => c.aa)));
  });

  function remember(pal: DailyPalette) {
    const id = `${pal.id}:${pal.serial}`;
    if (app.ws!.discoveries.some((x) => x.id === id)) return;
    const discovery: Discovery = { id, name: pal.name, createdAt: pal.createdAt, light: roleMap(pal.light), dark: roleMap(pal.dark) };
    app.edit([{ op: "archive", discovery }]);
  }
  async function reveal(): Promise<Card[] | null> {
    try {
      const next: Status = await ipc.daily("reveal");
      if (!next.fresh || !next.palette) { d = next; return null; }
      arrived = false;
      opening = true;
      d = next;
      remaining = next.remaining;
      remember(next.palette);
      app.toast("فُتحت باقة اليوم: {0}", undefined, [app.language === "en" ? next.palette.english : next.palette.name]);
      const list = dark ? next.palette.dark : next.palette.light;
      return list.map((r) => ({ hex: r.hex, ink: r.ink, label: t(r.label), code: code(r), name: r.name }));
    } catch (e) { app.fail(e); return null; }
  }
  const newId = () => crypto.randomUUID().replaceAll("-", "");
  async function save() {
    const palette: Palette = { id: "", name: title, collection: "", roles: roleMap(roles), colors: roles.map((r) => ({ id: newId(), name: t(r.label), hex: r.hex, favorite: false })) };
    if (await app.edit([{ op: "import", palette, into: null }])) {
      app.paletteId = app.ws!.palettes.at(-1)!.id;
      await load("save");
      app.toast("حُفظت الباقة كلوحة جديدة");
    }
  }
  async function apply() {
    const target = app.palette!;
    if (await app.edit([{ op: "addColors", palette: target.id, hexes: roles.map((r) => r.hex) }])) {
      await load("apply");
      app.toast("أُضيفت ألوان الباقة إلى «{0}»", undefined, [target.name]);
    }
  }
  async function dismiss() { await load("dismiss"); app.toast("سنقلّل ظهور الباقات المشابهة"); }
  async function roleActions() {
    const i = await app.choose("إجراءات اللون", roles.map((r) => `${t(r.label)} · ${r.hex}`));
    if (i == null) return;
    const hex = roles[i].hex;
    const j = await app.choose(hex, ["نسخ اللون", "فتح في الملتقط", "إضافة إلى لوحة", "استخدام كلون للنص", "استخدام كلون للخلفية"]);
    if (j === 0) app.copy(hex);
    if (j === 1) { app.edit([{ op: "setColor", hex }]); app.page = 0; }
    if (j === 2) addToPalette([hex]);
    if (j === 3) useInContrast(hex, "fg");
    if (j === 4) useInContrast(hex, "bg");
  }
  async function exportThemes(th: Record<string, Record<string, string>>) {
    const i = await app.choose("صيغة التصدير", ["CSS · light + dark", "Design tokens · JSON", "WCAG contrast · JSON"]);
    if (i == null) return;
    try {
      const [name, text] = await ipc.semantic((["css", "tokens", "report"] as const)[i], th);
      if (await ipc.exportText(name, text)) app.toast("حُفظ الملف");
    } catch (e) { app.fail(e); }
  }
  async function replay() { await ipc.daily("reset"); d = null; await load(); app.toast("الظرف مغلق من جديد"); }
  const local = (iso: string | null) => (iso ? new Date(iso).toLocaleString(app.language === "ar" ? "ar" : "en", { dateStyle: "medium", timeStyle: "short" }) : "");
</script>

{#if app.preview}<p class="preview muted">{t("معاينة: مكتبة مؤقتة تُحذف عند الإغلاق ولا تمسّ مكتبتك. R لإعادة المعاينة.")}</p>{/if}

{#if d}
  {#if d.state === "sealed" || opening}
    <DailyStage bind:this={stage} sealed serial={sealSerial} {cards} onopen={reveal} ondone={() => (arrived = true)} oncopy={(c) => app.copy(c.code, true)} />
  {:else if p}
    {#key `${p.id}|${p.serial}|${dark}|${format}|${app.language}`}
      <DailyStage sealed={false} cards={cards} serial={p.serial} oncopy={(c) => app.copy(c.code, true)} />
    {/key}
  {/if}

  {#if d.state === "sealed"}
    <div class="words" out:fade={{ duration: app.motion ? 220 : 0 }}>
      <h2>{t(d.palette ? "باقة جديدة بانتظارك" : "اكتشاف اليوم")}</h2>
      <p class="muted">{t("باقة ألوان واحدة كل 24 ساعة، مستوحاة من مشهد طبيعي، بأدوار جاهزة للتصميم.")}</p>
      <button class="pill pill-accent open" onclick={() => stage?.open()}><Icon name="sparkles" size={16} />{t("افتح الباقة")}</button>
      {#if d.palette}
        <p class="muted">{t("الباقة السابقة: ")}{app.language === "en" ? d.palette.english : d.palette.name}</p>
        <div class="dots">
          {#each (dark ? d.palette.dark : d.palette.light) as r}
            <button class="dot" style:background={r.hex} title="{t(r.label)} {r.hex}" aria-label="{t(r.label)} {r.hex}" onclick={() => app.copy(r.hex)}></button>
          {/each}
        </div>
      {/if}
    </div>
  {/if}

  {#if p && d.state !== "sealed" && arrived}
    <section class="glass story">
      <div class="tell">
        <div class="top">
          <div class="moods">{#each p.moods as m}<span class="mood">{t(m.label)}</span>{/each}</div>
          <span class="code muted serial">no. {String(p.serial).padStart(2, "0")} / {p.english.toLowerCase()}</span>
        </div>
        <h2>{app.language === "en" ? p.english : p.name}</h2>
        <p class="lead">{story}</p>
        {#if d.state === "clock"}
          <strong class="danger">{t("وقت الجهاز غير صالح")}</strong>
          <p class="muted small">{t("ساعة الجهاز الآن أقدم من آخر وقت سجّله التطبيق، لذلك توقّف العدّ. صحّح الساعة، أو انتظر حتى ")}{local(d.retryAt)}{t(" لتفتح باقة جديدة. باقتك الحالية ما زالت متاحة.")}</p>
        {:else}
          <div class="timer"><Icon name="clock" size={18} class="muted" /><span class="muted">{t("الباقة التالية بعد")}</span><strong class="code">{span(remaining)}</strong></div>
        {/if}
        {#if d.altered}<p class="muted small">{t("تغيّر ملف حالة الباقة خارج التطبيق، فبدأ العدّ من جديد.")}</p>{/if}
      </div>
      <div class="controls">
        <div class="row">
          <button class="pill pill-accent" disabled={d.flags.saved} onclick={save}><Icon name={d.flags.saved ? "check" : "device-floppy"} size={16} />{t(d.flags.saved ? "محفوظة في لوحاتك" : "حفظ كلوحة")}</button>
          <button class="pill" disabled={d.flags.applied} onclick={apply}><Icon name={d.flags.applied ? "check" : "plus"} size={16} />{t(d.flags.applied ? "أُضيفت إلى اللوحة الحالية" : "تطبيق على اللوحة الحالية")}</button>
        </div>
        <div class="row" dir="ltr">
          <button class="round" onclick={() => exportColors(title, roles.map((r) => [t(r.label), r.hex]))} title={t("تصدير الباقة")} aria-label={t("تصدير الباقة")}><Icon name="download" size={16} /></button>
          <button class="round" onclick={() => app.copy(roles.map((r) => `${r.role}: ${code(r)}`).join("\n"), true)} title={t("نسخ كل الأكواد")} aria-label={t("نسخ كل الأكواد")}><Icon name="copy" size={16} /></button>
          <button class="round" onclick={roleActions} title={t("إجراءات اللون")} aria-label={t("إجراءات اللون")}><Icon name="dots" size={16} /></button>
          <button class="round" disabled={d.flags.dismissed} onclick={dismiss} title={t(d.flags.dismissed ? "استُبعدت" : "لا تعجبني هذه الباقة")} aria-label={t(d.flags.dismissed ? "استُبعدت" : "لا تعجبني هذه الباقة")}><Icon name="x" size={16} /></button>
        </div>
        <div class="row">
          <Select options={["داكن", "فاتح"]} value={dark ? 0 : 1} onchange={(i) => (dark = i === 0)} width={104} />
          <Select options={FORMATS} raw value={format} onchange={(i) => (format = i)} width={104} />
          <button class="pill" onclick={() => load("adjust", "lighter")}>{t("أفتح")}</button>
          <button class="pill" onclick={() => load("adjust", "darker")}>{t("أغمق")}</button>
          <button class="pill" onclick={() => load("adjust", "muted")}>{t("أهدأ")}</button>
          <button class="pill" onclick={() => load("adjust", "vivid")}>{t("أزهى")}</button>
        </div>
      </div>
    </section>
    {#if themes}
      <div class="row">
        <button class="pill" onclick={() => exportThemes(themes)}>{t("تصدير أدوار الفاتح والداكن")}</button>
        <span class="muted small">{t(allAA ? "كل أزواج النص المحددة تحقق AA" : "بعض الألوان لا تحقق AA كنص عادي. صدّر تقرير التباين.")}</span>
      </div>
    {/if}
  {/if}

  <div class="row">
    <button class="pill" onclick={() => (sheet = "archive")}>{t("أرشيف الباقات")}</button>
    <button class="pill" onclick={() => (sheet = "locks")}>{t("ألوان العلامة المثبتة")}</button>
  </div>
{/if}

{#if sheet === "archive"}
  <Sheet title={t("أرشيف الباقات")} onclose={() => (sheet = "")} width={560}>
    {#if app.ws!.discoveries.length === 0}<p class="muted">{t("ستظهر هنا الباقات التي تفتحها، حتى 90 باقة.")}</p>{/if}
    {#each app.ws!.discoveries as entry (entry.id)}
      <article class="entry">
        <div class="entry-head">
          <strong>{entry.name}</strong>
          <span class="muted small">{local(entry.createdAt)}</span>
        </div>
        <div class="dots">{#each ROLES as r}{#if entry[dark ? "dark" : "light"][r]}<span class="dot small-dot" style:background={entry[dark ? "dark" : "light"][r]} title={t(ROLE_LABELS[r])}></span>{/if}{/each}</div>
        <div class="row">
          <button class="pill" onclick={() => exportThemes({ light: withLocks(entry.light), dark: withLocks(entry.dark) })}>{t("تصدير")}</button>
          <button class="pill" onclick={() => app.edit([{ op: "import", into: null, palette: { id: "", name: entry.name + t(dark ? " (داكن)" : " (فاتح)"), collection: "", roles: entry[dark ? "dark" : "light"], colors: ROLES.filter((r) => entry[dark ? "dark" : "light"][r]).map((r) => ({ id: newId(), name: t(ROLE_LABELS[r]), hex: entry[dark ? "dark" : "light"][r], favorite: false })) } }], "حُفظت الباقة كلوحة جديدة")}>{t("حفظ كلوحة")}</button>
        </div>
      </article>
    {/each}
  </Sheet>
{/if}

{#if sheet === "locks"}
  <Sheet title={t("ألوان العلامة المثبتة")} onclose={() => (sheet = "")} width={460}>
    <p class="muted small">{t("تُطبّق على تصدير باقة اليوم. افحص التباين بعد التثبيت.")}</p>
    {#each ["primary", "accent"] as role}
      {@const locked = app.ws!.brandLocks[role]}
      <div class="lock">
        <span class="dot" style:background={locked ?? "transparent"}></span>
        <span class="label">{t(ROLE_LABELS[role])}</span>
        <span class="code muted">{locked ?? "—"}</span>
        <button class="pill" onclick={() => app.edit([{ op: "brandLock", role, hex: app.ws!.color }])}>{t("تثبيت اللون المحدد")}</button>
        {#if locked}<button class="pill" onclick={() => app.edit([{ op: "brandLock", role, hex: null }])}>{t("إلغاء التثبيت")}</button>{/if}
      </div>
    {/each}
  </Sheet>
{/if}

<style>
  .preview {
    margin: 0;
    font-size: var(--text-label);
  }
  .words {
    display: grid;
    justify-items: center;
    text-align: center;
    gap: 6px;
  }
  .words h2 {
    margin: 0;
    font-size: var(--text-display);
  }
  .words p {
    margin: 0;
  }
  .open {
    margin-top: 8px;
  }
  .story {
    display: grid;
    grid-template-columns: 1fr 1.25fr;
    gap: 24px;
    padding: 22px;
    animation: arrive 0.45s var(--ease-spring);
  }
  @keyframes arrive {
    from {
      opacity: 0;
      transform: translateY(12px) scale(0.98);
    }
  }
  @media (max-width: 1000px) {
    .story {
      grid-template-columns: 1fr;
    }
  }
  .tell,
  .controls {
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  .controls {
    justify-content: center;
  }
  .top {
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 10px;
  }
  .moods {
    display: flex;
    gap: 6px;
  }
  .mood {
    padding: 3px 12px;
    border-radius: 999px;
    border: 1px solid var(--line);
    background: var(--tint);
    font-size: var(--text-label);
  }
  .serial {
    font-size: var(--text-caption);
  }
  .tell h2 {
    margin: 0;
    font-size: var(--text-display);
  }
  .lead {
    margin: 0;
    font-size: var(--text-lead);
  }
  .timer {
    display: flex;
    align-items: center;
    gap: 10px;
  }
  .timer strong {
    font-size: var(--text-heading);
  }
  .danger {
    color: var(--danger);
  }
  .row {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    align-items: center;
  }
  .small {
    font-size: var(--text-label);
    margin: 0;
  }
  .dots {
    display: flex;
    gap: 8px;
    justify-content: center;
  }
  .dot {
    all: unset;
    width: 26px;
    height: 26px;
    border-radius: 999px;
    box-shadow: inset 0 0 0 1px var(--line);
    cursor: pointer;
  }
  .small-dot {
    width: 20px;
    height: 20px;
    cursor: default;
    --hand: idle;
  }
  .entry {
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding: 12px;
    border-radius: 14px;
    background: var(--tint);
  }
  .entry .dots {
    justify-content: flex-start;
  }
  .entry-head {
    display: flex;
    justify-content: space-between;
    gap: 10px;
  }
  .lock {
    display: flex;
    align-items: center;
    gap: 10px;
    flex-wrap: wrap;
  }
  .lock .label {
    font-weight: 600;
    min-width: 70px;
  }
</style>
