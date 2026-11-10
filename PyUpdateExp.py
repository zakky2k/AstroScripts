import argparse
import os
import re
import shutil
import tempfile
from pathlib import Path

from astropy.io import fits
from xisf import XISF


EXTENSIONS = {".fits", ".fit", ".fts", ".xisf"}
EXPOSURE_KEYS = ("EXPTIME", "EXPOSURE")
TYPE_KEYS = ("IMAGETYP", "FRAME", "OBSTYPE")


def numeric(value):
    return float(str(value).strip().strip("'"))


def update_status(values, force):
    if not values:
        return "SKIP (no exposure metadata)"
    if force:
        return None

    try:
        numbers = [numeric(value) for value in values]
    except (ValueError, TypeError):
        return "SKIP (invalid exposure metadata; use --force to overwrite)"

    if any(value != numbers[0] for value in numbers):
        return "SKIP (exposure mismatch; use --force to overwrite)"
    if numbers[0] != 0:
        return "SKIP"
    return None


def frame_type(values):
    aliases = {
        "light": "light",
        "lightframe": "light",
        "science": "light",
        "dark": "dark",
        "darkframe": "dark",
        "flat": "flat",
        "flatframe": "flat",
        "flatfield": "flat",
        "flatfieldframe": "flat",
        "bias": "bias",
        "biasframe": "bias",
        "offset": "bias",
        "offsetframe": "bias",
    }

    found = set()
    for value in values:
        text = str(value).strip().strip("'\"").strip()
        if not text:
            continue

        label = re.sub(r"[^a-z0-9]", "", text.lower())
        if label.startswith("master"):
            label = label[len("master"):]

        kind = aliases.get(label)
        if kind == "bias":
            return "bias"
        found.add(kind)

    if len(found) == 1 and None not in found:
        return found.pop()
    return None


def target_exposure(kind, exposure, force):
    if kind == "flat" and (not force or exposure is None):
        return 3.0
    return exposure


def confirm_update(path, exposure, flat, overwrite, force, confirmation):
    # Flats require confirmation only for a target other than 3 seconds.
    needs_confirmation = exposure != 3.0 if flat else overwrite
    if not needs_confirmation:
        return True
    if confirmation.get("all", False):
        return True

    reason = (
        "flat file"
        if flat else "file with existing or mismatched exposure"
    )
    choices = "[y] Yes / [n] No"
    if force:
        choices += " / [a] Yes to All remaining files (including flats)"

    while True:
        try:
            answer = input(
                f"Update {reason} {path} to {exposure:g} seconds? "
                f"{choices} [default: No]: "
            ).strip().lower()
        except EOFError:
            return False

        if answer in {"y", "yes"}:
            return True
        if answer in {"", "n", "no"}:
            return False
        if force and answer in {"a", "all", "yes to all"}:
            confirmation["all"] = True
            return True

        print(
            "Please enter y, n, or a."
            if force else "Please enter y or n."
        )


def exposure_filename(path, exposure):
    stem = re.sub(
        r"(^|_)(Exp)[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(s)(?=_|$)",
        lambda match: (
            f"{match[1]}{match[2]}{exposure:.2f}{match[3]}"
        ),
        path.stem,
        flags=re.IGNORECASE
    )
    return path.with_name(stem + path.suffix)


def rename_updated_file(path, destination):
    if destination == path:
        return ""

    # Creating the link fails if the destination already exists.
    os.link(path, destination)
    try:
        path.unlink()
    except OSError:
        destination.unlink()
        raise

    return f"; renamed to {destination.name}"


def fix_fits(path, exposure, dry_run, force=False, confirmation=None):
    if confirmation is None:
        confirmation = {}

    # Check the entire file before changing any HDU.
    with fits.open(path, memmap=False) as hdul:
        indices = []
        overwrite = False

        kind = frame_type([
            hdu.header.get(key, "")
            for hdu in hdul
            for key in TYPE_KEYS
        ])
        if kind == "bias":
            return "SKIP (bias frame)"
        if kind is None:
            return "SKIP (unknown or conflicting frame type)"
        flat = kind == "flat"

        for i, hdu in enumerate(hdul):
            values = [
                hdu.header[key]
                for key in EXPOSURE_KEYS
                if key in hdu.header
            ]
            if not values:
                continue

            if force and update_status(values, False) is not None:
                overwrite = True

            status = update_status(values, force)
            if status and status != "SKIP":
                return status
            if status is None:
                indices.append(i)

    if not indices:
        return "SKIP"

    target = target_exposure(kind, exposure, force)
    if target is None:
        return "SKIP (exposure argument required for non-flat files)"

    destination = exposure_filename(path, target)
    if destination != path and os.path.lexists(destination):
        return f"SKIP (destination already exists: {destination.name})"

    if dry_run:
        rename = (
            f"; would rename to {destination.name}"
            if destination != path else ""
        )
        return f"WOULD UPDATE ({target:g} seconds){rename}"

    if not confirm_update(
        path, target, flat, overwrite, force, confirmation
    ):
        return (
            "SKIP (flat update declined)"
            if flat else "SKIP (overwrite declined)"
        )

    with fits.open(path, mode="update", memmap=False) as hdul:
        for i in indices:
            for key in EXPOSURE_KEYS:
                hdul[i].header[key] = (
                    target, "Exposure time in seconds"
                )
        hdul.flush()

    return "UPDATED" + rename_updated_file(path, destination)


def xisf_children(node, name):
    return [
        child for child in node.childNodes
        if child.nodeType == child.ELEMENT_NODE
        and child.localName == name
        and child.namespaceURI == node.namespaceURI
    ]


def read_xisf_header(path):
    import struct
    from xml.dom import minidom

    size = path.stat().st_size
    with path.open("rb") as stream:
        signature = stream.read(16)
        if len(signature) != 16 or signature[:8] != b"XISF0100":
            raise ValueError(
                "Not a supported monolithic XISF 1.0 file"
            )

        length = struct.unpack("<I", signature[8:12])[0]
        if not 0 < length <= min(size - 16, 64 * 1024 * 1024):
            raise ValueError(
                "Invalid or excessively large XISF XML header"
            )
        xml = stream.read(length)

    if b"<!DOCTYPE" in xml.upper() or b"<!ENTITY" in xml.upper():
        raise ValueError(
            "XISF headers with DTD/entity declarations are unsupported"
        )

    document = minidom.parseString(xml)
    root = document.documentElement
    if (
        root.localName != "xisf"
        or root.namespaceURI != "http://www.pixinsight.com/xisf"
    ):
        raise ValueError("Unrecognized XISF XML root")

    attachments = []
    for element in document.getElementsByTagName("*"):
        location = element.getAttribute("location")

        if location.startswith("attachment:"):
            match = re.fullmatch(
                r"attachment:([0-9]+):([0-9]+)", location
            )
            if not match:
                raise ValueError(
                    f"Unsupported attachment location: {location}"
                )

            position, length = map(int, match.groups())
            if position < 16 + len(xml) or position + length > size:
                raise ValueError(
                    "XISF attachment is outside the file data area"
                )
            attachments.append((element, position, length))

        elif (
            element.hasAttribute("location")
            and not location.startswith(("inline:", "embedded"))
        ):
            raise ValueError(
                f"Unsupported/external data location: {location}"
            )

    return signature, xml, document, attachments


def write_xisf_header(
    path, temp_path, signature, old_xml, document, attachments
):
    import hashlib
    import math
    import struct

    # Preserve the declared block alignment when moving attachments.
    quantum = 4096
    for element in document.getElementsByTagName("*"):
        if element.getAttribute("id") == "XISF:BlockAlignmentSize":
            alignment = int(element.getAttribute("value"))
            if not 0 < alignment <= 1024 * 1024:
                raise ValueError("Unsupported XISF block alignment")
            quantum = math.lcm(quantum, alignment)

    # Preserve all bytes after the original XML, including padding.
    shift = 0
    while True:
        for element, position, length in attachments:
            element.setAttribute(
                "location",
                f"attachment:{position + shift}:{length}"
            )

        new_xml = document.toxml(encoding="UTF-8")
        if len(new_xml) <= len(old_xml) + shift:
            break

        required = len(new_xml) - len(old_xml) - shift
        shift += ((required + quantum - 1) // quantum) * quantum

    if len(new_xml) > 0xFFFFFFFF:
        raise ValueError("Updated XISF header is too large")

    old_start = 16 + len(old_xml)
    new_start = old_start + shift
    digest = hashlib.sha256()

    with path.open("rb") as source, temp_path.open("wb") as output:
        output.write(
            signature[:8]
            + struct.pack("<I", len(new_xml))
            + signature[12:]
        )
        output.write(new_xml)

        remaining = new_start - output.tell()
        while remaining:
            chunk = min(remaining, 1024 * 1024)
            output.write(b"\0" * chunk)
            remaining -= chunk

        source.seek(old_start)
        while True:
            block = source.read(1024 * 1024)
            if not block:
                break
            digest.update(block)
            output.write(block)

        output.flush()
        os.fsync(output.fileno())

    # Verify the XML and all preserved data before replacing the source.
    _, checked_xml, checked_doc, checked_attachments = (
        read_xisf_header(temp_path)
    )
    if (
        checked_xml != new_xml
        or len(checked_attachments) != len(attachments)
    ):
        raise RuntimeError("XISF header verification failed")
    checked_doc.unlink()

    if temp_path.stat().st_size != path.stat().st_size + shift:
        raise RuntimeError("XISF file-size verification failed")

    check = hashlib.sha256()
    with temp_path.open("rb") as stream:
        stream.seek(new_start)
        while True:
            block = stream.read(1024 * 1024)
            if not block:
                break
            check.update(block)

    if check.digest() != digest.digest():
        raise RuntimeError(
            "XISF image/auxiliary data verification failed"
        )


def fix_xisf(path, exposure, dry_run, force=False, confirmation=None):
    if confirmation is None:
        confirmation = {}

    signature, old_xml, document, attachments = read_xisf_header(path)

    try:
        images = xisf_children(document.documentElement, "Image")
        if not images:
            return "SKIP (XISF contains no images)"

        # WBPP reads the first image. Other images remain unchanged.
        image = images[0]
        keywords = xisf_children(image, "FITSKeyword")
        properties = xisf_children(image, "Property")

        type_values = [
            node.getAttribute("value")
            for node in keywords
            if node.getAttribute("name") in TYPE_KEYS
        ]

        for node in properties:
            if node.getAttribute("id") == "Instrument:Frame:Type":
                value = node.getAttribute("value") or "".join(
                    child.data
                    for child in node.childNodes
                    if child.nodeType in (
                        child.TEXT_NODE,
                        child.CDATA_SECTION_NODE
                    )
                )
                type_values.append(value)

        if not any(str(value).strip() for value in type_values):
            type_values.append(image.getAttribute("imageType"))

        kind = frame_type(type_values)
        if kind == "bias":
            return "SKIP (bias frame)"
        if kind is None:
            return "SKIP (unknown or conflicting frame type)"

        native_keys = (
            "Instrument:ExposureTime",
            "Instrument:FrameExposureTime"
        )
        exposure_nodes = [
            node for node in keywords
            if node.getAttribute("name") in EXPOSURE_KEYS
        ] + [
            node for node in properties
            if node.getAttribute("id") in native_keys
        ]
        values = [
            node.getAttribute("value") for node in exposure_nodes
        ]

        status = update_status(values, force)
        if status:
            return status

        target = target_exposure(kind, exposure, force)
        if target is None:
            return "SKIP (exposure argument required for non-flat files)"

        destination = exposure_filename(path, target)
        if destination != path and os.path.lexists(destination):
            return (
                "SKIP (destination already exists: "
                f"{destination.name})"
            )

        backup = path.with_name(path.name + ".bak")
        if os.path.lexists(backup):
            return "SKIP (backup already exists)"

        if dry_run:
            rename = (
                f"; would rename to {destination.name}"
                if destination != path else ""
            )
            return (
                f"WOULD UPDATE ({target:g} seconds; "
                f"{len(images)} images preserved){rename}"
            )

        overwrite = (
            force and update_status(values, False) is not None
        )
        if not confirm_update(
            path, target, kind == "flat",
            overwrite, force, confirmation
        ):
            return "SKIP (update declined)"

        def add_node(name):
            prefix = image.prefix + ":" if image.prefix else ""
            node = document.createElementNS(
                image.namespaceURI, prefix + name
            )
            image.appendChild(node)
            return node

        for key in EXPOSURE_KEYS:
            nodes = [
                node for node in keywords
                if node.getAttribute("name") == key
            ]
            if not nodes:
                nodes = [add_node("FITSKeyword")]

            for node in nodes:
                node.setAttribute("name", key)
                node.setAttribute("value", str(target))
                node.setAttribute(
                    "comment", "Exposure time in seconds"
                )

        for key in native_keys:
            nodes = [
                node for node in properties
                if node.getAttribute("id") == key
            ]
            if not nodes:
                nodes = [add_node("Property")]

            for node in nodes:
                node.setAttribute("id", key)
                node.setAttribute("type", "Float64")
                node.setAttribute("value", str(target))
                node.setAttribute(
                    "comment", "Exposure time in seconds"
                )

                # Exposure is now stored as an inline scalar value.
                for attribute in (
                    "location", "compression",
                    "subblocks", "checksum"
                ):
                    if node.hasAttribute(attribute):
                        node.removeAttribute(attribute)

                for child in list(node.childNodes):
                    node.removeChild(child)
                    child.unlink()

        attachments = [
            item for item in attachments
            if item[0].hasAttribute("location")
        ]

        with tempfile.NamedTemporaryFile(
            suffix=".xisf",
            dir=path.parent,
            delete=False
        ) as temp:
            temp_path = Path(temp.name)

        try:
            write_xisf_header(
                path, temp_path, signature,
                old_xml, document, attachments
            )
            shutil.copymode(path, temp_path)

            # Create the backup without replacing an existing file.
            try:
                with path.open("rb") as source, backup.open("xb") as output:
                    shutil.copyfileobj(source, output, 1024 * 1024)
            except FileExistsError:
                return "SKIP (backup already exists)"
            except Exception:
                if backup.exists():
                    backup.unlink()
                raise

            if destination == path:
                os.replace(temp_path, path)
                renamed = ""
            else:
                # Do not overwrite a destination created concurrently.
                os.link(temp_path, destination)
                try:
                    path.unlink()
                except OSError:
                    destination.unlink()
                    raise
                renamed = f"; renamed to {destination.name}"

        finally:
            if temp_path.exists():
                temp_path.unlink()

        return (
            f"UPDATED ({len(images)} images preserved; backup created)"
            + renamed
        )
    finally:
        document.unlink()

def main():
    parser = argparse.ArgumentParser(
        description=(
            "Correct exposure metadata and Exp filename tokens "
            "in FITS/XISF files"
        )
    )
    parser.add_argument("folder", type=Path)
    parser.add_argument(
        "exposure",
        type=float,
        nargs="?",
        help="Exposure for lights/darks; flats use 3 seconds "
             "unless --force is set"
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Overwrite existing exposure values, including mismatches; "
             "also permits flats to use the requested exposure; "
             "offers Yes, No, or Yes to All"
    )
    parser.add_argument(
        "--recursive",
        action="store_true",
        help="Include subfolders"
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Preview changes without modifying files"
    )

    args = parser.parse_args()

    if not args.folder.is_dir():
        parser.error("Folder does not exist")
    if args.exposure is not None and not 0 < args.exposure < float("inf"):
        parser.error("Exposure must be a positive finite number")

    files = (
        args.folder.rglob("*")
        if args.recursive
        else args.folder.glob("*")
    )
    counts = {"updated": 0, "skipped": 0, "errors": 0}
    confirmation = {"all": False}

    for path in sorted(files):
        if not path.is_file():
            continue

        ext = path.suffix.lower()
        if ext not in EXTENSIONS:
            continue

        try:
            fix = fix_xisf if ext == ".xisf" else fix_fits
            result = fix(
                path,
                args.exposure,
                args.dry_run,
                args.force,
                confirmation
            )

            if result.startswith(("UPDATED", "WOULD UPDATE")):
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