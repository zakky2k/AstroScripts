# AstroScripts

Utilities for inspecting and correcting astrophotography exposure metadata, tallying light frames, reporting PixInsight WBPP integration times, and recovering PixInsight swap images. Plus a night-planner app (`planner/`, `api/`, `web/app/`) that answers "is the sky usable tonight, and for how long?" — see [Night planner](#night-planner) at the end.

Run the Python examples from the repository root. Use Python 3.13, pinned in `.python-version`; `zstandard` and `lz4` have no prebuilt wheels for newer Python releases and would otherwise need a C++ compiler to install. For `PyUpdateExp.py` and `checkExposure.py`, install the dependencies first with `python -m pip install -r requirements.txt`. The other Python scripts use only the standard library.

## [checkExposure.py](checkExposure.py)

Reads an XISF header and prints JSON showing how `PyUpdateExp.py` classifies the frame and what exposure it would apply.

**Usage** (the optional exposure argument defaults to 180 seconds)

```powershell
python checkExposure.py "C:\Astro\Lights\frame.xisf" 180
```

## [PyUpdateExp.py](PyUpdateExp.py)

Corrects exposure metadata and updates `Exp…s` filename tokens in FITS and XISF files. Lights and darks use the requested exposure, while flats default to 3 seconds unless you specify another exposure with `--force`.

**Usage** (preview changes in the folder and its subfolders)

```powershell
python PyUpdateExp.py "C:\Astro\Frames" 180 --recursive --dry-run
```

Remove `--dry-run` to apply changes. Use `--force` to allow overwriting existing exposure values.

## [PI_updateexistMasterExposure.js](PI_updateexistMasterExposure.js)

Sets the native XISF exposure properties of the active master dark in PixInsight to the exposure of one source dark frame.

**Usage** Open and activate the master dark in PixInsight, set `seconds` at the top of the script, execute the script in PixInsight, and save the result as a new XISF file.

## [PI_Swap_recover.js](PI_Swap_recover.js)

Opens recoverable PixInsight swap images in new image windows. It skips continuation files such as `*-part1.swp`.

**Usage** Set `swap` near the top of the script to your swap folder, using forward slashes on Windows. Execute the script in PixInsight, select the primary `.swp` files, and save the images you recover.

## [pi_swap_to_xisf.py](pi_swap_to_xisf.py)

Recovers supported uncompressed Float32 and UInt16 grayscale or RGB images from PixInsight parallel swap files into XISF. It can also validate XISF files and repair headers from earlier conversions.

**Usage** Keep the primary swap file and any companion segments together. For batch recovery, use a copy of an inactive swap directory.

```powershell
python pi_swap_to_xisf.py "C:\Astro\SwapBackup\image.swp" "C:\Astro\Recovered\image.xisf"
python pi_swap_to_xisf.py --all "C:\Astro\SwapBackup" --output-dir "C:\Astro\Recovered" --dry-run
```

Remove `--dry-run` to run the batch conversion.

## [tally_astro.py](tally_astro.py)

Recursively tallies FITS light frames and exposure time by date and filter. It reads names such as `2026-10-27_21-58-28_LIGHT_O3_Exp180.00s_Frame-0012.fits` and falls back to a dated parent folder when the filename has no date.

**Usage**

```powershell
python tally_astro.py "C:\Astro\Capture"
```

## [wbpp_exposure_report.py](wbpp_exposure_report.py)

Summarizes captured, rejected, and integrated exposure from PixInsight Weighted Batch Preprocessing logs. It writes session and AstroBin TSV reports beside each processed log.

**Usage** Pass a single log or a folder. For a folder, the newest log is used by default. Add `--all-runs` for separate reports for every log.

```powershell
python wbpp_exposure_report.py "C:\Astro\WBPP\logs"
```

## [moon_calendar.py](moon_calendar.py)

Creates a PDF with monthly Moon-phase times, Moon altitude on phase days, and twilight bands. Add `--with-images` for a moon-free imaging schedule and up to 20 credited photos of targets listed in it.

**Usage**

```powershell
python -m pip install skyfield reportlab pillow tzdata
python moon_calendar.py
python moon_calendar.py 2027 --with-images --output "moon-calendar-2027.pdf"
python moon_calendar.py --year 2027 --lat 40 --lon -3.7038 --timezone Europe/Madrid
```

The default uses 50° N, Prague's longitude, and `Europe/Prague` time. Without a year, it starts today and goes through the end of next month. An explicit year (2026 or later) generates January through December. Latitude uses whole degrees, with negative values for south. Set longitude and time zone for local observing times outside Prague.

Skyfield downloads and caches the ephemeris when needed. Use `--ephemeris` with an existing BSP file to run offline. The optional imaging schedule uses the original northern deep-sky target list. The photo mode downloads missing images when a suitable source is available and caches the resized files. Point `--assets-dir` at an existing folder containing `images/` and `ephemeris/` to reuse assets.

## Night planner

The M1 night planner: a Next.js front end (`web/app/`) that draws the fixed-frame
altitude chart reviewed in `design/`, backed by an astropy computation package
(`planner/`) behind a thin stdlib HTTP layer (`api/`). The JSON contract is
`docs/REQUIREMENTS.md` §6; the SVG geometry reference is `design/render.py`.

**Run it** (two processes, from the repository root):

```powershell
# 1. the planner API on 127.0.0.1:8642
.venv\Scripts\python.exe -m api.server

# 2. the web app on localhost:4101 (Node >= 20)
cd web\app
npm install    # first time only
npm run dev
```

Open http://localhost:4101 — it defaults to Prague tonight; edit lat/lon/zone or
step nights with ◀ ▶. ZONE is a dropdown of real IANA zones showing the offset
each one uses on the night you are looking at, and it follows the lat/lon you
type; the list is bundled in the app, so nothing is looked up online. Stepping
nights keeps the red marker — and the Moon on it — at the same place on the UTC
scale instead of snapping back to the clock, so nights can be flicked through at
one fixed time; **⏱ live** hands control back to the clock; **DARK** beside the
TIME box retints everything into the warm-red family at the same lightness — sky
bands, control boxes, the Moon's own colour and the text, not just the sky. Set
`NEXT_PUBLIC_API_BASE` if the API is not on
`http://127.0.0.1:8642`. Both ports sit at **4101 or higher** (review 24 Nov,
round 7): 3000 and its neighbours are routinely taken by other local tools on
this machine — openWebUI holds 3000 — so the planner never has to fight for
them. The API is on 8642.

**Tests:**

```powershell
# astronomy + §6 payload correctness (exit 0 = pass)
.venv\Scripts\python.exe -m planner.tests.test_night

# web gates — one build, three checks (exit 0 each = pass)
cd web\app
npx tsc --noEmit
npx tsc -p tsconfig.gates.json
node scripts\fix-aliases.mjs .gates-build
node .gates-build\scripts\parity.js   # static NightChart vs the reviewed design SVGs
node .gates-build\scripts\golden.js   # LiveNightChart SSR vs tests/golden/*.tokens.txt
node .gates-build\scripts\units.js    # the §4.1 chart laws, as pure-function checks
```
