# Sentinel Profiles 0.2.1.10

Adds a compact icon inside each plugin Open and Settings button. The controls still wrap with long plugin names and narrow windows.

Saved profiles can be dragged into a preferred order, which persists across reloads. Move Up and Move Down support keyboard and controller users. Missing plugin rows remain at the bottom of the editor list.

Clicking the optional "Profiles: ..." server-info bar entry opens a scrollable profile picker. Selecting an entry applies that profile through the existing verified switching engine without opening the editor. The picker has an Open Profiles button for full configuration and disables apply choices while an application is running.

Profile rules, temporary switching and restart behavior, and window geometry remain unchanged. Automated tests and package validation run in CI; in-game visual, drag and controller checks remain pending.
