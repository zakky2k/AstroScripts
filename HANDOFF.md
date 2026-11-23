# HANDOFF — state at end of session 2026-11-23/24

Snapshot for the next session. Repo: `C:\Users\zakfo\Documents\Strata\Workspace\AstroScripts`
Remote: https://github.com/zakky2k/AstroScripts.git (main, everything pushed through `f6c5c1b`).

## Where we are
- Phases done: pinned requirements → design probes → planner API + Next.js app → 22 review rounds of the live chart.
- Latest commits (newest first): `f6c5c1b` ±1h reads live clock directly · `ab00ad1` turn-21 (scrubbing never changes the night, full-frame slider, palette buttons) · `96adc90` rise/set chip placement.
- Working tree clean. Gates green at HEAD: `tsc --noEmit` exit 0; parity gate PASS (prague 136 / summer 120 / sydney 132 tokens identical).

## The app in one paragraph
Next.js 16 (Turbopack) + TypeScript in `web/app/`, hand-composed SVG chart, Python astropy package `planner/`, stdlib HTTP API `api/server.py` on 127.0.0.1:8642 (`/api/night`, CORS `*`). The §6 JSON contract in `docs/REQUIREMENTS.md` is the only coupling.

## Chart laws (user's rules, do not regress)
- Frame = single night: left edge local **NOON**, centre **MIDNIGHT**, right edge **NOON+1** (REQUIREMENTS §4.1).
- **Scrubbing NEVER changes the chart or the night.** Slider spans exactly noon→noon+1, unclamped-value pattern; ±1 h buttons clamp at the edges and never flip days. Day changes ONLY via the Planner ◀/▶ day-stepper / "tonight" (window events `planner:date` etc.).
- Moon glyph may park at rise/set (`glyphSec` pinning) but the focus moves freely — no snapping, no arc-end flipping (turn 21 superseded turn 18's flip-at-moonset).
- Text colours: uniform bright ink everywhere; sunset/sunrise labels must EXACTLY match civil/nautical transition label colours. Scrub-row buttons labelled "−1 h" / "+1 h", standard palette styling, FIXED widths (62/62/78 px — button positions are static).
- Geometry consts: `H=600; M_LEFT=56, M_RIGHT=24, M_TOP=40, M_BOTTOM=150; PLOT_H=410; W=980; Y_TOP=90; floorY=510` — 1 h ≈ 37.7 px.
- Hook-order rule: ALL `useState/useRef/useEffect` hoisted to top of `LiveNightChart` body before any computation.

## Open item (resolved with caveats)
Turn-21 item 1 ("−1 h doesn't move back") was proven a **headless-harness artifact**, not an app bug: React's post-click commit starves under Edge virtual time, and after local midnight the live clock is always past the right edge so it clamps at v=noon+1. Runs 47/49/51–95 show −1 h landing exactly delta=3600 with no day flip. A true mid-frame (live inside frame) capture never materialized — if the user reports it again in a real browser, ask for exact local time + what they see before touching code.

## NEXT ROUND — user comments from 24 Nov round 4 (verbatim; items 1–7 already implemented in `ab00ad1`/`f6c5c1b`, verify visually with the user)
1) "-1 hour on the scrub line doesn't move the hour back, nor switch chart to previous day if already at the beginning."
2) "scrubbing start and end should always be set to start from 12:00pm up to maximum 12:00+1 the next day."
3) "the moon will rise and set along its path and then have no effect once below the horizon (allow the moon to set completely but don't change day unless the +1h button or +1day button are pressed)"
4) "similarly for the reverse action, no affect should occur when the scrub is moved earlier than the rise unless the hour -1 or day -1 is clicked."
5) "to summarize, no day / chart change should occur just with the scrubbing bar."
6) "the labels of the scrubbing bar should display -1h or +1h respectively. the colours of those buttons and the live button need to match the rest of the palette"
7) "sunset and sunrise labels are not the same colour as the civil, naut transition labels."
8) NEW, NOT YET DONE: "the moonset text box does not seem to follow the set point closely enough, can you make both moonrise and moonset following their respective points with only a 30 minute gap from their closest edge — 30 minutes BEHIND the moonrise point for the moonrise box, and 30 minutes AFTER the moonset point for the start of the moonset box. basically I want both boxes on the OUTSIDE of the moon's curve following the points fairly closely about 30 minutes gap, unless there is collision or overlap with the moon's path in which case switch side of the curve to be inside the curve. I believe this part is already implemented." (Rise/set chip flip logic exists from `96adc90` for sun chips; moon chips need the 30-min offset + outside-first rule checked/adjusted.)

## OPEN QUESTION — ask the user BEFORE changing anything (their words: "check with me")
Clock seconds don't seem to increment. Investigate the live refresh mechanism first (the `liveSec` tick in `LiveNightChart` — find its interval), then report: is it minute-by-minute or per-second? User's decision tree: if minute-based → REMOVE the seconds from displayed clocks (no visual resolution benefit); if already per-second → re-adjust simulation clock ticks to per-MINUTE only. Also investigate reported LAGS, especially when switching between days — make sure whatever refresh exists is not resource intensive. Do NOT implement until user confirms.

## Test recipes (both proven, reuse verbatim)
- **Parity gate** (static NightChart frozen): from `web\app` → `$env:PATH = "C:\Program Files\nodejs;$env:PATH"` → `npx.cmd tsc -p tsconfig.parity.json` → `node.exe scripts\fix-aliases.mjs` → write `.parity-build\package.json` = `{"type":"module"}` → `node.exe .parity-build\scripts\parity.js`.
- **Headless Edge screenshot**: full path `"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"` with `@("--headless=new","--disable-gpu","--no-first-run","--user-data-dir=<ABS>","--virtual-time-budget=NNNNN","--window-size=W,H","--screenshot=<ABS.png>","<url>")` via `Start-Process -NoNewWindow -Wait -PassThru`, then read_image. URL params `?dark=1&tz=-1` boot into states. Without `--virtual-time-budget` backgrounded headless Edge FREEZES page timers.
- **Interaction harness** (scratch, kept in `web/app/_edgedump/`, untracked): outer page `scrubtest.html` served from `web/app/public/` loads app in same-origin hidden iframe, injects driver scripts (`?ts=` cache-buster), drivers log to `window.top` `<pre id="out">`; screenshot shows the log. Hash `#drive` enables drive phase. Copy step: `Copy-Item _edgedump\<file> public\ -Force`. **ALWAYS delete the `public/scrub*` copies before committing** (done at `f6c5c1b`).

## Session hygiene (learned this session)
- This conversation spilled ~32 GB to `C:\Users\zakfo\Documents\Strata\conversation-spill\27740-…` because dozens of near-identical screenshots + logs were embedded, and every checkpoint re-spilled the whole giant transcript. **Next session: read each screenshot at most once; for repeat runs compare file hashes instead of re-reading identical PNGs; keep driver logs terse.** After starting a fresh session the old spill folder is safe to delete.

## Environment gotchas
- Node NOT on PATH: prefix `$env:PATH = "C:\Program Files\nodejs;$env:PATH"`, use `npx.cmd` / `node.exe`.
- Servers this session: Next dev :3000 (log `$env:TEMP\nextdev3.log`), API server — both die with the session; restart them next time.
- Write tool refuses to overwrite unread files — for scratch copies use `Copy-Item`.
- Prague test night data: sunset 16:02, sunrise 07:35, darkness 18:04–05:33, moon ~99 %, MOONRISE 14:57, MOONSET 07:26 (Nov 24).

## How to work with the user
Enthusiastic iterative visual reviewer. Numbered lists of chart complaints per round; wants SIMPLE solutions, static button positions, and `docs/REQUIREMENTS.md` kept in sync every round. Their latest summary overrides earlier micro-rules. Commit+push each round to main.
