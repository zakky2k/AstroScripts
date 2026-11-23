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
const H = 470;
const M_LEFT = 56, M_RIGHT = 24, M_TOP = 40, M_BOTTOM = 48;
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

/** Wall-clock parts (observer zone) of an epoch instant, via Intl. */
function wallParts(ms: number, tz: string): LocalTime {
  const iso = new Intl.DateTimeFormat("sv-SE", {
    timeZone: tz, year: "numeric", month: "2-digit", day: "2-digit",
    hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false,
  }).format(new Date(ms));
  return parseLocal(`${iso.replace(" ", "T")}+00:00`);
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

/** Focus time in observer wall-clock fields + fractional seconds. */
interface Focus { y: number; mo: number; d: number; h: number; mi: number; s: number }
const focusSec = (f: Focus) =>
  Date.UTC(f.y, f.mo - 1, f.d, f.h, f.mi) / 1000 + f.s;

export function LiveNightChart({ payload, tz }: { payload: NightPayload; tz: string }) {
  // ---- time state -------------------------------------------------------
  // "live": focus tracks the clock (minute ticks). Scrub/step switches to a
  // scrubbed focus until "tonight" re-arms live mode.
  const [tickMs, setTickMs] = useState<number | null>(null);
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

  // Frame edges in float seconds (see §4.1): payload.frameNoon is the pivot
  // NOON; the right edge is wall-clock noon+1, derived from the ISO literal so
  // DST landings are exact.
  const noonW = Date.parse(payload.frameNoon) / 1000;
  const noonEndW = shiftIsoDay(payload.frameNoon, 1);

  const [scrub, setScrub] = useState<Focus | null>(null);
  // Re-arm live mode whenever a new night payload arrives.
  useEffect(() => { setScrub(null); shiftRef.current = false; }, [payload]);

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

  // Night-shift when the scrubbed focus crosses a frame edge: flip payload to
  // the neighbouring night, parked at the anchor time (moonrise if the moon
  // rises in that night, else sunrise).
  const svgRef = useRef<SVGSVGElement | null>(null);
  const shiftRef = useRef(false);
  function shiftNight(dir: 1 | -1) {
    if (shiftRef.current) return; // ignore repeat edge crossings until swap lands
    shiftRef.current = true;
    const day = payload.nightStart.slice(0, 10);
    const d = new Date(Date.UTC(+day.slice(0, 4), +day.slice(5, 7) - 1, +day.slice(8, 10) + dir));
    const next = `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, "0")}-${String(d.getUTCDate()).padStart(2, "0")}`;
    // The parent re-fetches; we pre-park the focus at this night's moonrise.
    const anchor = payload.moon[dir === 1 ? "moonrise" : "moonset"][0];
    if (anchor) {
      const s = Date.parse(anchor) / 1000;
      const p = wallParts(s * 1000, tz);
      setScrub({ y: p.y, mo: p.mo, d: p.d, h: p.h, mi: p.m, s: 0 });
    } else {
      const p = wallParts((dir === 1 ? noonW + DAY_SEC : noonW - DAY_SEC), tz);
      setScrub({ y: p.y, mo: p.mo, d: p.d, h: 12, mi: 0, s: 0 });
    }
    window.dispatchEvent(new CustomEvent("planner:date", { detail: next }));
  }

  function setFocusSec(sec: number) {
    if (sec < noonW) { shiftNight(-1); return; }
    if (sec > noonEndW) { shiftNight(1); return; }
    const p = wallParts(sec * 1000, tz);
    setScrub({ y: p.y, mo: p.mo, d: p.d, h: p.h, mi: p.m, s: sec % 60 });
  }

  function stepHours(delta: number) {
    const base = Number.isNaN(focusSecV) ? noonW : focusSecV;
    setFocusSec(Math.round(base + delta * 3600));
  }

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
  const moonUp = altF !== null && altF > 0;

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

  // ---- transition labels: vertical timestamps INSIDE the chart ----------
  // Each official stage gets a faint hairline at its exact x and a rotated
  // HH:MM label rising from the bottom of the plot. Dusk-side labels sit on
  // the left edge of their band going up; dawn-side labels hang down from the
  // top, so the tight twilight sequence never collides with itself or the
  // moon curve (which peaks mid-plot).
  const transitions: { sec: number; label: string; side: "dusk" | "dawn" }[] = [
    { sec: noonW, label: "12:00", side: "dusk" },
    { sec: Date.parse(payload.sun.sunset) / 1000, label: `${hhmm(tW, tz)} sunset`, side: "dusk" },
  ];
  for (const e of payload.sun.events) {
    const s = Date.parse(e.time) / 1000;
    if (s <= noonW || s >= noonEndW) continue;
    transitions.push({ sec: s, label: `${hhmm(s, tz)} ${e.band}`, side: e.edge === "start" ? "dusk" : "dawn" });
  }
  transitions.push(
    { sec: Date.parse(payload.sun.sunrise) / 1000, label: `${hhmm(tW1, tz)} sunrise`, side: "dawn" },
    { sec: noonEndW, label: "12:00", side: "dawn" },
  );
  const transEls = transitions
    .filter((t) => t.sec > noonW && t.sec < noonEndW)
    .map((t, i) => {
      const x = xOf(t.sec);
      return t.side === "dusk" ? (
        <g key={`tr${i}`}>
          <line x1={x} y1={M_TOP + PLOT_H} x2={x} y2={M_TOP + PLOT_H + 5}
            stroke={INK_BRIGHT} strokeOpacity={0.6} />
          <text x={x - 3} y={M_TOP + PLOT_H - 8} fontSize={9} fill={INK} opacity={0.85}
            transform={`rotate(-90 ${x - 3} ${M_TOP + PLOT_H - 8})`} textAnchor="end">{t.label}</text>
        </g>
      ) : (
        <g key={`tr${i}`}>
          <line x1={x} y1={M_TOP} x2={x} y2={M_TOP - 5}
            stroke={INK_BRIGHT} strokeOpacity={0.6} />
          {/* rotate the opposite way: the label grows DOWNWARD from the top
              edge, clear of the header readout and of each other */}
          <text x={x - 3} y={M_TOP + 10} fontSize={9} fill={INK} opacity={0.85}
            transform={`rotate(90 ${x - 3} ${M_TOP + 10})`} textAnchor="start">{t.label}</text>
        </g>
      );
    });

  // moonrise / moonset markers on the horizon (kept: §4.1 requires them)
  const riseSet: React.ReactNode[] = [];
  for (const [key, kind] of [["moonrise", "rise"], ["moonset", "set"]] as const) {
    payload.moon[key].forEach((iso, i) => {
      const s = Date.parse(iso) / 1000;
      if (s < noonW || s > noonEndW) return;
      riseSet.push(
        <text key={`${key}${i}`} x={xOf(s)} y={yOf(0) - 16} textAnchor="middle"
          fontSize={9} fill={INK}>{kind}</text>
      );
    });
  }

  // the arc the moon glyph travels (clipped to the one-night frame)
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

  const focusLabel = inFrame ? hhmm(focusSecV, tz) : "";
  const liveReadout = inFrame && illumF !== null
    ? `${focusLabel} · ${(illumF * 100).toFixed(0)}% lit` + (moonUp ? ` · alt ${altF!.toFixed(1)}°` : " · below horizon")
    : "";

  // Right-edge dawn labels rotate the other way so they grow DOWNWARD from
  // the top of the plot and can never collide with the header readout.

  // ---- mouse tooltip -------------------------------------------------------
  const [hover, setHover] = useState<{ x: number; y: number; sec: number; alt: number } | null>(null);
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
          {moonUp && (
            <g transform={`translate(${xOf(focusSecV)},${yOf(altF!)})`}
              style={{ transition: "transform 0.9s linear" }}>
              <circle r={13} fill="#161D2E" stroke={CURVE} strokeOpacity={0.4} />
              <path d={terminator(illumF!, "13.0", payload.moon.limb)} fill={CURVE} filter="url(#glow-live)" />
            </g>
          )}
          {riseSet}
        </g>
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

      {/* time control: scrub the focus; crossing an edge flips the night */}
      <div style={{ display: "flex", alignItems: "center", gap: 10, marginTop: 8, color: "#8C9BB4", fontSize: 12 }}>
        <button onClick={() => stepHours(-1)} aria-label="back one hour">◀ 1 h</button>
        <input type="range" min={noonW} max={noonEndW} step={60}
          value={Number.isNaN(focusSecV) ? noonW : focusSecV}
          onChange={(e) => setFocusSec(Number(e.target.value))}
          style={{ flex: 1, accentColor: "#E2555A" }} aria-label="time of night" />
        <button onClick={() => stepHours(1)} aria-label="forward one hour">1 h ▶</button>
        {!isLive && (
          <button onClick={() => setScrub(null)} style={{ padding: "4px 10px" }}>⏱ live</button>
        )}
        <span>{isLive ? "tracking live clock" : `scrubbed to ${focusLabel}`}</span>
      </div>
    </div>
  );
}
