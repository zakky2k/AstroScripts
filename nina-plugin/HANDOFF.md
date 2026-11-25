# HANDOFF — Unphased Night Planner (N.I.N.A. plugin), state at 2026-11-25 (morning)

For the next session. Parent repo `AstroScripts` (remote `github.com/zakky2k/AstroScripts.git`, main).
Tip is **`6c240ce` — feat(nina-plugin): make the night chart readable at panel size** (the first beta was
`7883ab7`), **pushed** — `main` is level with `origin/main`. Version stamped **0.2.0.0** in `Plugin.props`,
and the deployed DLL really reports 0.2.0.0 (`GetAssemblyName(...).Version`) — the old 1.0.0.0 in the
deployed folder was a stale build and is gone.
Root `AstroScripts/HANDOFF.md` covers the web planner; this file covers only `nina-plugin/`.

## What exists and works
A dockable panel inside N.I.N.A. 3.2.1.3002: **Imaging page → the puzzle-piece button in the Info bar**
(not Options). Header `Tonight  ‹ ›  Tue 24 Nov → Wed 25 Nov 2026`, a hand-drawn chart, and a night list
below whose selected row and the chart are kept in sync. Status line reads `Site 50.0880, 14.4208 - window 21 nights`
(profile `842b5009-…`, Prague). Deployed build loads with **no plugin ERROR** in the N.I.N.A. log.

Layout of `nina-plugin/`: `UnphasedCore` (C# port of the planner's astronomy: `NightCalculator.cs`, `Models.cs`),
`UnphasedCore.Tests` (accuracy vs the web fixtures Prague/summer/Sydney), `UnphasedNina/` — `src` (Plugin.cs,
`Features/planner/{Plugin,VM,chart}`, `Features/settings`), `tests`, `build` (Stage/Deploy/Package/Verify +
`build/vendor/CreateManifest.ps1`), `probe` (payload compared byte-wise to the web `probe/night-*.json`),
`reflect` (scratch reflection dump over NINA assemblies).

## Commands (all proven this session)
- Tests: `dotnet test AstroScripts\nina-plugin\UnphasedNina\tests\UnphasedNina.Tests\UnphasedNina.Tests.csproj -c Debug -f net8.0-windows --nologo` → **42 passed**.
- **Visual smoke (this is how chart work gets verified here)**: same command plus
  `--filter "FullyQualifiedName~PlannerChartRenderSmoke"` → writes `%TEMP%\planner-preview-<day>-at-<width>.png`
  (1200 px wide = two-night mode, 720 px = one-night mode), then `read_image` them. The test is `[Explicit]`,
  so it never runs in the normal suite.
- Deploy: close N.I.N.A. first (it holds a lock on the DLLs), then
  `dotnet msbuild "AstroScripts\nina-plugin\UnphasedNina\src\UnphasedNina\UnphasedNina.csproj" -t:DeployPlugin -v:m -nologo`
  → `%LOCALAPPDATA%\NINA\Plugins\3.0.0\Unphased Night Planner\`. Verify with `Get-FileHash` against
  `AstroScripts\nina-plugin\UnphasedNina\artifacts\stage\Debug`. The script prints
  `Compatibility check: plugin needs N.I.N.A. 3.2.0.9001, installed app is 3.2.1.3002.` and refuses with
  `Cannot replace '…astronomy.dll'. Close NINA and retry.` when the app is running.
- Log check: newest `%LOCALAPPDATA%\NINA\Logs\*-3.2.1.3002-*.log`, grep `ERROR` and `not compatible with this version`.

## Chart laws (do not regress)
- Abstract canvas `NightChartVM.Width=1000 / Height=320`; payload frame is **local noon → +48 h**
  (`FullFrameMinutes=2880`). X is `XAt(payload,t)`, Y is
  `YSun(alt) = (TopAltitude-alt)/(TopAltitude-BottomAltitude)*320` with **`TopAltitude=90 / BottomAltitude=-24`**.
  History, because this axis has been changed three times: +80/-32 (0.2.0.0, my call) → ±90 (0.3.0.0, the user asked
  for it back) → +90/-24 (0.4.0.0, the user corrected themselves: the *zenith* is what must stay, and the below-
  horizon half only needs enough room for the −6 / −12 / −18° labels). −24 puts the horizon just above mid-height,
  leaves ~17 px between degree chips at a 360 px row and ~13.6 px at 260 px — which is why the de-stacking walk is
  still load-bearing. **`YMoon == YSun`** — one shared altitude domain, so nights are comparable. Changing the band
  changes every Y-dependent test, and **ask the user before touching the axis**: they have corrected this twice.
- Curves are **cut at the window edge, never clamped to it**: `NightChartVM.TrimmedPolyline(payload, series, yOf)`
  emits points separated by spaces and separate drawable runs separated by `" | "`, with the boundary crossing
  interpolated so a run ends exactly on the edge. `NightChartControl.DrawCurve` starts a fresh figure per run.
  Clamping every sample instead drew a flat horizontal line along the bottom of the panel — asserted against by
  `CurvesStopAtTheWindowEdgeInsteadOfRunningAlongIt`.
- The **scrub marker** is state on `NightPlannerVM`, not on the chart control: `ScrubMinutes` (minutes from frame
  noon, snapped to whole hours by `NightChartVM.SnapToHour` with `MidpointRounding.AwayFromZero`, clamped to
  `VisibleSpanMinutes`, which follows the **zoom** — `FrameMinutesAtZoom(zoomNights)`), plus `SetScrub` /
  `StepScrub` / `ClearScrub` and the `ScrubLabel` readout. Commands `ScrubBack/ScrubForward/ScrubClearCommand` on
  `PlannerDockableVM`; input on the chart is click-drag, wheel, ←/→, Home/End, Escape.
  `NightChartControl.SetScrub(double?)` is the drawing mirror so the control renders in tests without a planner.
  Altitudes come from `NightChartVM.AltitudeAt` (interpolated, NaN outside the frame). The moon marker on the scrub
  line is the **phase glyph**, not a dot — `NightChartVM.ScrubMoonGlyph(minutes)` returns `(illum, litOnRight)`
  (illumination interpolated over `Payload.MoonSamples`, limb from `Payload.Limb`) and the control draws it on a
  small dark disc so it reads over a label. It is skipped when the body is outside the plotted window; the readout
  deliberately is not clamped, so it can still name an altitude that is off-canvas.
- The **frame is chosen by the panel, not by the payload**: `NightChartVM.MaxZoomNights=7`,
  `FrameMinutesAtZoom(nights) = clamp(nights,1,7) * OneNightMinutes`, and `HourGridMinutes(frameMinutes)` →
  4 h ≤2 nights, 8 h ≤4, else 12 h. `sx = w / Width` always maps the *frame* onto the panel; the abstract canvas
  is never stretched by the span. `new NightChartVM(payload)` deliberately keeps the payload's own 48 h frame, so
  a bare-payload chart still shows everything it carries — the panel always passes a frame explicitly.
- Wider frames are stitched **in the plugin** by `NightFrame.For(payloads, nights)`: one day-slice per consecutive
  payload, each cut on *that payload's* `FrameNoon` (so DST seams stay exact), half-open except the last, moon
  rise/set deduplicated at the seams. The guard measures the **gap** between successive `FrameNoon`s (12..36 h is
  a sequence); comparing against an expected noon measured 0 h and silently cut the frame to one day. A payload
  for a night that dropped out stops the stitch instead of bridging the hole. Core is untouched: it is shared with
  the web planner and has fixtures.
- **Nothing may draw past the frame edge.** `NightChartVM.WithinFrame` cuts the sun/moon series at both frame
  edges with interpolated crossings (so `SunPolyline`/`MoonPolyline` only contain x in 0..Width) and
  `ShadingRuns(payload, frameMinutes)` clamps each run to the canvas before merging. WPF does **not** clip
  `OnRender` output, so before this a 1-night chart painted 48 h of curve, colour and chips off the right end of
  the panel. Asserted by `NothingLeaksPastTheFrameEdge`.
- Horizon event labels come from the **plotted curve**, not the payload's event list:
  `NightChartVM.SunCrossings()` interpolates every crossing of the trimmed sun polyline, so each night *in the
  frame* gets its own sunset/sunrise and nothing off-frame gets one. `moonrise`/`moonset` still come from the
  payload (they were already per-frame events).
- **Labels step around each other**: `DrawGrid` records the boxes its own axis labels occupy (`axisChips`) and the
  event chips skip any box that would intersect one, per band (`Fits`). A clock label is never slid sideways off
  its gridline — if its gridline has no room before the degrees column, the label is left out and the line stays.
- Moon track runs the **whole frame** like the sun curve (`MoonPolyline` filters nothing); the phase glyph goes
  at `NightChartVM.NightMoonPeak()` — the moon's highest point *of the night*, which is the imaging-relevant altitude.
- Glyph: `NightChartControl.MoonPhaseGeometry(cx,cy,r,illum,litOnRight)` — public only so it can be unit-tested.
  Lit limb semicircle + terminator half-ellipse of half-width `r*|1-2f|`; gibbous bulges into the dark side,
  crescent into the lit side; `litOnRight` comes from `payload.Limb` ("left" ⇒ lit on the left), so southern
  hemispheres mirror correctly. New moon = outline only, full moon = solid disc.
  WPF trap: `StreamGeometryContext.ArcTo` takes **7 arguments** including a `SweepDirection`; `Sweep(bool)` is the helper.
- Twilight background = `PlannerPalette.Twilight(dark, sunAltitude)`, four alpha overlays for >0 / 0..-6 / -6..-12 /
  -12..-18, nothing below -18 (the bare chart background is the darkest layer). Red night-vision variants exist;
  both ladders are asserted distinct by `TwilightPaletteFollowsTheStandardAltitudeBands`.
- The bands are painted as **runs, not samples**: `NightChartVM.ShadingRuns(payload)` (pure, testable) merges
  consecutive samples in the same `PlannerPalette.TwilightBand` into one `(x0, x1, band)` rectangle with exact
  shared edges. One hairline rect per ~10-minute sample plus a `+0.5` overlap fudge is what produced the comb of
  "vertical lines that don't represent anything" in the daylight part — never go back to it, and keep the
  rect-count test (band transitions + 1) green.
- Legibility consts live at the top of `NightChartControl`: `LabelFs=10` (degrees, hour clock), `MarkerFs=11`
  (sunset/sunrise/moonrise/moonset), `NoteFs=12` (`NN% moon at NN°`), `LegendFs=11`, `TipFs=13`,
  `MoonGlyphRadius=10.5`. **Every** label goes through the chip helper (fill `PlannerPalette.ChipFill`, a
  translucent near-black, with a hairline border in the label's own ink) — plain text over the daylight band is
  unreadable. Keep `moonrise`/`moonset` worded **above** the horizon crossing and `sunset`/`sunrise` **below** it
  (`NightChartVM.ChipAbove` / `ChipBelow`, which flip to the other side rather than let a label leave the canvas),
  and the four degree labels walked top-to-bottom so they can never stack in a short panel. No full-height vertical
  marker lines for those events — they are the lines "that dont seem to represent anything", and the scrub line is
  now the only vertical that moves.
- The 0° horizon has its own pen: 1.6 px `PlannerPalette.Horizon(dark)`, drawn after the 0.6 px twilight lines.
  Hour gridlines follow `HourGridMinutes` (4 / 8 / 12 h by zoom) and the degrees column is drawn **before** the
  clock row, because the clock labels have to know how much of the right edge is already taken.
- Row label shows the **evening** date; navigation and the header use the **morning** date (`MorningDay = EveningDay+1`).
- Build is **net8.0-windows** with `NinaPackageVersion 3.2.0.9001`. Do NOT modernize to net10 / 3.3 nightly:
  N.I.N.A. 3.2.1.3002 then rejects the plugin with `requires a minimum version of 3.3.0.1060`.

## Round of 25 Nov — all five review items delivered in `6c240ce`
Kept verbatim as the record of what was asked:
> *"glyph and text in the actual chart are too small, increase font size a couple of points and glyph by 50%.*
> *including words moonrise and moonset.*
> *if possible make the test into chips with small tight bounding box around the labels and a black background inside the box.*
> *The horizon line at 0 deg alt should be bolder and maybe slightly thicker. each parallel line below horizon and horizon itself should have degree label*
> *too many verticle lines, that dont sem to represent anything, mainly present during the daytime parts of teh graph"*

Where it landed: `NightChartControl.cs` (font consts, the chip helper, the horizon pen, moonrise/moonset wording
at the horizon crossing), `NightChartVM.cs` (`ShadingRuns`, the +80/-32 altitude band), `PlannerPalette.cs`
(`Horizon`, `ChipFill`, `TwilightBand`), plus four new assertions in `PlannerChartTests.cs`. The "Chart laws"
section above is the outcome. Two calls made without asking, both worth re-checking with the user: chips are
translucent near-black `#E6000000` rather than literally solid black (solid black hid the chip edge on the dark
theme), and the altitude window went from ±90 to +80/-32 — that narrowing is what actually made the twilight
labels readable, but the user has not seen it yet and may want it back.

Standing verify order for chart work: unit tests (expect **42**) → the `[Explicit]` smoke render (nine frames: one
night at 1200 and 720 px, two- and three-night frames, a week at 1200 and at 620×260, plus scrubbed frames — a near
full moon at 01:00, a 24 % crescent at 13:00 to prove the phase glyph on the scrub line, and a three-night scrub) →
`read_image` → ask the user to close
N.I.N.A. → `-t:DeployPlugin` → `Get-FileHash` against `artifacts\stage\Debug` → relaunch → log check. Re-run the
deploy after any commit that touches `Plugin.props`: the deployed DLL kept reporting 1.0.0.0 through two chart
deploys because the staged build predated the stamp.

## Round of 25 Nov (second pass) — ±90 back, edge-cut curves, scrub marker → `0.3.0.0`
Asked verbatim:
> *"actually we must keep the 90 degree altitude visibility"*
> *"For sunset and sunrise place them similar as the moon chips but under the horizon instead."*
> *"stop drawing the moon and sun path lines if they reach the bottom of the graph, instead of having a flat horizontal line."*
> *"no need for the dashed vertical red line as it exists currently, i was hoping to get a scrubbing mechanism to move it hour by hour"*

The ±90 revert was the correction of an unasked-for decision from the previous round — that is the second time a
convenience change to an axis surprised the user, so chart-axis changes get asked about, not assumed. Not yet
verified inside N.I.N.A.: the scrub *input* (drag / wheel / keys) has no automated coverage, only snapping,
clamping and the readout do, and the readout chip can land on the right-hand degree labels when the marker is near
the right edge. Next round: watch for that overlap and for whether the wheel-steals-scroll complaint appears.

## Round of 25 Nov (third pass) — −24 floor, scrub phase glyph, zoom to a week → `0.4.0.0`
Asked verbatim:
> *"the scrubbing mechanism is perfect and love that i can click and drag, great implementation."*
> *"it seems i may have misspoke... There's no need to display the full -90deg altitude, only the +90deg alt, for the portion below the horizon what was displayed previous was more than enough, and could be even reduced a bit further just enough to ensure the -6, -12 and -18 degrees are visible."*
> *"for the moon can the same glyph currently display that changes in function of the phase be use instead of just the dot when scrubbing?"*
> *"new requirement. for some reason when switching to plugin the display cover 2 nights instead of just one on the desktop app. lets have a zoom button than can be used to scale from one night through to a week (or what maximum you feel is suitable for testing)"*

Item 1 needed nothing. Item 2 was the axis change the user initiated, so it went ahead (−90 → −24, zenith kept);
the four degree chips were then measured on the canvas (horizon y≈252.6, −6 → 269.5, −12 → 286.3, −18 → 303.2)
rather than assumed. Item 4 turned out to be a *frame* problem, not a rendering bug: the payload has always carried
noon → +48 h, and until now the chart drew all of it. Hence `MaxZoomNights=7`, `NightFrame.For` and the frame-edge
clipping law above. `-` means fewer nights and `+` more, matching the label between them.

Discovered while testing this round, worth remembering: **the 1-night frame is anchored at local noon**, so the
sun's daily maximum sits exactly at the left and right edges and the daylight arc reads as a shallow dip. Correct,
but the first thing the user will notice at 1-night zoom — the open question is whether to anchor a single-night
frame sunset → sunrise instead (the desktop app's one-night view may do that; ask before changing it).

## Open items and decisions
- **0.3.0.0 is live in the app**: deployed 16:05 on 25 Nov (`UnphasedNina.dll` 51,712 B, `UnphasedCore.dll` 24,576 B,
  `astronomy.dll` 162,816 B), byte-identical to `artifacts\stage\Debug`, reflected version `0.3.0.0+8fcdd68…`, and
  the launch log `20261125-160651-3.2.1.3002.13208-202611.log` had no `ERROR`, no `Unphased` complaint and no
  compatibility line beyond the expected "needs 3.2.0.9001, installed 3.2.1.3002". **0.4.0.0 is committed but not
  deployed** — N.I.N.A. is running again, so the deploy needs the app closed, and `Plugin.props` changed, so it must
  be a fresh `-t:DeployPlugin` followed by a `Get-FileHash` check and a version check on the deployed DLL.
- `Plugin.props` metadata is catalogue-placeholder: `RepositoryUrl` says `github.com/zakky2k/unphased` (the code now
  lives in `AstroScripts/nina-plugin`), `Description` is "A NINA plugin.", long description / changelog / screenshot
  URLs empty. Fix before publishing to the NINA plugin catalogue.
- Commit convention here is commit **and push** to main; `6c240ce` and the handoff are pushed.
- Offered, not chosen: degree labels on the **left** edge as well as the right; a deeper computed window
  (`WindowNights=10` → 21 payloads, so a 7-night zoom has nothing after the last few rows and their frame is blank
  on the right — raising it to ~14 would fix that at the cost of a longer first compute).
- Open question the user has not answered: should a **single-night** frame be anchored **sunset → sunrise** instead
  of noon → noon? At noon anchoring, daylight occupies both ends of a 1-night panel, which reads oddly next to the
  desktop app's one-night view. The user once wrote "12pm utc to 12pm utc +1" while describing that fallback; it is
  currently anchored at **local** noon (11:00 UTC in winter for Prague). Do not change the anchor unprompted.
- Still-open input wrinkle: the mouse wheel over the chart is handled by the scrub, so the panel cannot be scrolled
  while the pointer is over the chart. Watch for the complaint.
- Leftovers from the earlier net10 experiment: stale `bin/Debug/net10.0-windows` trees under `src` and `tests`
  (gitignored, safe to delete).

## Gotchas learned
- The panel is opened from the Info-bar puzzle button; the DataTemplate key must be exactly
  `"<VM type FullName>_Dockable"` (`PlannerDockableVM`), and it is a `DockableVM`, MEF-exported as `IDockableVM`.
- Setting `ListBox.SelectedItem` from inside a `CollectionChanged` Add notification does not stick; select after the
  collection settles (`NightPlannerVM.ApplySelection`, `NightPlannerVM.Ui` dispatcher).
- A unit-test host has no N.I.N.A. theme dictionaries, so `PlannerPalette` falls back to flat `Brushes.Gray` —
  background and curves then share a colour and a render looks blank. `PlannerChartRenderSmoke` injects stand-in
  brushes (`TertiaryBackgroundBrush`, `PrimaryBrush`, `SecondaryBrush`, `BorderBrush`, `ButtonBackgroundSelectedBrush`).
- Only **one** `System.Windows.Application` per AppDomain: build it once and reuse it across the render loop, on a
  single STA thread, or WPF throws.
- Stitching consecutive payloads: the sanity check has to measure the **gap between successive `FrameNoon`s**, not
  compare each noon against an expected one — the expected-difference is 0 h for a correct sequence, so the guard
  rejected every slice after the first and the "3-night" frame silently held one day. When a stitch looks wrong,
  print the `FrameNoon`s and the gaps from a scratch `[Test]`, then delete the scratch file.
- `Math.Min` / `Math.Max` have **no `DateTimeOffset` overload**; the compiler picks `byte` and reports
  `CS1503: cannot convert from 'System.DateTimeOffset' to 'byte'`. Write the ternary instead.
- Keep screenshots and previews small and look at each one once — an earlier session in this workspace spilled ~32 GB
  of embedded near-identical PNGs (root HANDOFF, "Session hygiene").

## How this user works
Enthusiastic, fast visual reviewer: numbered list of chart complaints per round, typos and all — read for intent.
Wants evidence before claims ("show me it works"), closes N.I.N.A. himself when asked, and accepts offered options
(dockable vs options page, automatic vs manual span) with a short yes. Small, honest commits; changelog updated with
the state of the thing, including what is still rough.
