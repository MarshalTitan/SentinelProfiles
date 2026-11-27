# Navigation polish - 0.2.1.17

Mouse activation now clears rounded focus/hover decoration after pointer exit; keyboard/controller focus keeps its visible cue. Native focus and button callbacks are unchanged.

Uses compatible SentinelCore UI 0.4.1.3, source `0d59460cc592f1589600e11e4e9e04478667b629`. Existing Modern sidebar icons have borderless idle states, persistent rounded selection, soft animated hover highlights and padded translucent tooltip bubbles. Custom minimize/restore controls use drawn down/up chevrons; close uses a more visible X. Native hit areas, keyboard/controller focus, window dragging, saved placement and callbacks remain intact. Headers follow the selected page. Classic and plugin functionality are preserved.

Dependency packages are restored from the immutable release and SHA-256 checked before locked restore. Locks record the release SHA-512 values. Automated builds/native UI/persistence checks are recorded in the coordinated Sentinel report. Actual FFXIV visual acceptance remains separate from native renderer checks.

---

# 0.2.1.15

Privacy maintenance: bundle Core 0.3.1.1 with project copyright attribution. Runtime source, configuration, and gameplay behavior are unchanged.

# Sentinel Profiles 0.2.1.14

Fixes an editor crash when Dalamud's local SQLite configuration database briefly reports "database is locked" during a profile selection or other settings save. The new save queue keeps the latest configuration in memory, retries with bounded backoff, and confirms recovery after a prolonged lock. An unresolved lock reports that settings remain unsaved rather than silently claiming persistence. Unrelated database/write errors are not swallowed.

This does not synchronize profiles between computers or FFXIV characters. The editor selection remains local configuration; profile application and restart behavior are unchanged. Core retry tests and installable-package validation run in CI; a live SQLite contention scenario remains to be tested in game.
