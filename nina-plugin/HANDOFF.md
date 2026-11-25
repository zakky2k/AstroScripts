# HANDOFF — Unphased Night Planner (N.I.N.A. plugin), state at 2026-11-25

For the next session. Parent repo `AstroScripts` (remote `github.com/zakky2k/AstroScripts.git`, main).
Plugin beta committed as **`7883ab7` — feat(nina-plugin): first beta of the dockable night planner panel**,
49 files, version stamped **0.1.0.0**. **Not pushed**: `origin/main` is still `3336987` (the web-app tip).
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
- Tests: `dotnet test AstroScripts\nina-plugin\UnphasedNina\tests\UnphasedNina.Tests\UnphasedNina.Tests.csproj -c Debug -f net8.0-windows --nologo` → **22 passed**.
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
  (`FullFrameMinutes=2880`). X is `XAt(payload,t)`, Y is `YSun(alt)=(90-alt)/180*320` and **`YMoon == YSun`**
  (one shared altitude domain, so nights are comparable).
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
- Row label shows the **evening** date; navigation and the header use the **morning** date (`MorningDay = EveningDay+1`).
- Build is **net8.0-windows** with `NinaPackageVersion 3.2.0.9001`. Do NOT modernize to net10 / 3.3 nightly:
  N.I.N.A. 3.2.1.3002 then rejects the plugin with `requires a minimum version of 3.3.0.1060`.

## NEXT ROUND — the user's items, verbatim (25 Nov)
> *"glyph and text in the actual chart are too small, increase font size a couple of points and glyph by 50%.*
> *including words moonrise and moonset.*
> *if possible make the test into chips with small tight bounding box around the labels and a black background inside the box.*
> *The horizon line at 0 deg alt should be bolder and maybe slightly thicker. each parallel line below horizon and horizon itself should have degree label*
> *too many verticle lines, that dont sem to represent anything, mainly present during the daytime parts of teh graph"*

All of it is inside `src/UnphasedNina/Features/planner/NightChartControl.cs` (+ `PlannerPalette.cs`). Suggested shape:
1. **Fonts + glyph.** The `DrawText` call sites hard-code sizes 7 / 8 / 9 px. Replace with named consts
   (`LabelFs` 7→9, `MarkerFs` 8→10, tip 9→11) and bump the glyph radius const **7 → 10.5 px** (that is the +50 %).
   The glyph deliberately does NOT scale with `sy` — scale only `r`, or the disc becomes an ellipse.
2. **moonrise / moonset wording** already exists (7 px, bottom strip) — after the bump it must stay clear of the
   hour labels at the bottom; consider anchoring each one beside its own crossing instead.
3. **Chips.** Add one helper, `DrawChip(dc, text, x, y, ink)`: measure a `FormattedText`, fill a tight rect
   (`x-3, y-2, w+6, h+4`) then draw the text. Use a translucent near-black (`#CC000000`) plus a 0.6 px border in
   the label ink rather than solid black, so it still reads on the dark theme's `#0E1622` chart background —
   confirm with the user if they want literally solid black. Route every label through it: sunset/sunrise,
   moonrise/moonset, `NN% moon at NN°`, the altitude labels, the hour labels and the legend. Keep the existing
   `Math.Clamp(x, 0, w - labelWidth - 2)` pattern so chips never leave the canvas.
4. **Horizon.** `DrawGrid` draws 0/-6/-12/-18 all with one 0.5 px pen. Give alt 0 its own pen (≈1.4 px, brighter ink
   via a new `PlannerPalette.Horizon(dark)`). Degree labels for those four lines already exist on the right edge —
   they were simply invisible at 7 px, so item 1 + the chip cover most of this. Ask whether they also want the
   numbers on the left edge.
5. **The phantom vertical lines are our own doing**: `DrawTwilightShading` paints one rectangle per ~10-minute sun
   sample (≈1–2 px wide each) with a `+0.5` px overlap; antialiased edges of equal-coloured neighbours read as a comb
   of lines, most obvious in the bright daylight band. Fix by **coalescing runs of the same band into one rectangle**
   (walk the series, emit when the band changes) and dropping the overlap hack; optionally thin the hour gridlines
   from every 2 h to every 4 h. Make the coalescing a pure helper (e.g. `TwilightShadingRuns(series)` returning
   `(x0, x1, band)`) so a plain test can assert the rect count is band transitions + 1 — no WPF needed.
6. Verify in this order: unit tests (expect 22) → the `[Explicit]` smoke render at 1200 and 720 px → `read_image`
   → ask the user to close N.I.N.A. → `DeployPlugin` → hash check → relaunch → log check.

## Open items and decisions
- The deployed DLL still reports **1.0.0.0** (it was built before the version stamp); the next deploy shows 0.1.0.0.
- `Plugin.props` metadata is catalogue-placeholder: `RepositoryUrl` says `github.com/zakky2k/unphased` (the code now
  lives in `AstroScripts/nina-plugin`), `Description` is "A NINA plugin.", long description / changelog / screenshot
  URLs empty. Fix before publishing to the NINA plugin catalogue.
- Commit convention here is commit **and push** to main — this beta is local only, push when the user says go.
- Offered, not chosen: a manual 1-night / 2-night toggle next to `Tonight ‹ ›`; narrowing the Y domain to about
  +50/-32° so the curves fill the plot (touches every Y mapping and its tests).
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
