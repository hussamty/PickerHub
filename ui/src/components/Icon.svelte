<script lang="ts">
  // Tabler outline icons (desktop/Icons), drawn in the inherited text color.
  const files = import.meta.glob("../icons/*.svg", { query: "?raw", import: "default", eager: true }) as Record<string, string>;
  const icons: Record<string, string> = {};
  // Normalized once: no comments, and every icon drawn as an outline in the current color.
  for (const [path, raw] of Object.entries(files)) {
    const body = raw.replace(/<!--[\s\S]*?-->/g, "").trim().replace(/<svg[^>]*>/, "").replace(/<\/svg>\s*$/, "");
    icons[path.split("/").pop()!.replace(".svg", "")] = body;
  }

  let { name, size = 18, class: cls = "" }: { name: string; size?: number; class?: string } = $props();
  const svg = $derived(
    `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${icons[name] ?? ""}</svg>`,
  );
</script>

<span class="icon {cls}" aria-hidden="true">{@html svg}</span>

<style>
  .icon {
    display: inline-grid;
    place-items: center;
    flex: none;
    line-height: 0;
  }
  .icon :global(svg) {
    stroke: currentColor;
  }
</style>
