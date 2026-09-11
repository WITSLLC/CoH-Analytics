# Changelog

All notable changes to CoH Analytics are documented here. Releases are early-access beta software.

## CoH Analytics 0.1.3 Beta

### What's new

- **Accounts → Build** viewer: a new read-only Build tab that shows a character's synced Homecoming build.
  - Sync from Build reads the character's current Homecoming buildsave.
  - Canonical Homecoming powerset and power names, canonical power icons, and enhancement icons.
  - Dedicated empty enhancement-slot artwork.
  - Three-column presentation with grouped, scrollable additional powersets/pools.
  - Per-character persisted Build snapshots that reload automatically.
  - Power hover details and enhancement hover values.
- **Analyze Build** window with Summary / By Set / PvP set-bonus views.
- **Update awareness**: **Help → Check for Updates** plus a startup update notification when a newer release is available.
- **Personal defeat percentage** in live session tracking.
- **Character gallery** expanded to **20 built-in icons** (six new icons).
- **Experimental Linux / Wine** installation guide (see [`docs/Linux-Wine.md`](docs/Linux-Wine.md)).

### Improvements

- Canonical power names and icons throughout the Build viewer.
- Character icon picker now uses a horizontal gallery: female icons on the top row, male icons on the bottom row, with mouse-wheel horizontal scrolling.
- Alternate (gender-neutral) badge-name presentation.
- Clipboard copy reliability with transient "Copied" feedback.

### Fixes

- Fixed a Build sync issue that could incorrectly merge or rename character records.
- Fixed Reference search results not displaying.
- Fixed Reference level filter defaults/state when switching sections.
- Fixed the tracked-session elapsed clock not refreshing.
- Fixed badge page mouse-wheel scrolling.
- Fixed corrected horizontal scrollbar sizing in the character icon gallery.
- Fixed Wine log-source continuity so normal chat-log growth is not mistaken for log replacement.
- Build Analysis correctness and stability fixes, including corrected set and global bonus handling.

### Packaging

- Canonical `0.1.3-beta` versioning sourced from `Directory.Build.props`.
- Improved MSI same-version and upgrade handling.
- Standardized portable release artifacts (self-contained and framework-dependent Windows x64 ZIPs).

### Notes

- CoH Analytics remains a **Windows application**.
- Linux use is **experimental compatibility through Wine**, not a native Linux build.
- Build Analysis reflects set and global bonuses from the synced build; it is not a full character planner or a complete stacking-aware combat model.
