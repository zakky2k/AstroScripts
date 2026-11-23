"use client";

// The planner front end: location + date controls (FR1/FR2), the fixed-scale
// night chart, and the FR6 summary strip. Scrolling the date is a pure
// translation of bands and moon curve — the frame never moves.

import { useCallback, useEffect, useMemo, useState } from "react";
import { NightChart, parseLocal, type LocalTime } from "@/components/night-chart/NightChart";
import { SummaryStrip } from "@/components/night-chart/SummaryStrip";
import { ApiFailure, fetchNight } from "@/lib/api";
import { DEFAULT_LOCATION, loadLocation, saveLocation, shiftDate, todayInZone, type Location } from "@/lib/settings";
import type { NightPayload } from "@/lib/types";

export function Planner() {
  const [loc, setLoc] = useState<Location>(DEFAULT_LOCATION);
  const [date, setDate] = useState<string>("");
  const [payload, setPayload] = useState<NightPayload | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [now, setNow] = useState<LocalTime | null>(null);

  // Hydrate from localStorage on mount only, so the server render matches.
  useEffect(() => {
    const saved = loadLocation();
    setLoc(saved);
    setDate(todayInZone(saved.tz));
  }, []);

  const load = useCallback(async (l: Location, d: string) => {
    if (!d) return;
    setBusy(true);
    setError(null);
    try {
      const p = await fetchNight({ date: d, lat: l.lat, lon: l.lon, tz: l.tz });
      setPayload(p);
      // "now" in the observer's wall clock for the now line. sv-SE prints
      // "YYYY-MM-DD HH:MM:SS"; the "+00:00" is a dummy zone — parseLocal
      // reads the literal wall-clock fields, so the browser zone never leaks
      // into displayed times.
      const iso = new Intl.DateTimeFormat("sv-SE", {
        timeZone: l.tz, year: "numeric", month: "2-digit", day: "2-digit",
        hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false,
      }).format(new Date());
      setNow(parseLocal(`${iso.replace(" ", "T")}+00:00`));
    } catch (e) {
      setPayload(null);
      setError(e instanceof ApiFailure ? `${e.code}: ${e.message}` : String(e));
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    if (date) void load(loc, date);
  }, [loc, date, load]);

  const patch = (p: Partial<Location>) => {
    const next = { ...loc, ...p };
    setLoc(next);
    saveLocation(next);
  };

  const shown = useMemo(() => {
    if (!payload) return null;
    // The now line only matters on the night that contains now.
    return payload;
  }, [payload]);

  return (
    <main className="page">
      <header className="controls">
        <h1>AstroScripts planner</h1>
        <div className="control-row">
          <label>
            Lat
            <input type="number" step="0.0001" min={-90} max={90} value={loc.lat}
              onChange={(e) => patch({ lat: Number(e.target.value) })} />
          </label>
          <label>
            Lon
            <input type="number" step="0.0001" min={-180} max={180} value={loc.lon}
              onChange={(e) => patch({ lon: Number(e.target.value) })} />
          </label>
          <label>
            Zone
            <input type="text" value={loc.tz} spellCheck={false}
              onChange={(e) => patch({ tz: e.target.value.trim() })} />
          </label>
          <label>
            Night of
            <input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </label>
          <div className="stepper">
            <button onClick={() => setDate((d) => shiftDate(d, -1))} aria-label="previous night">◀</button>
            <button onClick={() => setDate(todayInZone(loc.tz))}>tonight</button>
            <button onClick={() => setDate((d) => shiftDate(d, 1))} aria-label="next night">▶</button>
          </div>
        </div>
      </header>

      {error && (
        <p className="error" role="alert">
          {error}
          {error.includes("fetch") && " — is the planner API running?  .venv\\Scripts\\python.exe -m api.server"}
        </p>
      )}
      {busy && !payload && <p className="muted">computing ephemeris…</p>}

      {shown && (
        <>
          <SummaryStrip payload={shown} />
          <div className="chart-wrap">
            <NightChart payload={shown} now={now} ns="-live" />
          </div>
        </>
      )}
    </main>
  );
}
