// Text layout for the live chart: the 45° transition timestamps below the
// horizon, the moonrise/moonset chips, and the header line. All of it is pure
// string/number work so the labelling laws stay testable:
//   • every transition reads "HH:MM word" at its exact x, angled under the
//     plot and never inside it;
//   • dusk-side labels run up-right into their line, dawn-side mirror;
//   • one colour for all chart text (the bright ink) — sunset/sunrise match
//     the civil/nautical labels (review 23 Nov round 3, 24 Nov round 4).

import type { NightPayload } from "@/lib/types";
import { hhmm, dayOfWeek, monthOf, shiftDayStamp, utcDayOfInstant } from "./clock";
import { FLOOR_Y, xOf, type Frame } from "./geom";

export const LABEL_FS = 12; // transition + chip font size (was 9)
export const LABEL_CH = 7.4; // average glyph advance at LABEL_FS (Inter/Segoe UI)
export const TIP_FS = 11;    // tooltip font size
/** Tooltip box: average glyph advance at TIP_FS plus padding. */
export const tipWidth = (label: string) => label.length * 7 + 14;

/** Official transition times inside this night, with the word the reviewer
 *  asked for. Shortened band words keep the tight twilight sequence
 *  collision-free at the 45° angle. */
export interface TransitionLabel {
  key: string;
  /** x of the transition itself (the leader's far end). */
  x: number;
  /** x of the text anchor: middle of the LAST letter (dusk) or FIRST letter
   *  (dawn), which is where the leader meets the text. */
  ax: number;
  anchorY: number;
  label: string;
  rot: number;
  textAnchor: "end" | "start";
}

const BAND_WORD: Record<string, string> = { civil: "civil", nautical: "naut.", astronomical: "astro." };

export function transitionLabelsOf(payload: NightPayload, f: Frame, tz: string): TransitionLabel[] {
  // The leader tracks the transition's own timestamp; the printed clock reads
  // the night's start/end, exactly as reviewed.
  const items: { sec: number; label: string; side: "dusk" | "dawn" }[] = [
    {
      sec: Date.parse(payload.sun.sunset) / 1000,
      label: `${hhmm(Date.parse(payload.nightStart) / 1000, tz)} sunset`,
      side: "dusk",
    },
  ];
  for (const e of payload.sun.events) {
    const s = Date.parse(e.time) / 1000;
    if (s <= f.noonW || s >= f.noonEndW) continue;
    items.push({ sec: s, label: `${hhmm(s, tz)} ${e.band}`, side: e.edge === "start" ? "dusk" : "dawn" });
  }
  items.push({
    sec: Date.parse(payload.sun.sunrise) / 1000,
    label: `${hhmm(Date.parse(payload.nightEnd) / 1000, tz)} sunrise`,
    side: "dawn",
  });

  const anchorY = FLOOR_Y + 10; // last letter just below the horizon strip
  return items
    .filter((t) => t.sec > f.noonW && t.sec < f.noonEndW)
    .map((t, i) => {
      let label = t.label;
      for (const [full, short] of Object.entries(BAND_WORD)) label = label.replace(full, short);
      const x = xOf(f, t.sec);
      // Dusk: text runs up-right and ENDS at its line — first letter low-left,
      // last letter's middle at (x - CH/2, anchorY).
      // Dawn: mirrored — text starts at its line running down-right, so the
      // FIRST letter hugs the transition, its middle at (x + CH/2, anchorY).
      return t.side === "dusk"
        ? { key: `tr${i}`, x, ax: x - LABEL_CH / 2, anchorY, label, rot: -45, textAnchor: "end" as const }
        : { key: `tr${i}`, x, ax: x + LABEL_CH / 2, anchorY, label, rot: 45, textAnchor: "start" as const };
    });
}

/** Header centre label: the night's local midnight, "Mon 24 Nov 00:00" — the
 *  calendar day after the frame's pivot noon. */
export function midnightLabel(payload: NightPayload): string {
  const mid = shiftDayStamp(payload.frameNoon, 1);
  return `${dayOfWeek(mid)} ${mid.slice(8, 10)} ${monthOf(mid)} 00:00`;
}

/** Header line: "50.09, 14.42 · Sun 23 Nov 16:02 → Mon 24 Nov 07:35". The
 *  first weekday is the frame's pivot calendar day; the second is the sunrise
 *  INSTANT's UTC weekday (as shipped — see utcDayOfInstant), printed next to
 *  the sunrise calendar date. */
export function headerTitle(payload: NightPayload): string {
  return (
    `${payload.location.lat.toFixed(2)}, ${payload.location.lon.toFixed(2)}  ·  ` +
    `${dayOfWeek(payload.frameNoon)} ${payload.nightStart.slice(8, 10)} ${monthOf(payload.nightStart)}  ` +
    `${payload.sun.sunset.slice(11, 16)} → ` +
    `${utcDayOfInstant(payload.nightEnd)} ${payload.nightEnd.slice(8, 10)} ${monthOf(payload.nightEnd)}  ` +
    `${payload.sun.sunrise.slice(11, 16)}`
  );
}
