# Profile server-info bar — 0.2.1.7

Optional default-OFF display in Dalamud's server info bar (DTR).
Modern: /sprofiles → Appearance → Show profile in server info bar.
Classic: same checkbox in the header. Settings save immediately.

Shows the last applied profile, not whichever profile is selected for editing.
An exclamation mark means explicit plugin-state drift/missing plugins or an application
problem. Hover for details. Applying and no-profile states are explicit. A saved
last-applied name after restart does not silently re-apply plugin states.
Click opens Profiles only; it never switches or re-applies a profile.

Uses one stable entry, updates at most twice a second, hides while logged out or the
option is OFF, respects Dalamud bar visibility/order settings and removes callbacks/entry
on unload. No Hub/HUD dependency, new IPC, profile semantics change or Core upgrade.

Acceptance after normal update: enable the option, apply a normal safe profile and see its
name. Select a different profile for editing: the bar must retain the applied name.
Check tooltip/click, toggle OFF/ON and reload Profiles once: no duplicate/ghost entry.
If no entry appears, check Dalamud Settings → Server Info Bar for Sentinel Profiles visibility.
Report failures only with version/theme and concise description/screenshot.
Rollback: option OFF; retain config. Prior immutable 0.2.1.6 is a binary rollback point.
