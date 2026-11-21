<script lang="ts">
  import { app } from "../lib/app.svelte";
  import { t } from "../lib/i18n";
  import { ipc } from "../lib/ipc";
  import Sheet from "./Sheet.svelte";
  import Toggle from "./Toggle.svelte";
  import Choice from "./Choice.svelte";

  const ws = $derived(app.ws!);
  let limit = $state(app.ws?.limit ?? 32);

  // Updates: only builds made with the release key can update themselves (src-tauri/src/update.rs).
  type Offer = { version: string; current: string; notes: string | null; date: string | null };
  let updates = $state<{ supported: boolean; busy: boolean; checked: boolean; offer: Offer | null }>({ supported: false, busy: false, checked: false, offer: null });
  ipc.updateSupported().then((v) => (updates.supported = v)).catch(() => {});
  // Closing to the tray is offered only where a tray icon exists to bring the window back.
  let tray = $state(false);
  ipc.trayAvailable().then((v) => (tray = v)).catch(() => {});
  async function check() {
    updates.busy = true;
    try { updates.offer = await ipc.updateCheck(); updates.checked = true; } catch (e) { app.fail(e); }
    updates.busy = false;
  }
  async function install() {
    if (!(await app.confirm("تثبيت التحديث", "يُنزَّل الإصدار الجديد ويُتحقق من توقيعه، ثم يُثبَّت ويُعاد تشغيل PickerHub. مكتبتك تبقى كما هي."))) return;
    updates.busy = true;
    try { await ipc.updateInstall(); } catch (e) { app.fail(e); }
    updates.busy = false;
  }

  async function exportBackup() {
    try { if (await ipc.backupExport()) app.toast("حُفظ الملف"); } catch (e) { app.fail(e); }
  }
  async function restore() {
    if (!(await app.confirm("استعادة نسخة احتياطية", "تستبدل الاستعادة مكتبتك الحالية وحالة باقة اليوم. تُحفظ نسخة من المكتبة الحالية أولاً."))) return;
    try {
      const s = await ipc.backupRestore();
      if (s) { app.set(s); app.toast("استُعيدت النسخة الاحتياطية"); }
    } catch (e) { app.fail(e); }
  }
</script>

<Sheet title={t("الإعدادات")} onclose={() => (app.settingsOpen = false)} width={520}>
  <section>
    <h3>{t("المظهر والحركة")}</h3>
    <div class="line"><span>{t("اللغة")}</span>
      <Choice options={["العربية", "English"]} value={ws.language === "en" ? 1 : 0} onchange={(i) => app.setting("language", i ? "en" : "ar")} raw />
    </div>
    <div class="line"><span>{t("المظهر")}</span>
      <Choice options={["داكن", "فاتح"]} value={ws.theme === "light" ? 1 : 0} onchange={(i) => app.setting("theme", i ? "light" : "dark")} />
    </div>
    <Toggle label="اتباع مظهر النظام" checked={ws.followSystemTheme} onchange={(v) => app.setting("followSystemTheme", v)} />
    <Toggle label="تقليل الحركة" checked={ws.reduceMotion} onchange={(v) => app.setting("reduceMotion", v)} />
    <Toggle label="تقليل الشفافية" checked={ws.reduceTransparency} onchange={(v) => app.setting("reduceTransparency", v)} />
    <Toggle label="زجاج كامل" hint="تمويه حي خلف كل اللوحات. أجمل على الأجهزة القوية وأبطأ على غيرها." checked={!!ws.fullGlass} onchange={(v) => app.setting("fullGlass", v)} />
  </section>

  {#if tray}
    <section>
      <h3>{t("النافذة")}</h3>
      <Toggle label="عند الإغلاق، تصغير إلى شريط النظام" hint="يبقى PickerHub يعمل في الخلفية. أعد فتحه من أيقونة شريط النظام أو بتشغيله مرة أخرى، وأنهِه من «إنهاء PickerHub» في قائمتها." checked={!!ws.minimizeToTray} onchange={(v) => app.setting("minimizeToTray", v)} />
    </section>
  {/if}

  <section>
    <h3>{t("القطّارة")}</h3>
    <Toggle label="نسخ اللون تلقائياً بعد الالتقاط" checked={ws.autoCopy} onchange={(v) => app.setting("autoCopy", v)} />
    <div class="line"><span>{t("صيغة النسخ")}</span>
      <Choice options={["HEX", "RGB", "HSL"]} value={["HEX", "RGB", "HSL"].indexOf(ws.copyFormat)} onchange={(i) => app.setting("copyFormat", ["HEX", "RGB", "HSL"][i])} raw />
    </div>
    {#if ws.copyFormat === "HEX"}
      <Toggle label="أحرف HEX كبيرة" checked={ws.hexUppercase} onchange={(v) => app.setting("hexUppercase", v)} />
      <Toggle label="تضمين #" checked={ws.hexPrefix} onchange={(v) => app.setting("hexPrefix", v)} />
    {/if}
    <Toggle label="اختصار عام من أي نافذة" checked={ws.globalShortcut} onchange={(v) => app.setting("globalShortcut", v)} />
    {#if ws.globalShortcut}
      <div class="line"><span>{t("الاختصار")}</span>
        <Choice options={["Ctrl+Shift+P", "Ctrl+Alt+P", "Ctrl+Shift+C"]} value={["Ctrl+Shift+P", "Ctrl+Alt+P", "Ctrl+Shift+C"].indexOf(ws.shortcut)} onchange={(i) => app.setting("shortcut", ["Ctrl+Shift+P", "Ctrl+Alt+P", "Ctrl+Shift+C"][i])} raw />
      </div>
      <Toggle label="التقاط سريع" hint="ينسخ الاختصار العام دون تنشيط النافذة" checked={ws.quickPick} onchange={(v) => app.setting("quickPick", v)} />
    {/if}
    {#if app.platform === "linux" && app.session === "wayland"}
      <Toggle label="استخدام عدسة PickerHub عبر مشاركة الشاشة" hint="تستخدم القطّارة العادية منتقي ألوان النظام. فعّل المشاركة للعدسة الحيّة والتبديل بين النوافذ أثناء الالتقاط." checked={ws.useWaylandScreenSharing} onchange={(v) => app.setting("useWaylandScreenSharing", v)} />
      {#if app.sharing}
        <div class="row"><button class="pill" onclick={() => ipc.stopSharing()}>{t("إنهاء مشاركة الشاشة")}</button></div>
      {/if}
    {/if}
  </section>

  <section>
    <h3>{t("المكتبة المحلية")}</h3>
    <div class="line"><label for="history-limit">{t("عدد العناصر المحفوظة")}</label>
      <span class="limit">
        <input id="history-limit" class="field code" type="number" min="1" max="256" bind:value={limit} />
        <button class="pill" onclick={() => app.setting("limit", Math.max(1, Math.min(256, Math.round(limit))))}>{t("تطبيق")}</button>
      </span>
    </div>
    <div class="row">
      <button class="pill" onclick={exportBackup}>{t("تنزيل نسخة احتياطية")}</button>
      <button class="pill" onclick={restore}>{t("استعادة نسخة احتياطية")}</button>
    </div>
    <p class="muted small">{t("يبقى السجل على جهازك؛ لا تُرسل أي بيانات تلقائياً.")}</p>
  </section>

  <section>
    <h3>{t("التحديثات")}</h3>
    {#if updates.supported}
      <div class="row">
        <button class="pill" disabled={updates.busy} onclick={check}>{t(updates.busy && !updates.offer ? "جارٍ التحقق…" : "التحقق من التحديثات")}</button>
        {#if updates.offer}
          <button class="pill pill-accent" disabled={updates.busy} onclick={install}>{t("تثبيت {0} وإعادة التشغيل", updates.offer.version)}</button>
        {/if}
      </div>
      {#if updates.offer}
        <p class="small">{t("الإصدار {0} متاح. نسختك {1}.", updates.offer.version, updates.offer.current)}</p>
        {#if updates.offer.notes}<p class="muted small notes" dir="auto">{updates.offer.notes}</p>{/if}
      {:else if updates.checked}
        <p class="muted small">{t("تستخدم أحدث إصدار.")}</p>
      {/if}
      <p class="muted small">{t("لا يتصل PickerHub بالشبكة إلا حين تطلب التحقق. يُثبَّت التحديث فقط إذا طابق توقيعه مفتاح المشروع.")}</p>
    {:else}
      <p class="muted small">{t("هذه النسخة لا تحدّث نفسها. نزّل الإصدار الجديد من صفحة المشروع.")}</p>
    {/if}
  </section>

  <section>
    <h3>{t("الدعم والتشخيص")}</h3>
    <Toggle label="تسجيل الأخطاء والتوقيتات محلياً" checked={ws.diagnosticsEnabled} onchange={(v) => app.setting("diagnosticsEnabled", v)} />
    <div class="row">
      <button class="pill" onclick={async () => { try { if (await ipc.supportReport()) app.toast("حُفظ الملف"); } catch (e) { app.fail(e); } }}>{t("تنزيل تقرير الدعم")}</button>
    </div>
    <p class="muted small">{t("تقرير الدعم لا يتضمن ألوانك أو صورك أو مسارات ملفاتك.")}</p>
    <p class="muted small code">PickerHub {app.version} · {app.platform}{app.session ? " · " + app.session : ""}</p>
  </section>
</Sheet>

<style>
  section {
    display: flex;
    flex-direction: column;
    gap: 6px;
    padding-bottom: 12px;
    border-bottom: 1px solid var(--line);
  }
  .notes {
    white-space: pre-line;
    max-height: 8em;
    overflow-y: auto;
  }
  section:last-child {
    border-bottom: none;
  }
  h3 {
    margin: 0 0 4px;
    font-size: var(--text-title);
  }
  .line {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    min-height: 44px;
  }
  .row {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
  }
  .limit {
    display: flex;
    gap: 8px;
    align-items: center;
  }
  .limit input {
    width: 90px;
  }
  .small {
    font-size: var(--text-label);
    margin: 0;
  }
</style>
