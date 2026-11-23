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
      // Keep the previous payload on screen only for the initial load failure;
      // a failed night-flip otherwise just leaves the current chart in place.
      setError(e instanceof ApiFailure ? `${e.code}: ${e.message}` : String(e));
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    if (date) void load(loc, date);
  }, [loc, date, load]);

  // Editable focus time: mirrors the chart's focus (with seconds) and accepts
  // HH:MM[:SS] or h:mm AM/PM. The "24" button inside the box flips 12 h <->
  // 24 h display for every clock on screen.
  const [timeDraft, setTimeDraft] = useState("");
  const [h12, setH12] = useState(false);
  // "+1" in the ZONE box: whole-hour live-clock offset (cycles +1 -> +2 -> ...
  // -> -3 -> off) for zones whose DST state disagrees with the IANA zone.
  const [tzOff, setTzOff] = useState(0);
  // "DARK" beside TIME: red sky scheme at identical lightness (B->R swap).
  const [dark, setDark] = useState(false);
  // Headless-review hook: ?dark=1&tz=±h boot straight into those states.
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
    const tz = Number(q.get("tz"));
    if (Number.isInteger(tz) && tz !== 0) {
      setTzOff(tz);
      window.dispatchEvent(new CustomEvent("planner:tz-offset", { detail: tz }));
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
            <span className="time-wrap">
              <input type="text" value={loc.tz} spellCheck={false}
                style={{ width: 170, paddingRight: 40 }}
                onChange={(e) => patch({ tz: e.target.value.trim() })} />
              {/* whole-hour live-clock offset (DST nudge): +1 -> +2 ... -3 -> off */}
              <button className="h-toggle" title="shift the LIVE clock by whole hours (daylight-saving nudge)"
                onClick={() => {
                  const next = tzOff >= 3 ? -3 : tzOff + 1;
                  setTzOff(next);
                  window.dispatchEvent(new CustomEvent("planner:tz-offset", { detail: next }));
                }}>
                +/-1H
              </button>
            </span>
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
            Local Time
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
