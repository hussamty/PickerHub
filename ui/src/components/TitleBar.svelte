<script lang="ts">
  // The bar is the title bar: its empty space moves the window, and the window actions close the row.
  import { getCurrentWindow } from "@tauri-apps/api/window";
  import { app } from "../lib/app.svelte";
  import { t } from "../lib/i18n";
  import Icon from "./Icon.svelte";
  import ThemeSwitch from "./ThemeSwitch.svelte";
  import mascot from "../assets/mascot.png";

  let { onpick }: { onpick: () => void } = $props();
  const win = getCurrentWindow();
  const modeLabel = $derived(app.sharing ? "مشاركة الشاشة نشطة" : app.ws?.useWaylandScreenSharing ? "العدسة الحية جاهزة للاتصال" : "منتقي النظام");
</script>

<header class="bar glass glass-dense" data-tauri-drag-region dir="ltr">
  <div class="brand" data-tauri-drag-region>
    <img src={mascot} alt="" width="26" height="26" />
    <span class="word" data-tauri-drag-region>PickerHub</span>
  </div>
  <button class="pill pill-accent pick" onclick={onpick} title={t("القطّارة") + " · F8"}>
    <Icon name="color-picker" size={17} />
    <span>{t("القطّارة")}</span>
    <kbd class="code">F8</kbd>
  </button>
  {#if app.portal}
    <!-- Wayland only: which eyedropper F8 opens, and the state of screen sharing (desktop/CaptureOptions.cs). -->
    <button class="pill mode" class:sharing={app.sharing} onclick={() => (app.captureMenu = {})} title={t(modeLabel)} aria-label="{t('وضع الالتقاط')} · {t(modeLabel)}">
      <Icon name="settings" size={16} />
      <span>{t(modeLabel)}</span>
    </button>
  {/if}
  <div class="grow" data-tauri-drag-region></div>
  <button class="round" disabled={!app.canUndo} onclick={() => app.undo()} title={t("تراجع · Ctrl+Z")} aria-label={t("تراجع · Ctrl+Z")}>
    <Icon name="arrow-back-up" size={17} />
  </button>
  <button class="round" disabled={!app.canRedo} onclick={() => app.redo()} title={t("إعادة · Ctrl+Shift+Z")} aria-label={t("إعادة · Ctrl+Shift+Z")}>
    <Icon name="arrow-forward-up" size={17} />
  </button>
  <ThemeSwitch />
  <span class="sep"></span>
  <button class="win" onclick={() => win.minimize()} aria-label={t("تصغير")}><Icon name="minus" size={16} /></button>
  <button class="win" onclick={() => win.toggleMaximize()} aria-label={t("تكبير")}><Icon name="square" size={14} /></button>
  <button class="win close" onclick={() => win.close()} aria-label={t("إغلاق")}><Icon name="x" size={16} /></button>
</header>

<style>
  .bar {
    display: flex;
    align-items: center;
    gap: 8px;
    height: 56px;
    margin: 8px 8px 0;
    padding: 0 8px 0 12px;
    border-radius: 20px;
  }
  .brand {
    display: flex;
    align-items: center;
    gap: 8px;
    padding-inline-end: 8px;
  }
  .word {
    font-family: "Plus Jakarta Sans", sans-serif;
    font-weight: 700;
    font-size: 16px;
    letter-spacing: -0.01em;
  }
  .pick {
    min-height: 38px;
    font-family: var(--font-arabic);
  }
  .mode {
    min-height: 38px;
    padding-inline: 12px;
    font-size: var(--text-caption);
    font-weight: 500;
    color: var(--muted);
  }
  .mode.sharing {
    color: var(--ink);
    border-color: var(--color-brand);
  }
  .mode.sharing :global(svg) {
    color: var(--accent-text);
  }
  kbd {
    font-size: 11px;
    padding: 1px 6px;
    border-radius: 6px;
    background: rgb(17 17 17 / 0.14);
  }
  .grow {
    flex: 1;
    align-self: stretch;
  }
  .sep {
    width: 1px;
    height: 22px;
    background: var(--line);
    margin-inline: 4px;
  }
  .win {
    all: unset;
    display: grid;
    place-items: center;
    width: 34px;
    height: 34px;
    border-radius: 10px;
    cursor: pointer;
    color: var(--ink);
  }
  .win:hover {
    background: var(--tint-strong);
  }
  .win.close:hover {
    background: #c42b1c;
    color: white;
  }
</style>
