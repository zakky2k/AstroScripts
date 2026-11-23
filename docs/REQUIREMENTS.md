# AstroScripts — Observation Planner

Status: draft for review · Owner: zakky2k · Last updated: 2026-11-23

## 1. Vision

A phone-first web tool that answers one question at a glance: **is the sky
usable tonight, and for how long?** It answers visually — a single chart of the
dark window in which the Moon is drawn as a recognisable, physically accurate
icon travelling its real altitude path, sitting on top of shaded twilight
bands.

The same information later reaches an Apple Watch face complication, so a
user standing outside at 23:00 can see remaining dark time and current moon
illumination without unlocking a phone.

The existing repository is the calculation backbone. Its moon-calendar code
already computes phases, moon altitude on phase days and twilight bands; the
planner is the interactive, location-aware front end of that same math.

## 2. Goals

| # | Goal | Why it matters |
|---|---|---|
| G1 | One chart per night, sunset to sunrise, immediately legible | The whole product value is in a single glance |
| G2 | Moon icon whose illuminated fraction is accurate and progresses smoothly toward full, then wanes | Aesthetic hook and the actual decision input for astrophotography |
| G3 | Civil / nautical / astronomical twilight shaded distinctly at both dusk and dawn | Tells the user when the sky is genuinely dark, not just when the sun is down |
| G4 | Time on x, altitude on y, with the Moon's true path across the window | Matches the mental model already used by N.I.N.A. Sky Atlas and Framing Assistant |
| G5 | Correct for any observer location, not just Prague | Current scripts hardcode 50° N / Prague longitude |
| G6 | Astronomy computation isolated behind one contract | Lets deployment target change without touching the UI; enables the Watch later |
| G7 | Same chart reusable in the existing PDF calendar | One renderer, two outputs |

## 3. Non-goals for the first milestone

Explicitly out of scope, to keep the first release small and reviewable.

- Target catalogue, deep-sky object search, constellation figures.
- Mount, telescope, camera or filter management.
- Framing footprints, sensor overlays, rotator angles.
- Weather and cloud-forecast integration.
- Session logging, sub-frame tallying, WBPP reporting in the browser.
- User accounts, authentication, saved locations beyond local storage.
- Native iOS/watchOS application (the Watch deliverable is a later milestone).

## 4. Chart specification

### 4.1 Axes

- **x axis** — a **fixed single-night frame: local noon → midnight (plot
  centre) → noon+1**, re-pinned 23 Nov after live review. The left edge is
  ALWAYS 12:00, the exact centre is always 00:00, the right edge is always
  12:00 of the following day — a 24-hour span showing exactly one afternoon,
  one sunset, one twilight descent, one dark period, one sunrise and one
  morning. (The earlier "two-day" wording was a mistake: it described a
  noon→noon+2 render that showed a second dusk/dawn pair at the edges; that
  view is superseded.) Every night renders at identical pixel geometry, so
  scrolling date by date is a pure translation: the gridlines, labels and
  plot frame never move; only the twilight bands and the moon curve change.
  Daylight hours outside the night window are painted in the lightest band at
  low opacity, flat — never with a gradient that fakes a transition. The
  sun's position is expressed **entirely through the background gradient** —
  sunset and sunrise are where the gradient crosses the horizon colour, not
  separate graphical elements (amber bars removed after review, 23 Nov). The
  moon curve is the only thing drawn on top of the bands. The
  midnight centre marker carries the date, so rollover across two calendar
  dates is never ambiguous.
- **Realtime behaviour** — added 23 Nov with the live app. The chart is a
  live view: the now line tracks the observer's clock continuously (minute
  ticks), and exactly ONE moon glyph sits at the moon's current position on
  its arc, advancing along the curve as time passes. The static multi-glyph
  phase trail from the design probe is not part of the app view. A **time
  control** (scrub slider + ±1 h steps, placed ABOVE the chart) moves that
  focus point — and with it the moon glyph — anywhere in the frame; "live"
  re-arms clock tracking. An editable **TIME field** beside the night
  stepper mirrors the focus time to the SECOND and accepts typed HH:MM[:SS]
  or h:mm AM/PM; a small **"24" button inside the box** toggles every clock
  on screen between 12 h (AM/PM suffix) and 24 h display. The moon glyph is
  **pinned to a point on its arc**: whenever the focus sits where the moon is
  below the horizon, the glyph parks at the nearest rise/set ON the curve —
  no CSS tween that would cut a chord across the sky during fast scrubbing
  (review 23 Nov). Scrub/step additionally park the focus itself at the
  nearest rise/set when it would land below horizon; live clock ticks skip
  that snap — the moon genuinely sets and rises in real time, and the pin
  keeps the glyph on the curve regardless.
  Scrubbing past the left/right edge flips to the previous/next night; the
  incoming payload simply **re-arms live clock tracking** (review 23 Nov: an
  earlier "park at that night's moonrise" pre-step ping-ponged the focus
  between two nights and tripped React's update-depth guard). Mouseover
  anywhere in the plot shows a tooltip with the coordinates under the cursor:
  wall-clock time on x, altitude in degrees on y. The control row is
  **static**: every item has a fixed width (only the slider flexes) and the
  "live" button is always present, merely dimmed while live — it never pops
  into or over the cursor's spot, because the intended interaction is
  *repeatedly* pressing "1 h ▶" to walk the moon across the frame until the
  chart flips to the next night.
 - **Moon movement ends at moonset** — review 23 Nov: scrubbing and hour
   steps follow the moon only along its arc. The last in-frame moonset is the
   arc end; once the focus sits there, further "1 h ▶" presses (or dragging
   the slider past it) do NOT slide an absent moon through empty post-set sky
   — they flip straight to the NEXT day's chart, which is how a +1 h skip
   triggers the next-day view. The slider itself is clamped to the arc end.
   Live clock tracking is exempt: while live, the now line keeps moving to
   the frame edge as it always did.
 - **Zone DST nudge ("+1" button)** — review 23 Nov: a small button inside
   the right end of the ZONE box shifts the LIVE clock by whole hours for
   zones whose daylight-saving state disagrees with the IANA zone (the live
   time showed one hour fast and needed to fall back). It cycles +1 → +2 →
   +3 → −3 … → off. The offset applies to the observer's clock only —
   ephemeris events (moonrise, twilight, darkness) always stay on the true
   zone, so the chart's data never shifts, just the "now" marker.
 - **DARK scheme** — review 23 Nov: a **"DARK" button beside the TIME box**
   switches the sky palette from blue to red at *identical lightness*: every
   colour is its blue counterpart with the B channel moved into R (civil
   #27476E → #6E2727, page #0A0E18 → #180A0A, daylight #4E7FB5 → #B54E4E…),
   so band contrast and hierarchy are exactly preserved. The Moon's warm
   ivory curve and the red now line stay untouched — only sky state changes
   hue. (Contrast variants were reviewed; final palette pending user's pick,
   see §11.)
- **Transition timestamps** — review 23 Nov (refined twice): every official
  stage (sunset, each twilight edge, sunrise) prints its exact HH:MM at 45°
  **strictly BELOW the chart** — in the below-0° strip, never inside the plot.
  A short leader line runs from the middle of the label's last letter up to
  its transition line. Dusk-side labels sit left of their line sloping
  up-right into it; dawn-side labels mirror on the right. Stage words are
  abbreviated (naut., astro.) so the tight twilight sequence never collides.
- **Daylight tone** — review 23 Nov: the daylight fill must be a paler,
  brighter blue than the lightest twilight band (sunset/sunrise edge); day
  must never read darker than dusk. Flat fill, no gradient.
- **Reading-order invariant** — added 23 Nov after review. Left to right the
  chart must always run: Day 1 daylight → sunset → twilight (civil, nautical,
  astronomical) → darkest → **local midnight at the exact centre** → darkest →
  twilight in reverse → sunrise → Day 2 daylight. The sun's altitude is a
  single smooth curve across the frame with its minimum near midnight, so the
  band sequence can never fold back on itself; even on midsummer nights the
  darkest moment stays at the centre (shallowest twilight, but still centred).
  This is a sanity check the renderer must enforce, not rely on.
- **Moon curves span their whole arc** — a moon rising at 20:00 and setting
  during the following day is drawn across that entire arc; nothing about the
  frame clips or truncates it (the only cut is altitude ≤ 0°). The arc stays
  visible through the DAYLIGHT hours of the frame too, so its rise point can
  be seen after sunrise (review 23 Nov: the API therefore samples the moon's
  track across the whole noon→noon+2 span, not just sunset→sunrise).
  Moonrise and moonset markers always appear if they fall inside the
  noon→noon+1 window. Their labels are **horizontal chips INSIDE the plot,
  just above the horizon** (review 23 Nov): dark background, light-but-not-
  white text, a thin ivory border matching the curve, drawn LAST so nothing
  covers them. Slots are FIXED by construction — rightmost event pinned to
  the right plot edge, next one to the left edge — so chips can never overlap
  and no measure-and-settle loop is needed; each chip carries a leader line
  plus an exact crossing dot where the moon path reaches zero. The label
  prints the TIME ("rise 14:57"), and the header readout shows the
  corresponding moonrise for the current focus.
- **y axis** — altitude in degrees above the geometric horizon, **truncated at
  0°**: below-horizon geometry does not exist on this chart. Major graduations
  every 10°, from 0° to 90°, with faint gridlines and a distinct horizon rule.
  The moon curve is clipped to altitude > 0; moonrise and moonset markers sit
  exactly on the horizon line, labelled.
- **Zoom** — desktop and phone only: pinch/scroll zoom reveals per-moment
  altitude and illumination % tooltips. The watch keeps only the phase icon;
  no chart.
- Altitude reference: **standard astronomical altitude** (geometric horizon,
  no refraction correction) for the first milestone. Recorded as an open
  question in §11.

### 4.2 Twilight bands

Vertical bands spanning the full height of the plot, with a smooth gradient
between adjacent bands rather than hard edges. Two sets, mirrored around
local midnight. Palette decided 23 Nov: near-black night, progressively
lighter blues toward daylight; steps chosen roughly equal in perceptual
lightness so all four read as distinct zones (see `design/palette.py`, the
single source of truth):

| Band | Solar altitude range | Colour (dark theme) |
|---|---|---|
| Day | sun > 0° | lightest band at low opacity, outside the night window only |
| Civil | 0° to −6° | `#27476E` |
| Nautical | −6° to −12° | `#18304F` |
| Astronomical | −12° to −18° | `#0D1B31` |
| Night | sun < −18° | `#05070C`, near-black — the usable imaging region |

A night with **no** astronomical twilight — common at high northern latitudes
in June and July, where the sun never reaches −18° — must render honestly as a
permanently lit window, with a visible notice and how low the sun actually
got, rather than pretending to be dark. This is a correctness requirement, not
a cosmetic one.

### 4.3 Moon path

- Plotted as a smooth curve of altitude against time over the whole night
  window, sampled at 10-minute intervals (approximately 150 points per night)
  so the characteristic sinusoidal shape is faithful and never visibly
  polylinear.
- The curve exists only where altitude > 0°; it is clipped at the horizon
  rather than drawn below it. Moonrise and moonset markers sit on the horizon
  line with `rise`/`set` labels.
- A **marker every two hours** while above the horizon, plus one at moonrise
  and moonset, each showing the moon icon at that instant; glyphs nudge apart
  when they would collide with each other or the sunset/sunrise bars.
- Azimuth is not an axis. It is exposed as a tooltip and in the marker's
  accessible text label. Zoom (desktop/phone) reveals altitude and
  illumination % at any moment.

### 4.4 Moon icon

The icon is the visual signature of the product, so it is specified precisely.

- Drawn procedurally as an SVG path: the illuminated limb is a semicircle of
  radius *r*; the terminator is a half-ellipse with semi-minor axis
  *r* · |cos β|, where β is the phase angle. This yields the correct crescent,
  quarter, gibbous and full shapes continuously, with no discontinuity at the
  quarter phase.
- Illuminated fraction *w* = (1 − cos β) / 2, cross-checked against astropy's
  `illum_fraction` to within 0.01.
- Waxing moon is illuminated on the **right** limb as seen by a northern-
  hemisphere observer; waning on the left. In the southern hemisphere the two
  are mirrored. The API returns a `limb` field so the client never guesses.
- The icon is tinted, not merely resized: full moon renders bright, new moon
  renders as a faint disc outline. Illumination drives fill luminance so the
  progression "brightens toward full and dims after" is visible as a glow,
  which was the stated intent.
- A crescent thinner than roughly 1% illuminated renders as an outline plus a
  label rather than a sliver too thin to see.

## 5. Functional requirements

Each requirement is verifiable as written.

- **FR1 Location.** The user sets latitude, longitude and IANA time zone.
  Values persist locally between visits. Default is the repository's existing
  reference site (50° N, Prague longitude, `Europe/Prague`) until the user
  changes it. Acceptance: setting latitude −33.0 renders southern-hemisphere
  limb orientation and correct June/December behaviour.
- **FR2 Date selection.** The user chooses the night to start from, defaulting
  to the current date at the observer's location. Acceptance: selecting 2026-06-21
  at latitude 50° N shows no astronomical twilight and displays the honest
  "no true darkness" notice.
- **FR3 Night window.** Sunset, sunrise, and all six twilight transitions are
  computed for the chosen night and location, and match astropy's
  `altaz`-based altitudes to within 1 minute of time. Acceptance: automated
  comparison against the existing `moon_calendar.py` values for the same site.
- **FR4 Moon track.** Altitude and azimuth sampled every 10 minutes, plus exact
  moonrise and moonset times. Acceptance: the curve is smooth at 10× zoom;
  markers fall within 2 minutes of astropy-computed rise/set.
- **FR5 Moon phase.** Illuminated fraction and phase angle at every marker and
  at local midnight. Acceptance: agrees with astropy to within 0.01 fraction.
- **FR6 Summary strip.** Above the chart: sunset, sunrise, start and end of
  astronomical darkness, total dark minutes, moonrise, moonset, and Moon
  illumination at local midnight. Acceptance: all values present, in the
  observer's time zone, 24-hour clock.
- **FR7 Single endpoint.** All of the above is available from one API
  endpoint; the client performs no astronomy.
- **FR8 Responsive layout.** The chart is usable from a 320 px wide phone to a
  desktop window. Acceptance: at 320 px, hourly labels drop to every second
  hour without overlap; axis text never smaller than 11 px.
- **FR9 Chart export.** The rendered chart serialises to a standalone SVG that
  is embeddable in the existing PDF calendar. Acceptance: a unit test renders
  the chart to a file that opens in an SVG viewer.

## 6. API contract

`GET /api/night?date=YYYY-MM-DD&lat=<float>&lon=<float>&tz=<IANA>`

Response, all times ISO 8601 with explicit UTC offset for the observer's zone:

```json
{
  "location": { "lat": 50.0, "lon": 14.4, "tz": "Europe/Prague" },
  "nightStart": "2026-11-23T16:31:00+01:00",
  "nightEnd":   "2026-11-24T07:41:00+01:00",
  "sun": {
    "sunset":  "2026-11-23T16:31:00+01:00",
    "sunrise": "2026-11-24T07:41:00+01:00",
    "events": [
      { "band": "civil",       "time": "...", "edge": "start" },
      { "band": "nautical",    "time": "...", "edge": "start" },
      { "band": "astronomical","time": "...", "edge": "start" }
    ],
    "trueDarkness": false
  },
  "moon": {
    "samples": [
      { "time": "...", "altitude": 12.4, "azimuth": 118.0,
        "illumFraction": 0.42, "phaseAngle": 1.94 }
    ],
    "moonrise": ["...", "..."],
    "moonset":  ["..."],
    "limb": "right",
    "midnight": { "time": "...", "illumFraction": 0.44, "altitude": 31.2 }
  },
  "summary": { "darkMinutes": 412, "moonUpDuringDark": true }
}
```

Errors are returned as `{ "error": { "code": "...", "message": "..." } }` with
an appropriate HTTP status. Out-of-range latitude or longitude returns 422.

## 7. Architecture

- **Frontend:** Next.js + TypeScript. The chart is hand-composed SVG using
  `d3-scale` for scales and ticks and `d3-shape` for the curve path. No
  general-purpose charting library, because twilight gradients and a
  phase-accurate moon glyph are not series and do not fit a chart abstraction.
- **Astronomy:** Python with astropy, isolated in one package with no
  framework dependency, so it can run as a local API during development and be
  ported or reimplemented deliberately later.
- **Boundary:** the §6 contract is the only coupling. The UI must not
  reimplement any astronomy; the Python layer must not know about HTTP
  serialisation details beyond this shape.
- **Deployment:** undecided (§11). The contract is designed so the choice is
  reversible.

## 8. Non-functional requirements

- **NFR1 Accuracy.** Times within 1 minute and altitudes within 0.1° of
  astropy at the reference site for every year 2026–2035 (sampled nightly).
- **NFR2 Performance.** A full night response generated in under 300 ms warm;
  the chart paints within 100 ms of data arrival.
- **NFR3 Offline during development.** The ephemeris and any lookup tables are
  cached on disk so the app runs with no network, matching the existing
  `--ephemeris` offline behaviour.
- **NFR4 Accessibility.** Every chart element has a text equivalent; a
  screen-reader summary of the night is provided in words. Colour is never the
  sole encoding of a twilight band — band labels are drawn.
- **NFR5 Line endings and reproducibility.** Already enforced: `.gitattributes`
  normalises to LF, `.python-version` pins Python 3.13, `requirements.txt`
  pins exact versions.

## 9. Milestones

1. **M1 — Chart.** Contract implemented in Python; Next.js app renders the
   chart for any location and date; FR1–FR8 pass.
2. **M2 — Icon and polish.** Moon glyph tuned, summary strip, export (FR9),
   dark/light theme, accessibility pass.
3. **M3 — Calendar integration.** The same renderer feeds `moon_calendar.py`
   PDF output, replacing today's bespoke table layout for phase pages.
4. **M4 — Watch.** Data delivered for a watchOS complication: remaining dark
   minutes, current illumination, next twilight transition. Needs a decision
   on server-side versus on-device computation (§11).

## 10. Repository layout proposed for M1

```
AstroScripts/
  astro/                 existing scripts, unchanged
  web/
    app/                 Next.js + TypeScript
      components/night-chart/
    api/                 thin HTTP layer over planner
  planner/               pure Python astronomy package
    tests/
  docs/
```

No existing script is moved in M1. The planner package may import from the
existing modules, but the existing scripts must keep working from the
repository root exactly as documented in `README.md`.

## 11. Open questions

1. **Deployment target.** GitHub Pages static (astronomy must move to
   JavaScript) versus Docker on a VPS or home server (Python stays
   authoritative). Not blocking M1, because the contract is the same.
2. **Refraction.** Should altitudes include atmospheric refraction, which
   lifts the Moon by roughly 0.6° at the horizon and changes rise/set times by
   a few minutes? Recommend: no for M1, add a toggle later.
3. **Watch computation.** If the Watch must work offline, the astronomy has to
   run on the watch, which argues for a JavaScript or Swift reimplementation
   and against the Python-only plan. Decide before M4.
4. **Generated artefacts in git.** `CalendarOutputs/` currently tracks one
   generated PDF, and `--with-images` caches downloaded photography. Decide
   whether build outputs and downloaded media belong in history or in a
   release asset / object store.
5. **Which night is "tonight" near a time-zone boundary**, and behaviour for
   polar day and polar night where sunset or sunrise does not occur at all.
6. **DARK (red) scheme contrast.** The B→R channel-swap palette keeps the
   blue scheme's exact lightness ladder, but red bands sit closer to the
   ivory moon curve in hue than blue ever did. Variants reviewed 23 Nov:
   A = strict swap; B = lighter ink/grid + warmer daylight fill for more pop;
   C = darker bands for deeper night contrast. Awaiting the user's preference
   before the red palette is frozen.

## 12. Definition of done for M1

A chart renders in a browser for Prague, for a high-latitude summer night with
no astronomical darkness, and for a southern-hemisphere site, with moonrise and
moonrise and moonset markers, hourly graduations, 10° altitude gridlines, and
phase-accurate moon icons, and the §6 contract is covered by automated tests
that compare against astropy directly.
