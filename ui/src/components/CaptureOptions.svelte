<script lang="ts">
  // Capture mode on Wayland (desktop/CaptureOptions.cs). The system picker gives one color with no
  // lens; the live lens needs screen sharing, which the compositor asks consent for once and which
  // stays open until it is ended here, in Settings or from the tray. Opened from the button beside
  // the eyedropper, or when a multiple pick was refused because only the lens can make one.
  import { app } from "../lib/app.svelte";
  import { ipc } from "../lib/ipc";
  import { t } from "../lib/i18n";
  import { pickMany } from "../lib/pick";
  import Sheet from "./Sheet.svelte";

  let busy = $state(false);
  const label = $derived(app.sharing ? "مشاركة الشاشة نشطة" : app.ws?.useWaylandScreenSharing ? "العدسة الحية جاهزة للاتصال" : "منتقي النظام");

  async function system() {
    app.captureMenu = null;
    await app.setting("useWaylandScreenSharing", false);
    if (app.sharing) await ipc.stopSharing();
  }
  async function live() {
    const collect = app.captureMenu?.collect;
    app.captureMenu = null;
    await app.setting("useWaylandScreenSharing", true);
    if (collect) { await pickMany(collect); return; }
    busy = true;
    try {
      if (!(await ipc.startSharing())) app.toast("لم تبدأ المشاركة. يمكنك اختيار منتقي النظام من وضع الالتقاط.");
    } catch (e) {
      app.fail(e);
    } finally {
      busy = false;
    }
  }
  async function stop() {
    app.captureMenu = null;
    await ipc.stopSharing();
  }
</script>

{#if app.captureMenu}
  {@const collect = !!app.captureMenu.collect}
  <Sheet title={t("وضع الالتقاط")} onclose={() => (app.captureMenu = null)} width={440}>
    <p class="state">{t(label)}</p>
    <p class="muted">{t(collect ? "يتطلب الالتقاط المتعدد العدسة الحية ومشاركة الشاشة." : "يستقبل منتقي النظام لوناً واحداً. تبقى مشاركة الشاشة للعدسة الحية نشطة حتى توقفها.")}</p>
    <div class="options">
      <button class="pill" class:pill-accent={!collect} onclick={system}>{t("استخدام منتقي النظام")}</button>
      <button class="pill" class:pill-accent={collect} disabled={busy || (!collect && app.sharing)} onclick={live}>{t(collect ? "استخدام العدسة الحية" : "بدء مشاركة الشاشة")}</button>
      {#if app.sharing}
        <button class="pill" onclick={stop}>{t("إنهاء مشاركة الشاشة")}</button>
      {/if}
      <button class="pill" onclick={() => (app.captureMenu = null)}>{t("إغلاق")}</button>
    </div>
  </Sheet>
{/if}

<style>
  p {
    margin: 0;
  }
  .state {
    font-weight: 600;
  }
  .options {
    display: flex;
    flex-direction: column;
    gap: 8px;
    margin-top: 6px;
  }
</style>
