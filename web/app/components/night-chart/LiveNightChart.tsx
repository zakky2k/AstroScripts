"use client";

// Live night chart — the realtime view of the fixed single-night frame
// (docs/REQUIREMENTS.md §4.1): left edge ALWAYS local noon, centre ALWAYS
// midnight, right edge ALWAYS noon+1. One afternoon, one sunset, one twilight
// descent, one dark period, one sunrise, one morning — for every date.
//
// - The vertical band gradient carries the sun entirely (no extra bars);
//   daylight halves are a pale flat blue, brighter than any twilight band.
// - Official transition times (sunset, each twilight edge, darkness, sunrise)
//   are printed vertically INSIDE the chart at their exact x positions.
// - Mouseover shows a tooltip with the time (x) and altitude (y) under the
//   cursor, plus crosshair guides.
// - A focus time drives the moon glyph; in "live" mode it tracks the clock,
//   scrubbing or stepping past an edge flips to the previous/next night,
//   parked at the moonrise/sunrise that anchors that night's arc.

import { useEffect, useRef, useState } from "react";
import { parseLocal, terminator, type LocalTime } from "@/components/night-chart/NightChart";
import { BANDS, CURVE, GRID, INK, INK_BRIGHT, NOW, PAGE_BG, bandOf } from "@/lib/palette";
import type { Band, NightPayload } from "@/lib/types";

const W = 980;
// Bottom margin extended (was 48): the 45° labels below the chart were
// truncating at the old canvas edge (review 23 Nov). Font size stays 12.
const H = 600; // taller: the 45° transition labels below the horizon need
               // ~150px of margin so their diagonal text is never truncated.
const M_LEFT = 56, M_RIGHT = 24, M_TOP = 40, M_BOTTOM = 150;
const PLOT_W = W - M_LEFT - M_RIGHT;
const PLOT_H = H - M_TOP - M_BOTTOM;
const Y_TOP = 90.0;
const DAY_SEC = 86_400;

/** Sun altitude at the moment each twilight band starts (crossing threshold). */
const ALT_AT: Record<string, number> = { civil: -6, nautical: -12, astronomical: -18 };

/** Daylight fill: paler and brighter than the lightest twilight band. */
const DAY_FILL = "#4E7FB5";

/** Linear interpolation into a sample series (values at ascending times). */
function interp(times: number[], values: number[], t: number): number | null {
  if (!times.length || t < times[0] || t > times[times.length - 1]) return null;
  for (let i = 1; i < times.length; i++) {
    if (times[i - 1] <= t && t <= times[i]) {
      const f = (t - times[i - 1]) / (times[i] - times[i - 1]);
      return values[i - 1] + f * (values[i] - values[i - 1]);
    }
  }
  return values[values.length - 1];
}

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
const DAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

/** Wall-clock parts (observer zone) of an epoch instant, via Intl. The LocalTime
 *  type carries no seconds, so read them straight from the sv-SE ISO string. */
function wallParts(ms: number, tz: string): LocalTime & { s: number } {
  const iso = new Intl.DateTimeFormat("sv-SE", {
    timeZone: tz, year: "numeric", month: "2-digit", day: "2-digit",
    hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false,
  }).format(new Date(ms));
  const sec = +iso.slice(19, 21);
  return { ...parseLocal(`${iso.replace(" ", "T")}+00:00`), s: sec };
}

/** Add `days` to the calendar day of an offset-aware ISO string, keeping its
 *  time-of-day and zone designator; parse back to epoch seconds. DST-safe
 *  because the designator is re-parsed with whatever offset applies. */
function shiftIsoDay(iso: string, days: number): number {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso)!;
  const d = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3] + days));
  const day = `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, "0")}-${String(d.getUTCDate()).padStart(2, "0")}`;
  return Date.parse(`${day}${iso.slice(10)}`) / 1000;
}

const hhmm = (sec: number, tz: string) => {
  const p = wallParts(sec * 1000, tz);
  return `${String(p.h).padStart(2, "0")}:${String(p.m).padStart(2, "0")}`;
};

/** Full clock for the TIME field: HH:MM:SS in 24 h mode, h:mm:ss AM/PM in
 *  12 h mode. */
function fmtClock(sec: number, tz: string, h12: boolean, withSeconds = true): string {
  const p = wallParts(sec * 1000, tz);
  const s = withSeconds ? `:${String(Math.floor(p.s)).padStart(2, "0")}` : "";
  if (!h12) return `${String(p.h).padStart(2, "0")}:${String(p.m).padStart(2, "0")}${s}`;
  const suf = p.h >= 12 ? "PM" : "AM";
  const h = p.h % 12 === 0 ? 12 : p.h % 12;
  return `${String(h).padStart(2, "0")}:${String(p.m).padStart(2, "0")}${s} ${suf}`;
}

/** Parse HH:MM[:SS] or h:mm[:SS] AM/PM into wall-clock fields. */
function parseClock(v: string): { h: number; m: number; s: number } | null {
  const m = /^(\d{1,2}):(\d{2})(?::(\d{2}))?\s*(AM|PM)?$/i.exec(v.trim());
  if (!m) return null;
  let h = +m[1];
  const mi = +m[2], s = m[3] ? +m[3] : 0;
  const suf = m[4]?.toUpperCase();
  if (suf) {
    if (h < 1 || h > 12 || mi > 59 || s > 59) return null;
    if (suf === "PM" && h !== 12) h += 12;
    if (suf === "AM" && h === 12) h = 0;
  } else if (h > 23 || mi > 59 || s > 59) return null;
  return { h, m: mi, s };
}

/** Focus time in observer wall-clock fields + fractional seconds. */
interface Focus { y: number; mo: number; d: number; h: number; mi: number; s: number }
const focusSec = (f: Focus) =>
  Date.UTC(f.y, f.mo - 1, f.d, f.h, f.mi) / 1000 + f.s;

export function LiveNightChart({ payload, tz }: { payload: NightPayload; tz: string }) {
  // ---- time state -------------------------------------------------------
  // ALL hooks are declared up front, before any computation: a hook that
  // ends up behind an early return or conditional block breaks React's
  // hook-order rule ("Should have a queue" / update-depth churn).
  // "live": focus tracks the clock (minute ticks). Scrub/step switches to a
  // scrubbed focus until "tonight" re-arms live mode.
  const [tickMs, setTickMs] = useState<number | null>(null);
  // Mouse tooltip state.
  const [hover, setHover] = useState<{ x: number; y: number; sec: number; alt: number } | null>(null);
  // 12 h / 24 h display mode for the header TIME field (lives in Planner;
  // the chart formats its readouts to match).
  const [h12, setH12] = useState(false);
  useEffect(() => {
    let timer: ReturnType<typeof setTimeout>;
    const schedule = () => {
      const d = new Date();
      const delay = (60 - d.getSeconds()) * 1000 - d.getMilliseconds() + 50;
      timer = setTimeout(() => { setTickMs(Date.now()); schedule(); }, delay);
    };
    setTickMs(Date.now());
    schedule();
    return () => clearTimeout(timer);
  }, []);
  useEffect(() => {
    const on = () => setH12((v) => !v);
    window.addEventListener("planner:toggle-h", on);
    return () => window.removeEventListener("planner:toggle-h", on);
  }, []);

  // Frame edges in float seconds (see §4.1): payload.frameNoon is the pivot
  // NOON; the right edge is wall-clock noon+1, derived from the ISO literal so
  // DST landings are exact.
  const noonW = Date.parse(payload.frameNoon) / 1000;
  const noonEndW = shiftIsoDay(payload.frameNoon, 1);

  const [scrub, setScrub] = useState<Focus | null>(null);
  // Mirror for event handlers / effects: they must read the CURRENT scrub,
  // not the one captured at render time (the planner:set-time listener and
  // the night-flip effect both acted on stale state and ping-ponged the
  // focus between two nights — React's "Maximum update depth" error).
  const scrubRef = useRef<Focus | null>(null);
  const setScrubBoth = (f: Focus | null) => { scrubRef.current = f; setScrub(f); };
  const svgRef = useRef<SVGSVGElement | null>(null);
  const shiftRef = useRef(false);
  const [timeDraft, setTimeDraft] = useState("");

  // Re-arm live mode whenever a NEW night arrives. Keyed on the frame literal
  // (a stable string) rather than the payload object, so no re-render can
  // re-trigger it — an object dependency churned and hit React's "Maximum
  // update depth exceeded" guard during night flips. shiftNight already cleared
  // the scrub, so this simply releases the flip latch.
  useEffect(() => {
    setScrubBoth(null); shiftRef.current = false;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [payload.frameNoon]);

  const nowParts = tickMs !== null ? wallParts(tickMs, tz) : null;
  const liveSec = nowParts
    ? Date.UTC(nowParts.y, nowParts.mo - 1, nowParts.d, nowParts.h, nowParts.m) / 1000
    : NaN;

  // The focus instant the moon + marker follow.
  let focusSecV: number;
  if (scrub) focusSecV = focusSec(scrub);
  else if (!Number.isNaN(liveSec)) {
    // Clamp live focus into the displayed night so the glyph is always shown.
    focusSecV = Math.min(Math.max(liveSec, noonW), noonEndW);
  } else focusSecV = NaN;

  const inFrame = !Number.isNaN(focusSecV) && focusSecV >= noonW && focusSecV <= noonEndW;
  const isLive = scrub === null;

  // Keep the moon glyph ON its track. The glyph itself is pinned to a point
  // on the curve (below), so jumping across a below-horizon gap no longer
  // drags it along a straight chord — the old CSS transition made that cut
  // visible during fast scrubbing, so there is none any more. Scrub/step
  // additionally park at the nearest rise/set when the typed or dragged time
  // lands where the moon is under the horizon; live ticks skip that, since
  // the moon genuinely sets/rises in real time and the pin handles it.
  const trackEvents: number[] = [
    ...payload.moon.moonrise.map((s) => Date.parse(s) / 1000),
    ...payload.moon.moonset.map((s) => Date.parse(s) / 1000),
  ].filter((s) => s >= noonW && s <= noonEndW);
  const moonTimes = payload.moon.samples.map((s) => Date.parse(s.time) / 1000);
  const moonAlts = payload.moon.samples.map((s) => s.altitude);
  function snapToTrack(sec: number): number {
    const alt = interp(moonTimes, moonAlts, sec);
    if (alt !== null && alt > 0) return sec; // already on the arc
    let best = sec, bd = Infinity;
    for (const e of trackEvents) {
      const d = Math.abs(e - sec);
      if (d < bd) { bd = d; best = e; }
    }
    return best;
  }

  // Night-shift when the scrubbed focus crosses a frame edge: flip payload to
  // the neighbouring night. The parent re-fetches and keeps the previous
  // payload on screen until the new one lands, so the chart never unmounts
  // mid-scrub. We deliberately do NOT pre-park the focus at the next night's
  // moonrise: that instant can lie outside the CURRENT frame, which bounces
  // the focus back and forth between two nights (update-depth loop). The new
  // payload re-arms live mode instead — for "keep pressing ▶1h" this lands
  // on the incoming night's moonrise when it is in daylight, else on now.
  function shiftNight(dir: 1 | -1) {
    if (shiftRef.current) return; // ignore repeat edge crossings until swap lands
    shiftRef.current = true;
    setScrubBoth(null); // re-arm live focus for the incoming night
    const day = payload.nightStart.slice(0, 10);
    const d = new Date(Date.UTC(+day.slice(0, 4), +day.slice(5, 7) - 1, +day.slice(8, 10) + dir));
    const next = `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, "0")}-${String(d.getUTCDate()).padStart(2, "0")}`;
    window.dispatchEvent(new CustomEvent("planner:date", { detail: next }));
  }

  function setFocusSec(sec: number) {
    if (sec < noonW) { shiftNight(-1); return; }
    if (sec > noonEndW) { shiftNight(1); return; }
    const p = wallParts(sec * 1000, tz);
    setScrubBoth({ y: p.y, mo: p.mo, d: p.d, h: p.h, mi: p.m, s: sec % 60 });
  }

  // Slider drag: the range is hard-clamped to the frame, so "crossing an
  // edge" means hitting it while already scrubbing — flip the night; the new
  // payload re-arms live focus (see shiftNight).
  function setScrubRaw(sec: number) {
    if (scrubRef.current !== null && (sec === noonW || sec === noonEndW)) {
      shiftNight(sec === noonW ? -1 : 1);
      return;
    }
    const p = wallParts(sec * 1000, tz);
    setScrubBoth({ y: p.y, mo: p.mo, d: p.d, h: p.h, mi: p.m, s: sec % 60 });
  }

  function stepHours(delta: number) {
    const base = Number.isNaN(focusSecV) ? noonW : focusSecV;
    // Snap to the moon's track so the glyph never wanders off the curve when
    // an hour step lands where the moon is below the horizon.
    setFocusSec(snapToTrack(Math.round(base + delta * 3600)));
  }

  // Editable time field (HH:MM[:SS], 12 h AM/PM or 24 h): jumps the focus
  // within this night. Reports the focus to the header field and accepts
  // edits from it. (timeDraft state is declared with the other hooks above.)
  useEffect(() => {
    if (inFrame) setTimeDraft(fmtClock(focusSecV, tz, h12));
  }, [focusSecV, inFrame, tz, h12]);
  useEffect(() => {
    window.dispatchEvent(new CustomEvent("planner:focus", { detail: timeDraft }));
  }, [timeDraft]);
  useEffect(() => {
    const on = (e: Event) => {
      const v = (e as CustomEvent<string>).detail.trim();
      const c = parseClock(v);
      if (!c) return;
      // Wall-clock time on whichever calendar day the focus currently sits on.
      const p = inFrame ? wallParts(focusSecV * 1000, tz) : wallParts(noonW * 1000, tz);
      let sec = Date.UTC(p.y, p.mo - 1, p.d, c.h, c.m, c.s) / 1000;
      // The typed time may belong to the other half of the frame (e.g. it is
      // smaller than noon while we are past midnight): shift by a day so it
      // lands inside this night.
      if (sec < noonW - DAY_SEC / 2) sec += DAY_SEC;
      if (sec > noonEndW + DAY_SEC / 2) sec -= DAY_SEC;
      setFocusSec(snapToTrack(sec));
    };
    window.addEventListener("planner:set-time", on);
    return () => window.removeEventListener("planner:set-time", on);
  }, [focusSecV, inFrame, noonW, noonEndW, tz, h12]);

  // ---- scales -----------------------------------------------------------
  const xOf = (sec: number) => M_LEFT + (PLOT_W * (sec - noonW)) / (noonEndW - noonW);
  const secOfX = (px: number) => noonW + ((px - M_LEFT) * (noonEndW - noonW)) / PLOT_W;
  const yOf = (alt: number) => M_TOP + PLOT_H * (1.0 - Math.max(alt, 0.0) / Y_TOP);
  const altOfY = (px: number) => (1 - (px - M_TOP) / PLOT_H) * Y_TOP;

  // ---- moon series ------------------------------------------------------
  const samples = payload.moon.samples;
  const times = samples.map((s) => Date.parse(s.time) / 1000);
  const alts = samples.map((s) => s.altitude);
  const illum = samples.map((s) => s.illumFraction);

  const tW = Date.parse(payload.nightStart) / 1000;
  const tW1 = Date.parse(payload.nightEnd) / 1000;
  const midSec = (noonW + noonEndW) / 2; // plot centre: midnight

  // Moon state at the focus instant.
  const altF = inFrame ? interp(times, alts, focusSecV) : null;
  const illumF = inFrame ? interp(times, illum, focusSecV) : null;

  // The glyph is PINNED to the arc: whenever the focus sits where the moon is
  // below the horizon, it parks at the nearest rise/set point ON the curve
  // instead of drifting along a straight chord (the old CSS transition made
  // that drift visible during fast scrubbing).
  const glyphSec = (() => {
    if (!inFrame) return null;
    if (altF !== null && altF > 0) return focusSecV;
    let best: number | null = null, bd = Infinity;
    for (const e of trackEvents) {
      const d = Math.abs(e - focusSecV);
      if (d < bd) { bd = d; best = e; }
    }
    return best;
  })();
  const glyphAlt = glyphSec !== null ? interp(times, alts, glyphSec) : null;
  const moonUp = glyphAlt !== null && glyphAlt > 0;

  // ---- bands ------------------------------------------------------------
  const sunSeries = payload.sun.series;
  const sunAlt = sunSeries.map((s) => s.altitude);
  const sunTimes = sunSeries.map((s) => Date.parse(s.time) / 1000);
  const altAt = (sec: number) => interp(sunTimes, sunAlt, sec) ?? 0;

  const boundaries: { sec: number; alt: number }[] = [];
  boundaries.push({ sec: noonW, alt: altAt(noonW) });
  {
    const dusk = Date.parse(payload.sun.sunset) / 1000;
    if (dusk > noonW && dusk < noonEndW) boundaries.push({ sec: dusk, alt: -0.01 });
  }
  for (const e of payload.sun.events) {
    const s = Date.parse(e.time) / 1000;
    if (s <= noonW || s >= noonEndW) continue;
    const thr = ALT_AT[e.band];
    boundaries.push({ sec: s, alt: e.edge === "start" ? thr + 0.01 : thr - 0.01 });
  }
  {
    const dawn = Date.parse(payload.sun.sunrise) / 1000;
    if (dawn > noonW && dawn < noonEndW) boundaries.push({ sec: dawn, alt: 0.01 });
  }
  boundaries.push({ sec: noonEndW, alt: altAt(noonEndW) });
  boundaries.sort((p, q) => p.sec - q.sec);

  const bandRects: React.ReactNode[] = [];
  for (let i = 0; i < boundaries.length - 1; i++) {
    const a = boundaries[i], b = boundaries[i + 1];
    if (b.sec <= a.sec) continue;
    const midB = (a.sec + b.sec) / 2;
    const band = bandOf(altAt(midB));
    const grad = midB < midSec ? "dusk" : "dawn";
    if (band === "civil" && altAt(midB) > 0) {
      // Daylight: paler/brighter than the lightest twilight band, flat fill.
      bandRects.push(
        <rect key={`band${i}`} x={xOf(a.sec)} y={M_TOP} width={Math.max(xOf(b.sec) - xOf(a.sec), 0.5)}
          height={PLOT_H} fill={DAY_FILL} opacity={0.42} />
      );
    } else {
      bandRects.push(
        <rect key={`band${i}`} x={xOf(a.sec)} y={M_TOP} width={Math.max(xOf(b.sec) - xOf(a.sec), 0.5)}
          height={PLOT_H} fill={`url(#b-${band}-${grad}-live)`} />
      );
      bandRects.push(
        <line key={`edge${i}`} x1={xOf(a.sec)} y1={M_TOP} x2={xOf(a.sec)} y2={M_TOP + PLOT_H}
          stroke="#CBD5E1" strokeOpacity={0.13} />
      );
    }
  }

  // ---- grid --------------------------------------------------------------
  const gridEls: React.ReactNode[] = [];
  for (let alt = 0; alt <= Y_TOP; alt += 10) {
    const yy = yOf(alt);
    const major = alt % 30 === 0;
    gridEls.push(
      <line key={`g${alt}`} x1={M_LEFT} y1={yy} x2={M_LEFT + PLOT_W} y2={yy}
        stroke={GRID} strokeOpacity={major ? 0.3 : 0.16} strokeDasharray={major ? "5 6" : "1 6"} />,
      <text key={`t${alt}`} x={M_LEFT - 10} y={yy + 4} textAnchor="end" fontSize={11} fill={INK}>{alt}°</text>
    );
  }

  // ---- transition labels: 45° timestamps BELOW the horizon (0°) -----------
  // Strictly under the chart — in the below-zero strip, never inside the
  // plot. Each stage's label sits at 45° with a short leader line from the
  // MIDDLE of its last letter straight up to the MIDDLE of the transition
  // segment it names (halfway between the plot floor and the top). Dusk-side
  // labels sit left of their line sloping up-right toward it; dawn-side
  // labels mirror on the right. Shortened words keep the tight twilight
  // sequence collision-free.
  const transitions: { sec: number; label: string; side: "dusk" | "dawn" }[] = [
    { sec: Date.parse(payload.sun.sunset) / 1000, label: `${hhmm(tW, tz)} sunset`, side: "dusk" },
  ];
  for (const e of payload.sun.events) {
    const s = Date.parse(e.time) / 1000;
    if (s <= noonW || s >= noonEndW) continue;
    transitions.push({ sec: s, label: `${hhmm(s, tz)} ${e.band}`, side: e.edge === "start" ? "dusk" : "dawn" });
  }
  transitions.push({ sec: Date.parse(payload.sun.sunrise) / 1000, label: `${hhmm(tW1, tz)} sunrise`, side: "dawn" });
  const FS = 12; // slightly bigger font (was 9)
  const CH = 7.4; // average glyph advance at fontSize 12 (Inter/Segoe UI)
  const bandWord: Record<string, string> = { civil: "civil", nautical: "naut.", astronomical: "astro." };
  const floorY = M_TOP + PLOT_H; // the horizon line (0°)
  const transEls = transitions
    .filter((t) => t.sec > noonW && t.sec < noonEndW)
    .map((t, i) => {
      const x = xOf(t.sec);
      let label = t.label;
      for (const [full, short] of Object.entries(bandWord)) label = label.replace(full, short);
      const anchorY = floorY + 10; // last letter just below the horizon strip
      if (t.side === "dusk") {
        // text runs up-right and ENDS at its line: first letter low-left,
        // last letter's middle at (x - 3.7, anchorY)
        const ax = x - CH / 2;
        return (
          <g key={`tr${i}`}>
            <text x={ax} y={anchorY} fontSize={FS} fill={INK} opacity={0.9}
              transform={`rotate(-45 ${ax} ${anchorY})`} textAnchor="end">{label}</text>
            {/* leader: middle of last letter → up along the transition line */}
            <line x1={ax} y1={anchorY - 2} x2={x} y2={floorY - 6}
              stroke={INK} strokeOpacity={0.5} strokeWidth={0.8} />
          </g>
        );
      }
      // mirrored: text starts at its line running down-right, so the FIRST
      // letter hugs the transition and its middle sits at (x + 3.7, anchorY)
      const ax = x + CH / 2;
      return (
        <g key={`tr${i}`}>
          <text x={ax} y={anchorY} fontSize={FS} fill={INK} opacity={0.9}
            transform={`rotate(45 ${ax} ${anchorY})`} textAnchor="start">{label}</text>
          <line x1={ax} y1={anchorY - 2} x2={x} y2={floorY - 6}
            stroke={INK} strokeOpacity={0.5} strokeWidth={0.8} />
        </g>
      );
    });

  // moonrise / moonset labels: HORIZONTAL chips INSIDE the chart, just above
  // the horizon. Slots are FIXED by construction (rightmost event pinned to
  // the right plot edge, next one to the left edge) so chips can never
  // overlap and there is no measure-and-settle loop; a leader line drops
  // from each chip to the exact point where the moon path reaches zero.
  const riseSet: React.ReactNode[] = [];
  {
    const events: { sec: number; kind: "rise" | "set" }[] = [];
    for (const key of ["moonrise", "moonset"] as const) {
      for (const iso of payload.moon[key]) {
        const s = Date.parse(iso) / 1000;
        if (s >= noonW && s <= noonEndW) events.push({ sec: s, kind: key === "moonrise" ? "rise" : "set" });
      }
    }
    events.sort((a, b) => a.sec - b.sec);
    const chips = events.map((e, i) => {
      const label = `${e.kind} ${hhmm(e.sec, tz)}`;
      const w = label.length * CH + 10; // text width with padding
      const h = 16;
      const x = xOf(e.sec);
      const slot = events.length - 1 - i; // 0 = rightmost event
      let bx: number;
      if (slot === 0) bx = M_LEFT + PLOT_W - 8 - w;
      else if (slot === 1) bx = M_LEFT + 8;
      else bx = Math.min(Math.max(x - w / 2, M_LEFT + 8), M_LEFT + PLOT_W - 8 - w);
      const by = floorY - 10 - h;
      return { x, bx, by, w, h, label, key: `rs${i}` };
    });
    chips.forEach((c) => {
      // leader starts at the chip edge closest to the crossing point
      const lx = c.x < c.bx ? c.bx : c.x > c.bx + c.w ? c.bx + c.w : c.x;
      riseSet.push(
        <g key={c.key}>
          <line x1={lx} y1={c.by + c.h} x2={c.x} y2={floorY - 3}
            stroke={CURVE} strokeOpacity={0.75} strokeWidth={0.9} />
          {/* exact crossing dot: where the moon path reaches zero */}
          <circle cx={c.x} cy={yOf(0)} r={2.6} fill={CURVE} fillOpacity={0.9} />
          <rect x={c.bx} y={c.by} width={c.w} height={c.h} rx={3} fill="#0B101C" fillOpacity={0.92}
            stroke={CURVE} strokeOpacity={0.45} strokeWidth={0.7} />
          <text x={c.bx + c.w / 2} y={c.by + c.h - 4.5} fontSize={FS} fill="#C8D3E4" textAnchor="middle">{c.label}</text>
        </g>
      );
    });
  }

  // the arc the moon glyph travels: drawn across the FULL frame including
  // daylight hours (samples clipped only at altitude <= 0), so its rise
  // point is always visible.
  const above = times
    .map((s, i) => (alts[i] >= 0 && s >= noonW && s <= noonEndW ? `${xOf(s)},${yOf(alts[i])}` : null))
    .filter(Boolean)
    .join(" ");

  // ---- header -------------------------------------------------------------
  const fm = /^(\d{4})-(\d{2})-(\d{2})/.exec(payload.frameNoon)!;
  const mid = new Date(Date.UTC(+fm[1], +fm[2] - 1, +fm[3] + 1));
  const midIso = `${DAYS[mid.getUTCDay()]} ${String(mid.getUTCDate()).padStart(2, "0")} ${MONTHS[mid.getUTCMonth()]} 00:00`;
  const title = `${payload.location.lat.toFixed(2)}, ${payload.location.lon.toFixed(2)}  ·  ` +
    `${DAYS[new Date(Date.UTC(+fm[1], +fm[2] - 1, +fm[3])).getUTCDay()]} ` +
    `${payload.nightStart.slice(8, 10)} ${MONTHS[+payload.nightStart.slice(5, 7) - 1]}  ` +
    `${payload.sun.sunset.slice(11, 16)} → ` +
    `${DAYS[new Date(Date.parse(payload.nightEnd)).getUTCDay()]} ` +
    `${payload.nightEnd.slice(8, 10)} ${MONTHS[+payload.nightEnd.slice(5, 7) - 1]}  ` +
    `${payload.sun.sunrise.slice(11, 16)}`;

  const focusLabel = inFrame ? fmtClock(focusSecV, tz, h12) : "";
  // The corresponding moonrise: the next rise at/after the focus time within
  // this frame, updated as you scrub. Falls back to the last rise before it.
  const riseForFocus = (() => {
    if (!inFrame) return null;
    const future = payload.moon.moonrise.map((s) => Date.parse(s) / 1000)
      .filter((s) => s >= focusSecV && s <= noonEndW);
    if (future.length) return future[0];
    const past = payload.moon.moonrise.map((s) => Date.parse(s) / 1000)
      .filter((s) => s >= noonW && s < focusSecV);
    return past.length ? past[past.length - 1] : null;
  })();
  const liveReadout = inFrame && illumF !== null
    ? `${focusLabel} · ${(illumF * 100).toFixed(0)}% lit` +
      (moonUp ? ` · alt ${glyphAlt!.toFixed(1)}°` : " · below horizon") +
      (riseForFocus !== null ? ` · moonrise ${fmtClock(riseForFocus, tz, h12, false)}` : "")
    : "";

  // Right-edge dawn labels rotate the other way so they grow DOWNWARD from
  // the top of the plot and can never collide with the header readout.

  function onMove(e: React.MouseEvent<SVGSVGElement>) {
    const svg = svgRef.current;
    if (!svg) return;
    const r = svg.getBoundingClientRect();
    const sx = ((e.clientX - r.left) * W) / r.width;
    const sy = ((e.clientY - r.top) * H) / r.height;
    if (sx < M_LEFT || sx > M_LEFT + PLOT_W || sy < M_TOP || sy > M_TOP + PLOT_H) { setHover(null); return; }
    setHover({ x: sx, y: sy, sec: secOfX(sx), alt: Math.max(0, altOfY(sy)) });
  }

  const tip = (() => {
    if (!hover) return null;
    const label = `${hhmm(hover.sec, tz)}  ·  ${hover.alt.toFixed(1)}°`;
    const wTip = label.length * 7 + 14;
    let tx = hover.x + 12;
    if (tx + wTip > M_LEFT + PLOT_W) tx = hover.x - 12 - wTip;
    const ty = Math.max(M_TOP + 6, hover.y - 30);
    return (
      <g pointerEvents="none">
        <line x1={hover.x} y1={M_TOP} x2={hover.x} y2={M_TOP + PLOT_H} stroke={INK} strokeOpacity={0.35} strokeDasharray="2 4" />
        <line x1={M_LEFT} y1={hover.y} x2={M_LEFT + PLOT_W} y2={hover.y} stroke={INK} strokeOpacity={0.35} strokeDasharray="2 4" />
        <rect x={tx} y={ty} width={wTip} height={22} rx={4} fill="#161D2E" stroke={CURVE} strokeOpacity={0.35} />
        <text x={tx + 7} y={ty + 15} fontSize={11} fill={INK_BRIGHT}>{label}</text>
      </g>
    );
  })();

  return (
    <div>
      {/* time control ABOVE the chart: scrub the focus; crossing an edge flips
          the night. Hour steps snap to the moon's track so it never wanders.
          Every row item has a FIXED width (buttons + status text) and the
          slider flexes, so nothing under the cursor ever moves — repeated
          "1 h ▶" clicking keeps the pointer exactly where it belongs. */}
      <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 8, color: "#8C9BB4", fontSize: 12 }}>
        <button onClick={() => stepHours(-1)} aria-label="back one hour" style={{ width: 62, flexShrink: 0 }}>◀ 1 h</button>
        <input type="range" min={noonW} max={noonEndW} step={1}
          value={Number.isNaN(focusSecV) ? noonW : focusSecV}
          onChange={(e) => setScrubRaw(Number(e.target.value))}
          style={{ flex: 1, accentColor: "#E2555A" }} aria-label="time of night" />
        <button onClick={() => stepHours(1)} aria-label="forward one hour" style={{ width: 62, flexShrink: 0 }}>1 h ▶</button>
        {/* live button is ALWAYS present (disabled while live) so the hour
            buttons never move under the cursor during repeated clicking */}
        <button onClick={() => setScrubBoth(null)} disabled={isLive}
          style={{ padding: "4px 10px", width: 78, flexShrink: 0, opacity: isLive ? 0.45 : 1 }}>⏱ live</button>
        <span style={{ width: 150, flexShrink: 0 }}>{isLive ? "tracking live clock" : `scrubbed ${focusLabel}`}</span>
      </div>

      <svg ref={svgRef} xmlns="http://www.w3.org/2000/svg" width={W} height={H} viewBox={`0 0 ${W} ${H}`}
        fontFamily="Inter, Segoe UI, sans-serif" role="img" style={{ width: "100%", height: "auto" }}
        onMouseMove={onMove} onMouseLeave={() => setHover(null)}>
        <defs>
          {BANDS.flatMap(({ name, colour }) => {
            const n = name as Band;
            return [
              <linearGradient key={`${n}-dusk`} id={`b-${n}-dusk-live`} x1="0" x2="1" y1="0" y2="0">
                <stop offset="0" stopColor={colour} />
                <stop offset="1" stopColor={colour} stopOpacity={0.72} />
              </linearGradient>,
              <linearGradient key={`${n}-dawn`} id={`b-${n}-dawn-live`} x1="0" x2="1" y1="0" y2="0">
                <stop offset="0" stopColor={colour} stopOpacity={0.72} />
                <stop offset="1" stopColor={colour} />
              </linearGradient>,
            ];
          })}
          <clipPath id="plot-live"><rect x={M_LEFT} y={M_TOP} width={PLOT_W} height={PLOT_H} /></clipPath>
          <filter id="glow-live" x="-60%" y="-60%" width="220%" height="220%">
            <feGaussianBlur stdDeviation="2.6" result="b" />
            <feMerge><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge>
          </filter>
        </defs>
        <rect width={W} height={H} fill={PAGE_BG} />
        <g clipPath="url(#plot-live)">{bandRects}</g>
        <g>{gridEls}</g>
        <g>
          {transEls}
          <line x1={xOf(midSec)} y1={M_TOP} x2={xOf(midSec)} y2={M_TOP + PLOT_H}
            stroke={GRID} strokeOpacity={0.3} strokeDasharray="4 5" />
          <text x={xOf(midSec)} y={M_TOP - 6} textAnchor="middle" fontSize={10} fill={INK_BRIGHT}>{midIso}</text>
        </g>
        <g clipPath="url(#plot-live)">
          {above && <polyline points={above} fill="none" stroke={CURVE} strokeWidth={1.8}
            strokeOpacity={0.55} strokeLinejoin="round" />}
          {moonUp && glyphSec !== null && (
            <g transform={`translate(${xOf(glyphSec)},${yOf(glyphAlt!)})`}>
              <circle r={13} fill="#161D2E" stroke={CURVE} strokeOpacity={0.4} />
              <path d={terminator(illumF ?? interp(times, illum, glyphSec) ?? 1, "13.0", payload.moon.limb)}
                fill={CURVE} filter="url(#glow-live)" />
            </g>
          )}
        </g>
        {/* moonrise/moonset tick labels live BELOW the horizon like the
            transition timestamps, so they must not be plot-clipped */}
        <g>{riseSet}</g>
        {/* focus marker: the live now line, or the scrubbed-time handle */}
        {inFrame && (
          <g clipPath="url(#plot-live)">
            <line x1={xOf(focusSecV)} y1={M_TOP} x2={xOf(focusSecV)} y2={M_TOP + PLOT_H}
              stroke={NOW} strokeWidth={1.3} strokeDasharray="4 4" />
            <text x={xOf(focusSecV) + 5} y={M_TOP + PLOT_H - 16} fontSize={10} fill={NOW}>
              {isLive ? "now " : ""}{focusLabel}
            </text>
          </g>
        )}
        {tip}
        <text x={M_LEFT} y={24} fontSize={13} fontWeight={600} fill={INK_BRIGHT}>{title}</text>
        {liveReadout && (
          <text x={M_LEFT + PLOT_W - 34} y={24} textAnchor="end" fontSize={12} fill={INK}>{liveReadout}</text>
        )}
      </svg>
    </div>
  );
}
