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
- Tests: `dotnet test AstroScripts\nina-plugin\UnphasedNina\tests\UnphasedNina.Tests\UnphasedNina.Tests.csproj -c Debug -f net8.0-windows --nologo` → **26 passed**.
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
  `YSun(alt) = (TopAltitude-alt)/(TopAltitude-BottomAltitude)*320` with **`TopAltitude=80 / BottomAltitude=-32`**
  (at ±90 two thirds of the panel was empty sky, which squeezed the twilight bands and their labels together),
  and **`YMoon == YSun`** — one shared altitude domain, so nights are comparable. Changing the band changes
  every Y-dependent test.
- The *view* picks the span: `NightChartVM.FrameSpanMinutes(pixelWidth)` → 48 h at/above
  `TwoNightMinimumWidth=1040`, else 24 h (one noon-to-noon night) stretched to the full width.
  `sx = w / (Width * spanMinutes / FullFrameMinutes)`; the hour grid/labels use the same mapping, not raw pixels.
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
  unreadable. Keep `moonrise`/`moonset` worded at the horizon crossing, and the four degree labels walked
  top-to-bottom so they can never stack in a short panel.
- The 0° horizon has its own pen: 1.6 px `PlannerPalette.Horizon(dark)`, drawn after the 0.6 px twilight lines.
  Hour gridlines are every 4 h, not every 2 h.
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

Standing verify order for chart work: unit tests (expect 26) → the `[Explicit]` smoke render at 1200 and 720 px
→ `read_image` → ask the user to close N.I.N.A. → `-t:DeployPlugin` → `Get-FileHash` against
`artifacts\stage\Debug` → relaunch → log check. Re-run the deploy after any commit that touches `Plugin.props`:
the deployed DLL kept reporting 1.0.0.0 through two chart deploys because the staged build predated the stamp.

## Open items and decisions
- Deployed at 08:31 (0.2.0.0, `UnphasedNina.dll` 43,008 B, `UnphasedCore.dll` 24,576 B, `astronomy.dll` 162,816 B),
  byte-identical to `artifacts\stage\Debug`. N.I.N.A. was relaunched at 08:31:58 (PID 32908) and its window came
  up, but no log file had appeared for that instance yet when the session ended — so the log-based "plugin loaded
  cleanly" check for 0.2.0.0 is **still owed**; first thing next session: newest `%LOCALAPPDATA%\NINA\Logs\*.log`,
  grep `Unphased`, `not compatible`, `ERROR`.
- `Plugin.props` metadata is catalogue-placeholder: `RepositoryUrl` says `github.com/zakky2k/unphased` (the code now
  lives in `AstroScripts/nina-plugin`), `Description` is "A NINA plugin.", long description / changelog / screenshot
  URLs empty. Fix before publishing to the NINA plugin catalogue.
- Commit convention here is commit **and push** to main; `6c240ce` and the handoff are pushed.
- Offered, not chosen: a manual 1-night / 2-night toggle next to `Tonight ‹ ›`; degree labels on the **left** edge
  as well as the right.
- The user once wrote "12pm utc to 12pm utc +1" while describing the single-night fallback; it is currently anchored
  at **local** noon (11:00 UTC in winter for Prague). Flagged to them, not resolved — confirm which they want.
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
- Keep screenshots and previews small and look at each one once — an earlier session in this workspace spilled ~32 GB
  of embedded near-identical PNGs (root HANDOFF, "Session hygiene").

## How this user works
Enthusiastic, fast visual reviewer: numbered list of chart complaints per round, typos and all — read for intent.
Wants evidence before claims ("show me it works"), closes N.I.N.A. himself when asked, and accepts offered options
(dockable vs options page, automatic vs manual span) with a short yes. Small, honest commits; changelog updated with
the state of the thing, including what is still rough.
