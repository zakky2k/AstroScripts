# HANDOFF — Unphased Night Planner (N.I.N.A. plugin), state at 2026-11-25 (evening, sixth review round)

For the next session. Parent repo `AstroScripts` (remote `github.com/zakky2k/AstroScripts.git`, main).
The tip of `main` is the **sixth-pass commit, version 0.6.1.0**, which undoes the start-up crash that
**0.6.0.0 shipped with** (it threw `TargetParameterCountException` on the interface thread; see the last
section). Built, tested (52 passing), deployed at 18:44 and relaunched as PID 32668; the user has not yet
confirmed the panel by eye. `main` is level with `origin/main`.
Root `AstroScripts/HANDOFF.md` covers the web planner; this file covers only `nina-plugin/`.

## What exists and works
A dockable panel inside N.I.N.A. 3.2.1.3002: **Imaging page → the puzzle-piece button in the Info bar**
(not Options). Header `Tonight  ‹ ›  Tue 24 Nov → Wed 25 Nov 2026`, zoom `-` / `+` with the frame written between
them (1 night … 7 nights), the hour-scrub buttons `< hr` `hr >` `off`, and a `night: off/on` button; a hand-drawn
chart (click / drag / wheel / arrows to scrub, moon phase drawn on the scrub line); and a night list below whose
selected row and the chart are kept in sync. Options → plugin page: site latitude/longitude, the night-scheme
checkbox, and the three chart-colour pickers with a reset. Status line reads
`Site 50.0880, 14.4208 - window 31 nights` (10 back / 20 forward around the selected date; profile
`842b5009-…`, Prague). Deployed build loads with **no plugin ERROR** in the N.I.N.A. log.

Layout of `nina-plugin/`: `UnphasedCore` (C# port of the planner's astronomy: `NightCalculator.cs`, `Models.cs`),
`UnphasedCore.Tests` (accuracy vs the web fixtures Prague/summer/Sydney), `UnphasedNina/` — `src` (Plugin.cs,
`Features/planner/{Plugin,VM,chart}`, `Features/settings`), `tests`, `build` (Stage/Deploy/Package/Verify +
`build/vendor/CreateManifest.ps1`), `probe` (payload compared byte-wise to the web `probe/night-*.json`),
`reflect` (scratch reflection dump over NINA assemblies).

## Commands (all proven this session)
- Tests: `dotnet test AstroScripts\nina-plugin\UnphasedNina\tests\UnphasedNina.Tests\UnphasedNina.Tests.csproj -c Debug -f net8.0-windows --nologo` → **52 passed** (~5 s, nearly all of it the marshalling fixture's watchdog).
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
- Moon track runs the **whole frame** like the sun curve (`MoonPolyline` filters nothing). The phase is written
  **only on the scrub marker** — the static glyph and the `NN% moon at NN°` chip at `NightChartVM.NightMoonPeak()`
  were removed on 25 Nov (fourth pass: "lets keep this as main glyph and remove the static glyph and chip").
  `NightMoonPeak()` itself stays, because tests use it; the readout gets the phase from `NightChartVM.MoonIllumAt(t)`.
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
  (sunset/sunrise/moonrise/moonset), `NoteFs=12` (the scrub line's time / altitude / phase readout), `LegendFs=11`,
  `TipFs=13`, `MoonGlyphRadius=9.0` — one constant, the dark disc behind the glyph is drawn at `+2.0`, which is also
  the collision patch for labels underneath it. **Every** label goes through the chip helper (fill `PlannerPalette.ChipFill`, a
  translucent near-black, with a hairline border in the label's own ink) — plain text over the daylight band is
  unreadable. Keep `moonrise`/`moonset` worded **above** the horizon crossing and `sunset`/`sunrise` **below** it
  (`NightChartVM.ChipAbove` / `ChipBelow`, which flip to the other side rather than let a label leave the canvas),
  and the four degree labels walked top-to-bottom so they can never stack in a short panel. No full-height vertical
  marker lines for those events — they are the lines "that dont seem to represent anything", and the scrub line is
  now the only vertical that moves.
- The 0° horizon has its own pen: 1.6 px `PlannerPalette.Horizon(dark)`, drawn after the 0.6 px twilight lines.
  **Default is green** (`#4FC87C`), not the theme's red — the user asked for it, because a red horizon sat on top of
  the red moon track. Night mode keeps a red horizon (`HorizonDefault(dark)`).
- Every chart colour goes through a **three-way precedence: picked hex › scheme default › theme lookup**.
  `PlannerPalette.SetOverrides(sun, moon, horizon)` is fed from the options by `NightChartControl.ApplyChartColors()`
  (hooked in `HookPlanner()`, re-applied on `PlannerChanged`); `ParseHex` accepts `#rgb`/`#rrggbb`/`#aarrggbb` and
  returns **null** for empty or nonsense, so a bad field degrades to the palette instead of throwing in `OnRender`.
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

Standing verify order for chart work: unit tests (expect **51**) → the `[Explicit]` smoke render (nine frames: one
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

## Round of 25 Nov (fourth pass) — green horizon, colour pickers, panel-only night mode → `0.5.0.0`
Asked verbatim:
> *"The nights + button works to expand the chart. 7 nights max seems acceptable."*
> *"The nights - button is greyed out and non responsive, not possible to reduce the chart size"*
> *"lets adjust how many nights are pre-loaded lets add additional 10 in the future, keep past the same."*
> *"i dont like the horizon line as red. lets make it green."*
> *"i love the scrubbing moon glyph, lets keep this as main glyph and remove the static glyph and chip showing at the maximum altitude"*
> *"in the setting page, add color picker for the chart elements, sun path, moon path and horizon, a reset to defaults button (reset colors and take from nina environment palette)"*
> *"is it possible for a night mode button (with a default red color palette, that is the same as the desktop app) be used to affect only the plugin panel in the imaging tab ? i.e. my question does nina allow custom colors to be used and override the app's color palette ? if yes, then introduce a dark mode button."*

Item 1 needed nothing. Item 2 was **not** a chart bug: `+` worked, so `zoomNights` was changing; the `-` button's
`CanExecute` simply was never re-queried in a dockable pane. Fix is in two places on purpose — the view model now
exposes `CanZoomIn` / `CanZoomOut` (INPC) and the XAML binds `IsEnabled` to them ("the braces"), while the command
also calls `CommandManager.InvalidateRequerySuggested()` ("the belt"). Bind enabled-state to properties, never rely
on `CommandManager` requery, in this panel.

Item 7 was a question, and the honest answer is **half yes**: a plugin can define its own `ResourceDictionary` and
shadow N.I.N.A.'s brush keys (`BackgroundBrush`, `PrimaryBrush`, …) for its own subtree, because 1549 of N.I.N.A.'s
brush references are `StaticResource` resolved per-element — but it **cannot** recolour N.I.N.A.'s own implicit
control styles, which bound the palette when the application started. So: the panel's text, chrome and chart go red;
N.I.N.A.'s button *bodies* stay the app's colours (only their labels follow). Merging the dictionary at application
scope would affect every tab, which is the opposite of what was asked. N.I.N.A.'s own app-wide toggle is the eye
button in the title bar (`OptionsVM.ToggleColors()` → `ColorSchemaSettings.ToggleSchema()`, swapping
ColorSchema ↔ AltColorSchema). There is **no theme XAML in N.I.N.A. at all** — the 18 palettes are hard-coded C#
`ColorSchema` objects in `NINA.Core/Utility/ColorSchema/ColorSchema.cs`, and the red night-vision scheme the user
means is the one N.I.N.A. calls **"Dark"**, the shipped default `AltColorSchema`. Its verified hexes are copied into
`PlannerPalette.NightChrome`; note `Secondary` is navy (`#1B2A41`), not red, while `Primary`/`Border` are `#550C18`.
Read-only source snapshot used: `%TEMP%\nina-src-32\nina-Version-3.2` (repo `github.com/isbeorn/nina`, tag `Version-3.2`).

Mechanics worth keeping: `PlannerViewGlue.NightPalette` is an attached property that **assigns a fresh owned
`ResourceDictionary`** to the panel root — `FrameworkElement.Resources` is *inherited*, so mutating the value you get
back would recolour ancestors. The plugin's own XAML uses `DynamicResource` for brushes, which is what lets the
shadow take effect; `FrameworkElement.ResourcesProperty` does not exist, so "is this dictionary ours?" cannot be
asked that way.

## Round of 25 Nov (fifth pass) — one settings object, honest zoom buttons, figures per run → `0.6.0.0`

The user's list, verbatim:

1. "color picker in plugin config screen allows a color to be chosen but doesn't seem to
   effect anything ibn the chart. does it need an 'apply' button to trigger the color change ?"
2. "the - button still doesn't work, it is still greyed out (faded ,darker shade)"
3. "the night mode works great, like it a lot."
4. "still getting the straight path lines under the horizon for the sun and moon as they dip
   below the minimum altitude on the graph."

**Item 1 — no Apply button is needed; the plugin had two `PluginSettings` objects.** The
binding chain was right end to end (options page → `{Binding Settings.X}` on the manifest →
`NightPlannerVM` mirror → chart), so the fault had to be instance identity. N.I.N.A.'s
`PluginLoader.cs` composes the **manifest** at line 356
(`GetContainer(plugin).ComposeParts(manifestImport)`) and then the **parts** — dockables,
templates — at line 464 (`Compose(plugin, manifest.Name)`), and `GetContainer` at line 491
does `new CompositionContainer(catalog)` on **every call**. Two containers, so
`[PartCreationPolicy(Shared)]` produced two shared instances: one behind the options page
(the options DataTemplate's DataContext is the `IPluginManifest` instance) and one injected
into `PlannerDockableVM`. Each writes the same `settings.json`; the live file proved which
one won — written 17:35:41 with `{"darkMode":false,"sunColor":"","moonColor":"","horizonColor":""}`,
i.e. the panel's untouched copy stamped over the pick. Fix: the MEF export is gone and
`PluginSettings.For(IProfileService)` hands out one instance (`Shared` + a lock), `Plugin.cs`
imports `IProfileService` and exposes `Settings` as that instance, `Initialize()` calls
`Settings.Initialize()`, `Teardown()` disposes `Shared` (and dispose drops the reference, so
a reload builds a fresh one rather than reviving a stopped object). Side effect in the user's
favour: the night-mode checkbox on the options page now reaches the panel, which it never had.

**Item 2 — the fourth-round fix was not enough, because of an AND rule.**
`ButtonBase.IsEnabledCore()` returns local `IsEnabled` **AND** `command.CanExecute(...)`. In a
dockable pane the requery is unreliable, so a `RelayCommand` carrying a predicate re-greys
the `-` button whatever the `IsEnabled="{Binding Planner.CanZoomIn}"` binding says. Both zoom
commands now pass `() => true` and the INPC-bound `IsEnabled` is the only gate; a click past
an end of the range is a harmless no-op because `NightPlannerVM.SetZoom` clamps. Rule worth
remembering: **in this plugin, a command predicate on a dockable toolbar is a bug — put the
gate in the binding.**

**Item 4 — the polyline was already cut; the renderer was joining the pieces.** The trimmed
string is correct (`… 139.42,320.00 | 353.18,320.00 | 354.17,318.72 …`, runs separated by
`|`), but `NightChartControl.DrawCurve` called `BeginFigure` only for the very first point of
the whole string, so each later run was continued with a `LineTo` — a straight chord along
the bottom edge. Pixel evidence from `planner-preview-24-z1-at-1200.png` (before the fix):
row 359 carried **518 sun-coloured pixels spanning x=332..849** (canvas x≈277..707, exactly
the below-floor gap) while the rows above it carried 5–6. Now each run starts its own figure;
the nine re-rendered smoke frames measure 0–2 px of sun or moon colour on the bottom rows.
`PlannerChartPixelTests` guards it **on pixels**: 260 px flat run with the old renderer
(verified by reintroducing the bug), a handful now.

**Extra — bounded dispatch onto the interface thread.** Writing the colour test exposed an
unbounded `Dispatcher.Invoke` in `NightPlannerVM`: once some other thread had owned an
`Application`, the settings→planner announcement waited forever and the whole test run hung.
`OnUi` now dispatches with a two-second deadline, catches a shut-down dispatcher and falls
back to the calling thread — the same hazard would have hung N.I.N.A. on exit if a settings
write landed during teardown.

**Tests: 47 → 51, all passing.** New: `PlannerChartPixelTests` (floor crossing, above);
`TheOptionsPageAndThePanelShareOneSettingsObject` (`For` twice → same object, `Shared` null
after `Dispose`); `TheZoomCommandsNeverRefuseSoOnlyTheBindingGreysAButton`;
`AChartColourSetAnywhereReachesThePlannerAndAnnouncesItself`. `OptionsLookupUsesTheManifestName`
now builds `new Plugin()` because `Settings` is a computed property, not a settable one.
Note for whoever runs these next: a fixture that creates a WPF `Application` on its own thread
owns `Application.Current` for the whole test process — that is how the hang started.

Deploy: `0.6.0.0` built and pushed; the app (PID 17940) was still running 0.5.0.0, so
`-t:DeployPlugin` waits for the user to close N.I.N.A.

## Round of 25 Nov (sixth pass) — 0.6.0.0 crashed on start-up → `0.6.1.0`

User report, verbatim: *"this latest build does not execute successfully. upon nina launch i get the attached
error … if i choose to recover, i am able to view the plugin screen but no chart is displayed"* — the dialog
being "Unhandled Exception occurred … Should the application try to recover from the error?".

The log held one interesting line, and no plugin frame in the stack:
`System.Reflection.TargetParameterCountException: Parameter count mismatch` under
`System.Windows.Threading.ExceptionWrapper.InternalRealCall`
(`20261125-181411-3.2.1.3002.23312-202611.log`, 18:19:15, eight seconds after start).

Cause: the bounded dispatch added in the fifth pass —
`ui.Invoke(new Func<bool>(() => { … }), DispatcherPriority.Normal, TimeSpan.FromSeconds(2))`. Asking
`Dispatcher.Invoke` for a return value *and* a timeout hands the delegate to
`Delegate.DynamicInvoke` with an argument list, and a delegate taking no arguments comes back as
"Parameter count mismatch" — thrown on the interface thread, inside WPF's own wrapper, which is why it
looked like an application fault rather than a plugin one.

Fix: `ui.Invoke(action)` and nothing else, with `HasShutdownStarted` / `HasShutdownFinished`
checked first and an `InvalidOperationException` catch, so the dispatcher-already-gone case runs on
the calling thread. The deadline was the right instinct; the overload was not.

The first repair attempt was wrong in a way worth remembering: `ui.Invoke(action,
DispatcherPriority.Normal)` reads as the plain form, but WPF has no `Invoke(Action, priority)`
overload, so it binds to `Invoke(Delegate, DispatcherPriority, params object[])` — the reflection
path again — and aborted the whole test run with the same exception
(`PlannerChartPixelTests` → `PluginSettings.cs:183` → `NightPlannerVM.cs:68`, stack frame
`Dispatcher.Invoke(Delegate method, DispatcherPriority priority, Object[] args)`).

**Law: the call must bind to `Invoke(Action)`.** No priority, no timeout, no return value; if the
caller needs a result, capture it in a local.

`tests/…/Features/planner/PlannerThreadMarshallingTests.cs` reproduces the shape: an STA thread runs
`Dispatcher.Run()`, a worker thread writes `PluginSettings.SunColor`, and the test asserts the interface
thread took no exception and that the announcement arrived on it. Against the 0.6.0.0 call it fails with the
same `TargetParameterCountException` — that was verified by putting the bad call back for one run; against
this build it passes.

Two harness rules, each learned by hanging the suite for ten minutes:
- the fixture must **not** create a `System.Windows.Application`: `CompositionTests` owns the only instance
  the process is allowed, and shutting that one off also breaks `Application.LoadComponent` for everything
  later (`The Application object is being shut down`);
- a hand-pushed `DispatcherFrame` pushes at Normal and its `PushPriority` is read-only, so an exit item
  queued at Background never runs and the frame waits forever. `Dispatcher.Run()` plus a five-second
  watchdog calling `InvokeShutdown` is the shape that works.

**Tests: 51 → 52, all passing** (about five seconds of the wall clock is that watchdog).

Deploy: 0.6.1.0 deployed at 18:44 after the user closed the app (`UnphasedNina.dll` 73,728 B, all five
deployed files SHA256-identical to `artifacts\stage\Debug`, deployed `FileVersion` 0.6.1.0), relaunched as
PID 32668. There is **no log file for PID 32668**, which means nothing was logged — no crash, no
compatibility complaint — but the load is still unproven: `Process.Modules` on 32668 lists 183 modules and
**no** `UnphasedNina.dll`, which I could not explain. Needs the user's eyes on Imaging → puzzle-piece.

## Open items and decisions
- **0.6.1.0 is the deployed build**: deployed 18:44 on 25 Nov (`UnphasedNina.dll` 73,728 B, `FileVersion` 0.6.1.0,
  `UnphasedCore.dll` 24,576 B, `astronomy.dll` 162,816 B), every deployed file SHA256-identical to
  `artifacts\stage\Debug`; relaunched as PID 32668. 0.6.0.0 sat between those two and crashed the application on
  start-up (previous section). What is still **unproven by a human**: the colour pickers reaching the chart, the
  `-` button, the per-run figures, and whether the plugin loads in PID 32668 at all — that process has no log file
  and does not list `UnphasedNina.dll` among its modules. The colour dialog has never been opened in the running app.
- `Plugin.props` metadata is catalogue-placeholder: `RepositoryUrl` says `github.com/zakky2k/unphased` (the code now
  lives in `AstroScripts/nina-plugin`), `Description` is "A NINA plugin.", long description / changelog / screenshot
  URLs empty. Fix before publishing to the NINA plugin catalogue.
- Commit convention here is commit **and push** to main; `6c240ce` and the handoff are pushed.
- Offered, not chosen: degree labels on the **left** edge as well as the right.
- **Done in the fourth pass instead:** the deeper computed window. `NightPlannerVM.PastNights = 10` /
  `FutureNights = 20` (public, so a test can pin the shape) → 31 payloads; the user asked for "+10 in the future,
  keep past the same". The first compute is a little longer; the blank right edge on a 7-night frame near the end of
  the window is mostly gone.
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
  single STA thread, or WPF throws. In this suite `CompositionTests` owns it — any other fixture that builds one
  fails the rest (`Cannot create more than one System.Windows.Application instance`), and one that shuts it down
  breaks every later XAML load (`The Application object is being shut down`).
- A `Dispatcher` call that binds to `Invoke(Delegate, priority, params object[])` — the explicit delegate form,
  the generic `Invoke<T>(Func<T>, priority, timeout)` form, **or the innocent-looking
  `Invoke(action, DispatcherPriority.Normal)`**, since WPF has no `Invoke(Action, priority)` overload — throws
  `TargetParameterCountException` on the interface thread when the delegate takes no arguments, with WPF's
  `ExceptionWrapper` as the only thing in the stack. That crashed 0.6.0.0 at start-up and aborted a test run during
  the repair. Bind to `Invoke(Action)`.
- Stitching consecutive payloads: the sanity check has to measure the **gap between successive `FrameNoon`s**, not
  compare each noon against an expected one — the expected-difference is 0 h for a correct sequence, so the guard
  rejected every slice after the first and the "3-night" frame silently held one day. When a stitch looks wrong,
  print the `FrameNoon`s and the gaps from a scratch `[Test]`, then delete the scratch file.
- `Math.Min` / `Math.Max` have **no `DateTimeOffset` overload**; the compiler picks `byte` and reports
  `CS1503: cannot convert from 'System.DateTimeOffset' to 'byte'`. Write the ternary instead.
- A command's `CanExecute` in a **dockable pane goes stale**: `CommunityToolkit.Mvvm` relies on
  `CommandManager.RequerySuggested`, which does not fire when a dockable's own command changes state. Symptoms were
  exactly the user's item 2 — `+` worked, `-` stayed greyed. Expose INPC booleans and bind `IsEnabled`.
- N.I.N.A.'s brush keys are **`StaticResource` references resolved per element** (1549 of them vs 3 `DynamicResource`),
  which is why a plugin-local dictionary shadows them for the plugin's subtree — and why the same dictionary cannot
  reach N.I.N.A.'s own control styles, or another tab. App-scope merging works but is not scoped.
- `<UseWindowsForms>true</UseWindowsForms>` (needed only for `ColorDialog`) injects `System.Drawing` and
  `System.Windows.Forms` **implicit usings**, which turn every `Brush` / `Size` in the WPF code into `CS0104`
  ambiguity. `UnphasedNina/Directory.Build.targets` removes those two usings; do not delete it.
- `RelayCommand` is ambiguous between `CommunityToolkit.Mvvm.Input` and **`NINA.Core.Utility`**; the settings VM uses
  a `using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;` alias.
- The plugin options JSON is written from **fields**, so its keys are camelCase (`darkMode`, `sunColor`). Reading with
  `nameof(SunColor)` finds nothing and silently returns empty — the persistence test caught exactly that.
- The plugin options file is the **live one** (`%LOCALAPPDATA%\NINA\PluginSettings\09572065-…\settings.json`), so the
  settings tests overwrite the user's real choices. `FeatureTests` restores what it changed; anything new there must
  clean up after itself too, or split off a temp-path variant. Measured on 25 Nov: a suite run from the agent's
  sandboxed shell left that file's stamp at 17:35:41 untouched, even though a test that writes a colour and reads it
  back passed — so from inside the sandbox the write may be redirected rather than landing on the live path. Unresolved;
  treat "re-pick your colours after a suite run" as the safe assumption either way.
- Keep screenshots and previews small and look at each one once — an earlier session in this workspace spilled ~32 GB
  of embedded near-identical PNGs (root HANDOFF, "Session hygiene").

## How this user works
Enthusiastic, fast visual reviewer: numbered list of chart complaints per round, typos and all — read for intent.
Wants evidence before claims ("show me it works"), closes N.I.N.A. himself when asked, and accepts offered options
(dockable vs options page, automatic vs manual span) with a short yes. Small, honest commits; changelog updated with
the state of the thing, including what is still rough.
