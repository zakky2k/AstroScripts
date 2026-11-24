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
// - A focus time drives the moon glyph; in "live" mode it tracks the clock.
//   Scrubbing and ±1 h steps NEVER change the night — they move within the
//   fixed noon→noon+1 frame only (review 23 Nov, round 3).
//
// This file is the composition layer only. The laws it renders are kept as
// pure, separately testable functions:
//   lib/chart/geom       frame + pixel scales        lib/chart/sunbands   bands
//   lib/chart/clock      wall-clock text + focus     lib/chart/moonchips  rise/set chips
//   lib/chart/scrub      the scrubbing laws          lib/chart/moontrack  arc + glyph pinning
//   lib/chart/labels     transition + header text
// Gates: scripts/golden.ts pins this markup token-for-token, scripts/units.ts
// pins the laws those modules implement.

import { useEffect, useRef, useState } from "react";
import { terminator } from "@/components/night-chart/NightChart";
import { CURVE, NOW, bandsOf, chipBgOf, dayFillOf, edgeLineOf, glyphBgOf, gridOf, inkBrightOf, pageBgOf } from "@/lib/palette";
import type { Band, NightPayload } from "@/lib/types";
import {
  FLOOR_Y, H, M_LEFT, M_TOP, PLOT_H, PLOT_W, W, altOfY, frameOf, gridAlts, interp,
  isMajorGrid, secOfX, xOf, yOf, type Frame,
} from "@/lib/chart/geom";
import { fmtClock, focusAt, focusSec, hhmm, parseClock, shiftDayStamp, withZoneOffset, type Focus } from "@/lib/chart/clock";
import { clampFrame, focusEpoch, inFrameOf, liveSec, scrubFocus, stepHoursFocus, typedFocus } from "@/lib/chart/scrub";
import { bandItemsOf } from "@/lib/chart/sunbands";
import { LABEL_FS, TIP_FS, headerTitle, midnightLabel, tipWidth, transitionLabelsOf } from "@/lib/chart/labels";
import { crossingDotY, moonChipsOf, type MoonChip } from "@/lib/chart/moonchips";
import { arcPointsOf, glyphSecOf, moonSeriesOf, riseForFocusOf } from "@/lib/chart/moontrack";

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
  // "DARK" scheme toggle (lives in Planner): sky blues -> reds at the same
  // lightness (B channel swapped into R). Moon ivory stays put.
  const [dark, setDark] = useState(false);
  // Whole-hour clock offset from the ZONE box ("+/-1H" button), for zones whose
  // daylight-saving state disagrees with the IANA zone (e.g. a browser that
  // is one hour fast and needs to fall back). Applies to LIVE time only —
  // ephemeris events stay on the true zone.
  const [tzOffsetH, setTzOffsetH] = useState(0);
  // Set whenever the ZONE offset changes so the "current time" readouts (the
  // TIME box above all) update in the SAME paint as the button press — not
  // one render later via the draft-sync effect.
  const [clockTick, setClockTick] = useState(0);
  // Scrubbed focus as epoch seconds — kept in sync with `scrub` (the wall
  // parts) so hour steps can always read the CURRENT scrub position, even
  // in the same click sequence where React state has not flushed yet. This
  // is what fixes "−1 h does nothing" at the left edge.
  const scrubSecRef = useRef<number | null>(null);
  const [scrub, setScrub] = useState<Focus | null>(null);
  // Mirror for event handlers / effects: they must read the CURRENT scrub,
  // not the one captured at render time (the planner:set-time listener and
  // the night-flip effect both acted on stale state and ping-ponged the
  // focus between two nights — React's "Maximum update depth" error).
  const scrubRef = useRef<Focus | null>(null);
  const svgRef = useRef<SVGSVGElement | null>(null);
  const shiftRef = useRef(false);
  const [timeDraft, setTimeDraft] = useState("");

  // Minute-tick: re-sample the wall clock just after each minute boundary, so
  // the readout and the now line advance once per minute (no seconds shown).
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
  // Header controls live in Planner and talk to the chart over window events.
  useEffect(() => {
    const on = () => setH12((v) => !v);
    const onDark = (e: Event) => setDark(Boolean((e as CustomEvent<boolean>).detail));
    const onOff = (e: Event) => {
      setTzOffsetH((e as CustomEvent<number>).detail);
      setClockTick((t) => t + 1); // force the time readout to refresh this paint
    };
    window.addEventListener("planner:toggle-h", on);
    window.addEventListener("planner:toggle-dark", onDark);
    window.addEventListener("planner:tz-offset", onOff);
    return () => {
      window.removeEventListener("planner:toggle-h", on);
      window.removeEventListener("planner:toggle-dark", onDark);
      window.removeEventListener("planner:tz-offset", onOff);
    };
  }, []);

  const setScrubBoth = (f: Focus | null) => { scrubRef.current = f; setScrub(f); };

  // Frame edges in float seconds (see §4.1): payload.frameNoon is the pivot
  // NOON; the right edge is wall-clock noon+1, derived from the ISO literal so
  // DST landings are exact.
  const frame: Frame = frameOf(payload);
  const { noonW, noonEndW, midSec } = frame;

  // Scrub focus shifted by the ZONE box's whole-hour offset: everything
  // downstream (frame checks, slider value, now line) works in offset
  // wall-clock space. The ephemeris events themselves stay on the true zone.
  const scrubOff = scrub === null ? null : withZoneOffset(scrub, tzOffsetH);

  // Keep the epoch mirror of the scrub in sync (null = live mode).
  if (scrub === null) scrubSecRef.current = null;
  else if (scrubOff) scrubSecRef.current = focusSec(scrubOff);

  // Re-arm live mode whenever a NEW night arrives. Keyed on the frame literal
  // (a stable string) rather than the payload object, so no re-render can
  // re-trigger it — an object dependency churned and hit React's "Maximum
  // update depth exceeded" guard during night flips. shiftNight already cleared
  // the scrub, so this simply releases the flip latch.
  useEffect(() => {
    setScrubBoth(null); shiftRef.current = false;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [payload.frameNoon]);

  const live = liveSec(tickMs, tz);
  // The focus instant the moon + marker follow.
  const focusSecV = focusEpoch(scrubOff ? focusSec(scrubOff) : null, live, frame);
  const inFrame = inFrameOf(focusSecV, frame);
  const isLive = scrub === null;

  // Night-shift to the neighbouring night. ONLY the explicit day controls do
  // this (the header ◀ / ▶ stepper, or "tonight"): review 23 Nov (round 3) —
  // scrubbing and hour steps must NEVER change the chart on their own. The
  // parent re-fetches and keeps the previous payload on screen until the new
  // one lands, so the chart never unmounts mid-flip; the incoming night
  // re-arms live focus.
  function shiftNight(dir: 1 | -1) {
    if (shiftRef.current) return; // ignore repeat flips until swap lands
    shiftRef.current = true;
    setScrubBoth(null); // re-arm live focus for the incoming night
    window.dispatchEvent(new CustomEvent("planner:date", {
      detail: shiftDayStamp(payload.nightStart.slice(0, 10), dir),
    }));
  }

  // Move the scrubbed focus by whole hours (the −1 h / +1 h buttons). Review
  // 23 Nov round 3: this moves time within the FIXED frame only — no day
  // change, ever. At a frame edge it simply stops.
  function stepHours(delta: number) {
    // Read the CURRENT scrub position from refs, never from this render's
    // state: two presses in one click sequence (or a press right after a
    // slider drag) would otherwise both start from the same stale focus and
    // −1 h would look dead — review 23 Nov round 3, item 1. From LIVE the
    // base is the clock itself, so −1 h always lands a full hour back.
    const base = scrubSecRef.current ?? (Number.isNaN(live) ? noonW : live);
    setScrubBoth(stepHoursFocus(base, delta, frame, tz));
  }

  // Slider drag: hard-clamped to the frame (noon → noon+1). It NEVER flips
  // the night and never parks at moonset — the thumb spans the whole day, and
  // the position snaps to the MINUTE grid so it always matches the clock.
  const setScrubRaw = (rawSec: number) => setScrubBoth(scrubFocus(rawSec, frame, tz));

  // Editable time field (HH:MM[:SS], 12 h AM/PM or 24 h): jumps the focus
  // within this night. Reports the focus to the header field and accepts
  // edits from it. (timeDraft state is declared with the other hooks above.)
  // clockTick re-syncs immediately when the ZONE "+/-1H" offset changes, so
  // the current-time readout moves in the same paint as the button press.
  useEffect(() => {
    if (inFrame) setTimeDraft(fmtClock(focusSecV, tz, h12));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [focusSecV, inFrame, tz, h12, clockTick]);
  useEffect(() => {
    window.dispatchEvent(new CustomEvent("planner:focus", { detail: timeDraft }));
  }, [timeDraft]);
  useEffect(() => {
    const on = (e: Event) => {
      const c = parseClock((e as CustomEvent<string>).detail.trim());
      if (!c) return;
      // Wall-clock time on whichever calendar day the focus currently sits on;
      // typedFocus folds it into this night — a typed time never flips nights.
      const day = focusAt(inFrame ? focusSecV : noonW, tz);
      setScrubBoth(typedFocus(c, day, frame, tz));
    };
    window.addEventListener("planner:set-time", on);
    return () => window.removeEventListener("planner:set-time", on);
  }, [focusSecV, inFrame, noonW, noonEndW, tz, h12]);

  // ---- palette ----------------------------------------------------------
  // Review 23 Nov: ALL chart text uses ONE colour per scheme — the bright ink.
  const INK = inkBrightOf(dark);
  const GRID = gridOf(dark);
  const BANDS = bandsOf(dark), DAY_FILL = dayFillOf(dark), PAGE_BG = pageBgOf(dark);
  const CHIP_BG = chipBgOf(dark), EDGE_LINE = edgeLineOf(dark), GLYPH_BG = glyphBgOf(dark);

  // ---- sun --------------------------------------------------------------
  const sunSeries = payload.sun.series;
  const sunTimes = sunSeries.map((s) => Date.parse(s.time) / 1000);
  const sunAlt = sunSeries.map((s) => s.altitude);
  const altAt = (sec: number) => interp(sunTimes, sunAlt, sec) ?? 0;

  // ---- moon -------------------------------------------------------------
  const moon = moonSeriesOf(payload, frame);
  // Moon state at the focus instant.
  const altF = inFrame ? interp(moon.times, moon.alts, focusSecV) : null;
  const illumF = inFrame ? interp(moon.times, moon.illum, focusSecV) : null;
  // The glyph is PINNED to the arc: whenever the focus sits where the moon is
  // below the horizon, it parks at the nearest rise/set point ON the curve
  // instead of drifting along a straight chord.
  const glyphSec = glyphSecOf(moon, focusSecV, inFrame);
  const glyphAlt = glyphSec !== null ? interp(moon.times, moon.alts, glyphSec) : null;
  const moonUp = glyphAlt !== null && glyphAlt > 0;

  // ---- derived layout ---------------------------------------------------
  const bandItems = bandItemsOf(payload, frame, altAt);
  const transLabels = transitionLabelsOf(payload, frame, tz);
  const chips: MoonChip[] = moonChipsOf(payload, frame, tz);
  const above = arcPointsOf(moon, frame);
  const midIso = midnightLabel(payload);
  const title = headerTitle(payload);

  const focusLabel = inFrame ? fmtClock(focusSecV, tz, h12) : "";
  const riseForFocus = riseForFocusOf(payload, frame, focusSecV, inFrame);
  const liveReadout = inFrame && illumF !== null
    ? `${focusLabel} · ${(illumF * 100).toFixed(0)}% lit` +
      (moonUp ? ` · alt ${glyphAlt!.toFixed(1)}°` : " · below horizon") +
      (riseForFocus !== null ? ` · moonrise ${fmtClock(riseForFocus, tz, h12)}` : "")
    : "";

  function onMove(e: React.MouseEvent<SVGSVGElement>) {
    const svg = svgRef.current;
    if (!svg) return;
    const r = svg.getBoundingClientRect();
    const sx = ((e.clientX - r.left) * W) / r.width;
    const sy = ((e.clientY - r.top) * H) / r.height;
    if (sx < M_LEFT || sx > M_LEFT + PLOT_W || sy < M_TOP || sy > M_TOP + PLOT_H) { setHover(null); return; }
    setHover({ x: sx, y: sy, sec: secOfX(frame, sx), alt: Math.max(0, altOfY(sy)) });
  }

  const tip = (() => {
    if (!hover) return null;
    const label = `${hhmm(hover.sec, tz)}  ·  ${hover.alt.toFixed(1)}°`;
    const wTip = tipWidth(label);
    let tx = hover.x + 12;
    if (tx + wTip > M_LEFT + PLOT_W) tx = hover.x - 12 - wTip;
    const ty = Math.max(M_TOP + 6, hover.y - 30);
    return (
      <g pointerEvents="none">
        <line x1={hover.x} y1={M_TOP} x2={hover.x} y2={M_TOP + PLOT_H} stroke={INK} strokeOpacity={0.35} strokeDasharray="2 4" />
        <line x1={M_LEFT} y1={hover.y} x2={M_LEFT + PLOT_W} y2={hover.y} stroke={INK} strokeOpacity={0.35} strokeDasharray="2 4" />
        <rect x={tx} y={ty} width={wTip} height={22} rx={4} fill={GLYPH_BG} stroke={CURVE} strokeOpacity={0.35} />
        <text x={tx + 7} y={ty + 15} fontSize={TIP_FS} fill={INK}>{label}</text>
      </g>
    );
  })();

  return (
    <div>
      {/* time control ABOVE the chart: scrub the focus. The slider spans the
          FULL fixed frame — 12:00 on the left to 12:00+1 on the right — and
          scrubbing NEVER changes the night (review 23 Nov, round 3); only
          the header day stepper does. Hour steps are plain −1 h / +1 h time
          moves, clamped at the frame edges. Every row item has a FIXED width
          (buttons + status text) and the slider flexes, so nothing under the
          cursor ever moves — repeated "+1 h" clicking keeps the pointer
          exactly where it belongs. Buttons use the app's standard palette
          (globals.css), not custom inline colours. */}
      <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 8, color: INK, fontSize: 12 }}>
        <button onClick={() => stepHours(-1)} aria-label="back one hour" style={{ width: 62, flexShrink: 0 }}>−1 h</button>
        <input type="range" min={noonW} max={noonEndW} step={1}
          value={Number.isNaN(focusSecV) ? noonW : clampFrame(focusSecV, frame)}
          onChange={(e) => setScrubRaw(Number(e.target.value))}
          style={{ flex: 1 }} aria-label="time of night" />
        <button onClick={() => stepHours(1)} aria-label="forward one hour" style={{ width: 62, flexShrink: 0 }}>+1 h</button>
        {/* live button is ALWAYS present (disabled while live) so the hour
            buttons never move under the cursor during repeated clicking */}
        <button onClick={() => setScrubBoth(null)} disabled={isLive}
          style={{ width: 78, flexShrink: 0, opacity: isLive ? 0.45 : 1 }}>⏱ live</button>
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
        <g clipPath="url(#plot-live)">
          {bandItems.map((it) =>
            it.kind === "day" ? (
              <rect key={it.key} x={it.x} y={M_TOP} width={it.w} height={PLOT_H} fill={DAY_FILL} opacity={0.42} />
            ) : it.kind === "band" ? (
              <rect key={it.key} x={it.x} y={M_TOP} width={it.w} height={PLOT_H} fill={`url(#b-${it.band}-${it.grad}-live)`} />
            ) : (
              <line key={it.key} x1={it.x} y1={M_TOP} x2={it.x} y2={FLOOR_Y} stroke={EDGE_LINE} strokeOpacity={0.13} />
            ),
          )}
        </g>
        <g>
          {gridAlts().map((alt) => (
            <g key={`g${alt}`}>
              <line x1={M_LEFT} y1={yOf(alt)} x2={M_LEFT + PLOT_W} y2={yOf(alt)}
                stroke={GRID} strokeOpacity={isMajorGrid(alt) ? 0.3 : 0.16} strokeDasharray={isMajorGrid(alt) ? "5 6" : "1 6"} />
              <text x={M_LEFT - 10} y={yOf(alt) + 4} textAnchor="end" fontSize={11} fill={INK}>{alt}°</text>
            </g>
          ))}
        </g>
        <g>
          {transLabels.map((t) => (
            <g key={t.key}>
              <text x={t.ax} y={t.anchorY} fontSize={LABEL_FS} fill={INK} opacity={0.9}
                transform={`rotate(${t.rot} ${t.ax} ${t.anchorY})`} textAnchor={t.textAnchor}>{t.label}</text>
              {/* leader: middle of last letter → up along the transition line */}
              <line x1={t.ax} y1={t.anchorY - 2} x2={t.x} y2={FLOOR_Y - 6}
                stroke={INK} strokeOpacity={0.5} strokeWidth={0.8} />
            </g>
          ))}
          <line x1={xOf(frame, midSec)} y1={M_TOP} x2={xOf(frame, midSec)} y2={M_TOP + PLOT_H}
            stroke={GRID} strokeOpacity={0.3} strokeDasharray="4 5" />
          <text x={xOf(frame, midSec)} y={M_TOP - 6} textAnchor="middle" fontSize={10} fill={INK}>{midIso}</text>
        </g>
        <g clipPath="url(#plot-live)">
          {above && <polyline points={above} fill="none" stroke={CURVE} strokeWidth={1.8}
            strokeOpacity={0.55} strokeLinejoin="round" />}
          {moonUp && glyphSec !== null && (
            <g transform={`translate(${xOf(frame, glyphSec)},${yOf(glyphAlt!)})`}>
              <circle r={13} fill={GLYPH_BG} stroke={CURVE} strokeOpacity={0.4} />
              <path d={terminator(illumF ?? interp(moon.times, moon.illum, glyphSec) ?? 1, "13.0", payload.moon.limb)}
                fill={CURVE} filter="url(#glow-live)" />
            </g>
          )}
        </g>
        {/* moonrise/moonset tick labels live BELOW the horizon like the
            transition timestamps, so they must not be plot-clipped */}
        <g>
          {chips.map((c) => (
            <g key={c.key}>
              <line x1={c.lx} y1={c.by + c.h} x2={c.x} y2={FLOOR_Y - 3}
                stroke={CURVE} strokeOpacity={0.75} strokeWidth={0.9} />
              {/* exact crossing dot: where the moon path reaches zero */}
              <circle cx={c.x} cy={crossingDotY()} r={2.6} fill={CURVE} fillOpacity={0.9} />
              <rect x={c.bx} y={c.by} width={c.w} height={c.h} rx={3} fill={CHIP_BG} fillOpacity={0.92}
                stroke={CURVE} strokeOpacity={0.45} strokeWidth={0.7} />
              <text x={c.bx + c.w / 2} y={c.by + c.h - 4.5} fontSize={LABEL_FS} fill={INK} textAnchor="middle">{c.label}</text>
            </g>
          ))}
        </g>
        {/* focus marker: the live now line, or the scrubbed-time handle */}
        {inFrame && (
          <g clipPath="url(#plot-live)">
            <line x1={xOf(frame, focusSecV)} y1={M_TOP} x2={xOf(frame, focusSecV)} y2={M_TOP + PLOT_H}
              stroke={NOW} strokeWidth={1.3} strokeDasharray="4 4" />
            <text x={xOf(frame, focusSecV) + 5} y={M_TOP + PLOT_H - 16} fontSize={10} fill={NOW}>
              {isLive ? "now " : ""}{focusLabel}
            </text>
          </g>
        )}
        {tip}
        <text x={M_LEFT} y={24} fontSize={13} fontWeight={600} fill={INK}>{title}</text>
        {liveReadout && (
          <text x={M_LEFT + PLOT_W - 34} y={24} textAnchor="end" fontSize={12} fill={INK}>{liveReadout}</text>
        )}
      </svg>
    </div>
  );
}
