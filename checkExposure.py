import json
import sys
from pathlib import Path

import PyUpdateExp as updater

path = Path(sys.argv[1])
requested = float(sys.argv[2]) if len(sys.argv) > 2 else 180.0

_, _, document, _ = updater.read_xisf_header(path)

try:
    image = updater.xisf_children(
        document.documentElement, "Image"
    )[0]
    keywords = updater.xisf_children(image, "FITSKeyword")
    properties = updater.xisf_children(image, "Property")

    fits_types = [
        node.getAttribute("value")
        for node in keywords
        if node.getAttribute("name") in updater.TYPE_KEYS
    ]

    native_types = []
    for node in properties:
        if node.getAttribute("id") == "Instrument:Frame:Type":
            value = node.getAttribute("value") or "".join(
                child.data
                for child in node.childNodes
                if child.nodeType in (
                    child.TEXT_NODE, child.CDATA_SECTION_NODE
                )
            )
            native_types.append(value)

    type_values = fits_types + native_types
    if not any(str(value).strip() for value in type_values):
        type_values.append(image.getAttribute("imageType"))

    kind = updater.frame_type(type_values)

    print(json.dumps({
        "script_loaded": str(Path(updater.__file__).resolve()),
        "file": str(path),
        "fits_type_values": fits_types,
        "native_type_values": native_types,
        "imageType": image.getAttribute("imageType"),
        "detected_type": kind,
        "requested_seconds": requested,
        "target_without_force": (
            updater.target_exposure(kind, requested, False)
            if kind in {"light", "dark", "flat"} else "SKIP"
        ),
    }, indent=2))

finally:
    document.unlink()