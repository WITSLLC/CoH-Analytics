# Changelog

All notable changes to CoH Analytics are documented here. Releases are early-access beta software.

## CoH Analytics 0.1.4 Beta

### What's new

- **Analytics → Overview** now lists durable historical session segments after a session is finished.
- **Analytics → Combat** historical views for finished segments: Offense, Incoming, and Healing.
- **Analytics → Compare** compares two finished historical segments side by side.
- **Durable segment persistence**: finished sessions are written as historical segments that can be browsed, compared, and deleted.
- **Finish Session** on Live Session: manually close the current gameplay session and persist it as a historical segment.
- **Automatic session finalization** when the bound Homecoming client process exits, using exact process ownership only (not heuristic log attribution).
- **Automatic Analytics refresh**: Overview, Combat, and Compare update when a new historical segment is published.
- HTML combat reports for historical segments.

### Improvements

- Parser drain and session-finish fencing so live ingest cannot race finalization.
- Combat parser and analytics engine work from this cycle is now exposed through the historical Analytics surfaces.
- Proc identity attribution, frozen-build context, and combat report language/chart refinements.
- Segment publication isolates subscriber failures so a UI refresh error cannot rewrite a successful persist.

### Fixes

- Durable historical segment deletion.
- Live Welcome session boundary handling.
- First-use character candidate fallback.
- Combat snapshot coalescing across candidate reset.
- Authoritative process-exit lifecycle: PID reuse is not treated as the same process; remaining-client contexts are retired correctly; cancelled waits after a completed persist still return the completed result.

### Packaging

- Canonical `0.1.4-beta` versioning sourced from `Directory.Build.props`.
- Same three public artifacts as 0.1.3: Windows x64 MSI, portable self-contained ZIP, and portable framework-dependent ZIP.

### Notes

- CoH Analytics remains a **Windows application**.
- Linux use is **experimental compatibility through Wine**, not a native Linux build. The last end-to-end Wine validation was **0.1.3 Beta** (self-contained win-x64). **0.1.4 Beta has not been re-validated under Wine** in this release pass. The built-in character icon gallery is a known Wine issue (empty / unavailable); it works on Windows.
- Historical Combat and Compare report observed segment totals. They are not a full combat model or character planner.
- Automatic process-exit finalization requires an unambiguous bound Homecoming process. Ambiguous or stale bindings fail closed and do not invent a session owner.

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
