// Single source of truth for the palette, ported from design/palette.py.
// Warm ivory is reserved for the Moon; cool blues carry sky state only.

import type { Band } from "./types";

export const BANDS: { name: Band; range: string; colour: string }[] = [
  { name: "civil", range: "0° to −6°", colour: "#27476E" },
  { name: "nautical", range: "−6° to −12°", colour: "#18304F" },
  { name: "astronomical", range: "−12° to −18°", colour: "#0D1B31" },
  { name: "night", range: "sun < −18°", colour: "#05070C" },
];

export const CURVE = "#EFE0B7"; // moon ivory
export const PAGE_BG = "#0A0E18";
export const GROUND = "#0D1220";
export const INK = "#8C9BB4";
export const INK_BRIGHT = "#E2E8F0";
export const GRID = "#94A3B8";
export const NOW = "#F87171";

/** Daylight fill (blue scheme): paler and brighter than the lightest band. */
export const DAY_FILL = "#4E7FB5";

// ---- dark (red) scheme ----------------------------------------------------
// Review 23 Nov: the "DARK" toggle swaps every sky-state BLUE for a RED at
// identical lightness — each colour is the blue one with its B channel moved
// into R (B drops, R rises), so band contrast and hierarchy are preserved.
// The moon's warm ivory (CURVE) and the now-red line stay untouched: only
// sky state changes hue, never the Moon.

export const BANDS_RED: { name: Band; range: string; colour: string }[] = [
  { name: "civil", range: "0° to −6°", colour: "#6E2727" },
  { name: "nautical", range: "−6° to −12°", colour: "#4F1818" },
  { name: "astronomical", range: "−12° to −18°", colour: "#310D0D" },
  { name: "night", range: "sun < −18°", colour: "#0C0505" },
];

export const PAGE_BG_RED = "#180A0A";
export const DAY_FILL_RED = "#B54E4E";
// Ink pair for the red scheme: same lightness as #8C9BB4 / #E2E8F0.
export const INK_RED = "#B48C8C";
export const INK_BRIGHT_RED = "#F0E2E2";
// Grid + chip surfaces with the B→R channel swap applied.
export const GRID_RED = "#B89494";
export const CHIP_BG_RED = "#1C0B0B";
// Band-edge hairlines and moon-glyph backdrop (blue scheme #CBD5E1 / #161D2E).
export const EDGE_LINE_RED = "#E1CBCB";
export const GLYPH_BG_RED = "#2E1616";

export function bandsOf(dark: boolean) { return dark ? BANDS_RED : BANDS; }
export function dayFillOf(dark: boolean) { return dark ? DAY_FILL_RED : DAY_FILL; }
export function pageBgOf(dark: boolean) { return dark ? PAGE_BG_RED : PAGE_BG; }
export function inkOf(dark: boolean) { return dark ? INK_RED : INK; }
export function inkBrightOf(dark: boolean) { return dark ? INK_BRIGHT_RED : INK_BRIGHT; }
export function gridOf(dark: boolean) { return dark ? GRID_RED : GRID; }
export function chipBgOf(dark: boolean) { return dark ? CHIP_BG_RED : "#0B101C"; }
export function edgeLineOf(dark: boolean) { return dark ? EDGE_LINE_RED : "#CBD5E1"; }
export function glyphBgOf(dark: boolean) { return dark ? GLYPH_BG_RED : "#161D2E"; }

export function bandOf(alt: number): Band {
  if (alt > 0) return "civil";
  if (alt > -6) return "civil";
  if (alt > -12) return "nautical";
  if (alt > -18) return "astronomical";
  return "night";
}

export function colourOf(band: Band): string {
  const found = BANDS.find((b) => b.name === band);
  return found ? found.colour : "#000000";
}
