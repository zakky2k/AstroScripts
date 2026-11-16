#!/usr/bin/env python3
"""Tally astronomy LIGHT frames using filenames and folder names only."""

import argparse
import re
from collections import defaultdict
from datetime import date
from decimal import Decimal
from pathlib import Path

FITS_EXTENSIONS = (".fit", ".fits", ".fts", ".fit.gz", ".fits.gz", ".fts.gz")

# Expected example:
# 2026-10-27_21-58-28_LIGHT_O3_Exp180.00s_..._Frame-0012.fits
TYPE_RE = re.compile(
    r"(?:^|_)(LIGHT|DARK|BIAS|FLAT|DARKFLAT|FLATDARK)(?=_|$)",
    re.IGNORECASE,
)
FILTER_RE = re.compile(r"(?:^|_)LIGHT_(.+?)_Exp", re.IGNORECASE)
EXPOSURE_RE = re.compile(
    r"(?:^|_)Exp(\d+(?:\.\d+)?)s(?=_|$)", re.IGNORECASE
)
DATE_RE = re.compile(r"(?<!\d)(\d{4}-\d{2}-\d{2})(?!\d)")


def duration(seconds):
    """Format exposure without limiting the number of hours."""
    hours, remainder = divmod(seconds, Decimal(3600))
    minutes, seconds = divmod(remainder, Decimal(60))
    return f"{int(hours)}h {int(minutes):02d}m {seconds:05.2f}s"


def filename_stem(path):
    """Remove a FITS extension, including optional gzip compression."""
    lower_name = path.name.lower()
    for extension in FITS_EXTENSIONS:
        if lower_name.endswith(extension):
            return path.name[:-len(extension)]
    return path.stem


def valid_date(text):
    for match in DATE_RE.finditer(text):
        try:
            return date.fromisoformat(match.group(1)).isoformat()
        except ValueError:
            continue
    return None


def metadata(path):
    stem = filename_stem(path)

    # Prefer the filename's frame type. Otherwise, inspect folder names
    # from nearest to farthest.
    frame_type = TYPE_RE.search(stem)
    if frame_type is None:
        for parent in path.parents:
            frame_type = TYPE_RE.search(parent.name)
            if frame_type:
                break

    if frame_type is None or frame_type.group(1).upper() != "LIGHT":
        return None

    exposure_match = EXPOSURE_RE.search(stem)
    if exposure_match is None:
        raise ValueError("missing exposure, expected e.g. Exp180.00s")

    exposure = Decimal(exposure_match.group(1))
    if exposure <= 0:
        raise ValueError("exposure must be greater than zero")

    session_date = valid_date(stem)
    if session_date is None:
        for parent in path.parents:
            session_date = valid_date(parent.name)
            if session_date:
                break
    if session_date is None:
        raise ValueError("missing valid YYYY-MM-DD date")

    filter_match = FILTER_RE.search(stem)
    if filter_match:
        filter_name = filter_match.group(1).strip()
    else:
        # Expected fallback layout: .../<dated LIGHT folder>/<filter>/<file>
        filter_folder = path.parent.name
        light_folder_type = TYPE_RE.search(path.parent.parent.name)
        if (
            light_folder_type is None
            or light_folder_type.group(1).upper() != "LIGHT"
        ):
            raise ValueError("missing filter in filename or filter folder")
        filter_name = filter_folder.strip()

    if not filter_name:
        raise ValueError("empty filter name")

    return session_date, filter_name.upper(), exposure


def print_table(headers, rows):
    rows = [[str(value) for value in row] for row in rows]
    widths = [
        max(len(header), max((len(row[i]) for row in rows), default=0))
        for i, header in enumerate(headers)
    ]

    def render(row):
        return "  ".join(
            value.ljust(width) for value, width in zip(row, widths)
        )

    print(render(headers))
    print(render(["-" * width for width in widths]))
    for row in rows:
        print(render(row))


def main():
    parser = argparse.ArgumentParser(
        description="Tally LIGHT FITS frames without reading FITS headers.",
        epilog=(
            "Dates use the filename date, falling back to the nearest dated "
            "folder. Counts represent files; duplicate copies count separately."
        ),
    )
    parser.add_argument("folder", type=Path, help="Starting folder")
    args = parser.parse_args()

    root = args.folder.expanduser()
    if not root.is_dir():
        parser.error(f"Not a folder: {root}")

    # Each value contains [sub count, exposure seconds].
    by_date_filter = defaultdict(lambda: [0, Decimal(0)])
    by_filter = defaultdict(lambda: [0, Decimal(0)])
    skipped = []
    scan_errors = []
    fits_count = 0

    def walk_error(error):
        scan_errors.append(str(error))

    # os.walk supports reporting inaccessible directories.
    import os

    for directory, _, filenames in os.walk(root, onerror=walk_error):
        for name in filenames:
            if not name.lower().endswith(FITS_EXTENSIONS):
                continue

            fits_count += 1
            path = Path(directory) / name
            try:
                result = metadata(path)
            except ValueError as error:
                skipped.append((path, str(error)))
                continue

            if result is None:
                continue

            session_date, filter_name, exposure = result
            for totals, key in (
                (by_date_filter, (session_date, filter_name)),
                (by_filter, filter_name),
            ):
                totals[key][0] += 1
                totals[key][1] += exposure

    print(f"\nFolder: {root}")
    print(f"FITS files found: {fits_count}")

    print("\nBy date and filter:")
    print_table(
        ["Date", "Filter", "Subs", "Exposure"],
        [
            [day, filt, count, duration(seconds)]
            for (day, filt), (count, seconds) in sorted(by_date_filter.items())
        ],
    )

    print("\nTotals per filter:")
    print_table(
        ["Filter", "Subs", "Exposure"],
        [
            [filt, count, duration(seconds)]
            for filt, (count, seconds) in sorted(by_filter.items())
        ],
    )

    total_subs = sum(value[0] for value in by_filter.values())
    total_seconds = sum(
        (value[1] for value in by_filter.values()), Decimal(0)
    )
    total_dates = len({day for day, _ in by_date_filter})

    print(f"\nDates: {total_dates}")
    print(f"Total LIGHT subs: {total_subs}")
    print(f"Total exposure: {duration(total_seconds)}")

    if skipped:
        print(f"\nLIGHT files skipped: {len(skipped)}")
        for path, reason in skipped:
            print(f"  {path}: {reason}")

    if scan_errors:
        print("\nScan errors — totals may be incomplete:")
        for error in scan_errors:
            print(f"  {error}")


if __name__ == "__main__":
    main()