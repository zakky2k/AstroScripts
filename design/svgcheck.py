"""Structural checks on the rendered SVG. No image viewing involved: this
parses the markup and asserts the properties the design depends on."""

import re
import sys

def check(path):
    src = open(path, encoding="utf-8").read()
    rects = [
        (m.group(3), float(m.group(1)), float(m.group(2)))
        for m in re.finditer(r'<rect x="([\d.]+)" y="\d+" width="([\d.]+)" height="\d+" fill="url\(#b-(\w+)', src)
    ]
    glyphs = len(re.findall(r'<path d="M 0,-', src))
    ticks = len(re.findall(r'<rect x="[\d.]+" y="\d+" width="3" height="\d+" fill="#F0B86E"', src))
    gridlines = len(re.findall(r'stroke-dasharray="[15] 6"', src))
    horizon = 'HORIZON' in src
    notice = re.search(r'No astronomical darkness|True darkness', src)

    gaps = []
    for i in range(1, len(rects)):
        prev_end = rects[i - 1][1] + rects[i - 1][2]
        gaps.append(round(rects[i][1] - prev_end, 2))
    overlaps = [g for g in gaps if g < -0.5]
    holes = [g for g in gaps if g > 0.5]

    print(f"{path}")
    print(f"  bands          : {len(rects)}  " + " ".join(f"{b}" for b, _, _ in rects))
    print(f"  band gaps      : {gaps}")
    print(f"  holes / overlaps: {len(holes)} / {len(overlaps)}")
    print(f"  moon glyphs     : {glyphs}")
    print(f"  sun/moon bars   : {ticks} (expect 2: sunset + sunrise)")
    print(f"  altitude lines  : {gridlines} (expect 10 above horizon: 0..90)")
    print(f"  horizon rule   : {horizon}")
    print(f"  darkness notice : {bool(notice)}")
    ok = not holes and not overlaps and gridlines == 10 and horizon and notice and glyphs >= 2 and ticks == 2
    print(f"  -> {'PASS' if ok else 'CHECK'}")
    return ok


if __name__ == "__main__":
    results = [check(p) for p in sys.argv[1:]]
    sys.exit(0 if all(results) else 1)
