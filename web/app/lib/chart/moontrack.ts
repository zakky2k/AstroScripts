// The moon's track through the frame: the arc that gets drawn, where the
// glyph sits, and which moonrise the readout should name.
//
// The glyph law (review 23 Nov round 3): the glyph is PINNED to the arc.
// Whenever the focus sits where the moon is below the horizon it parks at the
// nearest rise/set crossing ON the curve instead of drifting along a straight
// chord — the moon rises and sets along its path and has NO effect while under
// the horizon. The slider thumb still spans the whole day; only the glyph
// parks.

import type { NightPayload } from "@/lib/types";
import { interp, xOf, yOf, type Frame } from "./geom";

/** Sample series, parsed once from the payload. */
export interface MoonSeries {
  times: number[];
  alts: number[];
  illum: number[];
  /** Rise/set crossings inside the frame — the glyph's parking spots. */
  trackEvents: number[];
}

export function moonSeriesOf(payload: NightPayload, f: Frame): MoonSeries {
  const samples = payload.moon.samples;
  return {
    times: samples.map((s) => Date.parse(s.time) / 1000),
    alts: samples.map((s) => s.altitude),
    illum: samples.map((s) => s.illumFraction),
    trackEvents: [
      ...payload.moon.moonrise.map((s) => Date.parse(s) / 1000),
      ...payload.moon.moonset.map((s) => Date.parse(s) / 1000),
    ].filter((s) => s >= f.noonW && s <= f.noonEndW),
  };
}

/** The instant the glyph is drawn at: the focus while the moon is up,
 *  otherwise the nearest rise/set crossing (null if the moon never crosses in
 *  this frame). */
export function glyphSecOf(ms: MoonSeries, focusSec: number, inFrame: boolean): number | null {
  if (!inFrame) return null;
  const alt = interp(ms.times, ms.alts, focusSec);
  if (alt !== null && alt > 0) return focusSec;
  let best: number | null = null, bd = Infinity;
  for (const e of ms.trackEvents) {
    const d = Math.abs(e - focusSec);
    if (d < bd) { bd = d; best = e; }
  }
  return best;
}

/** The arc the glyph travels: drawn across the FULL frame including daylight
 *  hours (samples clipped only at altitude <= 0), so its rise point is always
 *  visible. Empty string when nothing is above the horizon. */
export function arcPointsOf(ms: MoonSeries, f: Frame): string {
  return ms.times
    .map((s, i) => (ms.alts[i] >= 0 && s >= f.noonW && s <= f.noonEndW ? `${xOf(f, s)},${yOf(ms.alts[i])}` : null))
    .filter(Boolean)
    .join(" ");
}

/** The corresponding moonrise for the readout: the next rise at/after the
 *  focus time within this frame, updated as you scrub; falls back to the last
 *  rise before it. */
export function riseForFocusOf(payload: NightPayload, f: Frame, focusSec: number, inFrame: boolean): number | null {
  if (!inFrame) return null;
  const rises = payload.moon.moonrise.map((s) => Date.parse(s) / 1000);
  const future = rises.filter((s) => s >= focusSec && s <= f.noonEndW);
  if (future.length) return future[0];
  const past = rises.filter((s) => s >= f.noonW && s < focusSec);
  return past.length ? past[past.length - 1] : null;
}
