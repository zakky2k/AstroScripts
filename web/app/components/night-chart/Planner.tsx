"use client";

// The planner front end: location + date controls (FR1/FR2), the fixed-scale
// night chart, and the FR6 summary strip. Scrolling the date is a pure
// translation of bands and moon curve — the frame never moves.

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { SummaryStrip } from "@/components/night-chart/SummaryStrip";
import { LiveNightChart } from "@/components/night-chart/LiveNightChart";
import { ApiFailure, fetchNight } from "@/lib/api";
import { nearestZone, zoneOptions } from "@/lib/chart/timezones";
import { DEFAULT_LOCATION, loadLocation, saveLocation, shiftDate, todayInZone, type Location } from "@/lib/settings";
import type { NightPayload } from "@/lib/types";

// A night payload is ~54 KB of astropy output and is completely deterministic
// for a (date, lat, lon, tz) query, so the 1-2 s stepper wait is a cost worth
// paying only once per night. This module-scope map is that second payment:
// stepping back onto a visited night is a hit and swaps with no delay at all.
// It is capped, because a long flick through the calendar would otherwise
// keep every 54 KB payload alive forever; eviction is oldest-first (a Map keeps
// insertion order and cacheNight re-inserts hits, so the victim is the least
// recently used entry rather than a random one).
const CACHE_LIMIT = 30;
const nightCache = new Map<string, NightPayload>();

// Queries that are still running, so a prefetch that has not landed by the time
// the user steps onto that night is awaited instead of recomputed server-side.
const nightInflight = new Map<string, Promise<NightPayload>>();

const nightKey = (l: Location, d: string): string => `${d}|${l.lat}|${l.lon}|${l.tz}`;

function cacheNight(key: string, payload: NightPayload): void {
  nightCache.delete(key);
  nightCache.set(key, payload);
  while (nightCache.size > CACHE_LIMIT) {
    const oldest = nightCache.keys().next();
    if (oldest.done) break;
    nightCache.delete(oldest.value);
  }
}

/** Resolve the night for (l, d) from cache, or from one shared network call. */
function ensureNight(l: Location, d: string): Promise<NightPayload> {
  const key = nightKey(l, d);
  const cached = nightCache.get(key);
  if (cached) return Promise.resolve(cached);
  const started = nightInflight.get(key);
  if (started) return started;
  const run = fetchNight({ date: d, lat: l.lat, lon: l.lon, tz: l.tz })
    .then((payload) => {
      cacheNight(key, payload);
      return payload;
    })
    .finally(() => {
      nightInflight.delete(key);
    });
  nightInflight.set(key, run);
  return run;
}

// requestIdleCallback is absent in some Safari builds, hence the optional
// members and the setTimeout(..., 0) fallback in the prefetch effect.
interface IdleWindow {
  requestIdleCallback?: (cb: () => void, opts?: { timeout?: number }) => number;
  cancelIdleCallback?: (handle: number) => void;
}

export function Planner() {
  const [loc, setLoc] = useState<Location>(DEFAULT_LOCATION);
  const [date, setDate] = useState<string>("");
  const [payload, setPayload] = useState<NightPayload | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Hydrate from localStorage on mount only, so the server render matches.
  useEffect(() => {
    const saved = loadLocation();
    setLoc(saved);
    setDate(todayInZone(saved.tz));
  }, []);

  // Generation counter: an awaited payload may only paint while nothing newer
  // has started, so a slow response can never overwrite the night the user has
  // already stepped on to, and cannot clear/resurrect `busy` behind its back.
  const generation = useRef(0);
  // Which (loc, date) the payload currently on screen belongs to; the prefetch
  // effect uses it to tell "this night rendered" from a stale render.
  const shownKey = useRef("");

  const load = useCallback(async (l: Location, d: string) => {
    if (!d) return;
    const gen = ++generation.current;
    const key = nightKey(l, d);
    const cached = nightCache.get(key);
    if (cached) {
      // The wait was already paid for: paint immediately and never raise
      // `busy`, which is exactly what makes a stepper press feel instant.
      setError(null);
      setBusy(false);
      shownKey.current = key;
      setPayload(cached);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const p = await ensureNight(l, d);
      if (gen !== generation.current) return;
      shownKey.current = key;
      setPayload(p);
    } catch (e) {
      // Keep the previous payload on screen only for the initial load failure;
      // a failed night-flip otherwise just leaves the current chart in place.
      if (gen !== generation.current) return;
      setError(e instanceof ApiFailure ? `${e.code}: ${e.message}` : String(e));
    } finally {
      if (gen === generation.current) setBusy(false);
    }
  }, []);

  useEffect(() => {
    if (date) void load(loc, date);
  }, [loc, date, load]);

  // Prefetch: the neighbouring nights are what the stepper asks for next, and
  // their latency is pure server-side computation, so buy them with idle time
  // after the current night has rendered — the next press is then a cache hit.
  // It runs off the critical path (requestIdleCallback when available) and
  // never touches busy/error: a failed prefetch is invisible and just falls
  // back to the normal load path.
  useEffect(() => {
    if (!payload || !date) return;
    // Skip when the payload on screen is not this (loc, date): the location or
    // date changed mid-flight, so these nights are no longer the neighbours.
    if (shownKey.current !== nightKey(loc, date)) return;
    const gen = generation.current;
    let cancelled = false;
    const alive = () => !cancelled && gen === generation.current;
    const swallow = (p: Promise<NightPayload>) => p.catch(() => undefined);
    const near = [shiftDate(date, 1), shiftDate(date, -1)];
    const far = [shiftDate(date, 2), shiftDate(date, -2)];
    const run = () => {
      if (!alive()) return;
      // Near neighbours first, ±2 only once those have landed: firing four
      // 54 KB astropy jobs at once would slow the very press we are smoothing.
      void Promise.all(near.map((d) => swallow(ensureNight(loc, d)))).then(() => {
        if (!alive()) return;
        for (const d of far) swallow(ensureNight(loc, d));
      });
    };
    const w = window as unknown as IdleWindow;
    if (typeof w.requestIdleCallback === "function") {
      const handle = w.requestIdleCallback(run, { timeout: 2000 });
      return () => {
        cancelled = true;
        w.cancelIdleCallback?.(handle);
      };
    }
    const handle = window.setTimeout(run, 0);
    return () => {
      cancelled = true;
      window.clearTimeout(handle);
    };
  }, [payload, loc, date]);

  // Editable focus time: mirrors the chart's focus (with seconds) and accepts
  // HH:MM[:SS] or h:mm AM/PM. The "24" button inside the box flips 12 h <->
  // 24 h display for every clock on screen.
  const [timeDraft, setTimeDraft] = useState("");
  const [h12, setH12] = useState(false);
  // "DARK" beside TIME: red sky scheme at identical lightness (B->R swap).
  const [dark, setDark] = useState(false);
  // Headless-review hook: ?dark=1 boots straight into that state.
  // The chart is a sibling rendered AFTER Planner, so its window listeners do
  // not exist yet when Planner's mount effect runs; wait for the payload to
  // arrive (chart mounted + hydrated) before dispatching to it.
  useEffect(() => {
    if (!payload) return;
    const q = new URLSearchParams(window.location.search);
    if (q.get("dark") === "1") {
      setDark(true);
      document.documentElement.dataset.scheme = "red";
      window.dispatchEvent(new CustomEvent("planner:toggle-dark", { detail: true }));
    }
  }, [payload]);
  useEffect(() => {
    const on = (e: Event) => setDate((e as CustomEvent<string>).detail);
    const onFocus = (e: Event) => {
      const detail = (e as CustomEvent<string>).detail;
      // Only mirror the focus time while the user isn't typing in the field.
      if (document.activeElement instanceof HTMLInputElement &&
          document.activeElement.dataset.role === "focus-time") return;
      setTimeDraft(detail);
    };
    window.addEventListener("planner:date", on);
    window.addEventListener("planner:focus", onFocus);
    return () => {
      window.removeEventListener("planner:date", on);
      window.removeEventListener("planner:focus", onFocus);
    };
  }, []);

  const commitTime = () => {
    if (timeDraft.trim() === "") return;
    window.dispatchEvent(new CustomEvent("planner:set-time", { detail: timeDraft.trim() }));
  };

  const patch = (p: Partial<Location>) => {
    const next = { ...loc, ...p };
    setLoc(next);
    saveLocation(next);
  };

  // ZONE as a dropdown of real IANA zones (review 24 Nov, round 7, item 4).
  // The offset printed in each entry is the one that zone is USING on the
  // night on screen, so a June list reads `Europe/Prague · UTC+2` and a
  // November one `UTC+1` — the DST behaviour the reviewer asked to keep
  // (item 3) comes from the zone itself, not from a number we maintain.
  // All of it is static data plus Intl: no geo service, no network call.
  const nightMs = date ? Date.parse(`${date}T12:00:00Z`) : Date.now();
  const zoneChoices = useMemo(() => zoneOptions(nightMs, loc.tz), [nightMs, loc.tz]);

  // "Link the entered location to automatically choose the relevant offset":
  // committing lat/lon picks the nearest zone in the table. Only on commit
  // (blur / Enter), so typing a coordinate never fights the user mid-edit,
  // and a zone chosen by hand afterwards stays put.
  const applyNearestZone = () => {
    if (!Number.isFinite(loc.lat) || !Number.isFinite(loc.lon)) return;
    const z = nearestZone(loc.lat, loc.lon);
    if (z !== loc.tz) patch({ tz: z });
  };

  return (
    <main className="page">
      <header className="controls">
        <h1>AstroScripts planner</h1>
        <div className="control-row">
          <label>
            Lat
            <input type="number" step="0.0001" min={-90} max={90} value={loc.lat}
              onChange={(e) => patch({ lat: Number(e.target.value) })}
              onBlur={applyNearestZone}
              onKeyDown={(e) => { if (e.key === "Enter") applyNearestZone(); }} />
          </label>
          <label>
            Lon
            <input type="number" step="0.0001" min={-180} max={180} value={loc.lon}
              onChange={(e) => patch({ lon: Number(e.target.value) })}
              onBlur={applyNearestZone}
              onKeyDown={(e) => { if (e.key === "Enter") applyNearestZone(); }} />
          </label>
          <label>
            {/* Round 7 removed the "+/-1H" DST nudge and everything behind it: the
                local clock comes straight off the IANA zone, which owns the
                summer/winter switch itself. The zone is now picked from a list of
                real zones ordered by the offset each one uses on THIS night
                (west-to-east, so UTC sits mid-list) and auto-selected from the
                lat/lon above — the planner's API takes an IANA name, so a bare
                numeric offset could not be used and could not follow DST. */}
            Zone <span className="hint">(offset for this night)</span>
            <select value={loc.tz} style={{ width: 210 }}
              onChange={(e) => patch({ tz: e.target.value })}>
              {zoneChoices.map((z) => (
                <option key={z.value} value={z.value}>{z.label}</option>
              ))}
            </select>
          </label>
          <label>
            Night of
            <input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </label>
          <div className="stepper">
            <button onClick={() => setDate((d) => shiftDate(d, -1))} aria-label="previous night">◀</button>
            <button onClick={() => { setDate(todayInZone(loc.tz)); setTimeDraft(""); }}>tonight</button>
            <button onClick={() => setDate((d) => shiftDate(d, 1))} aria-label="next night">▶</button>
          </div>
          <label className="time-box">
            {/* Round 7: the "UTC" suffix is off every clock on the chart, so the
                scale is named ONCE, here — the only local clock on screen. */}
            Local Time <span className="hint">(All chart elements UTC)</span>
            <span className="time-wrap">
              <input type="text" data-role="focus-time" value={timeDraft} placeholder="--:--:--"
                size={h12 ? 10 : 8} spellCheck={false} inputMode="numeric"
                onChange={(e) => setTimeDraft(e.target.value)}
                onKeyDown={(e) => { if (e.key === "Enter") commitTime(); }}
                onBlur={commitTime} />
              <button className="h-toggle" title="toggle 12 h / 24 h display"
                onClick={() => { setH12((v) => !v); window.dispatchEvent(new Event("planner:toggle-h")); }}>
                {h12 ? "24" : "12"}
              </button>
            </span>
          </label>
          {/* DARK: red sky scheme at identical lightness (B channel -> R).
              Fixed width so the row never shifts when it toggles. */}
          <button className="dark-toggle" title="switch the sky palette to red (same lightness)"
            style={{ opacity: dark ? 1 : 0.6 }}
            onClick={() => {
              const next = !dark;
              setDark(next);
              document.documentElement.dataset.scheme = next ? "red" : "blue";
              window.dispatchEvent(new CustomEvent("planner:toggle-dark", { detail: next }));
            }}>
            DARK
          </button>
        </div>
      </header>

      {error && (
        <p className="error" role="alert">
          {error}
          {error.includes("fetch") && " — is the planner API running?  .venv\\Scripts\\python.exe -m api.server"}
        </p>
      )}
      {busy && !payload && <p className="muted">computing ephemeris…</p>}

      {payload && (
        <>
          <SummaryStrip payload={payload} />
          <div className="chart-wrap">
            <LiveNightChart payload={payload} tz={loc.tz} />
          </div>
        </>
      )}
    </main>
  );
}
