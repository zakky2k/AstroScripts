# This folder is empty on purpose

The N.I.N.A. plugin that used to live here moved to its own repository on 26 November 2026:

**https://github.com/zakky2k/unphased-nina-plugin**

- Unphased Night Planner - a dockable night-planning chart for N.I.N.A. (Sun/Moon altitude, twilight
  bands and moon phase across consecutive nights, with a calendar, week zoom and an hour scrub).
- Local working path: C:\Users\zakfo\Documents\unphased-nina-plugin
- Copied out of this folder at commit 1141440 with an identical file list (78 files, byte-verified
  against the git tree) and the same layout (UnphasedCore, UnphasedCore.Tests, UnphasedNina), so
  nothing here was left dangling. Its stale build output (bin/, obj/, artifacts/, local.props) was
  deleted from disk; .gitignore carries rules for it in case it reappears.
- UnphasedCore is the C# mirror of the night computation in this repository's Python planner/, kept
  for cross-checking against the probe fixtures. The web planner is Python and never built against
  it, so the copy in the plugin repository is independent: if the planner's astronomy changes, port
  it there too.
- Round-by-round history and the state of play live in HANDOFF.md in that repository. This
  repository's HANDOFF.md covers the web planner.
