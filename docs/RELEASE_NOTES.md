# Sentinel Profiles 0.2.1.8

Adds a cached, versioned Hub overview contract for saved profile names/stable IDs, selected and last-applied profiles, existing match/drift status and application progress. Revision notifications replace repeated full-list polling.

Hub can request that a valid profile be opened in the existing editor. This changes editor selection only; it never applies a profile, edits rules, or toggles plugins. Opening is refused during application. Unsupported/off-thread calls fail safely and providers unregister on unload.

Profile management, existing apply/restart behavior, saved settings/layouts and the optional server-info-bar display remain unchanged. Core UI remains exactly 0.3.1. See docs/HUB_IPC.md for the additive public contract and acceptance checks.