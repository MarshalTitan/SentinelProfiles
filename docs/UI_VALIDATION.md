# Profiles UI repair validation

Version: 0.2.1.11. Scope: drag safety, compact quick switch, and button icon spacing.

## Automated

- Existing core behavior, configuration round-trip, theme migration, and
  minimized-window persistence tests remain enabled.
- State-control fit tests use the 680px Modern minimum width budget, wider
  widths, 100/125/150/200% UI scale, and independent 85/100/120% font metrics.
- The three Profile State controls remain inline at the supported minimum width.
- Boundary checks require stacking only below the measured intrinsic fit width.
- Plugin Open/Settings layout checks cover 680px minimum, long names, scale/font
  variations, and stacked fallback at smaller transient widths.
- Profile move tests verify insertion above/below a target, no-op drops, saved
  ordering, and unchanged selection, last applied ID and plugin rules.
- Sidebar rendering iterates a snapshot, so a drop that saves a new order within
  the draw frame cannot invalidate the current profile enumerator.
- CI builds against Dalamud API 15 and validates the installable ZIP.
- The optional bar picker uses the existing application coordinator; profile
  rules and restart switching logic are untouched.

## In-game checks (pending; not claimed by CI)

At 680 × 560 and 1080 × 720 logical pixels, in both themes and at 100/150/200%:

1. Empty, one, and many profiles, including 64-character names: scroll to every
   Profile Action; create, duplicate, rename, delete, apply, and reapply are
   reachable with their existing busy/protection rules.
2. Long plugin display/internal names, missing and protected rows: labels wrap;
   current state remains distinct from profile state. All three inline state buttons,
   row selection, search/filter, and bulk controls remain reachable. Verify
   Open and Settings invoke the matching plugin windows, show icons within
   their buttons without overlapping the words at all UI scales, and remain disabled for missing, unloaded, and no-UI plugins.
   Missing plugin rows remain sorted below installed plugins.
3. Long apply feedback and problem lists: reach the plugin table by scrolling.
4. Drag several profiles above and below others repeatedly with the editor
   open; verify there is no collection-modified exception, then restart and
   verify the saved order. Move Up/Down must work through keyboard/controller focus.
   Click the server-info bar entry to toggle a Sentinel-styled, titleless list
   of only profile names and Open Profiles. Apply a profile
   from it with the editor closed; verify busy state, long names, scrolling,
   outside-click dismissal, second-click dismissal, and Open Profiles.
5. Appearance: reach and activate Switch to Classic at minimum width.
6. Keyboard/gamepad navigation: focus and activate profile entries, actions,
   search/filter, selection, state controls, Open/Settings, and modal buttons; scrolling follows
   focus. Resize all three create/rename/delete modals, scroll each at minimum
   height, and check that fields, action buttons, and Cancel remain reachable
   at 100/150/200% UI scale, including a small display. Confirm content has
   a visible left and right inset in both Classic and Modern themes.
7. Move and resize, close/reopen, switch theme, minimize/expand, reload plugin,
   and restart: saved geometry remains intact. Existing restart switching
   semantics are expected to remain unchanged, not to be repaired here.

## Core API decision

Compared published Core UI source trees for 0.3.1, 0.4.0, and 0.4.1: all UI C#
blobs match. Keep exact 0.3.1 package/lock/vendor pins. Reuse Core app shell,
settings rows, glass cards, navigation, action buttons, status, and styling.
Core cards and secondary surfaces intentionally disable scrolling; normal ImGui
children provide consumer-owned overflow. The only local sizing policy is the
Profiles-specific three-state button fit, using active-font measurements.
