# Changelog

## 0.1.0.0 — very early beta

First build that runs inside N.I.N.A.: a dockable **Unphased Night Planner** panel
(Image page → the puzzle-piece button in the Info bar) drawing the selected night
from N.I.N.A.'s own astronomy.

- Dockable panel with a Tonight / step-by-one-night header, a window of nights below
  the chart, and the selected row kept in sync with what the chart shows.
- Hand-drawn chart: sun altitude curve, moon track across the whole frame, the
  standard 0 / -6 / -12 / -18° twilight bands painted as background colour,
  labelled sunset/sunrise and moonrise/moonset, and a moon phase glyph — lit limb
  plus terminator, mirrored for the observer's hemisphere — at the moon's highest
  point of that night, labelled with its illumination.
- The time scale adapts to the panel: two nights side by side while they stay
  legible, otherwise one noon-to-noon night stretched to the full width.
- The astronomy is a C# port (`UnphasedCore`), checked against the web planner's
  Prague / summer / Sydney fixtures.

Deliberately still rough at this stage: chart text and the phase glyph are small at
panel scale, labels sit directly on the sky with no backing, the horizon line is no
stronger than the other altitude lines, and the twilight shading leaves a faint
vertical seam per sample across the daylight bands. Next round is listed in
`nina-plugin/HANDOFF.md`.
