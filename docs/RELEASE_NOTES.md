# Sentinel Profiles 0.2.1.11

Fixes the exception shown when dropping a saved profile above or below another one. Reordering still saves immediately, and Move Up / Move Down remain available for keyboard and controller use.

The optional server-info quick switch uses Sentinel Modern styling, with only the created profile list and Open Profiles button. It has no title bar or close X; clicking the Profiles bar entry again closes it.

Plugin Open and Settings buttons now measure their icon space from the active font, leaving a small clear gap before each label across UI scales. Missing plugin rows stay at the bottom. Profile rules, restart behavior, and saved window geometry are unchanged.

Automated behavior tests and package validation run in CI; visual and controller checks in game remain pending.
