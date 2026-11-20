// The last analysed picture, kept while the user moves between pages.
import type { ImageResult } from "./types";
class Images {
  result = $state<ImageResult | null>(null);
  /** Leaders draw out once per picture; returning to the page shows them at rest. */
  drawn = $state(false);
}
export const images = new Images();
