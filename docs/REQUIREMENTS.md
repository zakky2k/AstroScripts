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

- **x axis** — continuous time from sunset to sunrise, inclusive of both dusk
  and dawn twilight. Hourly major graduations with `HH:00` labels; minor ticks
  at 30-minute intervals. Date rollover across midnight must be labelled
  correctly, including the case where the window spans two calendar dates.
- **y axis** — altitude in degrees above the geometric horizon. Major
  graduations every 10°, from 0° to 90°, with faint gridlines. Region below
  0° is drawn as a distinct "below horizon" band rather than clipped away, so
  the reader can see when the Moon rises and sets.
- Altitude reference: **standard astronomical altitude** (geometric horizon,
  no refraction correction) for the first milestone. Recorded as an open
  question in §11.

### 4.2 Twilight bands

Vertical bands spanning the full height of the plot, with a smooth gradient
between adjacent bands rather than hard edges. Two sets, mirrored around
local midnight:

| Band | Solar altitude range | Shade (northern night sky, dark theme) |
|---|---|---|
| Day | sun > 0° | not shown inside the window |
| Civil | 0° to −6° | lightest slate |
| Nautical | −6° to −12° | mid slate |
| Astronomical | −12° to −18° | deep slate |
| Night | sun < −18° | near-black, the usable imaging region |

A night with **no** astronomical twilight — common at high northern latitudes
in June and July, where the sun never reaches −18° — must render honestly as a
permanently lit window, with a visible notice, rather than pretending to be
dark. This is a correctness requirement, not a cosmetic one.

### 4.3 Moon path

- Plotted as a smooth curve of altitude against time over the whole window,
  sampled at 10-minute intervals (approximately 150 points per night) so the
  characteristic sinusoidal shape is faithful and never visibly polylinear.
- Where the Moon is below 0° altitude, the curve continues into the below-
  horizon band at reduced opacity, so rise and set times are legible.
- A **marker at each hour**, plus one at moonrise and moonset, each showing
  the moon icon at that instant.
- Azimuth is not an axis. It is exposed as a tooltip and in the marker's
  accessible text label.

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
    "moonrise": "...",
    "moonset":  "...",
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

## 12. Definition of done for M1

A chart renders in a browser for Prague, for a high-latitude summer night with
no astronomical darkness, and for a southern-hemisphere site, with moonrise and
moonrise and moonset markers, hourly graduations, 10° altitude gridlines, and
phase-accurate moon icons, and the §6 contract is covered by automated tests
that compare against astropy directly.
