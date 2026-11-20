<script lang="ts">
  // The X11 lens window: it only shows what Rust sends; the pointer is held by the native session.
  import { listen } from "@tauri-apps/api/event";
  import { onMount } from "svelte";
  import Lens, { type LensFrame } from "./Lens.svelte";
  let frame = $state<LensFrame | null>(null);
  let zoom = $state(12);
  onMount(() => {
    const a = listen<LensFrame>("lens-frame", (e) => (frame = e.payload));
    const b = listen<number>("lens-zoom", (e) => (zoom = Math.max(6, Math.min(24, zoom + 2 * e.payload))));
    return () => { a.then((f) => f()); b.then((f) => f()); };
  });
</script>

<Lens {frame} {zoom} />
