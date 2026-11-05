# Sentinel Profiles 0.2.1.12

Fixes the quick switch list and Open Profiles button not responding to clicks. The compact Sentinel-styled picker now runs through Dalamud's window system, giving it the same mouse and controller focus handling as the main editor. Clicking a profile applies it; Open Profiles opens the full editor, and clicking the server-info entry again closes the picker.

No profile state rules, missing plugin ordering, restart behavior, or saved editor window size/position were changed. CI tests and package validation verify the build; in-game click confirmation remains pending.
