<script lang="ts">
  // Every pair of up to 12 colors, with the WCAG ratio, and approximate color-vision previews.
  import { app } from "../lib/app.svelte";
  import { ipc } from "../lib/ipc";
  import { t } from "../lib/i18n";
  import type { Swatch } from "../lib/types";
  import Sheet from "./Sheet.svelte";
  import Choice from "./Choice.svelte";

  let { colors, onclose }: { colors: Swatch[]; onclose: () => void } = $props();
  let ratios = $state<number[][]>([]);
  let mode = $state(-1);
  let sims = $state<Record<string, string[]>>({});

  $effect(() => {
    Promise.all(colors.map((fg) => Promise.all(colors.map((bg) => ipc.contrast(fg.hex, bg.hex).then((r) => r.ratio)))))
      .then((r) => (ratios = r)).catch((e) => app.fail(e));
    Promise.all(colors.map((c) => ipc.vision(c.hex).then((v) => [c.hex, v] as const)))
      .then((pairs) => (sims = Object.fromEntries(pairs)));
  });
  const shown = (hex: string) => (mode < 0 ? hex : sims[hex]?.[mode] ?? hex);
</script>

<Sheet title={t("مصفوفة التباين")} {onclose} width={900}>
  <p class="muted small">{t("أول 12 لوناً من التحديد أو اللوحة. ✓ يحقق AA للنص العادي.")}</p>
  <Choice options={["عام", "عمى الأحمر (Protanopia)", "عمى الأخضر (Deuteranopia)", "عمى الأزرق (Tritanopia)"]} value={mode + 1} onchange={(i) => (mode = i - 1)} />
  {#if mode >= 0}<p class="muted small">{t("محاكاة رؤية الألوان تقريبية. قياسات التباين للألوان الأصلية.")}</p>{/if}
  <div class="wrap scroll">
    <table dir="ltr">
      <thead>
        <tr><th></th>{#each colors as c}<th><span class="chip" style:background={shown(c.hex)} title={c.name}></span></th>{/each}</tr>
      </thead>
      <tbody>
        {#each colors as fg, i}
          <tr>
            <th><span class="chip" style:background={shown(fg.hex)} title={fg.name}></span></th>
            {#each colors as bg, j}
              <td style:background={shown(bg.hex)} style:color={shown(fg.hex)}>
                {#if ratios[i]}<span class="code">{ratios[i][j].toFixed(1)}{ratios[i][j] >= 4.5 ? " ✓" : ""}</span>{/if}
              </td>
            {/each}
          </tr>
        {/each}
      </tbody>
    </table>
  </div>
</Sheet>

<style>
  .wrap {
    overflow: auto;
    max-height: 60vh;
  }
  table {
    border-collapse: separate;
    border-spacing: 3px;
  }
  td {
    min-width: 58px;
    height: 40px;
    text-align: center;
    border-radius: 8px;
    font-size: var(--text-label);
    font-weight: 700;
  }
  .chip {
    display: inline-block;
    width: 22px;
    height: 22px;
    border-radius: 7px;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .small {
    font-size: var(--text-label);
    margin: 0;
  }
</style>
