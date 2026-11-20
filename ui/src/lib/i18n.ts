// Arabic is the source language. The English catalog (desktop/Localization.en.json, carried over
// unchanged) maps each Arabic string to its English text; a missing entry shows the Arabic.
import en from "../en.json";
import extra from "./en-extra.json";
import { app } from "./app.svelte";

const catalog: Record<string, string> = { ...(en as Record<string, string>), ...(extra as Record<string, string>) };

export function t(text: string, ...args: (string | number)[]): string {
  let out = app.language === "en" ? (catalog[text] ?? text) : text;
  args.forEach((a, i) => (out = out.replaceAll(`{${i}}`, String(a))));
  return out;
}
/** The color name in the interface language, from a describe() result. */
export function colorName(info: { name: string; english: string } | null | undefined): string {
  if (!info) return "";
  return app.language === "en" ? info.english : info.name;
}
export function has(text: string): boolean {
  return text in catalog;
}
