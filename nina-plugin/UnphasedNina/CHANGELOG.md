# Changelog

## 0.6.0.0 — picked colours arrive, the minus button works, and nothing lies on the floor

- **A colour picked on the options page now repaints the chart, without an Apply button.**
  The wiring was live all along; there were two settings objects. N.I.N.A. composes a
  plugin's manifest at `PluginLoader.cs:356` and its dockable panels separately at `:464`,
  and `GetContainer` (`:491`) builds a **new** `CompositionContainer` for each call — so
  `[PartCreationPolicy(Shared)]` gave the options page one `PluginSettings` and the panel
  another. Both write the same `settings.json`, and the panel's copy (all three colours
  empty) was written last, which is why the file read
  `{"sunColor":"","moonColor":"","horizonColor":""}` seconds after a pick. The plugin now
  asks for the object itself through `PluginSettings.For`, so the manifest and the panel
  hold the same instance. Side effect, for the better: the night-mode checkbox on the
  options page reaches the panel too, which it never did before.
- **The `-` button un-greys.** A WPF button's enabled state is the AND of its own
  `IsEnabled` and its command's `CanExecute`, so the binding added in 0.5.0.0 was still
  being overridden by the command's predicate, and a dockable pane does not reliably
  re-query commands anyway. The zoom commands now always allow execution, the binding is
  the only thing that greys a button, and clicking past an end of the range is a no-op
  rather than a refusal.
- **The sun and moon tracks stop at the bottom of the chart.** The data was already cut —
  the polyline is split into runs where a track leaves the window — but the renderer began a
  new figure only at the very first point of the whole string, so the pen joined the runs
  with a straight line across every gap. On a one-night Prague frame that was **518 pixels**
  of sun colour hugging the bottom edge, which reads as the sun sitting on the floor for
  hours. Each run is now its own figure; re-rendered frames measure 0–2 pixels of track
  colour along the bottom edge.
- A settings change or a finished calculation that reaches the panel while N.I.N.A. is
  closing can no longer hang the application: the dispatch onto the interface thread has a
  two-second deadline and falls back to the calling thread instead of waiting on a
  dispatcher that will never run again.

Still rough: the single settings object is one the plugin creates itself, so unloading the
plugin without calling `Teardown` would leave the old instance behind — reload has not been
exercised inside the running application; the colour fix is proven by the file and by tests,
but its effect on the live panel has to be confirmed by eye; the test suite still writes the
real plugin options file, so running it clears any colours picked in the live app; scrubbing
to the hour of a moonrise or moonset puts the phase glyph on top of that chip; N.I.N.A.'s own
button bodies keep the application's colours in night mode; a week on a narrow panel drops
roughly every other horizon label; a single-night frame is anchored at local noon; the mouse
wheel over the chart steals panel scrolling.

## 0.5.0.0 — colours you can set, and a night mode that stays in the panel

- The **horizon is green** by default. Its old colour came from the theme's own red, which
  put it on top of the red moon track and made it read as one more curve. Night mode keeps
  a red horizon, because there everything else is red too.
- The moon's phase is written **once**, on the scrub line. The static glyph and the
  `NN% moon at NN°` chip that used to sit at the highest point of the moon's track are
  gone; the phase now reads in the scrub line's own readout
  (`13:00  sun +20°  moon +5°  23% moon`) and the glyph on the marker is larger. Where the
  moon's phase matters is the hour being looked at, not one hour chosen by the ephemeris.
- **Zoom buttons know their own limits.** The enabled state comes from the view model
  rather than from the commands: in a dockable pane nothing re-queried the `-` button after
  `+` had been clicked, so it stayed greyed and the frame could only ever grow.
- **The window is deeper ahead than behind** — 10 nights back, 20 forward, where it was 10
  either side. Planning looks forward, and a week-long frame picked near the old end of the
  window had nothing left to stitch from, which showed up as blank space on the right.
- The **options page has colour pickers** for the sun path, the moon path and the horizon.
  Each row shows a swatch of what the chart actually paints right now (a picked colour, or
  what the current scheme resolves to), a hex field that can be typed exactly, and `Pick`
  for the colour dialog. An empty field means follow the palette. **Reset colours to
  defaults** clears all three: sun and moon go back to the live N.I.N.A. theme brushes, the
  horizon back to green. The picked values are stored with the other plugin options.
- The panel has a **night button** (`night: off` / `night: on`, the same setting as the
  checkbox in the options). It puts the values of N.I.N.A.'s own red "Dark" scheme into the
  panel's resource dictionary, and WPF stops its colour lookup there — so the panel, its
  text and the chart go night-red while every other tab keeps the user's palette. The
  app-wide version of this is still N.I.N.A.'s own eye button in the title bar.

Still rough: the colour dialog has never been opened inside the running app (it is a WinForms
`ColorDialog`, the only thing in the plugin that needs WinForms); scrubbing to the hour of a moonrise or moonset
puts the phase glyph on top of that chip; the plugin can recolour what it draws, but not N.I.N.A.'s own button
style —
those styles bound the palette when the application started, so the button bodies keep the
app's colours and only their labels follow the night scheme; the chart keeps its own reds
in night mode rather than the theme's darker ones, because they were reviewed for
legibility; the test suite writes the real plugin options file, so running it clears any
colours picked in the live app; a week on a narrow panel still drops roughly every other
horizon label; a single-night frame is anchored at local noon; the mouse wheel over the
chart still steals panel scrolling.

## 0.4.0.0 — a floor under the sky, and a week in one frame

- The altitude window runs **+90° down to −24°**. The −90 floor was never asked for and
  bought nothing: nothing on the chart happens below about −30°, and it squeezed the
  twilight bands and the sun's winter dip into the bottom tenth of the panel. −24° keeps
  the three twilight lines (−6 / −12 / −18°) far enough apart to label at panel height
  and clear of the clock row. The zenith is unchanged, so a high summer moon still
  lands where it belongs.
- **Zoom, from one night to a week.** `-` / `+` in the header, with the frame written
  between them. A wider frame is built in the plugin by stitching consecutive nights of
  the computed window into one payload (`NightFrame.For`): each slice is cut at its *own*
  local noon, so daylight saving transitions fall where they belong, and a duplicated
  moonrise/moonset at a seam is dropped. If a night dropped out of the window entirely
  (polar day or night), the stitch stops rather than bridging the hole. The clock grid
  thins as the frame widens — 4 h, then 8 h, then 12 h — or the times merge into a band.
- **Nothing draws past the edge of the frame.** The sun and moon curves are cut at both
  ends at the interpolated crossing, and twilight shading is clamped to the canvas.
  Before this a one-night chart drew 48 hours of curve and colour off the right-hand end
  of the panel, because WPF does not clip what `OnRender` paints.
- Every night in a zoomed frame gets its **own sunset / sunrise labels**, read off the
  plotted curve where it crosses the horizon rather than from the selected night's pair,
  so a label never sits where no curve crosses.
- The scrub marker's moon dot is the **same phase glyph** the peak of the night carries,
  so dragging across the chart also shows what the moon looked like at that hour. It
  sits on a small dark disc so it stays readable when a label is underneath it, and is
  left off entirely when the moon is below the plotted window — the readout still names
  its altitude.
- **Labels step around each other.** A chip that would land on one already drawn is left
  out, clock labels never slide off their own gridline, and the degrees column keeps its
  corner of the panel.

Still rough: a week on a narrow panel drops roughly every other horizon label — legible,
but less informative than zooming in; rows near the end of the computed window have
fewer nights after them, so their zoomed frame is blank on the right; a single-night
frame is anchored at local noon, so daylight occupies both ends of the panel (worth
comparing with how the desktop planner anchors its one-night view); the mouse wheel over
the chart still steals panel scrolling; zoom is covered by automated tests but has not
yet been driven by hand in the running app.

## 0.3.0.0 — the whole sky back, and a scrub marker

- The altitude window is **±90° again**. The +80 / -32 window introduced in 0.2.0.0
  made the twilight bands roomier, but it cut high moons out of the chart and pressed
  the deep half of the night against the bottom edge. The degree labels are close
  together as a result; what keeps them readable is the chips and the walk that pushes
  a crowded label down, not a shortened axis.
- The sun and moon curves now **stop where they leave the chart** instead of running
  flat along the bottom edge. A curve that goes out and comes back is drawn as separate
  runs, joined at the edge at the interpolated crossing point — a flat line along an
  edge looked like a feature of the sky.
- `sunset` / `sunrise` labels hang **under** the horizon, mirroring `moonrise` /
  `moonset` above it, so the two sets of words no longer compete for the same pixels.
- The fixed vertical marker lines are **gone** (the sunset/sunrise solids and the
  moonrise/moonset dashes). They were the verticals that looked meaningful but were not.
- In their place, an **hour-by-hour scrub marker**: click or drag on the chart, scroll
  the wheel over it, press ← / → (or Home / End, Escape to clear), or use the new
  `< hr` `hr >` `off` buttons in the header. It snaps to whole hours and prints the clock
  time with the real sun and moon altitudes at that instant, plus a dot on each curve.
  Stepping can never walk it off the panel, and it stays put when you change night, so
  the same clock hour can be compared across nights.

Still rough: the scrub input itself (drag, wheel, arrow keys) is not covered by the
automated tests — only the snapping, clamping and the readout are — and the readout chip
can land on top of the right-hand degree labels when the marker is near the right edge.

## 0.2.0.0 — chart readability pass

Nothing new is computed; the chart is made legible at panel size.

- Chart text a couple of points larger (labels 10, markers 11, illumination readout 12,
  tip 13) and the moon phase glyph 50 % bigger.
- Every label is now a chip: a tight rounded box with near-black backing and a hairline
  edge in the label's own colour, so words stay readable over the bright daylight band.
- `moonrise` / `moonset` are labelled at the horizon, where the moon actually crosses it.
- The 0° horizon gets its own heavier, brighter line, and each altitude line
  (0 / -6 / -12 / -18°) carries its degrees on the right, never stacked on each other.
- Twilight shading merged into one rectangle per stretch of band instead of one per
  sample: the faint vertical seams that read as meaningless lines across the daylight
  parts are gone. Hour gridlines every 4 hours rather than every 2.
- Altitude window narrowed from ±90° to +80 / -32: empty sky used to occupy two thirds
  of the panel, which is what squeezed the twilight bands and their labels together.
  (Reverted in 0.3.0.0: it also cut off high moons and flattened the deep night.)

## 0.1.0.0 — very early beta

First build that runs inside N.I.N.A.: a dockable **Unphased Night Planner** panel
(Image page → the puzzle-piece button in the Info bar) drawing the selected night
from N.I.N.A.'s own astronomy.

- Dockable panel with a Tonight / step-by-one-night header, a window of nights below
  the chart, and the selected row kept in sync with what the chart shows.
- Hand-drawn chart: sun altitude curve, moon track across the whole frame, the
  standard 0 / -6 / -12 / -18° twilight bands painted as background colour,
  labelled sunset/sunrise and moonrise/moonset, and a moon phase glyph — lit limb
  plus terminator, mirrored for the observer's hemisphere — at the moon's highest
  point of that night, labelled with its illumination.
- The time scale adapts to the panel: two nights side by side while they stay
  legible, otherwise one noon-to-noon night stretched to the full width.
- The astronomy is a C# port (`UnphasedCore`), checked against the web planner's
  Prague / summer / Sydney fixtures.
