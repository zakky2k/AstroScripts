// Unit gate for the pure chart-law modules under lib/chart. This is where the
// §4.1 chart laws become executable: the frame is fixed at NOON → midnight →
// NOON+1, scrubbing NEVER changes the night, the scrub axis is a REAL epoch
// (round 5), every clock has minute resolution, chart clocks print UTC, moon
// chips keep their 30-minute gap and their outside-the-curve side, and the moon
// glyph is hidden while the moon is below the horizon.
//
// The two render gates cover appearance (parity: NightChart vs the design SVGs;
// golden: LiveNightChart vs its committed token stream). Neither can cover
// wall-clock-dependent behaviour — the live focus, the ±1 h steps and the chip
// placement all depend on the observer zone — so those laws are asserted here.
//
// Run (from web/app):
//   npx.cmd tsc -p tsconfig.gates.json
//   node.exe scripts\fix-aliases.mjs .gates-build
//   node.exe .gates-build\scripts\units.js
// Style follows planner/tests/test_night.py: hand-rolled check(), no runner.

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import {
  ALT_AT, DAY_SEC, FLOOR_Y, H, M_LEFT, M_TOP, PLOT_H, PLOT_W, W, Y_TOP,
  altOfY, frameOf, gridAlts, interp, isMajorGrid, secOfX, shiftIsoDay, xOf, yOf,
  type Frame,
} from "../lib/chart/geom.js";
import {
  epochAtWall, fmtClock, focusAt, focusSec, hhmm, hhmmUtc, parseClock, shiftClock,
} from "../lib/chart/clock.js";
import {
  clampFrame, focusEpoch, inFrameOf, liveSec, scrubFocus, snapMinute, stepHoursFocus, typedFocus,
} from "../lib/chart/scrub.js";
import { bandItemsOf, boundariesOf } from "../lib/chart/sunbands.js";
import { CHIP_ALT, moonChipsOf } from "../lib/chart/moonchips.js";
import { arcPointsOf, glyphSecOf, moonSeriesOf, riseForFocusOf } from "../lib/chart/moontrack.js";
import type { NightPayload } from "../lib/types.js";

const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "..");
const load = (name: string): NightPayload =>
  JSON.parse(readFileSync(join(root, "design", "output", `night-${name}.json`), "utf8")) as NightPayload;
const payloads = ["prague", "summer", "sydney"].map(load);

let pass = 0;
const fails: string[] = [];
const check = (name: string, ok: boolean) => { if (ok) pass++; else fails.push(name); };
const near = (a: number, b: number, eps = 1e-6) => Math.abs(a - b) <= eps;
const wall = (sec: number, tz: string) => { const f = focusAt(sec, tz); return `${f.y}/${f.mo}/${f.d} ${f.h}:${f.mi}`; };
const sunTimes = (p: NightPayload) => p.sun.series.map((s) => Date.parse(s.time) / 1000);
const sunAlts = (p: NightPayload) => p.sun.series.map((s) => s.altitude);
const altAtOf = (p: NightPayload) => (sec: number) => interp(sunTimes(p), sunAlts(p), sec) ?? -90;
const crossings = (p: NightPayload, f: Frame) => [
  ...p.moon.moonrise.map((s) => ({ sec: Date.parse(s) / 1000, kind: "rise" })),
  ...p.moon.moonset.map((s) => ({ sec: Date.parse(s) / 1000, kind: "set" })),
].filter((e) => e.sec >= f.noonW && e.sec <= f.noonEndW).sort((a, b) => a.sec - b.sec);

// ---- geometry constants (§4.1): 980x600, plot 900x410 at (56,40) ---------
check("plot box is 900x410 at (56,40)", PLOT_W === 900 && PLOT_H === 410 && M_LEFT === 56 && M_TOP === 40 && W === 980 && H === 600);
check("one hour spans exactly 37.5 px", near((PLOT_W * 3600) / DAY_SEC, 37.5));
check("the horizon sits at the bottom of the plot (FLOOR_Y = M_TOP + PLOT_H = 450)", FLOOR_Y === M_TOP + PLOT_H && FLOOR_Y === 450 && near(yOf(0), FLOOR_Y));
check("the scale tops out at M_TOP", near(yOf(Y_TOP), M_TOP));
check("everything below the horizon shares the floor", near(yOf(-20), FLOOR_Y) && near(yOf(-90), FLOOR_Y));
check("altOfY inverts yOf at and above the horizon", near(altOfY(yOf(45)), 45) && near(altOfY(FLOOR_Y), 0));
check("gridlines every 10 degrees, 0 to 90", gridAlts().join() === "0,10,20,30,40,50,60,70,80,90");
check("every third gridline is major", [0, 30, 60, 90].every(isMajorGrid) && ![10, 20, 40].some(isMajorGrid));
check("interp is null outside the samples", interp([0, 10], [1, 3], -1) === null && interp([0, 10], [1, 3], 11) === null);
check("interp is exact at a node, linear between", interp([0, 10], [1, 3], 10) === 3 && interp([0, 10], [1, 3], 5) === 2);
check("twilight thresholds are -6 / -12 / -18", ALT_AT.civil === -6 && ALT_AT.nautical === -12 && ALT_AT.astronomical === -18);

// ---- the frame: NOON → midnight → NOON+1, fixed for the night -------------
for (const p of payloads) {
  const tz = p.location.tz, f = frameOf(p);
  const a = focusAt(f.noonW, tz), c = focusAt(f.midSec, tz), b = focusAt(f.noonEndW, tz);
  check(`${tz}: left edge is local noon`, a.h === 12 && a.mi === 0);
  check(`${tz}: centre is local midnight`, c.h === 0 && c.mi === 0);
  check(`${tz}: right edge is local noon the next day`, b.h === 12 && b.mi === 0 && `${b.y}/${b.mo}/${b.d}` > `${a.y}/${a.mo}/${a.d}`);
  check(`${tz}: centre is half a day from each edge`, near(f.midSec - f.noonW, DAY_SEC / 2) && near(f.noonEndW - f.midSec, DAY_SEC / 2));
  check(`${tz}: the edges map to the plot box`, near(xOf(f, f.noonW), M_LEFT) && near(xOf(f, f.noonEndW), M_LEFT + PLOT_W));
  check(`${tz}: secOfX inverts xOf`, near(secOfX(f, xOf(f, f.midSec + 4321)), f.midSec + 4321, 1e-9));
  check(`${tz}: the centre is the plot centre`, near(xOf(f, f.midSec), M_LEFT + PLOT_W / 2));
  check(`${tz}: frameNoon + 1 day is the right edge`, near(shiftIsoDay(p.frameNoon, 1), f.noonEndW));
  check(`${tz}: shiftIsoDay keeps the clock time (no DST shortening)`, focusAt(shiftIsoDay(p.frameNoon, 1), tz).h === 12);
}

// ---- clocks: minute resolution everywhere (review 24 Nov, round 4) -------
for (const p of payloads) {
  const tz = p.location.tz;
  const sec = Date.parse(p.sun.sunset) / 1000;
  check(`${tz}: 24 h clock is HH:MM, no seconds`, /^\d{2}:\d{2}$/.test(fmtClock(sec, tz, false)) && fmtClock(sec, tz, false) === hhmm(sec, tz));
  check(`${tz}: 12 h clock is HH:MM AM/PM, no seconds`, /^\d{2}:\d{2} (AM|PM)$/.test(fmtClock(sec, tz, true)));
  check(`${tz}: 12 h clock stays on the 12-hour face`, /^(0[1-9]|1[0-2]):/.test(fmtClock(sec, tz, true)));
}
check("a real instant round-trips focusAt/focusSec exactly", (() => {
  const s = 1795431600;
  return ["UTC", "Europe/Prague", "Australia/Sydney"].every((tz) => focusSec(focusAt(s, tz), tz) === s);
})());
check("epochAtWall names the instant a wall clock reads (zone + DST)", (() => {
  // 13:00 in Prague on a CET day is 12:00 UTC; the same wall time in Sydney on an
  // AEDT day is 02:00 UTC — eleven hours, the offset the old axis got wrong.
  return epochAtWall(2026, 11, 24, 13, 0, 0, "Europe/Prague") === Date.UTC(2026, 10, 24, 12, 0) / 1000
    && epochAtWall(2026, 11, 24, 13, 0, 0, "Australia/Sydney") === Date.UTC(2026, 10, 24, 2, 0) / 1000;
})());
check("parseClock accepts HH:MM", JSON.stringify(parseClock("16:02")) === JSON.stringify({ h: 16, m: 2, s: 0 }));
check("parseClock accepts h:mm PM", JSON.stringify(parseClock("4:02 PM")) === JSON.stringify({ h: 16, m: 2, s: 0 }));
check("parseClock reads 12:00 AM as midnight and 12:30 PM as noon", parseClock("12:00 AM")?.h === 0 && parseClock("12:30 PM")?.h === 12);
check("parseClock rejects nonsense", [parseClock("25:00"), parseClock("7:5"), parseClock("0:00 PM"), parseClock("")].every((v) => v === null));
check("the ZONE ±1H nudge is a whole-hour shift of the LOCAL clock only", (() => {
  // Round 5: the nudge corrects a local clock whose daylight-saving state
  // disagrees with the IANA zone. It is applied at one place and one place only
  // — the Local Time box, as fmtClock(shiftClock(focusSecV, tzOffsetH), tz, h12).
  // Every chart number (axis, marker chip, tooltip, readout) is built from the
  // unshifted focus instant, so the nudge cannot move the marker or an ephemeris.
  const mid = frameOf(load("prague")).midSec;   // local midnight, 23:00 UTC
  return hhmm(mid, "Europe/Prague") === "00:00"
    && hhmm(shiftClock(mid, 1), "Europe/Prague") === "01:00"
    && hhmm(shiftClock(mid, -1), "Europe/Prague") === "23:00"
    && hhmmUtc(mid) === "23:00"                                   // the scale the chart prints
    && near(shiftClock(mid, 1) - mid, 3600) && near(shiftClock(mid, -1) - mid, -3600);
})());

// ---- scrubbing never changes the night, and the axis is the instant -------
// Round 5 rewrote this block: every value the scrubber carries is a real epoch,
// so the thumb, the marker, the readout and the payload samples all live on one
// number line. The checks below are the reviewer's own probes from round 4 —
// "hard left parks at 14:00", "−1 h steps forward", "typed 22:00 shows 01:00".
for (const p of payloads) {
  const tz = p.location.tz, f = frameOf(p);
  const firstDay = wall(f.noonW, tz).split(" ")[0];
  const lastDay = wall(f.noonEndW, tz).split(" ")[0];
  check(`${tz}: clamp holds both edges`, near(clampFrame(f.noonW - 99999, f), f.noonW) && near(clampFrame(f.noonEndW + 99999, f), f.noonEndW));
  const lo = scrubFocus(f.noonW - 7200, f);
  const hi = scrubFocus(f.noonEndW + 7200, f);
  check(`${tz}: scrubbing past the left edge parks at 12:00 of the same night`, near(lo, f.noonW) && wall(lo, tz) === `${firstDay} 12:0`);
  check(`${tz}: scrubbing past the right edge parks at 12:00 of the same night`, near(hi, f.noonEndW) && wall(hi, tz) === `${lastDay} 12:0`);
  check(`${tz}: the thumb reaches the very left edge and stays inside`, inFrameOf(scrubFocus(f.noonW, f), f) && inFrameOf(scrubFocus(f.noonEndW, f), f));
  check(`${tz}: snapMinute lands on the minute grid`, near(snapMinute(f.noonW + 40007.9) % 60, 0) && snapMinute(f.noonW) === f.noonW);
  const mid = scrubFocus(f.noonW + 40007, f);
  check(`${tz}: a scrub has no seconds and matches its clock`, mid % 60 === 0 && fmtClock(mid, tz, false) === hhmm(mid, tz));
  // −1 h / +1 h at the edges: they stop, they never flip the night.
  const back = stepHoursFocus(f.noonW, -1, f);
  const fwd = stepHoursFocus(f.noonEndW, +1, f);
  check(`${tz}: −1 h at the left edge stays parked on the night's first noon`, near(back, f.noonW) && wall(back, tz) === `${firstDay} 12:0`);
  check(`${tz}: +1 h at the right edge stays parked on the night's last noon`, near(fwd, f.noonEndW) && wall(fwd, tz) === `${lastDay} 12:0`);
  const from = f.noonW + 40000;
  const one = stepHoursFocus(from, -1, f), two = stepHoursFocus(from, +1, f);
  check(`${tz}: a mid-frame step moves the clock exactly one hour, minutes intact`, hhmm(one, tz) === hhmm(from - 3600, tz) && hhmm(two, tz) === hhmm(from + 3600, tz));
  check(`${tz}: that same step moves the marker exactly 37.5 px, never the other way`, near(xOf(f, one), xOf(f, from) - 37.5) && near(xOf(f, two), xOf(f, from) + 37.5));
  check(`${tz}: a step that would leave the frame still stops at the edge`, near(stepHoursFocus(f.noonW - 60, -1, f), f.noonW) && near(stepHoursFocus(f.noonEndW + 60, +1, f), f.noonEndW));
  // A typed time: it lands where its own clock says it does, and it never
  // leaves the displayed night.
  const typed = typedFocus({ h: 3, m: 30, s: 0 }, f.midSec + 3600, f, tz);
  check(`${tz}: a typed 03:30 reads 03:30 on the observer's clock`, hhmm(typed, tz) === "03:30" && typed % 60 === 0);
  check(`${tz}: a typed 03:30 sits exactly 3.5 h right of local midnight`, near(xOf(f, typed) - xOf(f, f.midSec), 3.5 * 37.5, 1e-6));
  check(`${tz}: a typed time never leaves the displayed night`, inFrameOf(typed, f));
  check(`${tz}: the frame edges count as inside, NaN does not`, inFrameOf(f.noonW, f) && inFrameOf(f.noonEndW, f) && !inFrameOf(f.noonW - 1, f) && !inFrameOf(NaN, f));
}

// ---- the focus axis is a real epoch (round-5 fix) -------------------------
// This block replaces the check that used to PIN the defect ("KNOWN (awaiting
// review): the focus axis is wall-fields-as-UTC"). The defect made Prague sit
// 37.5 px right of its own scale and Sydney 412 px (11 h) right of it.
for (const p of payloads) {
  const tz = p.location.tz, f = frameOf(p);
  const noon = focusAt(f.noonW, tz);
  check(`${tz}: the frame's own noon round-trips through the focus helpers exactly`, focusSec(noon, tz) === f.noonW);
  const onePm = epochAtWall(noon.y, noon.mo, noon.d, 13, 0, 0, tz);
  check(`${tz}: 13:00 on the observer's clock lands exactly one hour right of noon`, near(xOf(f, onePm) - xOf(f, f.noonW), 37.5) && hhmm(onePm, tz) === "13:00");
  check(`${tz}: one hour on the axis is exactly 37.5 px, whatever the zone`, near(xOf(f, f.noonW + 3600) - xOf(f, f.noonW), 37.5));
}
check("the live tick is the real instant of a wall minute, not the naive epoch of its fields", (() => {
  const utc = Date.UTC(2026, 10, 24, 23, 0, 37);   // = 10:00:37 AEDT on 25 Nov in Sydney
  const s = liveSec(utc, "Australia/Sydney");
  return s === Date.UTC(2026, 10, 24, 23, 0) / 1000 && wall(s, "Australia/Sydney") === "2026/11/25 10:0";
})());

// ---- live focus: nothing is shown before the first tick lands ------------
check("the live clock is NaN before the first tick", Number.isNaN(liveSec(null, "Europe/Prague")));
const tick = liveSec(Date.UTC(2026, 10, 24, 12, 2, 57), "UTC");
check("the live clock snaps to the minute", tick % 60 === 0 && hhmm(tick, "UTC") === "12:02");
const pr = payloads[0], prf = frameOf(pr);
check("no focus while live is NaN and nothing is scrubbed", Number.isNaN(focusEpoch(null, liveSec(null, "UTC"), prf)));
check("a scrubbed focus wins over the live clock", focusEpoch(123456, tick, prf) === 123456);
check("the live clock is clamped into the displayed night", near(focusEpoch(null, prf.noonW - 5000, prf), prf.noonW) && near(focusEpoch(null, prf.noonEndW + 5000, prf), prf.noonEndW));

// ---- moon chips: 30-minute gap, outside first, flip inside on collision --
// Round 5 (review 24 Nov, C1): a chip never sits inside the moon's path if it
// can help it. It keeps its outside side, clamping to the plot edge when the
// gap no longer fits, and only moves to the inside when the drawn arc actually
// cuts through the box. Its bottom edge now sits at 10° altitude, and its clock
// is UTC like every other printed chart time.
const GAP_PX = (30 * 60 * PLOT_W) / DAY_SEC;
for (const p of payloads) {
  const tz = p.location.tz, f = frameOf(p);
  const ev = crossings(p, f);
  const chips = moonChipsOf(p, f);
  check(`${tz}: one chip per in-frame crossing`, chips.length === ev.length && ev.length > 0);
  for (const c of chips) {
    const kind = c.label.split(" ")[0];
    const match = ev.filter((e) => e.kind === kind);
    const ex = match.length ? match.reduce((a, b) => Math.abs(xOf(f, b.sec) - c.x) < Math.abs(xOf(f, a.sec) - c.x) ? b : a) : null;
    check(`${tz}: "${c.label}" prints the crossing time on the UTC scale`, ex !== null && c.label === `${kind} ${hhmmUtc(ex.sec)}` && near(c.x, xOf(f, ex.sec)));
    check(`${tz}: "${c.label}" stays inside the plot`, c.bx >= M_LEFT + 4 - 1e-6 && c.bx + c.w <= M_LEFT + PLOT_W - 4 + 1e-6);
    check(`${tz}: "${c.label}" bottom edge sits at 10° altitude`, near(c.by + c.h, yOf(CHIP_ALT)) && CHIP_ALT === 10);
    check(`${tz}: "${c.label}" box width fits its text`, c.w > c.label.length * 6 && c.w < c.label.length * 9 + 12);
    check(`${tz}: "${c.label}" leader anchor lies between box and crossing`, c.lx >= Math.min(c.bx, c.x) - 1e-6 && c.lx <= Math.max(c.bx + c.w, c.x) + 1e-6);
    if (ex && kind === "rise") {
      const outside = c.bx + c.w <= c.x + 1e-6;
      check(`${tz}: rise chip is outside-left, clamped to the plot edge, or a clean flip`, (outside && c.x - (c.bx + c.w) >= GAP_PX - 1e-6) || near(c.bx, M_LEFT + 4) || c.bx >= c.x - 1e-6);
    }
    if (ex && kind === "set") {
      const outside = c.bx >= c.x - 1e-6;
      check(`${tz}: set chip is outside-right, clamped to the plot edge, or a clean flip`, (outside && c.bx - c.x >= GAP_PX - 1e-6) || near(c.bx + c.w, M_LEFT + PLOT_W - 4) || c.bx + c.w <= c.x + 1e-6);
    }
  }
  const sorted = [...chips].sort((a, b) => a.x - b.x);
  for (let i = 1; i < sorted.length; i++) {
    const a = sorted[i - 1], b = sorted[i];
    check(`${tz}: chips do not stack on top of each other`, a.bx + a.w < b.bx + a.w && (b.bx >= a.bx + a.w || a.bx >= b.bx + b.w || Math.abs(a.x - b.x) < 2 * GAP_PX));
  }
}

// All three design nights have a moonset and no in-frame moonrise (the round-4
// Prague chart the reviewer was pointing at is a LIVE night, not a fixture), so
// the rise side of the placement law gets synthetic nights: the payload's own
// moonset moved to the far side of midnight, and one placed right at the frame's
// left edge where the plot runs out of room.
const nightWith = (base: NightPayload, moonrise: number[], moonset: number[]): NightPayload => ({
  ...base,
  moon: {
    ...base.moon,
    moonrise: moonrise.map((s) => new Date(s * 1000).toISOString()),
    moonset: moonset.map((s) => new Date(s * 1000).toISOString()),
  },
});
{
  const p = payloads[0], f = frameOf(p);
  const roomy = moonChipsOf(nightWith(p, [f.midSec], []), f);
  check("synthetic rise at midnight: chip sits LEFT of its crossing with the full 30 min gap",
    roomy.length === 1 && roomy[0].label === `rise ${hhmmUtc(f.midSec)}`
    && roomy[0].bx + roomy[0].w <= roomy[0].x - GAP_PX + 1e-6);
  const edge = moonChipsOf(nightWith(p, [f.noonW + 15 * 60], []), f);
  check("synthetic rise 15 min into the frame: clamped to the plot edge, still on its outside side",
    edge.length === 1 && near(edge[0].bx, M_LEFT + 4) && edge[0].bx < edge[0].x && edge[0].x - edge[0].bx < GAP_PX);
  const pair = moonChipsOf(nightWith(p, [f.midSec], [f.midSec + 40 * 60]), f);
  check("synthetic rise/set pair 40 min apart: both keep their outside sides and clear each other",
    pair.length === 2 && pair.every((c) => c.by + c.h === yOf(CHIP_ALT))
    && (pair[0].bx + pair[0].w <= pair[1].bx || pair[1].bx + pair[1].w <= pair[0].bx));
}

// ---- moon glyph: hidden while the moon is down, tracks the focus while up --
// Round 5 replaced the round-4 law ("park the glyph on the rise/set it last
// crossed") with the reviewer's simpler one: below the horizon there is no Moon
// on the sky, so there is no glyph on the chart.
for (const p of payloads) {
  const tz = p.location.tz, f = frameOf(p);
  const ms = moonSeriesOf(p, f);
  const ev = crossings(p, f);
  check(`${tz}: the glyph is hidden outside the frame`, glyphSecOf(ms, f.midSec, false) === null);
  check(`${tz}: trackEvents are exactly the in-frame crossings`, ms.trackEvents.length === ev.length && ms.trackEvents.every((s) => s >= f.noonW && s <= f.noonEndW));
  const upIdx = ms.alts.findIndex((alt) => alt > 5);
  const downIdx = ms.alts.findIndex((alt) => alt < -5);
  if (upIdx >= 0) {
    check(`${tz}: the glyph follows the focus while the moon is up`, glyphSecOf(ms, ms.times[upIdx], true) === ms.times[upIdx]);
  }
  if (downIdx >= 0) {
    check(`${tz}: the glyph is hidden while the moon is below the horizon`, glyphSecOf(ms, ms.times[downIdx], true) === null);
  }
  check(`${tz}: the glyph hides at the crossing, not after it`, (() => {
    if (!ev.length) return true;
    const just = glyphSecOf(ms, ev[0].sec + 1, true);
    const at = glyphSecOf(ms, ev[0].sec - 1, true);
    return ev[0].kind === "rise" ? (at === null && just === ev[0].sec + 1) : (at === ev[0].sec - 1 && just === null);
  })());
  const pts = arcPointsOf(ms, f).trim().split(" ").filter(Boolean);
  const upCount = ms.times.filter((s, i) => ms.alts[i] >= 0 && s >= f.noonW && s <= f.noonEndW).length;
  check(`${tz}: the arc is drawn only above the horizon, inside the plot`, pts.length === upCount && pts.length > 0 && pts.every((pt) => {
    const [px, py] = pt.split(",").map(Number);
    return px >= M_LEFT - 1e-6 && px <= M_LEFT + PLOT_W + 1e-6 && py <= FLOOR_Y + 1e-6 && py >= M_TOP - 1e-6;
  }));
  if (ev.some((e) => e.kind === "rise")) {
    const r = ev.find((e) => e.kind === "rise")!.sec;
    check(`${tz}: the readout names the next rise before it happens`, riseForFocusOf(p, f, r - 600, true) === r);
    check(`${tz}: the readout keeps the last rise once it has passed`, (() => { const g = riseForFocusOf(p, f, r + 600, true); return g === null || (g <= r && g >= f.noonW); })());
  }
  check(`${tz}: no rise is reported outside the frame`, riseForFocusOf(p, f, f.midSec, false) === null);
}

// ---- twilight bands: edges at the band thresholds, painted in order ------
for (const p of payloads) {
  const f = frameOf(p);
  const altAt = altAtOf(p);
  const bounds = boundariesOf(p, f, altAt);
  check(`${p.location.tz}: the boundary list spans the frame`, bounds.length >= 2 && bounds[0].sec === f.noonW && bounds[bounds.length - 1].sec === f.noonEndW);
  check(`${p.location.tz}: boundaries are in time order`, bounds.every((b, i) => i === 0 || b.sec >= bounds[i - 1].sec));
  const evs = p.sun.events.filter((e) => { const s = Date.parse(e.time) / 1000; return s > f.noonW && s < f.noonEndW; });
  check(`${p.location.tz}: each in-frame twilight event adds a boundary`, bounds.length === 2 + evs.length + [p.sun.sunset, p.sun.sunrise].filter((t) => { const s = Date.parse(t) / 1000; return s > f.noonW && s < f.noonEndW; }).length);
  check(`${p.location.tz}: a boundary nudges to the side the sun is heading`, evs.every((e) => {
    const th = ALT_AT[e.band], s = Date.parse(e.time) / 1000;
    const b = bounds.find((x) => near(x.sec, s));
    return b !== undefined && (e.edge === "start" ? b.alt > th : b.alt < th);
  }));
  const items = bandItemsOf(p, f, altAt);
  const days = items.filter((i) => i.kind === "day");
  const bands = items.filter((i) => i.kind === "band");
  const edges = items.filter((i) => i.kind === "edge");
  check(`${p.location.tz}: the day band opens and closes the stack`, items[0].kind === "day" && items[items.length - 1].kind === "day" && days.length === 2);
  check(`${p.location.tz}: band gradients name a real band and edge`, bands.length > 0 && bands.every((i) => ["civil", "nautical", "astronomical", "night"].includes(i.band) && (i.grad === "dusk" || i.grad === "dawn")));
  check(`${p.location.tz}: every dusk band precedes every dawn band`, (() => {
    const seen = bands.map((i) => i.grad);
    return seen.lastIndexOf("dusk") < seen.indexOf("dawn");
  })());
  check(`${p.location.tz}: consecutive boundaries give exactly one fill each`, bands.length + days.length === bounds.length - 1);
  check(`${p.location.tz}: a hairline closes each twilight band`, edges.length === bands.length);
  check(`${p.location.tz}: every band item sits inside the plot`, items.every((i) => i.x >= M_LEFT - 1e-6 && (i.kind === "edge" ? i.x : i.x + i.w) <= M_LEFT + PLOT_W + 1e-6));
}

console.log(`${pass} checks passed, ${fails.length} failed`);
for (const f of fails) console.log(`FAIL  ${f}`);
process.exit(fails.length ? 1 : 0);
