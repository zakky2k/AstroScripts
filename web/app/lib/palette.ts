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
