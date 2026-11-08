
import argparse
import os
import shutil
import tempfile
from pathlib import Path

from astropy.io import fits
from xisf import XISF


EXTENSIONS = {".fits", ".fit", ".fts", ".xisf"}


def is_zero(value):
    try:
        return float(str(value).strip().strip("'")) == 0
    except (ValueError, TypeError):
        return False


def fix_fits(path, exposure, dry_run):
    # Check metadata before opening in update mode
    with fits.open(path, memmap=False) as hdul:
        indices = [
            i for i, hdu in enumerate(hdul)
            if is_zero(hdu.header.get("EXPTIME"))
            and "EXPTIME" in hdu.header
        ]

    if not indices:
        return "SKIP"

    if dry_run:
        return "WOULD UPDATE"

    with fits.open(path, mode="update", memmap=False) as hdul:
        for i in indices:
            hdul[i].header["EXPTIME"] = (
                exposure, "Exposure time in seconds"
            )
        hdul.flush()

    return "UPDATED"


def fix_xisf(path, exposure, dry_run):
    xisf = XISF(str(path))
    images_meta = xisf.get_images_metadata()

    if len(images_meta) != 1:
        return "SKIP (XISF requires single image)"

    meta = images_meta[0]
    keywords = meta.get("FITSKeywords", {})
    entries = keywords.get("EXPTIME", [])

    if not entries:
        return "SKIP"

    if not any(is_zero(e.get("value")) for e in entries):
        return "SKIP"

    if dry_run:
        return "WOULD UPDATE"

    for entry in entries:
        if is_zero(entry.get("value")):
            entry["value"] = str(exposure)
            entry["comment"] = "Exposure time in seconds"

    # Preserve the original XISF before rewriting.
    backup = path.with_name(path.name + ".bak")
    if backup.exists():
        return "SKIP (backup already exists)"

    image_data = xisf.read_image(0)
    file_meta = xisf.get_file_metadata()

    # Write new file to temporary location first.
    with tempfile.NamedTemporaryFile(
        suffix=".xisf",
        dir=path.parent,
        delete=False
    ) as temp:
        temp_path = Path(temp.name)

    try:
        XISF.write(
            str(temp_path),
            image_data,
            image_metadata=meta,
            xisf_metadata=file_meta
        )

        # Verify the rewritten file and its exposure metadata.
        check = XISF(str(temp_path))
        new_entries = check.get_images_metadata()[0].get(
            "FITSKeywords", {}
        ).get("EXPTIME", [])

        if not any(
            is_zero(e.get("value")) is False
            and abs(float(e["value"]) - exposure) < 1e-9
            for e in new_entries
        ):
            raise RuntimeError("XISF verification failed")

        shutil.copy2(path, backup)
        os.replace(temp_path, path)

    finally:
        if temp_path.exists():
            temp_path.unlink()

    return "UPDATED (backup created)"


def main():
    parser = argparse.ArgumentParser(
        description="Correct zero EXPTIME metadata in FITS/XISF files"
    )

    parser.add_argument("folder", type=Path)
    parser.add_argument("exposure", type=float)
    parser.add_argument(
        "--recursive", action="store_true",
        help="Include subfolders"
    )
    parser.add_argument(
        "--dry-run", action="store_true",
        help="Preview changes without modifying files"
    )

    args = parser.parse_args()

    if not args.folder.is_dir():
        parser.error("Folder does not exist")

    if not 0 < args.exposure < float("inf"):
        parser.error("Exposure must be a positive finite number")

    files = (
        args.folder.rglob("*")
        if args.recursive
        else args.folder.glob("*")
    )

    counts = {"updated": 0, "skipped": 0, "errors": 0}

    for path in sorted(files):
        if not path.is_file():
            continue

        ext = path.suffix.lower()
        if ext not in EXTENSIONS:
            continue

        try:
            if ext == ".xisf":
                result = fix_xisf(
                    path, args.exposure, args.dry_run
                )
            else:
                result = fix_fits(
                    path, args.exposure, args.dry_run
                )

            if "UPDATE" in result:
                counts["updated"] += 1
            else:
                counts["skipped"] += 1

            print(f"{result}: {path}")

        except Exception as exc:
            counts["errors"] += 1
            print(f"ERROR: {path}: {exc}")

    print("\nSummary")
    print(f"Updated / would update: {counts['updated']}")
    print(f"Skipped: {counts['skipped']}")
    print(f"Errors: {counts['errors']}")


if __name__ == "__main__":
    main()