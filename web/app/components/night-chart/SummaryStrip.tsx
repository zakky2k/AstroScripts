"use client";

// FR6 summary strip: sunset, sunrise, darkness start/end, total dark minutes,
// moonrise, moonset, Moon illumination at local midnight — observer's zone,
// 24-hour clock. Every value comes from the payload; nothing is recomputed.

import type { NightPayload } from "@/lib/types";

function hhmm(iso: string): string {
  const m = /T(\d{2}:\d{2})/.exec(iso);
  return m ? m[1] : "—";
}

export function SummaryStrip({ payload }: { payload: NightPayload }) {
  const astro = payload.sun.events.filter((e) => e.band === "astronomical");
  const darkStart = astro.find((e) => e.edge === "start")?.time;
  const darkEnd = astro.find((e) => e.edge === "end")?.time;
  // The moonrise/set that belong to THIS night's frame (noon -> noon+1): the
  // API series spans noon -> noon+2, so pick events inside the displayed
  // window instead of blindly taking [0].
  const inFrame = (iso?: string) => {
    if (!iso) return false;
    const s = Date.parse(iso) / 1000;
    const a = Date.parse(payload.frameNoon) / 1000;
    return s >= a && s <= a + 86_400;
  };
  const rise = payload.moon.moonrise.find(inFrame);
  const set = payload.moon.moonset.find(inFrame);

  const cells: { label: string; value: string }[] = [
    { label: "Sunset", value: hhmm(payload.sun.sunset) },
    { label: "Sunrise", value: hhmm(payload.sun.sunrise) },
    { label: "Darkness begins", value: darkStart ? hhmm(darkStart) : "none" },
    { label: "Darkness ends", value: darkEnd ? hhmm(darkEnd) : "none" },
    { label: "Dark minutes", value: String(payload.summary.darkMinutes) },
    { label: "Moonrise", value: rise ? hhmm(rise) : "—" },
    { label: "Moonset", value: set ? hhmm(set) : "—" },
    { label: "Moon at midnight", value: `${(payload.moon.midnight.illumFraction * 100).toFixed(0)}%` },
  ];

  return (
    <div className="strip" role="group" aria-label="Tonight at a glance">
      {cells.map((c) => (
        <div key={c.label} className="cell">
          <span className="cell-label">{c.label}</span>
          <span className="cell-value">{c.value}</span>
        </div>
      ))}
    </div>
  );
}
