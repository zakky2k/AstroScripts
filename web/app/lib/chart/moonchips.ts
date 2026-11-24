// Moonrise / moonset chips inside the plot (review 24 Nov round 4, item 8).
//
// Placement laws, in the order they are applied — each one exists because the
// reviewer hit the failure mode it prevents:
//   1. a chip hugs its crossing point with a ~30-minute gap from the closest
//      edge (30 min ≈ 19 px at this frame width; the old fixed 100 px ≈ 2.6 h
//      was far too loose for the set chip);
//   2. it sits OUTSIDE the moon curve by default — rise chip to the LEFT of
//      the rise, set chip to the RIGHT of the set, over the flat parts;
//   3. only when that outside spot would cross the curve itself, or spill past
//      the plot, does it flip inside; a squeeze on one side is only "real" if
//      the alternative placement would break the same boundary;
//   4. two chips must never sit on top of each other.
//
// Slots are FIXED by construction, so there is no measure-and-settle loop and
// nothing can jitter between renders.

import type { NightPayload } from "@/lib/types";
import { hhmm } from "./clock";
import { FLOOR_Y, M_LEFT, PLOT_W, xOf, yOf, type Frame } from "./geom";
import { LABEL_CH } from "./labels";

/** Chip height, and the label→width estimate used everywhere in the plot. */
export const CHIP_H = 16;
export const chipWidth = (label: string) => label.length * LABEL_CH + 10;

export interface MoonChip {
  key: string;
  label: string;
  /** x of the crossing point (leader target, and the dot on the curve). */
  x: number;
  /** Chip box. */
  bx: number;
  by: number;
  w: number;
  h: number;
  /** Where the leader leaves the chip: the box edge nearest the crossing. */
  lx: number;
}

export function moonChipsOf(payload: NightPayload, f: Frame, tz: string): MoonChip[] {
  const events: { sec: number; kind: "rise" | "set" }[] = [];
  for (const key of ["moonrise", "moonset"] as const) {
    for (const iso of payload.moon[key]) {
      const s = Date.parse(iso) / 1000;
      if (s >= f.noonW && s <= f.noonEndW) events.push({ sec: s, kind: key === "moonrise" ? "rise" : "set" });
    }
  }
  events.sort((a, b) => a.sec - b.sec);

  const GAP_SEC = 30 * 60;                 // requested gap from the crossing point
  const PX_PER_SEC = PLOT_W / (24 * 3600); // ≈ 0.0105 px/s (37.7 px per hour)
  const CHIP_FLIP = 150;                   // fallback shift when both sides fail
  // Collision margins in px: how close a chip may come to the plot edges /
  // gridline before we consider flipping.
  const SQUEEZE_LEFT = 8;   // clearance from the midnight gridline before flipping
  const SQUEEZE_RIGHT = 46; // extra room past the box before flipping back
  const gridX = xOf(f, f.noonW + 12 * 3600 + 300); // the 00:05 gridline, near plot centre

  // The moon path occupies altitude > 0 between its rise and set crossings; a
  // chip whose box overlaps that time-band AND sits at horizon height is
  // "colliding with the curve".
  const moonBand: [number, number][] = [];
  for (let i = 0; i < events.length; i++) {
    if (events[i].kind !== "rise") continue;
    const after = events.slice(i + 1).find((e) => e.kind === "set");
    moonBand.push([events[i].sec, after ? after.sec : f.noonEndW]);
  }
  const hitsCurve = (bx: number, w: number) =>
    moonBand.some(([a, b]) => bx < xOf(f, b) && bx + w > xOf(f, a));

  // Pre-flip clamping: a chip must never spill past the plot edges. Checked
  // BEFORE deciding to flip, so a set point near the right edge — where
  // "outside" (after the set) barely fits — keeps its outside spot instead of
  // being needlessly dragged inside the curve.
  const LMIN = M_LEFT + 4, LMAX = M_LEFT + PLOT_W - 4;
  const chips = events.map((e, i) => {
    const label = `${e.kind} ${hhmm(e.sec, tz)}`;
    const w = chipWidth(label);
    const x = xOf(f, e.sec);
    let bx: number;
    if (e.kind === "rise") {
      // Prefer OUTSIDE: chip's RIGHT edge sits GAP_SEC before the rise.
      bx = Math.max(x - w - GAP_SEC * PX_PER_SEC, LMIN);
      if ((bx <= gridX + SQUEEZE_LEFT || hitsCurve(bx, w)) &&
          !(x + GAP_SEC * PX_PER_SEC + w <= LMAX - SQUEEZE_RIGHT &&
            !hitsCurve(x + GAP_SEC * PX_PER_SEC, w))) {
        bx = x + GAP_SEC * PX_PER_SEC; // flip inside the curve
      }
    } else {
      // Prefer OUTSIDE: chip's LEFT edge starts GAP_SEC after the set.
      bx = Math.min(x + GAP_SEC * PX_PER_SEC, LMAX - w);
      if ((bx + w > LMAX || hitsCurve(bx, w)) &&
          !(x - GAP_SEC * PX_PER_SEC - w >= gridX + SQUEEZE_LEFT &&
            !hitsCurve(x - GAP_SEC * PX_PER_SEC - w, w))) {
        bx = x - CHIP_FLIP; // deep fallback left of the set point
      }
    }
    // never spill outside the plot — clamp as a last resort
    bx = Math.min(Math.max(bx, LMIN), LMAX - w);
    return { key: `rs${i}`, label, x, bx, by: FLOOR_Y - 10 - CHIP_H, w, h: CHIP_H, lx: 0 };
  });

  // Anti-overlap pass: if two chips ended up overlapping (both squeezed to the
  // same side), push the later event's chip rightwards past the earlier one;
  // if that spills out, push the earlier one left instead.
  for (let i = 1; i < chips.length; i++) {
    const a = chips[i - 1], c = chips[i];
    if (c.bx < a.bx + a.w + 6 && c.bx < a.bx + c.w) {
      const moved = a.bx + a.w + 6;
      if (moved + c.w <= LMAX) c.bx = moved;
      else a.bx = Math.max(LMIN, c.bx - c.w - 6);
    }
  }
  for (const c of chips) {
    c.lx = c.x < c.bx ? c.bx : c.x > c.bx + c.w ? c.bx + c.w : c.x;
  }
  return chips;
}

/** The dot marking where the moon path reaches the horizon. */
export const crossingDotY = () => yOf(0);
