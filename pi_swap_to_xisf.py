#!/usr/bin/env python3
"""
PixInsight Parallel Swap to XISF Recovery
Recovers uncompressed Float32 grayscale and RGB images from
PixInsight parallel swap files into XISF 1.0 format.
Supports:
    - Multiple parallel swap segments
    - Direct conversion to XISF
    - Repair of previously generated XISF headers
    - Structural validation of XISF files
    - Batch recovery of all supported images in a swap directory
    - Float32 RGB image recovery (3-channel planar data)
    - Reorder per-segment RGB channel blocks into full XISF planes
    - Dry-run preview and per-image failure reporting
No third-party Python dependencies required.
IMPORTANT:
    Always preserve your original swap files.
    Back up any XISF before using --repair-header.
    Batch mode should be run against a backup of an inactive swap directory.
The swap format is undocumented and this converter
has been verified with supplied Float32 monochrome data.
RGB swap layout: each parallel segment contains consecutive R, G, B
blocks for its portion of the image. Output must combine corresponding channel blocks
across all segments into complete planar R, G, B channels.
"""
import argparse
import os
import re
import struct
import tempfile
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

# --------------------------------------------------
# Configuration
# --------------------------------------------------
SWAP_HEADER_SIZE = 128
ATTACHMENT_OFFSET = 4096
CHUNK_SIZE = 4 * 1024 * 1024
XISF_SIGNATURE = b"XISF0100"
XISF_NAMESPACE = "http://www.pixinsight.com/xisf"
XISF_ROOT = f"{{{XISF_NAMESPACE}}}xisf"
IMAGE_TAG = f"{{{XISF_NAMESPACE}}}Image"
METADATA_TAG = f"{{{XISF_NAMESPACE}}}Metadata"
PREAMBLE = struct.Struct("<8sII")
SAMPLE_SIZE = 4  # Float32
SWAP_PIXEL_FORMAT = bytes.fromhex("0101002001000000")
CONTINUATION = re.compile(r"-part\d+\.swp$", re.IGNORECASE)

# --------------------------------------------------
# XISF XML header
# --------------------------------------------------

def color_space(channels):
    return "Gray" if channels == 1 else "RGB"


def serialize_xml(root):
    # PixInsight can reject a namespace-prefixed root such as <ns0:xisf>.
    # Register the default namespace to emit <xisf xmlns="..."> instead.
    ET.register_namespace("", XISF_NAMESPACE)
    return ET.tostring(root, encoding="utf-8", xml_declaration=True)


def create_xml(width, height, data_size,
               image_id="RecoveredImage", channels=1):
    """Build the XML for one uncompressed, planar Float32 attachment."""
    if channels not in (1, 3):
        raise ValueError("Only grayscale and RGB images are supported")
    root = ET.Element(XISF_ROOT, version="1.0")
    ET.SubElement(
        root, IMAGE_TAG,
        id=image_id,
        geometry=f"{width}:{height}:{channels}",
        sampleFormat="Float32",
        bounds="0:1",
        colorSpace=color_space(channels),
        pixelStorage="Planar",
        byteOrder="little",
        location=f"attachment:{ATTACHMENT_OFFSET}:{data_size}",
    )
    ET.SubElement(root, METADATA_TAG)
    return serialize_xml(root)


def padded_header(xml):
    """Pack the binary preamble and pad the XML up to the pixel attachment."""
    padding = ATTACHMENT_OFFSET - PREAMBLE.size - len(xml)
    if padding < 0:
        raise ValueError("XISF header exceeds reserved space")
    return PREAMBLE.pack(XISF_SIGNATURE, len(xml), 0) + xml + bytes(padding)


# --------------------------------------------------
# XISF parsing and validation
# --------------------------------------------------

def read_xml(stream):
    preamble = stream.read(PREAMBLE.size)
    if len(preamble) != PREAMBLE.size:
        raise ValueError("Incomplete XISF preamble")
    signature, header_length, reserved = PREAMBLE.unpack(preamble)
    if signature != XISF_SIGNATURE:
        raise ValueError("Invalid XISF signature")
    if reserved != 0:
        raise ValueError("Invalid reserved header field")
    if not 0 < header_length <= ATTACHMENT_OFFSET - PREAMBLE.size:
        raise ValueError("Invalid XML header length")
    xml = stream.read(header_length)
    if len(xml) != header_length:
        raise ValueError("Incomplete XISF XML header")
    return xml


def parse_image(xml):
    """Read the single image element, allowing prefixes for header repair."""
    root = ET.fromstring(xml)
    if root.tag != XISF_ROOT:
        raise ValueError("Invalid XISF XML namespace")
    if root.get("version") != "1.0":
        raise ValueError("Invalid XISF version")
    images = root.findall(IMAGE_TAG)
    if len(images) != 1:
        raise ValueError("Expected exactly one image element")
    return root, images[0]


def attachment_location(image):
    location = image.get("location", "").split(":")
    if len(location) != 3:
        raise ValueError("Invalid attachment location")
    kind, offset, data_size = location
    if kind != "attachment":
        raise ValueError("Unsupported attachment type")
    return int(offset), int(data_size)


def validate_xml(xml, file_size):
    """Check the XISF structure supported by this recovery script."""
    root, image = parse_image(xml)
    if not re.search(rb"<xisf(?:\s|>)", xml):
        raise ValueError("XISF XML must contain an unprefixed <xisf> root element")
    if root.find(METADATA_TAG) is None:
        raise ValueError("Missing XISF Metadata element")
    required = [
        "geometry",
        "sampleFormat",
        "bounds",
        "colorSpace",
        "location"
    ]
    for attribute in required:
        if attribute not in image.attrib:
            raise ValueError(
                f"Missing mandatory attribute: {attribute}"
            )
    if image.get("sampleFormat") != "Float32":
        raise ValueError("Unsupported pixel format")
    if image.get("colorSpace") not in ("Gray", "RGB"):
        raise ValueError("Unsupported colour space")
    dimensions = tuple(
        int(x) for x in image.get("geometry").split(":")
    )
    if len(dimensions) != 3:
        raise ValueError("Invalid image geometry")
    width, height, channels = dimensions
    if width <= 0 or height <= 0 or channels not in (1, 3):
        raise ValueError("Unsupported image dimensions")
    if image.get("colorSpace") != color_space(channels):
        raise ValueError("Channel count and colour space do not match")
    if image.get("pixelStorage", "Planar") != "Planar":
        raise ValueError("Unsupported pixel storage")
    bounds = tuple(
        float(x) for x in image.get("bounds").split(":")
    )
    if len(bounds) != 2 or bounds[0] >= bounds[1]:
        raise ValueError("Invalid floating-point bounds")
    offset, data_size = attachment_location(image)
    expected_size = width * height * channels * SAMPLE_SIZE
    if data_size != expected_size:
        raise ValueError(
            "Image attachment size does not match dimensions"
        )
    if offset < PREAMBLE.size + len(xml):
        raise ValueError(
            "Image attachment overlaps XML header"
        )
    if file_size != offset + data_size:
        raise ValueError(
            "XISF file size does not match attachment"
        )
    return width, height, image.get("colorSpace"), data_size


def validate(path):
    """Validate the XML, attachment location, and total XISF file size."""
    with Path(path).open("rb") as stream:
        width, height, space, data_size = validate_xml(
            read_xml(stream), os.fstat(stream.fileno()).st_size
        )
    print(f"Validated: {width}x{height}, Float32 {space}, {data_size:,} image bytes")


# --------------------------------------------------
# Repair existing XISF header
# --------------------------------------------------

def repair_header(path):
    """Restore missing bounds and Metadata and normalize the XML namespace."""
    with Path(path).open("r+b") as stream:
        root, image = parse_image(read_xml(stream))
        image.attrib.setdefault("bounds", "0:1")
        if root.find(METADATA_TAG) is None:
            ET.SubElement(root, METADATA_TAG)

        offset, _ = attachment_location(image)
        if offset != ATTACHMENT_OFFSET:
            raise ValueError("Cannot repair unexpected attachment offset")

        # Check the proposed header before overwriting anything. Rewrite only
        # the reserved header bytes so the pixel attachment stays untouched.
        xml = serialize_xml(root)
        header = padded_header(xml)
        validate_xml(xml, os.fstat(stream.fileno()).st_size)
        stream.seek(0)
        stream.write(header)

    validate(path)
    print("Header repair completed successfully.")


# --------------------------------------------------
# Inspect PixInsight parallel swap files
# --------------------------------------------------

@dataclass(frozen=True)
class SwapSegment:
    path: Path
    data_offset: int
    channel_bytes: int


@dataclass(frozen=True)
class SwapImage:
    width: int
    height: int
    channels: int
    segments: tuple

    @property
    def pixel_bytes(self):
        return self.width * self.height * self.channels * SAMPLE_SIZE


def is_primary_swap(path):
    return path.suffix.lower() == ".swp" and not CONTINUATION.search(path.name)


def read_swap(primary):
    """Read the swap geometry and locate each segment's channel blocks."""
    if not is_primary_swap(primary):
        raise ValueError("Specify a primary .swp file")
    with primary.open("rb") as stream:
        header = stream.read(SWAP_HEADER_SIZE)
    if len(header) != SWAP_HEADER_SIZE:
        raise ValueError("Incomplete swap header")
    if not header.startswith(b"egamiwar"):
        raise ValueError("Unrecognized PixInsight swap signature")

    # These fields describe the known uncompressed Float32 swap layout.
    if header[0x0c:0x14] != SWAP_PIXEL_FORMAT:
        raise ValueError("Unsupported PixInsight swap pixel format")
    width, height, channels = struct.unpack_from("<III", header, 0x1c)
    thread_count, = struct.unpack_from("<Q", header, 0x68)
    if not (1 <= width <= 200000 and 1 <= height <= 200000
            and channels in (1, 3) and 1 <= thread_count <= 256):
        raise ValueError(
            f"Unsupported swap geometry or thread count: {width}x{height}, "
            f"{channels} channels, {thread_count} threads"
        )

    # Only the primary has a header. Each continuation holds raw pixels.
    paths = [primary]
    paths.extend(
        primary.with_name(f"{primary.stem}-part{index:03d}.swp")
        for index in range(1, thread_count)
    )
    segments = []
    for index, path in enumerate(paths):
        if not path.is_file():
            raise FileNotFoundError(f"Missing swap segment: {path}")
        offset = 0 if index else SWAP_HEADER_SIZE
        pixel_bytes = path.stat().st_size - offset
        # A segment can end partway through a row. Each channel block must
        # still contain the same number of complete Float32 samples.
        if pixel_bytes < 0 or pixel_bytes % (channels * SAMPLE_SIZE):
            raise ValueError(f"Cannot split channel blocks: {path.name}")
        segments.append(SwapSegment(path, offset, pixel_bytes // channels))

    image = SwapImage(width, height, channels, tuple(segments))
    actual_bytes = sum(segment.channel_bytes for segment in segments) * channels
    if actual_bytes != image.pixel_bytes:
        raise ValueError(
            f"Pixel data size mismatch: {actual_bytes:,} vs {image.pixel_bytes:,}"
        )
    return image


def inspect_swap(primary):
    """Return geometry, segment count, and byte count for a valid group."""
    image = read_swap(Path(primary))
    return image.width, image.height, image.channels, len(image.segments), image.pixel_bytes


# --------------------------------------------------
# Recover PixInsight parallel swap to XISF
# --------------------------------------------------

def copy_pixels(image, destination):
    """Stream the swap segments into complete XISF channel planes."""
    # For RGB, each segment contains its own R, G, and B blocks. In XISF,
    # every segment's R block goes into the R plane, followed by the full G
    # and B planes. Copying whole RGB segments would produce striped images.
    # For grayscale, there is one plane and segments are simply concatenated.
    labels = "RGB" if image.channels == 3 else ("Gray",)
    for channel, label in enumerate(labels):
        print(f"Writing {label} channel ({channel + 1}/{image.channels})", flush=True)
        for index, segment in enumerate(image.segments, 1):
            print(f"  Segment {index}/{len(image.segments)}: {segment.path.name}", flush=True)
            with segment.path.open("rb") as stream:
                stream.seek(segment.data_offset + channel * segment.channel_bytes)
                remaining = segment.channel_bytes
                while remaining:
                    block = stream.read(min(CHUNK_SIZE, remaining))
                    if not block:
                        raise EOFError(f"Unexpected end of segment: {segment.path.name}")
                    destination.write(block)
                    remaining -= len(block)


def write_recovery(image, output):
    """Create a new XISF, then remove it if writing or validation fails."""
    print(f"Image dimensions: {image.width} x {image.height}")
    print(f"Parallel swap segments: {len(image.segments)}")
    print(f"Pixel data: {image.pixel_bytes:,} bytes")
    header = padded_header(create_xml(
        image.width, image.height, image.pixel_bytes, channels=image.channels
    ))

    # Open exclusively before entering the cleanup block. If another process
    # created the destination first, its file must survive the failed open.
    destination = output.open("xb")
    try:
        with destination:
            destination.write(header)
            copy_pixels(image, destination)
        validate(output)
    except BaseException:
        output.unlink(missing_ok=True)
        raise


def recover(primary, output):
    """Recover a single primary swap file and its continuation segments."""
    image = read_swap(Path(primary).resolve())
    output = Path(output).resolve()
    write_recovery(image, output)
    print(f"Recovery completed: {output}")


# --------------------------------------------------
# Batch recovery of a swap directory
# --------------------------------------------------


def original_image_identifier(primary):
    """Recover the image identifier from the PixInsight swap *filename*.

    Example: ~PI~TOKEN~MasterLight_Ha_c.swp -> MasterLight_Ha_c
    This name is not taken from embedded XISF metadata; there is no XISF yet.
    """
    match = re.fullmatch(r"~PI~([^~]+)~(.+)\.swp", primary.name, re.IGNORECASE)
    if not match:
        return None, None
    return match.group(2), match.group(1)


def safe_image_name(name):
    """Remove Windows-invalid path characters; retain useful image identifiers."""
    name = re.sub(r'[<>:"/\\|?*\x00-\x1f]', '_', name)
    name = name.strip(' .')
    return name[:160] or 'UnnamedImage'


def batch_destination(directory, identifier, token, reserved_names):
    """Choose a filename, using the session token and a suffix on collisions."""
    base = "Recovered_" + safe_image_name(identifier)
    stem = base
    suffix = 1
    while True:
        name = f"{stem}.xisf"
        if name.lower() not in reserved_names and not (directory / name).exists():
            reserved_names.add(name.lower())
            return directory / name
        if suffix == 1:
            base += "_" + safe_image_name(token)
            stem = base
        else:
            stem = f"{base}_{suffix}"
        suffix += 1


def recover_batch_image(image, destination):
    """Publish a validated file from a temporary directory on the same volume."""
    with tempfile.TemporaryDirectory(prefix=".recover_", dir=destination.parent) as directory:
        temporary = Path(directory) / "image.partial"
        write_recovery(image, temporary)
        # Publish atomically and fail if another process claimed the name
        # during recovery. On Windows rename already has this behavior.
        if os.name == "nt":
            temporary.rename(destination)
        else:
            os.link(temporary, destination)


def recover_all(swap_directory, output_directory, dry_run=False):
    """Find primary files, recover each supported group, continue on errors."""
    swap_directory = Path(swap_directory).resolve()
    output_directory = Path(output_directory).resolve()
    if not swap_directory.is_dir():
        raise NotADirectoryError(swap_directory)
    if swap_directory == output_directory:
        raise ValueError('Output directory must be separate from swap directory')
    if not dry_run:
        output_directory.mkdir(parents=True, exist_ok=True)
    # Only scan this directory; numbered -partNNN.swp files are not primaries.
    primaries = sorted(
        (p for p in swap_directory.iterdir()
         if p.is_file() and is_primary_swap(p)),
        key=lambda p: p.name.lower()
    )
    print(f'Found {len(primaries)} candidate primary swap file(s).')
    recovered = skipped = failed = 0
    reserved_names = set()
    for index, primary in enumerate(primaries, 1):
        print(f'\n[{index}/{len(primaries)}] {primary.name}')
        identifier, token = original_image_identifier(primary)
        if identifier is None:
            print('  SKIP: filename does not match ~PI~TOKEN~ImageId.swp')
            skipped += 1
            continue
        try:
            image = read_swap(primary)
        except (OSError, ValueError, struct.error) as exc:
            print(f'  SKIP: {exc}')
            skipped += 1
            continue
        destination = batch_destination(output_directory, identifier, token, reserved_names)
        print(
            f"  {image.width}x{image.height} Float32 {color_space(image.channels)}, "
            f"{len(image.segments)} segment(s), {image.pixel_bytes:,} pixel bytes"
        )
        print(f"  Output: {destination}")
        if dry_run:
            recovered += 1
            continue
        try:
            recover_batch_image(image, destination)
            recovered += 1
            print(f"  SUCCESS: {destination.name}")
        except Exception as exc:
            failed += 1
            print(f"  FAILED: {type(exc).__name__}: {exc}")
    print(f'\nBatch summary: {recovered} '
          f'{"eligible" if dry_run else "recovered"}, '
          f'{skipped} skipped, {failed} failed.')
    return 1 if failed else 0


# --------------------------------------------------
# Command-line interface
# --------------------------------------------------

def main():
    parser = argparse.ArgumentParser(
        description=(
            "Recover PixInsight parallel swap files "
            "to XISF 1.0"
        )
    )
    parser.add_argument(
        "primary",
        nargs="?",
        help="Primary PixInsight .swp file"
    )
    parser.add_argument(
        "output",
        nargs="?",
        help="Output XISF file"
    )
    parser.add_argument(
        "--repair-header",
        metavar="XISF",
        help="Repair an existing XISF header"
    )
    parser.add_argument(
        "--validate",
        metavar="XISF",
        help="Validate an existing XISF file"
    )
    # Batch mode additions; single-file and repair commands remain unchanged.
    parser.add_argument(
        "--all",
        metavar="SWAP_DIR",
        help="Recover all supported primary swap files from a directory"
    )
    parser.add_argument(
        "--output-dir",
        metavar="DIRECTORY",
        help="Destination directory for batch-recovered XISF images"
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Show recoverable files and output filenames without writing"
    )
    args = parser.parse_args()
    if args.all:
        if not args.output_dir:
            parser.error("--all requires --output-dir")
        if args.primary or args.output or args.repair_header or args.validate:
            parser.error("Batch mode cannot be combined with other operations")
        raise SystemExit(recover_all(args.all, args.output_dir, args.dry_run))
    if args.dry_run or args.output_dir:
        parser.error("--dry-run and --output-dir require --all")
    if args.repair_header:
        repair_header(args.repair_header)
    elif args.validate:
        validate(args.validate)
    elif args.primary and args.output:
        recover(args.primary, args.output)
    else:
        parser.print_help()


if __name__ == "__main__":
    main()