<script lang="ts">
  // Search across every project, palette, color name, HEX code and role.
  import { app } from "../lib/app.svelte";
  import { t } from "../lib/i18n";
  import { ROLE_LABELS } from "../lib/types";
  import Sheet from "./Sheet.svelte";

  let { onclose }: { onclose: () => void } = $props();
  let query = $state("");
  let typed = 0;
  // How long a keystroke takes to reach the drawn results, for the local timing log.
  $effect(() => { void results; if (typed) { app.timed("palette-search-to-frame", typed); typed = 0; } });
  const results = $derived.by(() => {
    const q = query.trim().toLowerCase().replace(/^#/, "");
    if (!q) return [];
    const out: { palette: string; label: string; hex?: string; detail: string }[] = [];
    for (const p of app.ws!.palettes) {
      if (p.name.toLowerCase().includes(q) || p.collection.toLowerCase().includes(q))
        out.push({ palette: p.id, label: p.name, detail: p.collection ? t("المشروع: ") + p.collection : "" });
      for (const c of p.colors)
        if (c.name.toLowerCase().includes(q) || c.hex.toLowerCase().includes(q)) out.push({ palette: p.id, label: c.name, hex: c.hex, detail: p.name });
      for (const [role, hex] of Object.entries(p.roles))
        if (role.includes(q) || t(ROLE_LABELS[role]).toLowerCase().includes(q) || hex.toLowerCase().includes(q))
          out.push({ palette: p.id, label: t(ROLE_LABELS[role]), hex, detail: p.name });
    }
    return out;
  });
</script>

<Sheet title={t("البحث في المشاريع")} {onclose} width={560}>
  <input class="field" dir="auto" bind:value={query} oninput={() => (typed = performance.now())} placeholder={t("مشروع أو لوحة أو اسم لون أو HEX أو دور")} />
  {#if query.trim() && results.length === 0}<p class="muted">{t("لا توجد نتائج مطابقة.")}</p>{/if}
  <ul>
    {#each results.slice(0, 30) as r}
      <li>
        <button onclick={() => { app.paletteId = r.palette; onclose(); }}>
          {#if r.hex}<span class="chip" style:background={r.hex}></span>{/if}
          <span class="label">{r.label}</span>
          {#if r.hex}<span class="code muted">{r.hex}</span>{/if}
          <span class="muted detail">{r.detail}</span>
        </button>
      </li>
    {/each}
  </ul>
  {#if results.length > 30}<p class="muted small">{t("تظهر أول 30 نتيجة. ضيّق البحث لعرض نتائج أخرى.")}</p>{/if}
</Sheet>

<style>
  ul {
    list-style: none;
    margin: 0;
    padding: 0;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  li button {
    all: unset;
    display: flex;
    align-items: center;
    gap: 10px;
    width: 100%;
    box-sizing: border-box;
    padding: 8px 10px;
    border-radius: 10px;
    cursor: pointer;
  }
  li button:hover,
  li button:focus-visible {
    background: var(--tint-strong);
  }
  .chip {
    width: 20px;
    height: 20px;
    border-radius: 6px;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .label {
    font-weight: 600;
  }
  .detail {
    margin-inline-start: auto;
    font-size: var(--text-label);
  }
  .small {
    font-size: var(--text-label);
  }
</style>
