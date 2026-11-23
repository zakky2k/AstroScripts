"use client";

// The planner front end: location + date controls (FR1/FR2), the fixed-scale
// night chart, and the FR6 summary strip. Scrolling the date is a pure
// translation of bands and moon curve — the frame never moves.

import { useCallback, useEffect, useState } from "react";
import { SummaryStrip } from "@/components/night-chart/SummaryStrip";
import { LiveNightChart } from "@/components/night-chart/LiveNightChart";
import { ApiFailure, fetchNight } from "@/lib/api";
import { DEFAULT_LOCATION, loadLocation, saveLocation, shiftDate, todayInZone, type Location } from "@/lib/settings";
import type { NightPayload } from "@/lib/types";

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

  const load = useCallback(async (l: Location, d: string) => {
    if (!d) return;
    setBusy(true);
    setError(null);
    try {
      const p = await fetchNight({ date: d, lat: l.lat, lon: l.lon, tz: l.tz });
      setPayload(p);
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
