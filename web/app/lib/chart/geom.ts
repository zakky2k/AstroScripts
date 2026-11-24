// Chart geometry for the live night chart (docs/REQUIREMENTS.md §4.1).
// Pure numbers + scales, no React: the frame laws and the pixel mapping are
// unit-tested here (web/app/scripts/units.ts) and the rendered result is
// pinned by the golden gate (web/app/scripts/golden.ts).

import type { NightPayload } from "@/lib/types";

// Bottom margin extended (was 48): the 45° labels below the chart were
// truncating at the old canvas edge (review 23 Nov). Font size stays 12.
// H is taller than wide for the same reason: the 45° transition labels below
// the horizon need ~150 px of margin so their diagonal text is never cut off.
export const W = 980;
export const H = 600;
export const M_LEFT = 56, M_RIGHT = 24, M_TOP = 40, M_BOTTOM = 150;
export const PLOT_W = W - M_LEFT - M_RIGHT;
export const PLOT_H = H - M_TOP - M_BOTTOM;
export const Y_TOP = 90.0;
export const DAY_SEC = 86_400;
/** The horizon line (altitude 0) in svg pixels. */
export const FLOOR_Y = M_TOP + PLOT_H;

/** Sun altitude at the moment each twilight band starts (crossing threshold). */
export const ALT_AT: Record<string, number> = { civil: -6, nautical: -12, astronomical: -18 };

/** Linear interpolation into a sample series (values at ascending times).
 *  Returns null outside the sampled range — callers decide the fallback. */
export function interp(times: number[], values: number[], t: number): number | null {
  if (!times.length || t < times[0] || t > times[times.length - 1]) return null;
  for (let i = 1; i < times.length; i++) {
    if (times[i - 1] <= t && t <= times[i]) {
      const f = (t - times[i - 1]) / (times[i] - times[i - 1]);
      return values[i - 1] + f * (values[i] - values[i - 1]);
    }
  }
  return values[values.length - 1];
}

/** The fixed single-night frame, in float epoch seconds: left edge local NOON,
 *  centre midnight, right edge NOON+1. The right edge is derived from the ISO
 *  literal rather than by adding 86400 s, so DST landings stay exact. */
export interface Frame {
  noonW: number;
  noonEndW: number;
  /** Plot centre = local midnight of the displayed night. */
  midSec: number;
}

export function frameOf(payload: NightPayload): Frame {
  const noonW = Date.parse(payload.frameNoon) / 1000;
  const noonEndW = shiftIsoDay(payload.frameNoon, 1);
  return { noonW, noonEndW, midSec: (noonW + noonEndW) / 2 };
}

/** Add `days` to the calendar day of an offset-aware ISO string, keeping its
 *  time-of-day and zone designator; parse back to epoch seconds. DST-safe
 *  because the designator is re-parsed with whatever offset applies. */
export function shiftIsoDay(iso: string, days: number): number {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso)!;
  const d = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3] + days));
  const day = `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, "0")}-${String(d.getUTCDate()).padStart(2, "0")}`;
  return Date.parse(`${day}${iso.slice(10)}`) / 1000;
}

/** Seconds → svg x, and the inverse. */
export function xOf(frame: Frame, sec: number): number {
  return M_LEFT + (PLOT_W * (sec - frame.noonW)) / (frame.noonEndW - frame.noonW);
}
export function secOfX(frame: Frame, px: number): number {
  return frame.noonW + ((px - M_LEFT) * (frame.noonEndW - frame.noonW)) / PLOT_W;
}
/** Altitude → svg y (altitudes below 0 share the floor), and the inverse. */
export function yOf(alt: number): number {
  return M_TOP + PLOT_H * (1.0 - Math.max(alt, 0.0) / Y_TOP);
}
export function altOfY(px: number): number {
  return (1 - (px - M_TOP) / PLOT_H) * Y_TOP;
}

/** Altitudes the horizontal gridlines sit at: every 10° up to the top of the
 *  plot, with every third one drawn major. */
export function gridAlts(): number[] {
  const out: number[] = [];
  for (let alt = 0; alt <= Y_TOP; alt += 10) out.push(alt);
  return out;
}
export const isMajorGrid = (alt: number) => alt % 30 === 0;
