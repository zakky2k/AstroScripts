"use client";

// Live night chart — the realtime view of the same fixed noon → midnight →
// noon+1 frame the design review locked (docs/REQUIREMENTS.md §4.1). The
// static design render (NightChart.tsx) stays frozen as the M1 reference and
// the parity gate; this component changes only how time is *experienced*:
// - The whole two-day frame is drawn at identical pixel geometry — day, then
//   sunset → twilight → darkest at centre → twilight → sunrise, then day. The
//   vertical band gradient carries the sun entirely (no extra bars).
// - ONE moon glyph sits at its current altitude on the curve, interpolated
//   from the 10-minute ephemeris samples and advanced every minute, so it
//   visibly travels along its arc. The static hourly phase glyphs are gone.
// - The red now line tracks the observer's clock in real time and is visible
//   anywhere in the frame, not only inside the night window.

import { useEffect, useState } from "react";
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

export function LiveNightChart({ payload, tz }: { payload: NightPayload; tz: string }) {
  // Tick on the minute boundary so the moon advances in visible steps along
  // its curve and the now line tracks real time.
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

  const nowParts = tickMs !== null ? wallParts(tickMs, tz) : null;

  // Frame ruler in float seconds, identical to NightChart's: Date.parse of the
  // payload's offset-aware ISO strings is exactly render.py's instant maths.
  const noonW = Date.parse(payload.frameNoon) / 1000;
  const xOf = (sec: number) => M_LEFT + (PLOT_W * (sec - noonW)) / (2 * DAY_SEC);
  const yOf = (alt: number) => M_TOP + PLOT_H * (1.0 - Math.max(alt, 0.0) / Y_TOP);

  const samples = payload.moon.samples;
  const times = samples.map((s) => Date.parse(s.time) / 1000);
  const alts = samples.map((s) => s.altitude);
  const illum = samples.map((s) => s.illumFraction);

  const tW = Date.parse(payload.nightStart) / 1000;
  const tW1 = Date.parse(payload.nightEnd) / 1000;
  const midSec = noonW + DAY_SEC; // plot centre: midnight after the pivot night

  const nowSec = nowParts
    ? Date.UTC(nowParts.y, nowParts.mo - 1, nowParts.d, nowParts.h, nowParts.m) / 1000
    : NaN;
  const inFrame = !Number.isNaN(nowSec) && nowSec >= noonW && nowSec <= noonW + 2 * DAY_SEC;

  // Moon state at "now" — meaningful only while above the horizon.
  const altNow = inFrame ? interp(times, alts, nowSec) : null;
  const illumNow = inFrame ? interp(times, illum, nowSec) : null;
  const moonUp = altNow !== null && altNow > 0;

  // ---- bands: classify the sun series, merge runs (same rules as design) ----
  const sunSeries = payload.sun.series;
  const sunAlt = sunSeries.map((s) => s.altitude);
  const sunTimes = sunSeries.map((s) => Date.parse(s.time) / 1000);
  const bandRects: React.ReactNode[] = [];
  let bi = 0;
  while (bi < sunSeries.length) {
    const band = bandOf(sunAlt[bi]);
    let j = bi;
    while (j + 1 < sunSeries.length && bandOf(sunAlt[j + 1]) === band) j++;
    const a = bi === 0 ? tW : sunTimes[bi];
    const b = j + 1 < sunSeries.length ? sunTimes[j + 1] : tW1;
    const grad = a + (b - a) / 2 < midSec ? "dusk" : "dawn";
    bandRects.push(
      <rect key={`band${bi}`} x={xOf(a)} y={M_TOP} width={Math.max(xOf(b) - xOf(a), 0.5)}
        height={PLOT_H} fill={`url(#b-${band}-${grad}-live)`} />
    );
    if (band !== "civil" && bi > 0) {
      bandRects.push(
        <line key={`edge${bi}`} x1={xOf(a)} y1={M_TOP} x2={xOf(a)} y2={M_TOP + PLOT_H}
          stroke="#CBD5E1" strokeOpacity={0.13} />
      );
    }
    bi = j + 1;
  }

  // ---- grid: 10° intervals, fixed for every night ----
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

  // sunset / sunrise: tick + label only; the gradient is the sun.
  const sunTicks = ([
    [tW, payload.sun.sunset, "sunset"],
    [tW1, payload.sun.sunrise, "sunrise"],
  ] as const).map(([sec, iso, kind]) => (
    <g key={kind}>
      <line x1={xOf(sec)} y1={M_TOP + PLOT_H} x2={xOf(sec)} y2={M_TOP + PLOT_H + 5}
        stroke={INK_BRIGHT} strokeOpacity={0.8} />
      <text x={xOf(sec)} y={M_TOP + PLOT_H + 18} textAnchor="middle" fontSize={10}
        fill={INK_BRIGHT} opacity={0.9}>{iso.slice(11, 16)} {kind}</text>
    </g>
  ));

  // moonrise / moonset markers on the horizon (kept: §4.1 requires them)
  const riseSet: React.ReactNode[] = [];
  for (const [key, kind] of [["moonrise", "rise"], ["moonset", "set"]] as const) {
    payload.moon[key].forEach((iso, i) => {
      const s = Date.parse(iso) / 1000;
      if (s < noonW || s > noonW + 2 * DAY_SEC) return;
      riseSet.push(
        <text key={`${key}${i}`} x={xOf(s)} y={yOf(0) - 16} textAnchor="middle"
          fontSize={9} fill={INK}>{kind}</text>
      );
    });
  }

  // the arc the single glyph travels
  const above = times
    .map((s, i) => (alts[i] >= 0 ? `${xOf(s)},${yOf(alts[i])}` : null))
    .filter(Boolean)
    .join(" ");

  // ---- header ----
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

  const liveReadout = inFrame && illumNow !== null
    ? `${(illumNow * 100).toFixed(0)}% lit · limb ${payload.moon.limb}` +
      (moonUp ? ` · alt ${altNow!.toFixed(1)}°` : " · below horizon")
    : "";

  return (
    <svg xmlns="http://www.w3.org/2000/svg" width={W} height={H} viewBox={`0 0 ${W} ${H}`}
      fontFamily="Inter, Segoe UI, sans-serif" role="img" style={{ width: "100%", height: "auto" }}>
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
        {sunTicks}
        <line x1={xOf(midSec)} y1={M_TOP} x2={xOf(midSec)} y2={M_TOP + PLOT_H}
          stroke={GRID} strokeOpacity={0.3} strokeDasharray="4 5" />
        <text x={xOf(midSec)} y={M_TOP - 6} textAnchor="middle" fontSize={10} fill={INK_BRIGHT}>{midIso}</text>
      </g>
      <g clipPath="url(#plot-live)">
        {above && <polyline points={above} fill="none" stroke={CURVE} strokeWidth={1.8}
          strokeOpacity={0.55} strokeLinejoin="round" />}
        {/* one live moon glyph, interpolated along the curve, eased between minutes */}
        {moonUp && (
          <g transform={`translate(${xOf(nowSec)},${yOf(altNow!)})`}
            style={{ transition: "transform 1s linear" }}>
            <circle r={13} fill="#161D2E" stroke={CURVE} strokeOpacity={0.4} />
            <path d={terminator(illumNow!, "13.0", payload.moon.limb)} fill={CURVE} filter="url(#glow-live)" />
          </g>
        )}
        {riseSet}
      </g>
      {/* live now line, visible anywhere in the frame */}
      {inFrame && (
        <g clipPath="url(#plot-live)">
          <line x1={xOf(nowSec)} y1={M_TOP} x2={xOf(nowSec)} y2={M_TOP + PLOT_H}
            stroke={NOW} strokeWidth={1.3} strokeDasharray="4 4" />
          <text x={xOf(nowSec) + 5} y={M_TOP + PLOT_H - 16} fontSize={10} fill={NOW}>
            now {String(nowParts!.h).padStart(2, "0")}:{String(nowParts!.m).padStart(2, "0")}
          </text>
        </g>
      )}
      <text x={M_LEFT} y={24} fontSize={13} fontWeight={600} fill={INK_BRIGHT}>{title}</text>
      {liveReadout && (
        <text x={M_LEFT + PLOT_W - 34} y={24} textAnchor="end" fontSize={12} fill={INK}>{liveReadout}</text>
      )}
    </svg>
  );
}
