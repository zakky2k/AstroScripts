// Twilight band layout for the live chart (docs/REQUIREMENTS.md §4.1): the
// vertical gradient bands carry the sun entirely — no extra bars. Boundaries
// come from the payload's official transition times; between two boundaries
// the band is whichever altitude band the sun sits in at the midpoint.
//
// Emission order is preserved (rect, then its left edge line) because that is
// the order the reviewed reference renders in.

import type { NightPayload } from "@/lib/types";
import { bandOf } from "@/lib/palette";
import { ALT_AT, xOf, type Frame } from "./geom";

/** Sun altitude at each transition, nudged to the side the sun is heading so
 *  the boundary sits on the correct side of the threshold. */
export function boundariesOf(payload: NightPayload, f: Frame, altAt: (sec: number) => number) {
  const boundaries: { sec: number; alt: number }[] = [{ sec: f.noonW, alt: altAt(f.noonW) }];
  const dusk = Date.parse(payload.sun.sunset) / 1000;
  if (dusk > f.noonW && dusk < f.noonEndW) boundaries.push({ sec: dusk, alt: -0.01 });
  for (const e of payload.sun.events) {
    const s = Date.parse(e.time) / 1000;
    if (s <= f.noonW || s >= f.noonEndW) continue;
    const thr = ALT_AT[e.band];
    boundaries.push({ sec: s, alt: e.edge === "start" ? thr + 0.01 : thr - 0.01 });
  }
  const dawn = Date.parse(payload.sun.sunrise) / 1000;
  if (dawn > f.noonW && dawn < f.noonEndW) boundaries.push({ sec: dawn, alt: 0.01 });
  boundaries.push({ sec: f.noonEndW, alt: altAt(f.noonEndW) });
  boundaries.sort((p, q) => p.sec - q.sec);
  return boundaries;
}

/** Paintable items between consecutive boundaries: a flat pale fill above the
 *  horizon, a directional twilight gradient below it, plus the hairline that
 *  separates one twilight band from the next. */
export type BandItem =
  | { kind: "day"; key: string; x: number; w: number }
  | { kind: "band"; key: string; x: number; w: number; band: string; grad: "dusk" | "dawn" }
  | { kind: "edge"; key: string; x: number };

export function bandItemsOf(payload: NightPayload, f: Frame, altAt: (sec: number) => number): BandItem[] {
  const boundaries = boundariesOf(payload, f, altAt);
  const items: BandItem[] = [];
  for (let i = 0; i < boundaries.length - 1; i++) {
    const a = boundaries[i], b = boundaries[i + 1];
    if (b.sec <= a.sec) continue;
    const midB = (a.sec + b.sec) / 2;
    const band = bandOf(altAt(midB));
    const grad = midB < f.midSec ? "dusk" : "dawn";
    const x = xOf(f, a.sec);
    const w = Math.max(xOf(f, b.sec) - x, 0.5);
    if (band === "civil" && altAt(midB) > 0) {
      // Daylight: paler/brighter than the lightest twilight band, flat fill.
      items.push({ kind: "day", key: `band${i}`, x, w });
    } else {
      items.push({ kind: "band", key: `band${i}`, x, w, band, grad });
      items.push({ kind: "edge", key: `edge${i}`, x });
    }
  }
  return items;
}
