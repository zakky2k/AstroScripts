"""Build a local-time Moon phase calendar and optional deep-sky imaging guide.

Install with: python -m pip install skyfield reportlab pillow tzdata
Run with --help for date, location, output, and asset options.
"""
from __future__ import annotations

import argparse
from calendar import month_name
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import date, datetime, time, timedelta, timezone
from html import escape, unescape
from io import BytesIO
import json
from pathlib import Path
import re
import sys
from tempfile import NamedTemporaryFile
from urllib.parse import urlencode, urlsplit
from urllib.request import Request, urlopen
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

import numpy as np
from PIL import Image, ImageOps
from reportlab.lib.colors import HexColor, white
from reportlab.lib.pagesizes import A4, landscape
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen.canvas import Canvas
from reportlab.platypus import Paragraph
from skyfield import almanac
from skyfield.api import Loader, Star, load_file, wgs84
from skyfield.errors import EphemerisRangeError

UTC = timezone.utc
PRAGUE_LONGITUDE = 14.4378
ASSET_DIR = Path(__file__).resolve().parent / "tmp" / "moon_calendar"
PHASE_NAMES = ("New Moon", "First Quarter", "Full Moon", "Last Quarter")
SUN_BANDS = ("#F3F6F9", "#DCE6F0", "#BECFE2", "#94ADC9", "#6F8FAF")
SUN_LABELS = ("Daylight", "Civil twilight", "Nautical twilight", "Astronomical twilight", "Astronomical dark")
SUN_LIMITS = (-18, -12, -6, 0)
MIN_NIGHT_SECONDS = 3 * 3600
MIN_TARGET_SECONDS = 3600
MIN_TARGET_ALTITUDE = 50
MAX_PHOTOS = 20
W, H = landscape(A4)
NAVY = HexColor("#18324B")
INK = HexColor("#203246")
MUTED = HexColor("#4D647A")
TEAL = HexColor("#43BCC7")
PALE = HexColor("#EDF3F7")
GOLD = HexColor("#FFBF3F")
RED = HexColor("#A92F39")
AGENT = "MoonCalendar/1.0 (local astronomy calendar; Wikimedia media lookup)"
MAX_DOWNLOAD = 20 * 1024 * 1024


@dataclass(frozen=True)
class Location:
    latitude: int
    longitude: float
    zone: ZoneInfo

    @property
    def label(self):
        north_south = "N" if self.latitude >= 0 else "S"
        east_west = "E" if self.longitude >= 0 else "W"
        return f"{abs(self.latitude)}° {north_south}  |  {abs(self.longitude):.4f}° {east_west}  |  {self.zone.key}"


@dataclass(frozen=True)
class Target:
    identifier: str
    name: str
    ra_hours: float
    declination: float


@dataclass(frozen=True)
class Transit:
    target: Target
    utc_seconds: float
    altitude: float
    usable_seconds: float


@dataclass(frozen=True)
class Night:
    evening: date
    windows: list[tuple[float, float]]
    transits: list[Transit]

    @property
    def dark_seconds(self):
        return sum(end - start for start, end in self.windows)


@dataclass(frozen=True)
class Photo:
    path: Path
    source: str
    credit: str
    license_name: str = ""
    license_url: str = ""



# Deep-sky target catalog from the original imaging schedule (J2000).
TARGET_ROWS = (('NGC 281', 'Pacman Nebula', 0.88, 56.6),
 ('M 31', 'Andromeda Galaxy', 0.7116666666666667, 41.3),
 ('M 33', 'Triangulum Galaxy', 1.565, 30.7),
 ('NGC 457', 'Owl Cluster', 1.3183333333333334, 58.3),
 ('NGC 891', '', 2.376666666666667, 42.3),
 ('Double Cluster', 'NGC 869/884', 2.3166666666666664, 57.1),
 ('IC 1805', 'Heart Nebula', 2.545, 61.5),
 ('IC 1848', 'Soul Nebula', 2.85, 60.4),
 ('NGC 7635', 'Bubble Nebula', 23.346666666666668, 61.2),
 ('NGC 1499', 'California Nebula', 4.055, 36.4),
 ('M 45', 'Pleiades', 3.783333333333333, 24.1),
 ('M 1', 'Crab Nebula', 5.575, 22.0),
 ('IC 405', 'Flaming Star Nebula', 5.275, 34.3),
 ('IC 410', 'Tadpole Nebula', 5.366666666666666, 33.4),
 ('IC 443', 'Jellyfish Nebula', 6.283333333333333, 22.5),
 ('M 35', '', 6.15, 24.3),
 ('NGC 2403', 'Caldwell 7', 7.615, 65.6),
 ('M 81', 'Bode’s Galaxy', 9.926666666666666, 69.1),
 ('M 82', 'Cigar Galaxy', 9.93, 69.7),
 ('M 44', 'Beehive Cluster', 8.666666666666666, 19.7),
 ('NGC 2903', '', 9.536666666666667, 21.5),
 ('M 65', 'Leo Triplet', 11.315, 13.1),
 ('M 66', 'Leo Triplet', 11.336666666666666, 13.0),
 ('NGC 3628', 'Leo Triplet', 11.338333333333333, 13.6),
 ('M 51', 'Whirlpool Galaxy', 13.498333333333333, 47.2),
 ('M 63', 'Sunflower Galaxy', 13.263333333333334, 42.0),
 ('M 101', 'Pinwheel Galaxy', 14.053333333333333, 54.3),
 ('M 106', '', 12.316666666666666, 47.3),
 ('M 94', '', 12.848333333333333, 41.1),
 ('M 64', 'Black Eye Galaxy', 12.945, 21.7),
 ('NGC 4565', 'Needle Galaxy', 12.605, 26.0),
 ('M 87', 'Virgo A', 12.513333333333334, 12.4),
 ('M 3', '', 13.703333333333333, 28.4),
 ('M 13', 'Hercules Cluster', 16.695, 36.5),
 ('M 92', '', 17.285, 43.1),
 ('M 27', 'Dumbbell Nebula', 19.993333333333332, 22.7),
 ('M 57', 'Ring Nebula', 18.893333333333334, 33.0),
 ('NGC 6888', 'Crescent Nebula', 20.201666666666668, 38.4),
 ('NGC 7000', 'North America Nebula', 20.98, 44.3),
 ('NGC 6960', 'Western Veil', 20.76, 30.7),
 ('NGC 6992', 'Eastern Veil', 20.938333333333333, 31.7),
 ('IC 1396', 'Elephant Trunk region', 21.651666666666667, 57.5),
 ('M 39', '', 21.53, 48.4),
 ('NGC 6946', 'Fireworks Galaxy', 20.581666666666667, 60.2),
 ('NGC 7331', '', 22.618333333333332, 34.4),
 ('NGC 7789', 'Caroline’s Cluster', 23.956666666666667, 56.7),
 ('M 15', '', 21.5, 12.2))

# Photo sources and attribution from the supplied assets and image archives.
PHOTO_SOURCES = {'IC 1805': ('02_IC1805.jpg',
             'https://d2pn8kiwq2w21t.cloudfront.net/original_images/jpegPIA13112.jpg',
             'https://www.jpl.nasa.gov/images/pia13112-heart-and-soul/',
             'NASA/JPL-Caltech/UCLA'),
 'IC 1848': ('04_IC1848.jpg',
             'https://d2pn8kiwq2w21t.cloudfront.net/original_images/jpegPIA13014.jpg',
             'https://www.jpl.nasa.gov/images/pia13014-soul-nebula/',
             'NASA/JPL-Caltech/UCLA'),
 'IC 405': ('03_IC405.jpg',
            'https://assets.science.nasa.gov/content/dam/science/cds/apod/apod/2020/january/IC405hp_ColesHelm_3447.jpg',
            'https://science.nasa.gov/image-article/apod-2020-january-7-ic-405-the-flaming-star-nebula/',
            'Eric Coles and Mel Helm / NASA Astronomy Picture of the Day'),
 'IC 410': ('06_IC410.jpg',
            'https://assets.science.nasa.gov/content/dam/science/cds/apod/apod/2024/february/Tadpoles2048original.png',
            'https://science.nasa.gov/image-article/apod-2024-february-2-ngc-1893-and-the-tadpoles-of-ic-410/',
            'Sander de Jong / NASA Astronomy Picture of the Day'),
 'IC 443': ('07_IC443.jpg',
            'https://assets.science.nasa.gov/content/dam/science/cds/apod/apod/2023/december/Jellyfish_Payne_7846.jpg',
            'https://science.nasa.gov/image-article/apod-2023-december-26-ic-443-the-jellyfish-nebula/',
            'David Payne / NASA Astronomy Picture of the Day'),
 'M 81': ('09_M81.jpg',
          'https://science.nasa.gov/wp-content/uploads/2023/04/m81-print-jpg.webp',
          'https://science.nasa.gov/image-detail/m81-print/',
          'NASA, ESA, and the Hubble Heritage Team'),
 'M 82': ('10_M82.jpg',
          'https://d2pn8kiwq2w21t.cloudfront.net/original_images/jpegPIA02917.jpg',
          'https://www.jpl.nasa.gov/images/pia02917-smokin-hot-galaxy-animation/',
          'NASA/JPL-Caltech'),
 'NGC 1499': ('05_NGC1499.jpg',
              'https://d2pn8kiwq2w21t.cloudfront.net/original_images/jpegPIA13108.jpg',
              'https://www.jpl.nasa.gov/images/pia13108-menkhib-and-the-california-nebula/',
              'NASA/JPL-Caltech/UCLA'),
 'NGC 2403': ('08_NGC2403.jpg',
              'https://science.nasa.gov/wp-content/uploads/2023/04/c7-1-jpg.webp',
              'https://science.nasa.gov/mission/hubble/science/explore-the-night-sky/hubble-caldwell-catalog/caldwell-7/',
              'NASA, ESA, and the Hubble Heritage Team'),
 'NGC 281': ('01_NGC281.jpg',
             'https://assets.science.nasa.gov/content/dam/science/psd/solar/2023/09/p/i/a/1/PIA14731.jpg',
             'https://science.nasa.gov/resource/the-pacman-nebula/',
             'X-ray: NASA/CXC/CfA/S. Wolk; infrared: NASA/JPL/CfA/S. Wolk'),
 'NGC 7635': ('11_NGC7635.jpg',
              'https://cdn.esahubble.org/archives/images/large/heic1608a.jpg',
              'https://esahubble.org/images/heic1608a/',
              'NASA, ESA, and the Hubble Heritage Team')}
PHOTO_SOURCES.update({
    "M 15": (
        "M15.jpg", "https://cdn.esahubble.org/archives/images/large/potw1107a.jpg",
        "https://esahubble.org/images/potw1107a/", "ESA/Hubble & NASA",
        "CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/",
    ),
    "M 44": (
        "M44.jpg", "https://live.staticflickr.com/65535/53644310402_32253e6465_o.png",
        "https://commons.wikimedia.org/wiki/File:Messier_44_-_Beehive_Cluster_(53644310402).png",
        "Kevin M. Gill", "CC BY 2.0", "https://creativecommons.org/licenses/by/2.0/",
    ),
    "NGC 2903": (
        "NGC2903.jpg", "https://cdn.esahubble.org/archives/images/large/potw1917a.jpg",
        "https://esahubble.org/images/potw1917a/", "ESA/Hubble & NASA, L. Ho et al.",
        "CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/",
    ),
    "NGC 6960": (
        "NGC6960.jpg", "https://thumb.wikimedia.org/wikipedia/commons/thumb/6/6e/Veil_Nebula_-_NGC6960.jpg/1280px-Veil_Nebula_-_NGC6960.jpg",
        "https://commons.wikimedia.org/wiki/File:Veil_Nebula_-_NGC6960.jpg",
        "Ken Crawford", "CC BY-SA 3.0", "https://creativecommons.org/licenses/by-sa/3.0/",
    ),
})

def advance_month(day):
    return date(day.year + (day.month == 12), day.month % 12 + 1, 1)


def date_range(year, today):
    if year is not None:
        return date(year, 1, 1), date(year + 1, 1, 1)
    start = max(today, date(2026, 1, 1))
    return start, advance_month(advance_month(start))


def month_ranges(start, end):
    while start < end:
        following = min(advance_month(start), end)
        yield start, following
        start = following


def midnight(day, zone):
    return datetime.combine(day, time.min, zone)


def open_ephemeris(custom, cache, assets, start, end):
    loader = Loader(str(cache), verbose=False)
    if custom:
        return loader.timescale(), load_file(str(custom))
    # DE421 covers the supplied calendar years. Check its actual end before
    # sampling the extra morning needed by a December 31 night.
    for folder in (assets / "ephemeris", cache, Path.home() / ".skyfield"):
        path = folder / "de421.bsp"
        if path.is_file() and end < date(2053, 10, 8):
            return loader.timescale(), load_file(str(path))
    if end < date(2053, 10, 8):
        name = "de421.bsp"
    elif start.year < 2150 and end.year < 2150:
        name = "de440s.bsp"
    else:
        raise ValueError("Supply --ephemeris with a Skyfield-compatible BSP covering these dates.")
    return loader.timescale(), loader(name)


def sample_times(ts, start, end):
    first, last = start.timestamp(), end.timestamp()
    seconds = np.r_[np.arange(first, last, 300), last]
    return seconds, ts.from_datetimes([datetime.fromtimestamp(x, UTC) for x in seconds])


def altitudes(observer_at, eph):
    return tuple(observer_at.observe(eph[body]).apparent().altaz()[0].degrees
                 for body in ("sun", "moon"))


def phase_events(ts, eph, start, end, zone):
    first = ts.from_datetime(midnight(start, zone))
    last = ts.from_datetime(midnight(end, zone))
    times, kinds = almanac.find_discrete(first, last, almanac.moon_phases(eph))
    by_month = defaultdict(list)
    for moment, kind in zip(times.astimezone(zone), kinds):
        if start <= moment.date() < end:
            by_month[moment.year, moment.month].append((moment, int(kind)))
    return by_month


def interpolate(left, right, a, b):
    return left + (right - left) * (-a / (b - a))


def positive_intervals(seconds, values):
    """Find nonnegative runs and interpolate their threshold crossings."""
    above = np.asarray(values) >= 0
    edges = np.flatnonzero(above[:-1] != above[1:])
    boundaries = [interpolate(seconds[i], seconds[i + 1], values[i], values[i + 1])
                  for i in edges]
    if above[0]:
        boundaries.insert(0, seconds[0])
    if above[-1]:
        boundaries.append(seconds[-1])
    return [(start, end) for start, end in zip(boundaries[::2], boundaries[1::2]) if start < end]


def intersections(left, right):
    return [(max(a, c), min(b, d)) for a, b in left for c, d in right
            if max(a, c) < min(b, d)]


def imaging_night(day, location, ts, eph, observer, catalog):
    zone = location.zone
    start = datetime.combine(day, time(12), zone)
    end = datetime.combine(day + timedelta(days=1), time(12), zone)
    seconds, sky_times = sample_times(ts, start, end)
    sun, moon = altitudes(observer.at(sky_times), eph)
    windows = intersections(positive_intervals(seconds, -18 - sun), positive_intervals(seconds, -moon))
    if sum(b - a for a, b in windows) < MIN_NIGHT_SECONDS:
        return None

    midpoint = ts.from_datetime(datetime.fromtimestamp((seconds[0] + seconds[-1]) / 2, UTC))
    middle_observer = observer.at(midpoint)
    sidereal = sky_times.gast * 15 + location.longitude
    latitude = np.radians(location.latitude)
    transits = []
    for target, star in catalog:
        ra, dec, _ = middle_observer.observe(star).apparent().radec(epoch="date")
        peak = 90 - abs(location.latitude - dec.degrees)
        if peak < MIN_TARGET_ALTITUDE:
            continue
        ha = (sidereal - ra.degrees + 180) % 360 - 180
        for i in np.flatnonzero((ha[:-1] <= 0) & (ha[1:] > 0)):
            transit = interpolate(seconds[i], seconds[i + 1], ha[i], ha[i + 1])
            window = next(((a, b) for a, b in windows if a <= transit <= b), None)
            if window is None:
                continue
            dec_radians = np.radians(dec.degrees)
            sin_alt = (np.sin(latitude) * np.sin(dec_radians)
                       + np.cos(latitude) * np.cos(dec_radians) * np.cos(np.radians(ha)))
            threshold = np.sin(np.radians(MIN_TARGET_ALTITUDE))
            usable = sum(b - a for a, b in intersections([window], positive_intervals(seconds, sin_alt - threshold)))
            if usable >= MIN_TARGET_SECONDS:
                transits.append(Transit(target, transit, peak, usable))
    if not transits:
        return None
    return Night(day, windows, sorted(transits, key=lambda entry: entry.utc_seconds)[:3])


def format_range(start, end):
    last = end - timedelta(days=1)
    if start == last:
        return start.strftime("%d %b %Y")
    return f"{start:%d %b %Y} - {last:%d %b %Y}"


def clock_label(seconds, zone, evening):
    moment = datetime.fromtimestamp(seconds, zone)
    suffix = "*" if moment.date() > evening else ""
    if moment.replace(fold=0).utcoffset() != moment.replace(fold=1).utcoffset():
        return f"{moment:%H:%M %Z}{suffix}"
    return f"{moment:%H:%M}{suffix}"


def label(pdf, x, y, value, size=10, color=INK, bold=False, align="left"):
    pdf.setFont("Helvetica-Bold" if bold else "Helvetica", size)
    pdf.setFillColor(color)
    draw = {"left": pdf.drawString, "right": pdf.drawRightString, "center": pdf.drawCentredString}[align]
    draw(x, y, value)


def header(pdf, title, category, details):
    pdf.setFillColor(NAVY)
    pdf.rect(0, H - 86, W, 86, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.rect(0, H - 86, 7, 86, fill=1, stroke=0)
    label(pdf, 32, H - 24, category.upper(), 9, TEAL, bold=True)
    label(pdf, 32, H - 53, title, 24, white, bold=True)
    label(pdf, 32, H - 72, details, 9, white)


def footer(pdf, text):
    pdf.setStrokeColor(PALE)
    pdf.line(32, 37, W - 32, 37)
    label(pdf, 32, 22, text, 8, MUTED)
    label(pdf, W - 32, 22, str(pdf.getPageNumber()), 8, MUTED, align="right")


def phase_chart(pdf, box, moment, kind, zone, ts, eph, observer):
    x, y, width, height = box
    day = moment.date()
    start = midnight(day, zone)
    end = midnight(day + timedelta(days=1), zone)
    seconds, sky_times = sample_times(ts, start, end)
    sun, moon = altitudes(observer.at(sky_times), eph)

    label(pdf, x, y + height - 12, PHASE_NAMES[kind], 12, bold=True)
    label(pdf, x + width, y + height - 12, f"{moment:%a %d %b | %H:%M %Z}", 9, MUTED, align="right")
    left, bottom = x + 27, y + 22
    plot_width, plot_height = width - 32, height - 44
    first, last = seconds[0], seconds[-1]

    def plot_x(t):
        return left + (t - first) / (last - first) * plot_width

    def plot_y(degrees):
        return bottom + degrees / 90 * plot_height

    bands = 4 - np.searchsorted(SUN_LIMITS, (sun[:-1] + sun[1:]) / 2, side="right")
    changes = np.r_[0, np.flatnonzero(bands[:-1] != bands[1:]) + 1, len(bands)]
    for a, b in zip(changes[:-1], changes[1:]):
        pdf.setFillColor(HexColor(SUN_BANDS[bands[a]]))
        pdf.rect(plot_x(seconds[a]), bottom, plot_x(seconds[b]) - plot_x(seconds[a]),
                 plot_height, fill=1, stroke=0)

    pdf.setStrokeColor(white)
    pdf.setLineWidth(.6)
    for tick in (0, 30, 60, 90):
        height_y = plot_y(tick)
        pdf.line(left, height_y, left + plot_width, height_y)
        label(pdf, left - 5, height_y - 3, f"{tick}°", 8, MUTED, align="right")
    for hour in range(0, 24, 3):
        tick = datetime.combine(day, time(hour), zone).timestamp()
        if first <= tick < last:
            tick_x = plot_x(tick)
            pdf.line(tick_x, bottom, tick_x, bottom + plot_height)
            label(pdf, tick_x, bottom - 12, f"{hour:02d}:00", 7.5, MUTED, align="center")

    phase_x = plot_x(moment.timestamp())
    pdf.setStrokeColor(RED)
    pdf.setLineWidth(1)
    pdf.setDash(3, 2)
    pdf.line(phase_x, bottom, phase_x, bottom + plot_height)
    pdf.setDash()
    pdf.setStrokeColor(INK)
    pdf.line(left, bottom, left + plot_width, bottom)
    for color, line_width in ((NAVY, 3.4), (GOLD, 1.8)):
        pdf.setStrokeColor(color)
        pdf.setLineWidth(line_width)
        for a, b in positive_intervals(seconds, moon):
            inner = (seconds > a) & (seconds < b)
            t = np.r_[a, seconds[inner], b]
            alt = np.interp(t, seconds, moon).clip(0, 90)
            path = pdf.beginPath()
            path.moveTo(plot_x(t[0]), plot_y(alt[0]))
            for instant, degrees in zip(t[1:], alt[1:]):
                path.lineTo(plot_x(instant), plot_y(degrees))
            pdf.drawPath(path)


def phase_month(pdf, start, end, events, location, ts, eph, observer):
    header(pdf, f"{month_name[start.month]} {start.year}", "Moon phases & altitude",
           f"{location.label}  |  {format_range(start, end)}")
    if not events:
        label(pdf, 32, H - 129, "No principal Moon phase occurs in this part of the month.", 13)
    width = (W - 64 - 26) / 2
    count = max(2, (len(events) + 1) // 2)
    panel_height = (H - 170) / count
    for index, (moment, kind) in enumerate(events):
        row, col = divmod(index, 2)
        box = (32 + col * (width + 26), H - 108 - (row + 1) * panel_height, width, panel_height)
        phase_chart(pdf, box, moment, kind, location.zone, ts, eph, observer)
    for i, (color, name) in enumerate(zip(SUN_BANDS, SUN_LABELS)):
        x = 32 + (0, 77, 180, 301, 453)[i]
        pdf.setFillColor(HexColor(color))
        pdf.rect(x, 51, 13, 9, fill=1, stroke=0)
        label(pdf, x + 18, 51, name, 8, MUTED)
    footer(pdf, "Gold: Moon above horizon  |  Red dash: exact phase  |  Chart times are local.")
    pdf.showPage()

def schedule_month(pdf, start, end, nights, location):
    header(pdf, f"{month_name[start.month]} {start.year}", "Moon-free imaging targets",
           f"{location.label}  |  {format_range(start, end)}")
    x_positions = (32, 123, 335, 493, 651)
    widths = (91, 212, 158, 158, W - 683)
    y = H - 120
    pdf.setFillColor(NAVY)
    pdf.rect(32, y - 22, W - 64, 22, fill=1, stroke=0)
    for x, text in zip(x_positions, ("Evening", "Moon-free darkness", "1st transit", "2nd transit", "3rd transit")):
        label(pdf, x + 7, y - 14, text, 9, white, bold=True)
    y -= 22
    style = ParagraphStyle("schedule", fontName="Helvetica", fontSize=8.7, leading=10, textColor=INK)
    if not nights:
        label(pdf, 39, y - 27, "No night and catalog target meet the criteria in this date range.", 11)
    for i, night in enumerate(nights):
        y -= 36
        pdf.setFillColor(PALE if i % 2 == 0 else white)
        pdf.rect(32, y, W - 64, 36, fill=1, stroke=0)
        window_text = "; ".join(f"{clock_label(a, location.zone, night.evening)}-{clock_label(b, location.zone, night.evening)}"
                                for a, b in night.windows)
        cells = [night.evening.strftime("%a %d %b"), escape(window_text)]
        for transit in night.transits:
            target = transit.target
            display = f"<b>{escape(target.identifier)}</b> {escape(target.name)}"
            when = clock_label(transit.utc_seconds, location.zone, night.evening)
            cells.append(f"{display}<br/>{when}  |  {transit.altitude:.0f}° at transit")
        for x, width, content in zip(x_positions, widths, cells):
            paragraph = Paragraph(content, style)
            _, height = paragraph.wrap(width - 14, 34)
            paragraph.drawOn(pdf, x + 7, y + (36 - height) / 2)
    label(pdf, 32, 52, "Up to 10 nights per month. Sun at or below -18°, Moon below the horizon, at least 3 hours total.",
          8, MUTED)
    footer(pdf, "Targets transit in darkness and stay above 50° for at least 1 hour in that window. * Following local day.")
    pdf.showPage()


def imaging_schedule(pdf, start, end, location, ts, eph, observer):
    catalog = [(target, Star(ra_hours=target.ra_hours, dec_degrees=target.declination, epoch=ts.J2000))
               for target in (Target(*row) for row in TARGET_ROWS)
               if 90 - abs(location.latitude - target.declination) >= MIN_TARGET_ALTITUDE - 1]
    counts = Counter()
    usable = Counter()
    for first, following in month_ranges(start, end):
        days = (first + timedelta(days=i) for i in range((following - first).days))
        eligible = [night for day in days if (night := imaging_night(day, location, ts, eph, observer, catalog))]
        best = sorted(eligible, key=lambda night: (-night.dark_seconds, -len(night.transits), night.evening))[:10]
        best.sort(key=lambda night: night.evening)
        schedule_month(pdf, first, following, best, location)
        for night in best:
            for transit in night.transits:
                counts[transit.target] += 1
                usable[transit.target] += transit.usable_seconds
    ranked = sorted(counts, key=lambda target: (-counts[target], -usable[target], target.identifier))
    return [(target, counts[target]) for target in ranked[:MAX_PHOTOS]]


def fetch(url, max_bytes):
    request = Request(url, headers={"User-Agent": AGENT})
    with urlopen(request, timeout=10) as response:
        content = response.read(max_bytes + 1)
    if len(content) > max_bytes:
        raise ValueError("Download exceeded the size limit")
    return content


def normalize_photo(path_or_bytes):
    with Image.open(path_or_bytes) as original:
        picture = ImageOps.exif_transpose(original).convert("RGB")
        picture.thumbnail((2200, 1600), Image.Resampling.LANCZOS)
        data = BytesIO()
        picture.save(data, "JPEG", quality=89, optimize=True)
        return data.getvalue()


def plain_text(value):
    return " ".join(unescape(re.sub(r"<[^>]+>", " ", value)).split())


def web_url(value):
    return value if urlsplit(value).scheme in {"http", "https"} else ""


def commons_candidates(target):
    aliases = [target.identifier]
    messier = re.fullmatch(r"M \d+", target.identifier)
    if messier:
        aliases.append(target.identifier.replace("M ", "Messier "))
    if target.name and target.name != "Leo Triplet":
        aliases.insert(0, target.name)

    patterns = [r"(?<![A-Za-z0-9])" + r"\s*[-_ ]?\s*".join(map(re.escape, name.split()))
                + r"(?![A-Za-z0-9])" for name in aliases]
    queries = dict.fromkeys([*aliases, target.identifier.replace(" ", "")])
    seen = set()
    for query in queries:
        for page in commons_search(query):
            if page["pageid"] in seen:
                continue
            seen.add(page["pageid"])
            if not re.search("|".join(patterns), page["title"].replace("_", " "), re.I):
                continue
            if re.search(r"\b(chart|finder|map|location|diagram|position|light curve)\b", page["title"], re.I):
                continue
            if candidate := commons_photo(page, require_astronomy=bool(messier)):
                yield candidate


def commons_search(query):
    url = "https://commons.wikimedia.org/w/api.php?" + urlencode({
        "action": "query", "format": "json", "formatversion": 2,
        "generator": "search", "gsrnamespace": 6, "gsrlimit": 12, "gsrsearch": query,
        "prop": "imageinfo", "iiprop": "url|extmetadata|mime|size", "iiurlwidth": 1920,
        "iiextmetadatafilter": "Artist|Credit|LicenseShortName|LicenseUrl|ImageDescription",
    })
    payload = json.loads(fetch(url, 2 * 1024 * 1024))
    if "error" in payload:
        raise ValueError(payload["error"].get("info", "Wikimedia Commons search failed"))
    return sorted(payload.get("query", {}).get("pages", []), key=lambda item: item.get("index", 0))


def commons_photo(page, require_astronomy):
    images = page.get("imageinfo", [])
    if not images:
        return None
    info = images[0]
    if info.get("mime") not in {"image/jpeg", "image/png", "image/webp", "image/tiff"}:
        return None
    if max(info.get("width", 0), info.get("height", 0)) < 600:
        return None
    fields = {name: item.get("value", "") for name, item in info.get("extmetadata", {}).items()}
    if require_astronomy:
        context = page["title"].replace("_", " ") + " " + plain_text(fields.get("ImageDescription", ""))
        if not re.search(r"\b(messier|ngc|galaxy|nebula|constellation|globular cluster|open cluster|astronomy|astrophotography)\b",
                         context, re.I):
            return None
    license_name = plain_text(fields.get("LicenseShortName", ""))
    if not re.fullmatch(r"(public domain|cc0|cc by(?:-sa)? [2-4]\.\d(?: \w+)?)", license_name, re.I):
        return None
    media = info.get("thumburl") or info.get("url", "")
    source = web_url(info.get("descriptionurl", ""))
    credit = plain_text(fields.get("Artist") or fields.get("Credit", ""))
    license_url = web_url(fields.get("LicenseUrl", ""))
    if source and credit and urlsplit(media).hostname in {"upload.wikimedia.org", "thumb.wikimedia.org"}:
        return media, source, credit, license_name, license_url
    return None


def write_atomic(path, data):
    with NamedTemporaryFile(dir=path.parent, delete=False) as temporary:
        draft = Path(temporary.name)
        temporary.write(data)
    try:
        draft.replace(path)
    finally:
        draft.unlink(missing_ok=True)


def find_photo(target, asset_dir, cache):
    key = re.sub(r"[^a-z0-9]", "", target.identifier.lower())
    metadata = cache / f"{key}.json"
    destination = cache / f"{key}.jpg"
    if destination.is_file() and metadata.is_file():
        try:
            with Image.open(destination) as cached:
                cached.verify()
            return Photo(destination, *json.loads(metadata.read_text(encoding="utf-8")))
        except (OSError, ValueError, TypeError):
            pass

    candidates = PHOTO_SOURCES.get(target.identifier)
    if candidates:
        image_name, media, *attribution = candidates
        local_image = asset_dir / "images" / image_name
        try:
            original = local_image if local_image.is_file() else BytesIO(fetch(media, MAX_DOWNLOAD))
            write_atomic(destination, normalize_photo(original))
            write_atomic(metadata, json.dumps(attribution).encode("utf-8"))
            return Photo(destination, *attribution)
        except (OSError, ValueError) as error:
            print(f"{target.identifier}: original source unavailable ({error}); trying Wikimedia Commons.", file=sys.stderr)

    for media, source, credit, license_name, license_url in commons_candidates(target):
        try:
            picture = normalize_photo(BytesIO(fetch(media, MAX_DOWNLOAD)))
        except (OSError, ValueError):
            continue
        write_atomic(destination, picture)
        write_atomic(metadata, json.dumps([source, credit, license_name, license_url]).encode("utf-8"))
        return Photo(destination, source, credit, license_name, license_url)
    return None


def photo_page(pdf, target, appearances, photo, number, count):
    title = f"{target.identifier}  |  {target.name}" if target.name else target.identifier
    label_text = f"{appearances} listed {'night' if appearances == 1 else 'nights'}"
    header(pdf, title, f"Deep-sky photographs  |  {number} of {count}", label_text)
    pdf.setFillColor(NAVY)
    pdf.rect(32, 119, W - 64, H - 223, fill=1, stroke=0)
    pdf.drawImage(ImageReader(str(photo.path)), 32, 119, W - 64, H - 223,
                  preserveAspectRatio=True, anchor="c")
    style = ParagraphStyle("credit", fontName="Helvetica", fontSize=9, leading=12, textColor=MUTED)
    credit = f"Credit: {escape(photo.credit)}. "
    if photo.license_name:
        license_name = escape(photo.license_name)
        credit += (f'<link href="{escape(photo.license_url)}" color="#203246">{license_name}</link>. '
                   if photo.license_url else f"{license_name}. ")
    if photo.source:
        credit += f'<link href="{escape(photo.source)}" color="#203246"><u>Original image and source details</u></link>. '
    credit += "Image resized and reformatted for the PDF."
    paragraph = Paragraph(credit, style)
    _, height = paragraph.wrap(W - 64, 66)
    paragraph.drawOn(pdf, 32, 105 - height)
    footer(pdf, "Selected from the targets listed in this calendar.")
    pdf.showPage()


def gallery(pdf, selected, asset_dir, cache):
    cache.mkdir(parents=True, exist_ok=True)
    photos = []
    unavailable = []
    for target, appearances in selected:
        try:
            photo = find_photo(target, asset_dir, cache)
            if photo:
                photos.append((target, appearances, photo))
                continue
        except (OSError, ValueError) as error:
            print(f"{target.identifier}: {error}", file=sys.stderr)
        unavailable.append(target.identifier)
    for number, (target, appearances, photo) in enumerate(photos, 1):
        photo_page(pdf, target, appearances, photo, number, len(photos))
    if unavailable:
        print(f"No usable photo found for: {', '.join(unavailable)}", file=sys.stderr)
    print(f"Included {len(photos)} of {len(selected)} selected object photos.")


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    parser.add_argument("year", nargs="?", type=int, help="Calendar year, 2026 or later. Omit for today through the end of next month.")
    parser.add_argument("--year", type=int, dest="year_option", help="Calendar year, as an alternative to the positional year")
    parser.add_argument("--lat", "--latitude", type=int, default=50, help="Latitude in whole degrees (north positive, default: 50)")
    parser.add_argument("--lon", "--longitude", type=float, default=PRAGUE_LONGITUDE, help="Longitude in degrees (east positive, default: Prague)")
    parser.add_argument("--timezone", default="Europe/Prague", help="IANA time zone (default: Europe/Prague)")
    parser.add_argument("--with-images", action="store_true", help="Add the moon-free imaging schedule and up to 20 credited photos")
    parser.add_argument("--output", type=Path, help="Output PDF path (default: a date-based filename in the current directory)")
    parser.add_argument("--assets-dir", type=Path, default=ASSET_DIR, help="Optional asset folder containing images/ and ephemeris/")
    parser.add_argument("--cache-dir", type=Path, default=Path.home() / ".moon-calendar", help="Cache for downloaded ephemerides and photos")
    parser.add_argument("--ephemeris", type=Path, help="Use the specified Skyfield-compatible BSP file")
    args = parser.parse_args(argv)
    if args.year is not None and args.year_option is not None:
        parser.error("supply the year either positionally or with --year")
    if args.year_option is not None:
        args.year = args.year_option
    if args.year is not None and not 2026 <= args.year <= 9998:
        parser.error("year must be an integer from 2026 to 9998")
    if not -90 <= args.lat <= 90:
        parser.error("latitude must be a whole degree from -90 to 90")
    if not -180 <= args.lon <= 180:
        parser.error("longitude must be from -180 to 180")
    try:
        location = Location(args.lat, args.lon, ZoneInfo(args.timezone))
    except (ZoneInfoNotFoundError, ValueError):
        parser.error(f"unknown IANA time zone: {args.timezone}")
    return args, location


def main(argv=None):
    args, location = parse_args(argv)
    start, end = date_range(args.year, datetime.now(location.zone).date())
    suffix = str(args.year) if args.year else f"{start.isoformat()}_to_{end - timedelta(days=1)}"
    output = args.output or Path(f"moon_calendar_{suffix}.pdf")
    output.parent.mkdir(parents=True, exist_ok=True)
    try:
        ts, eph = open_ephemeris(args.ephemeris, args.cache_dir, args.assets_dir, start, end)
        try:
            observer = eph["earth"] + wgs84.latlon(location.latitude, location.longitude)
            phases = phase_events(ts, eph, start, end, location.zone)
            with NamedTemporaryFile(dir=output.parent, suffix=".pdf", delete=False) as file:
                draft = Path(file.name)
            try:
                pdf = Canvas(str(draft), pagesize=(W, H), pageCompression=1)
                pdf.setTitle(f"Moon Calendar | {format_range(start, end)}")
                for first, following in month_ranges(start, end):
                    phase_month(pdf, first, following, phases[first.year, first.month], location, ts, eph, observer)
                if args.with_images:
                    selected = imaging_schedule(pdf, start, end, location, ts, eph, observer)
                    gallery(pdf, selected, args.assets_dir, args.cache_dir / "photos")
                pdf.save()
                draft.replace(output)
            finally:
                draft.unlink(missing_ok=True)
        finally:
            eph.close()
    except EphemerisRangeError as error:
        raise SystemExit(f"Ephemeris cannot cover the requested dates: {error}. Use --ephemeris to supply a suitable BSP.")
    except (OSError, ValueError) as error:
        raise SystemExit(f"Could not build the calendar: {error}")
    print(f"Wrote {output.resolve()}")


if __name__ == "__main__":
    main()
