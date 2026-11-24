// The scrubbing laws of the live chart (docs/REQUIREMENTS.md §4.1, review
// 23 Nov round 3 / 24 Nov round 4). Kept as pure functions so the laws are
// unit-testable in isolation — they are the behaviour the reviewer keeps
// probing ("−1 h does nothing", "the day flipped"), and the only way to
// change the displayed night is the header day stepper, never time scrubbing.

import type { Frame } from "./geom";
import { DAY_SEC } from "./geom";
import { focusAt, wallParts, type Focus } from "./clock";

/** Hard clamp into the fixed noon → noon+1 frame. Scrubbing NEVER changes the
 *  night and NEVER parks at moonset: the thumb spans the whole day. */
export const clampFrame = (sec: number, f: Frame): number =>
  Math.min(Math.max(sec, f.noonW), f.noonEndW);

/** Snap to the MINUTE grid: the clock readout has no seconds, so a scrub
 *  position must always match what the clock shows (review 24 Nov round 4). */
export const snapMinute = (sec: number): number => Math.floor(sec / 60) * 60;

/** Slider drag / click on the track. */
export const scrubFocus = (rawSec: number, f: Frame, tz: string): Focus =>
  focusAt(snapMinute(clampFrame(rawSec, f)), tz);

/** The −1 h / +1 h buttons: move time within the FIXED frame only. At a frame
 *  edge the step simply stops (clamped), it does not wrap and it does not
 *  change the night. Past moonset/rise the moon stays below the horizon and
 *  nothing else happens. */
export function stepHoursFocus(baseSec: number, delta: number, f: Frame, tz: string): Focus {
  let sec = Math.round(baseSec + delta * 3600);
  if (sec < f.noonW) sec = f.noonW; // hard clamp at 12:00 — no day flip from hours
  if (sec > f.noonEndW) sec = f.noonEndW; // ... or at 12:00+1
  return focusAt(sec, tz);
}

/** A time typed into the header TIME box: wall-clock time on whichever
 *  calendar day the focus currently sits on (`daySec`), folded into this
 *  frame if it belongs to the other half of it — a typed time never flips
 *  nights either. */
export function typedFocus(c: { h: number; m: number; s: number }, dayFocus: Focus, f: Frame, tz: string): Focus {
  let sec = Date.UTC(dayFocus.y, dayFocus.mo - 1, dayFocus.d, c.h, c.m, c.s) / 1000;
  if (sec < f.noonW - DAY_SEC / 2) sec += DAY_SEC;
  if (sec > f.noonEndW + DAY_SEC / 2) sec -= DAY_SEC;
  return scrubFocus(sec, f, tz);
}

/** Live focus candidate: observer wall clock at the sampled tick, as epoch
 *  seconds (NaN before the first minute-tick lands). The ZONE box's whole-hour
 *  offset is applied to the SCRUBBED focus only (withZoneOffset in clock.ts);
 *  the live clock itself stays on the true zone, as reviewed. */
export function liveSec(tickMs: number | null, tz: string): number {
  if (tickMs === null) return NaN;
  const p = wallParts(tickMs, tz);
  return Date.UTC(p.y, p.mo - 1, p.d, p.h, p.m) / 1000;
}

/** The instant the moon glyph + marker follow: the scrubbed focus if any,
 *  otherwise the live clock clamped into the displayed night so the glyph is
 *  always shown. NaN until the first tick has landed and nothing is scrubbed. */
export function focusEpoch(scrubbedSec: number | null, live: number, f: Frame): number {
  if (scrubbedSec !== null) return scrubbedSec;
  if (!Number.isNaN(live)) return clampFrame(live, f);
  return NaN;
}

export const inFrameOf = (sec: number, f: Frame) =>
  !Number.isNaN(sec) && sec >= f.noonW && sec <= f.noonEndW;
