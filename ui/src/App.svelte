<script lang="ts">
  import { onMount } from "svelte";
  import { getCurrentWindow } from "@tauri-apps/api/window";
  import { app } from "./lib/app.svelte";
  import { ipc } from "./lib/ipc";
  import { t } from "./lib/i18n";
  import { TOOLS } from "./lib/tools";
  import { pick } from "./lib/pick";
  import { listen } from "@tauri-apps/api/event";
  import type { Snapshot } from "./lib/types";
  import TitleBar from "./components/TitleBar.svelte";
  import Spine from "./components/Spine.svelte";
  import Toasts from "./components/Toasts.svelte";
  import Dialogs from "./components/Dialogs.svelte";
  import CaptureOptions from "./components/CaptureOptions.svelte";
  import Settings from "./components/Settings.svelte";
  import HandCursor from "./components/HandCursor.svelte";
  import Backdrop from "./components/Backdrop.svelte";
  import Picker from "./pages/Picker.svelte";
  import Library from "./pages/Library.svelte";
  import Contrast from "./pages/Contrast.svelte";
  import Images from "./pages/Images.svelte";
  import Gradients from "./pages/Gradients.svelte";
  import History from "./pages/History.svelte";
  import Daily from "./pages/Daily.svelte";

  const pages = [Picker, Library, Contrast, Images, Gradients, History, Daily];
  let ready = $state(false);

  onMount(async () => {
    const media = matchMedia("(prefers-color-scheme: dark)");
    app.systemDark = media.matches;
    media.addEventListener("change", (e) => (app.systemDark = e.matches));
    try {
      const boot = await ipc.boot();
      app.set(boot);
      app.platform = boot.platform;
      app.session = boot.session;
      app.version = boot.version;
      app.preview = boot.preview;
      if (boot.preview) app.page = 6;
      if (boot.recoveryNotice) app.toast(boot.recoveryNotice);
    } catch (e) {
      app.fail(e);
    }
    ready = true;
    app.timed("launch-to-workbench", 0, true);
    ipc.capturePortal().then((v) => (app.portal = v)).catch(() => {});
    ipc.sharing().then((v) => (app.sharing = v)).catch(() => {});
    // Picks started from the tray or the global shortcut report back here.
    type Picked = { hex: string; copied: string | null; snapshot: Snapshot };
    listen<Picked>("picked", (e) => {
      app.set(e.payload.snapshot);
      app.toast(e.payload.copied ? "نُسخ {0}" : "{0}", e.payload.hex, [e.payload.copied ?? e.payload.hex]);
    });
    listen("pick-cancelled", () => app.toast("أُلغي الالتقاط"));
    listen<boolean>("sharing", (e) => { app.sharing = e.payload; app.toast(e.payload ? "مشاركة الشاشة نشطة" : "توقفت مشاركة الشاشة."); });
    listen("pick-failed", (e) => app.fail(e.payload));
    listen("open-settings", () => (app.settingsOpen = true));
    // Show the window once the first frame is laid out, so there is no white flash. WebKitGTK runs no
    // animation frames for a window that is not shown yet, so a short timeout shows it regardless.
    let shown = false;
    const show = () => { if (!shown) { shown = true; getCurrentWindow().show(); } };
    requestAnimationFrame(show);
    setTimeout(show, 120);
  });

  // The document follows the library's language and appearance.
  $effect(() => {
    const root = document.documentElement;
    root.lang = app.language;
    root.dir = app.language === "ar" ? "rtl" : "ltr";
    root.dataset.theme = app.theme;
    root.classList.toggle("reduce-motion", !!app.ws?.reduceMotion);
    root.classList.toggle("follow-motion", true);
    root.classList.toggle("reduce-transparency", !!app.ws?.reduceTransparency);
    root.classList.toggle("full-glass", !!app.ws?.fullGlass);
  });

  // The tray speaks the interface's language.
  $effect(() => {
    const language = app.language;
    if (!ready || !language) return;
    ipc.trayLabels({ pick: t("التقاط لون"), show: t("إظهار PickerHub"), hide: t("إخفاء PickerHub"), settings: t("الإعدادات"), quit: t("إنهاء PickerHub") }).catch(() => {});
  });
  // The global shortcut follows its settings; a refused binding is reported, not fatal.
  // Keyed on the three settings alone: every edit replaces the library snapshot, and re-binding
  // on each one would ask the Wayland portal for consent again and again.
  const shortcutKey = $derived(app.ws ? `${app.ws.globalShortcut}|${app.ws.shortcut}|${app.ws.quickPick}` : "");
  $effect(() => {
    const key = shortcutKey;
    if (!ready || !key) return;
    const [enabled, shortcut, quick] = key.split("|");
    ipc.applyShortcut(enabled === "true", shortcut, quick === "true").catch((e) => app.fail(e));
  });

  function keys(e: KeyboardEvent) {
    const typing = e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement;
    if (e.key === "F8") { e.preventDefault(); pick(); return; }
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "z" && !typing) {
      e.preventDefault();
      e.shiftKey ? app.redo() : app.undo();
    }
  }
</script>

<svelte:window onkeydown={keys} />

<div class="room shell">
  <Backdrop />
  <TitleBar onpick={pick} />
  {#if ready && app.ws}
    {@const Page = pages[app.page]}
    <main class="scroll">
      <div class="page">
        <header class="title">
          <h1>{t(TOOLS[app.page].title)}</h1>
          <p class="muted">{t(TOOLS[app.page].about)}</p>
        </header>
        {#key app.page}
          <Page />
        {/key}
      </div>
    </main>
    <Spine />
  {/if}
</div>
<Toasts />
<CaptureOptions />
{#if app.settingsOpen && app.ws}<Settings />{/if}
<!-- Last: a confirmation or prompt opens over the sheet that asked for it (Settings, Capture options). -->
<Dialogs />
<HandCursor />

<style>
  .shell {
    position: relative;
    height: 100%;
    display: flex;
    flex-direction: column;
  }
  main {
    flex: 1;
    min-height: 0;
    overflow-x: hidden;
  }
  .page {
    max-width: 1180px;
    margin: 0 auto;
    /* The spine always sits on the window's right edge, in both languages, as in 1.x. */
    padding: 18px 66px 40px 28px;
    display: flex;
    flex-direction: column;
    gap: 18px;
  }
  .title {
    display: flex;
    align-items: baseline;
    gap: 14px;
    flex-wrap: wrap;
  }
  h1 {
    margin: 0;
    font-size: var(--text-display);
    font-weight: 700;
    line-height: 1.4;
  }
  .title p {
    margin: 0;
    font-size: var(--text-label);
  }
</style>
