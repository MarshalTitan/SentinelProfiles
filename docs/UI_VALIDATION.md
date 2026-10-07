# Profiles UI repair validation

Version: 0.2.1.6. Scope: presentation only.

## Automated

- Existing core behavior, configuration round-trip, theme migration, and
  minimized-window persistence tests remain enabled.
- State-control fit tests use the 680px Modern minimum width budget, wider
  widths, 100/125/150/200% UI scale, and independent 85/100/120% font metrics.
- The three Profile State controls remain inline at the supported minimum width.
- Boundary checks require stacking only below the measured intrinsic fit width.
- CI builds against Dalamud API 15 and validates the installable ZIP.
- No changes to Models, Core, Services, Configuration.cs, or Plugin.cs.

## In-game checks (pending; not claimed by CI)

At 680 × 560 and 1080 × 720 logical pixels, in both themes and at 100/150/200%:

1. Empty, one, and many profiles, including 64-character names: scroll to every
   Profile Action; create, duplicate, rename, delete, apply, and reapply are
   reachable with their existing busy/protection rules.
2. Long plugin display/internal names, missing and protected rows: labels wrap;
   current state remains distinct from profile state. All three inline state buttons,
   row selection, search/filter, and bulk controls remain reachable.
3. Long apply feedback and problem lists: reach the plugin table by scrolling.
4. Appearance: reach and activate Switch to Classic at minimum width.
5. Keyboard/gamepad navigation: focus and activate profile entries, actions,
   search/filter, selection, state controls, and modal buttons; scrolling follows
   focus. Resize all three create/rename/delete modals, scroll each at minimum
   height, and check that fields, action buttons, and Cancel remain reachable
   at 100/150/200% UI scale, including a small display. Confirm content has
   a visible left and right inset in both Classic and Modern themes.
6. Move and resize, close/reopen, switch theme, minimize/expand, reload plugin,
   and restart: saved geometry remains intact. Existing restart switching
   semantics are expected to remain unchanged, not to be repaired here.

## Core API decision

Compared published Core UI source trees for 0.3.1, 0.4.0, and 0.4.1: all UI C#
blobs match. Keep exact 0.3.1 package/lock/vendor pins. Reuse Core app shell,
settings rows, glass cards, navigation, action buttons, status, and styling.
Core cards and secondary surfaces intentionally disable scrolling; normal ImGui
children provide consumer-owned overflow. The only local sizing policy is the
Profiles-specific three-state button fit, using active-font measurements.
