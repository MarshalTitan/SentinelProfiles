# Sentinel Profiles 0.2.1.14

Fixes an editor crash when Dalamud's local SQLite configuration database briefly reports "database is locked" during a profile selection or other settings save. The new save queue keeps the latest configuration in memory, retries with bounded backoff, and confirms recovery after a prolonged lock. An unresolved lock reports that settings remain unsaved rather than silently claiming persistence. Unrelated database/write errors are not swallowed.

This does not synchronize profiles between computers or FFXIV characters. The editor selection remains local configuration; profile application and restart behavior are unchanged. Core retry tests and installable-package validation run in CI; a live SQLite contention scenario remains to be tested in game.
