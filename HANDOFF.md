# HANDOFF — state at end of session 2026-11-23/24

Snapshot for the next session. Repo: `C:\Users\zakfo\Documents\Strata\Workspace\AstroScripts`
Remote: https://github.com/zakky2k/AstroScripts.git (main, everything pushed; previous session tip `e5a8f9f`).

## Where we are
- Phases done: pinned requirements → design probes → planner API + Next.js app → 22 review rounds of the live chart.
- Latest commits (newest first): this session's **gates-first refactor** (see "Refactor 2026-11-24") on top of `e5a8f9f` chart-comment round 8 · `f6c5c1b` ±1h reads live clock directly · `ab00ad1` turn-21 (scrubbing never changes the night, full-frame slider, palette buttons) · `96adc90` rise/set chip placement.
- Working tree clean. Gates green at HEAD: `tsc --noEmit` exit 0; **unified web gates** — parity PASS (prague 136 / summer 120 / sydney 132 tokens), golden PASS (prague 126 / summer 116 / sydney 126 tokens), units PASS (160 law checks); planner tests 20/20.

## The app in one paragraph
Next.js 16 (Turbopack) + TypeScript in `web/app/`, hand-composed SVG chart, Python astropy package `planner/`, stdlib HTTP API `api/server.py` on 127.0.0.1:8642 (`/api/night`, CORS `*`). The §6 JSON contract in `docs/REQUIREMENTS.md` is the only coupling.

## Chart laws (user's rules, do not regress)
- Frame = single night: left edge local **NOON**, centre **MIDNIGHT**, right edge **NOON+1** (REQUIREMENTS §4.1).
- **Scrubbing NEVER changes the chart or the night.** Slider spans exactly noon→noon+1, unclamped-value pattern; ±1 h buttons clamp at the edges and never flip days. Day changes ONLY via the Planner ◀/▶ day-stepper / "tonight" (window events `planner:date` etc.).
- Moon glyph may park at rise/set (`glyphSec` pinning) but the focus moves freely — no snapping, no arc-end flipping (turn 21 superseded turn 18's flip-at-moonset).
- Text colours: uniform bright ink everywhere; sunset/sunrise labels must EXACTLY match civil/nautical transition label colours. Scrub-row buttons labelled "−1 h" / "+1 h", standard palette styling, FIXED widths (62/62/78 px — button positions are static).
- Geometry consts (authoritative, `lib/chart/geom.ts`, pinned by the unit gate): `W=980 H=600 M_LEFT=56 M_RIGHT=24 M_TOP=40 M_BOTTOM=150 PLOT_W=900 PLOT_H=410 Y_TOP=90 FLOOR_Y=M_TOP+PLOT_H=450` → **1 h = 37.5 px exactly**. (Earlier notes here said `floorY=510` / "≈37.7 px" — both were doc slips; the code has always drawn the horizon at 450.)
- Hook-order rule: ALL `useState/useRef/useEffect` hoisted to top of `LiveNightChart` body before any computation.

## Open item (resolved with caveats)
Turn-21 item 1 ("−1 h doesn't move back") was proven a **headless-harness artifact**, not an app bug: React's post-click commit starves under Edge virtual time, and after local midnight the live clock is always past the right edge so it clamps at v=noon+1. Runs 47/49/51–95 show −1 h landing exactly delta=3600 with no day flip. A true mid-frame (live inside frame) capture never materialized — if the user reports it again in a real browser, ask for exact local time + what they see before touching code.

## NEXT ROUND — user comments from 24 Nov round 4 (items 1–7 implemented in `ab00ad1`/`f6c5c1b`; item 8 + seconds removal implemented this session, commit after `eaa1bd9`. All visually verified: rise chip right edge ~30 min before rise, set chip left edge ~30 min after set, both OUTSIDE the curve; TIME box shows "12:00" with no seconds.)
1) "-1 hour on the scrub line doesn't move the hour back, nor switch chart to previous day if already at the beginning."
2) "scrubbing start and end should always be set to start from 12:00pm up to maximum 12:00+1 the next day."
3) "the moon will rise and set along its path and then have no effect once below the horizon (allow the moon to set completely but don't change day unless the +1h button or +1day button are pressed)"
4) "similarly for the reverse action, no affect should occur when the scrub is moved earlier than the rise unless the hour -1 or day -1 is clicked."
5) "to summarize, no day / chart change should occur just with the scrubbing bar."
6) "the labels of the scrubbing bar should display -1h or +1h respectively. the colours of those buttons and the live button need to match the rest of the palette"
7) "sunset and sunrise labels are not the same colour as the civil, naut transition labels."
8) NEW, NOT YET DONE: "the moonset text box does not seem to follow the set point closely enough, can you make both moonrise and moonset following their respective points with only a 30 minute gap from their closest edge — 30 minutes BEHIND the moonrise point for the moonrise box, and 30 minutes AFTER the moonset point for the start of the moonset box. basically I want both boxes on the OUTSIDE of the moon's curve following the points fairly closely about 30 minutes gap, unless there is collision or overlap with the moon's path in which case switch side of the curve to be inside the curve. I believe this part is already implemented." (Rise/set chip flip logic exists from `96adc90` for sun chips; moon chips need the 30-min offset + outside-first rule checked/adjusted.)

## OPEN QUESTION — clock refresh: ANSWERED & IMPLEMENTED (24 Nov, same session)
Answered from the code: the live clock already ticks **once per wall-clock minute** (self-rescheduling `setTimeout` firing 50 ms after each minute boundary — cheap, no animation loops). User confirmed: **seconds removed from every display** (fmtClock now HH:MM; scrub snaps to the minute grid) and moon chips reworked to the 30-min gap rule. Both shipped in this session's next commit (see "Where we are"). Day-switch lag is separate: a day switch refetches the whole night payload + full re-render — profile that first if the user reports more lag; do NOT touch before checking with the user.

## Test recipes (both proven, reuse verbatim)
- **Web gates (unified, one build)**: from `web\app` → `$env:PATH = "C:\Program Files\nodejs;$env:PATH"` → `npx.cmd tsc --noEmit` → `npx.cmd tsc -p tsconfig.gates.json` → `node.exe scripts\fix-aliases.mjs .gates-build` (also appends `.js` to extensionless relative imports in the emitted tree only; the app graph itself must stay extensionless or Turbopack 500s) → then `node.exe .gates-build\scripts\parity.js` (static `NightChart` vs the committed design SVGs), `node.exe .gates-build\scripts\golden.js` (`LiveNightChart` SSR vs `tests/golden/*.tokens.txt`; `--update` re-baselines deliberately), `node.exe .gates-build\scripts\units.js` (the §4.1 laws as pure-function checks). All three share `.gates-build` (gitignored); `tsconfig.parity.json` / `.parity-build` are gone.
- **Planner tests**: `.venv\Scripts\python.exe -m planner.tests.test_night` → `20 checks passed` (hand-rolled `check()`, **pytest is not installed** — `python -m pytest` fails with "No module named pytest").
- **Headless Edge screenshot**: full path `"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"` with `@("--headless=new","--disable-gpu","--no-first-run","--user-data-dir=<ABS>","--virtual-time-budget=NNNNN","--window-size=W,H","--screenshot=<ABS.png>","<url>")` via `Start-Process -NoNewWindow -Wait -PassThru`, then read_image. URL params `?dark=1&tz=-1` boot into states. Without `--virtual-time-budget` backgrounded headless Edge FREEZES page timers.
- **Interaction harness** (scratch, kept in `web/app/_edgedump/`, untracked): outer page `scrubtest.html` served from `web/app/public/` loads app in same-origin hidden iframe, injects driver scripts (`?ts=` cache-buster), drivers log to `window.top` `<pre id="out">`; screenshot shows the log. Hash `#drive` enables drive phase. Copy step: `Copy-Item _edgedump\<file> public\ -Force`. **ALWAYS delete the `public/scrub*` copies before committing** (done at `f6c5c1b`).

## Session hygiene (learned this session)
- This conversation spilled ~32 GB to `C:\Users\zakfo\Documents\Strata\conversation-spill\27740-…` because dozens of near-identical screenshots + logs were embedded, and every checkpoint re-spilled the whole giant transcript. **Next session: read each screenshot at most once; for repeat runs compare file hashes instead of re-reading identical PNGs; keep driver logs terse.** After starting a fresh session the old spill folder is safe to delete.

## Environment gotchas
- Node NOT on PATH: prefix `$env:PATH = "C:\Program Files\nodejs;$env:PATH"`, use `npx.cmd` / `node.exe`.
- Servers this session: Next dev :3000 (log `$env:TEMP\nextdev3.log`), API server — both die with the session; restart them next time.
- Write tool refuses to overwrite unread files — for scratch copies use `Copy-Item`.
- Prague test night data: sunset 16:02, sunrise 07:35, darkness 18:04–05:33, moon ~99 %, MOONRISE 14:57, MOONSET 07:26 (Nov 24).

## Refactor 2026-11-24 (gates-first, behaviour frozen)
`LiveNightChart.tsx` went 738 → 399 lines: the component now only owns state, events and JSX. The chart laws moved into pure modules under `web/app/lib/chart/` — `geom.ts` (frame + scales), `clock.ts` (wall parts, clocks, focus), `scrub.ts` (clamp/snap/±1 h/typed/live/focus-epoch laws), `sunbands.ts` (twilight boundaries + paint order), `labels.ts` (transition leaders, midnight + header titles, tooltip width), `moonchips.ts` (30-min outside-first chip placement), `moontrack.ts` (moon series, glyph parking, arc, readout rise). Nothing else about behaviour changed: parity, golden and planner tokens are byte-identical to before the move.
- `scripts/tokens.ts` is now the single tokenizer used by both render gates (`parity.ts` rewritten to use it, `golden.ts` new). Goldens live in `web/app/tests/golden/<case>.tokens.txt`; first line = token count. They are the only thing that pins the LIVE chart, which parity cannot see (parity compares the frozen static probe).
- `scripts/units.ts` (new, 160 checks) covers what no screenshot can: frame edges/noon-centre/37.5 px per hour, scale inverses, minute snapping, ±1 h edge clamping with no day flip, `liveSec(null) === NaN`, chip bounds + 30-min gap, glyph parking at rise/set, arc above horizon, band structure. Run it after any change to `lib/chart/*`.
- NightChart.tsx (the static design probe) was deliberately left alone: it is the reference the parity gate compares against.

## KNOWN DEFECT — focus axis vs frame axis (found by the unit gate, NOT yet fixed)
`focusSec()` and `liveSec()` carry a focus as `Date.UTC(wall fields)` while the frame and every payload sample are real epochs (`Date.parse(ISO)`), so the focus sits one zone-offset right of the axis it is drawn on: scrubbing to 13:00 in Prague draws at x=131 instead of 93.5 (+37.5 px) and the readout says 14:00; Sydney is off by +11 h (412 px, 13:00 → readout 00:00). Same root cause behind the typed-time box landing on the wrong hour in far-east zones (`typedFocus` folds pseudo seconds against real edges). This is **pre-existing** — the arithmetic was ported verbatim, and both render gates are blind to it (no marker/tick in SSR, and the slider lives outside the SVG). Fix = make the focus real-epoch on both sides (`focusAt`/`liveSec`/`typedFocus` via a zone-aware epoch) and re-check the live screenshots. **Needs user sign-off before touching**, because it changes what the reviewer sees on screen. `units.ts` pins the current behaviour in a check literally named `KNOWN (awaiting review): the focus axis is wall-fields-as-UTC…` — when you fix it, that check fails on purpose; update it, this section and REQUIREMENTS §4.1 in the same commit.

## Harness state (do not chase this again)
Headless Edge currently renders ONLY the Planner chrome (blank chart area, empty date input, LOCAL TIME `--:--:--`) at budgets 20 000 / 45 000 / 90 000, byte-identical `14765 byte` PNG every run. Verified as a harness artifact, not a regression: the same PNG hash comes back with `LiveNightChart.tsx` stashed to HEAD. `GET :3000/` is 200 with no `<svg>` in SSR (the chart is fetched and rendered client-side) and the API returns 200/54 304 bytes for the Prague night, so both servers are healthy. Real-browser review remains the source of truth for visual state.
Housekeeping: `web/app/_edgedump/` browser-profile dirs from earlier sessions could not be deleted from inside the sandbox (Access denied; `cmd rmdir`, and one escalated retry, all failed) — ~2.43 GB / 119 dirs still on disk, delete manually outside the sandbox if space matters.

## Portability — verdict (full detail: `docs/PORTABILITY.md`)
Plain-language summary, so you do not have to decode ARCHITECTURE §5:
- **Nothing is entangled.** Astronomy = one Python module (`planner/night.py`); the UI
  is a plain browser client that asks one JSON endpoint (`/api/night`). Every target
  shape reuses that seam. No Next-server features are used at all (`next.config.ts` is
  empty, no API routes/middleware), so the UI is already host-agnostic.
- **Public web app = easy, and it is step 1.** One blocker: `web/app/lib/api.ts:7-8`
  bakes the API origin in at **build** time (`NEXT_PUBLIC_API_BASE`). Make it
  same-origin (`/api/night` proxied by the host) or runtime-configurable, add per-site+date
  response caching, and stop shipping CORS `*` publicly (`api/server.py` is
  HTTP/1.0 `ThreadingHTTPServer` on 127.0.0.1:8642, no keep-alive, no OPTIONS preflight).
- **PWA/offline = cheap add-on**: the chart needs no server once it has a payload, so
  manifest + service worker + cached payload gives an offline "last night".
- **Desktop/mobile = packaging, not a rewrite**: WebView shell (Tauri/Electron/Capacitor)
  around the exported UI; data from (i) a bundled Python sidecar — needs `api/server.py`
  to bind port 0 and expose a shutdown handle — or (ii) precomputed per-observer JSON
  packs for phones.
- **NINA plugin = feasible as a BRIDGE, not a port.** Plugin = one DLL exporting
  `[Export(typeof(IPluginManifest))]` under `%LOCALAPPDATA%\NINA\Plugins\3.3.0\`; NINA
  imports `IDockableVM` etc. by MEF. NINA already ships WebView2 and already anchors the
  night at 12:00 (`NighttimeCalculator.GetReferenceDate`), and samples moon altitude in
  10-minute steps — so the plugin hosts *this* chart in a dockable panel and feeds it
  NINA's own numbers. **Do not re-derive astropy in C#**: any library swap moves contact
  times by minutes and breaks every fixture/golden baseline. NINA's own astronomy is
  P/Invoke over native NOVAS/SOFA DLLs in an unchecked-out submodule anyway.
- **Ask the user before NINA work** (decisions are listed in PORTABILITY §5.5): whose
  numbers the NINA panel shows (recommend NINA's), whether the observatory PC has the
  Evergreen WebView2 runtime, how tightly to track NINA minors, separate repo or not.
- **Sequencing warning:** fix the focus-axis defect (section above) *before* Shape A
  ships, with the user's sign-off — it is the first thing a stranger would notice.
- Verified against the NINA clone at `db6b909` (3.3.0.1070), MPL-2.0, `net10.0-windows`.
  Two ARCHITECTURE §5 numbers were corrected while verifying (moon step 6 min → 10 min;
  component 399 → 376 lines).


## How to work with the user
Enthusiastic iterative visual reviewer. Numbered lists of chart complaints per round; wants SIMPLE solutions, static button positions, and `docs/REQUIREMENTS.md` kept in sync every round. Their latest summary overrides earlier micro-rules. Commit+push each round to main.
