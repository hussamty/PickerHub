// Shapes of what Rust sends. They mirror crates/store/src/model.rs and src-tauri/src/commands.rs.
export interface Swatch { id: string; name: string; hex: string; favorite: boolean }
export interface Palette { id: string; name: string; colors: Swatch[]; collection: string; roles: Record<string, string> }
export interface Stop { hex: string; pos: number }
export interface Gradient { id: string; name: string; type: "linear" | "radial"; angle: number; stops: Stop[] }
export interface Discovery { id: string; name: string; createdAt: string; light: Record<string, string>; dark: Record<string, string> }
export interface Workspace {
  version: number;
  nameScheme: number;
  color: string;
  limit: number;
  useWaylandScreenSharing: boolean;
  reduceMotion: boolean;
  diagnosticsEnabled: boolean;
  language: "ar" | "en";
  reduceTransparency: boolean;
  libraryView: "grid" | "list" | "deck";
  followSystemTheme: boolean;
  autoCopy: boolean;
  copyFormat: "HEX" | "RGB" | "HSL";
  hexUppercase: boolean;
  hexPrefix: boolean;
  globalShortcut: boolean;
  quickPick: boolean;
  shortcut: "Ctrl+Shift+P" | "Ctrl+Alt+P" | "Ctrl+Shift+C";
  theme: "dark" | "light";
  discoveries: Discovery[];
  brandLocks: Record<string, string>;
  history: string[];
  palettes: Palette[];
  gradients: Gradient[];
  /** New in 2.0: a live blur behind every pane. */
  fullGlass?: boolean;
  /** New in 2.0: closing the window hides it in the tray instead of quitting. */
  minimizeToTray?: boolean;
}
export interface Snapshot { workspace: Workspace; canUndo: boolean; canRedo: boolean }
export interface Boot extends Snapshot { recoveryNotice: string | null; platform: string; session: string; version: string; preview: boolean }
export interface ColorInfo {
  hex: string;
  name: string;
  english: string;
  base: string;
  family: string;
  shade: string;
  hue: number;
  ink: string;
  tone: string;
  reference: string;
  formats: Record<"HEX" | "RGB" | "RGBA" | "HSL" | "HSV" | "HSB" | "OKLCH" | "CMYK", string>;
  white: number;
  black: number;
}
export interface Frame extends ColorInfo { harmony: { hex: string; ink: string }[]; hsl: [number, number, number]; hsv: [number, number, number] }
export interface Contrast { ratio: number; aa: boolean; large: boolean; aaa: boolean; aaaLarge: boolean; suggestion: string }
export interface NameRow { hex: string; name: string; english: string; ink: string; base: string; family: string; shade: string }
export interface AppError { code: string; message: string; detail?: string }
export type Op =
  | { op: "setColor"; hex: string }
  | { op: "remember"; hex: string }
  | { op: "forget"; hex: string }
  | { op: "clearHistory" }
  | { op: "setting"; key: string; value: unknown }
  | { op: "addPalette"; name: string; colors: string[] }
  | { op: "renamePalette"; id: string; name: string }
  | { op: "deletePalette"; id: string }
  | { op: "setCollection"; id: string; collection: string }
  | { op: "setRoles"; id: string; roles: Record<string, string> }
  | { op: "addColors"; palette: string; hexes: string[] }
  | { op: "renameColor"; palette: string; id: string; name: string }
  | { op: "deleteColors"; palette: string; ids: string[] }
  | { op: "favorite"; palette: string; id: string; value: boolean }
  | { op: "moveColor"; palette: string; id: string; to: number }
  | { op: "moveColorsTo"; from: string; to: string; ids: string[] }
  | { op: "dedupe"; palette: string }
  | { op: "import"; palette: Palette; into: string | null }
  | { op: "saveGradient"; gradient: Gradient }
  | { op: "deleteGradient"; id: string }
  | { op: "archive"; discovery: Discovery }
  | { op: "brandLock"; role: string; hex: string | null };
export const ROLES = ["background", "surface", "primary", "accent", "text", "muted", "border"] as const;
export const ROLE_LABELS: Record<string, string> = {
  background: "الخلفية", surface: "السطح", primary: "الأساسي", accent: "التمييز", text: "النص", muted: "النص الثانوي", border: "الحدود",
};
export interface ImageColor { hex: string; name: string; english: string; family: string; percent: number; x?: number; y?: number; depth?: number }
export interface ImageResult { name: string; width: number; height: number; previewWidth: number; previewHeight: number; preview: string; colors: ImageColor[] }
