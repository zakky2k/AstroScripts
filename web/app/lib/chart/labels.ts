// Text layout for the live chart: the 45° transition timestamps below the
// horizon, the fixed-scale time labels at the corners of the plot, the
// moonrise/moonset chips, and the header line. All of it is pure
// string/number work so the labelling laws stay testable:
//   • every transition reads "HH:MM word" at its exact x, angled under the
//     plot and never inside it;
//   • dusk-side labels run up-right into their line, dawn-side mirror;
//   • one colour for all chart text (the bright ink) — sunset/sunrise match
//     the civil/nautical labels (review 23 Nov round 3, 24 Nov round 4);
//   • review 24 Nov round 5: every CLOCK printed on the chart is UTC (the
//     scale itself is labelled UTC at the plot corners), while every DATE keeps
//     naming the observer's local night. One rule to remember: dates name the
//     night, clocks are UTC.

import type { NightPayload } from "@/lib/types";
import { hhmmUtc, dayOfWeek, monthOf, shiftDayStamp } from "./clock";
import { FLOOR_Y, H, M_LEFT, M_TOP, PLOT_W, xOf, type Frame } from "./geom";

export const LABEL_FS = 12; // transition + chip + axis font size (was 9)
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
  // the night's start/end, exactly as reviewed. Round 5: the printed clocks are
  // UTC, because the axis they sit under is a UTC axis.
  const items: { sec: number; label: string; side: "dusk" | "dawn" }[] = [
    {
      sec: Date.parse(payload.sun.sunset) / 1000,
      label: `${hhmmUtc(Date.parse(payload.nightStart) / 1000)} sunset`,
      side: "dusk",
    },
  ];
  for (const e of payload.sun.events) {
    const s = Date.parse(e.time) / 1000;
    if (s <= f.noonW || s >= f.noonEndW) continue;
    items.push({ sec: s, label: `${hhmmUtc(s)} ${e.band}`, side: e.edge === "start" ? "dusk" : "dawn" });
  }
  items.push({
    sec: Date.parse(payload.sun.sunrise) / 1000,
    label: `${hhmmUtc(Date.parse(payload.nightEnd) / 1000)} sunrise`,
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

/** Fixed-scale time labels ON the plot (review 24 Nov round 5: "no static time
 *  labels along the bottom x axis"). Three anchors, each with its UTC clock and
 *  its LOCAL calendar date, since the frame is anchored to local noon / local
 *  midnight while every clock printed here is UTC:
 *    • bottom-left  = frame's noon,   "Mon 23 · 11:00 UTC"
 *    • bottom-right = frame's noon+1, "Tue 24 · 11:00 UTC"
 *    • top-centre   = local midnight, "Mon 24 Nov · midnight" (a word, not a
 *      clock: the centre is defined by the observer's local night, so giving it
 *      a UTC number — 23:00 in Prague, 22:00 in summer — would fight the law
 *      the frame is built on).
 *  The caller renders all three in the same style as the transition text. */
export interface AxisLabel {
  key: string;
  x: number;
  y: number;
  label: string;
  anchor: "start" | "middle" | "end";
}

export function axisLabelsOf(payload: NightPayload, f: Frame): AxisLabel[] {
  const start = payload.frameNoon;
  const end = shiftDayStamp(start, 1);
  const mid = shiftDayStamp(start, 1);
  const bottom = H - 8; // below the transition text, inside the viewBox
  return [
    {
      key: "ax0",
      x: M_LEFT,
      y: bottom,
      label: `${dayOfWeek(start)} ${start.slice(8, 10)} · ${hhmmUtc(f.noonW)} UTC`,
      anchor: "start",
    },
    {
      key: "ax1",
      x: M_LEFT + PLOT_W,
      y: bottom,
      label: `${dayOfWeek(end)} ${end.slice(8, 10)} · ${hhmmUtc(f.noonEndW)} UTC`,
      anchor: "end",
    },
    {
      key: "ax2",
      x: M_LEFT + PLOT_W / 2,
      y: M_TOP - 10,
      label: `${dayOfWeek(mid)} ${mid.slice(8, 10)} ${monthOf(mid)} · midnight`,
      anchor: "middle",
    },
  ];
}

/** Header line: the local night, with its sun times printed on the chart's UTC
 *  scale so a header number and the axis under it never disagree. The weekday +
 *  date stay the observer's local calendar day (round 5 also dropped the old
 *  UTC-weekday quirk that made Sydney print "Mon 24 Nov" beside a Tuesday). */
export function headerTitle(payload: NightPayload): string {
  const sunsetSec = Date.parse(payload.sun.sunset) / 1000;
  const riseSec = Date.parse(payload.sun.sunrise) / 1000;
  return (
    `${payload.location.lat.toFixed(2)}, ${payload.location.lon.toFixed(2)}  ·  ` +
    `${dayOfWeek(payload.nightStart)} ${payload.nightStart.slice(8, 10)} ${monthOf(payload.nightStart)}  ` +
    `${hhmmUtc(sunsetSec)} UTC → ` +
    `${dayOfWeek(payload.nightEnd)} ${payload.nightEnd.slice(8, 10)} ${monthOf(payload.nightEnd)}  ` +
    `${hhmmUtc(riseSec)} UTC`
  );
}
