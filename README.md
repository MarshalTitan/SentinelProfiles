# Sentinel Profiles

<p align="center">
  <img src="assets/icon.png" alt="Sentinel Profiles crest" width="256" height="256">
</p>

Sentinel Profiles is a standalone Dalamud plugin for creating named, manually
applied plugin configurations. It discovers the user's installed plugins at
runtime and gives every plugin one of three literal states:

- **Enable** — ensure the plugin is loaded when the profile is applied.
- **Leave Alone** — do not change the plugin's current state.
- **Disable** — ensure the plugin is unloaded when the profile is applied.

Profiles are switches, not continuous enforcement. After applying one, users
remain free to change plugins manually. Sentinel Profiles remembers the last
profile applied and reports whether its explicitly managed plugins still match.

## Features

- Discovers installed plugins dynamically from Dalamud's public
  `IDalamudPluginInterface.InstalledPlugins` API.
- Uses stable plugin `InternalName` values for persistence and reconnects rules
  automatically when a missing plugin is reinstalled.
- Preserves missing-plugin rules and shows them as **Missing / Not Installed**.
- Uses a clear segmented **Enable | Leave Alone | Disable** editor alongside a
  separately displayed live enabled/disabled state.
- Supports search, managed/state/missing filters, multi-select, and bulk edits.
- Creates Blank Profiles, independent duplicates, and full Capture Current
  Setup snapshots.
- Applies required disables first, then required enables, verifies every
  transition, and continues after individual failures.
- Reports enabled, disabled, already-correct, left-alone, missing, unsupported,
  and failed results without rolling back successful changes.
- Detects drift only from explicit Enable/Disable rules. Leave Alone never
  contributes to drift.
- Offers **Classic** and **Sentinel Modern** configuration themes. Modern uses
  the canonical `MarshalTitan.SentinelCore.UI` 0.3.1 application shell,
  navigation, glass cards, responsive settings rows, switches, status pills,
  styling, motion, and ambient treatment; there is no runtime dependency on
  another installed Sentinel plugin.
- Sentinel Modern uses one compact custom header, a real-icon primary rail,
  a scrollable text-only profile sidebar, a continuous unified surface, and
  right-side profile or appearance content. Navigation never stacks.
- Follows Dalamud's reduced-motion preference and uses Core's stronger `0.9`
  procedural ambient treatment without copying shared palette or paint code.
- Migrates existing schema-one users explicitly to Classic without changing
  saved profiles, the selected profile, the last-applied profile, or window
  placement. Sentinel Modern remains an opt-in presentation choice.
- Protects `SentinelProfiles` from being configured for self-unload. The safety
  policy is centralized so more protected InternalNames can be added later.
- Never edits another plugin's settings.

## Responsive configuration UI (0.2.1.3)

Both themes support a 680 × 560 logical-pixel minimum, scaled by Dalamud's UI
scale. Profile actions and editor content scroll vertically; long names and
feedback wrap. Narrow plugin tables combine details into one column and stack
the three state buttons when their measured labels do not fit side by side.
Existing window identity, saved position/size, minimize/restore state, profile
rules, and native temporary switching commands are preserved.

Core UI inspection: all UI C# source blobs in published tags `v0.4.0.0` and
`v0.4.1.0` match `v0.3.1.0`. This repair therefore reuses the pinned shared UI
package without pulling in unrelated navigation foundations. No local shell,
palette, card, or settings-row renderer was added.

Automated checks cover state-control fit at multiple scales and fonts plus the
existing behavior suite. See [UI validation](docs/UI_VALIDATION.md) for the
remaining in-game visual, saved-geometry, and input checks. The known temporary
override/reset-on-restart behavior is a separate issue, unchanged by this repair.

## Resizable profile dialogs (0.2.1.4)

Create, Rename, and Delete open as resizable dialogs. Each starts at a size
bounded by the current display, and long content remains reachable by scrolling.
Fields and action buttons use the dialog's current width. The main configuration
window and saved profile settings are unaffected.

## Profile dialog spacing (0.2.1.5)

The Create, Rename, and Delete dialogs use a small internal inset in both
themes. Their fields, descriptions, and buttons remain aligned while the
dialogs are resized or scrolled.

## Compact Profile State buttons (0.2.1.6)\n\nThe three Profile State choices sit beside each other at the supported minimum\nwindow width, keeping plugin rows short. Button widths follow each label's\nmeasured text and use compact spacing. At still narrower transient widths,\nthey stack to keep every choice reachable. Bulk state choices use the same\nfit rule.\n\n## Commands

- `/sprofiles` — open or toggle the main window
- `/sprofiles apply <profile name>` — apply a profile (quotes are optional)
- `/sprofiles reapply` — reapply the last-applied profile
- `/sprofiles list` — list saved profiles
- `/sprofiles help` — show command help

The graphical interface is the primary workflow; commands are optional.

## Creating and applying a profile

1. Run `/sprofiles` and choose **New Profile**.
2. Choose **Create Blank Profile** for a focused profile where everything starts
   as literal Leave Alone. This is the recommended normal mode.
3. Alternatively, choose **Capture Current Setup** to record every installed
   plugin. This creates an intentionally aggressive full snapshot.
4. Click Enable, Leave Alone, or Disable for the desired installed-plugin rows.
5. Click **Apply _Profile Name_**. Editing alone never changes live plugins.
6. Use **Reapply Profile** if the status later reports **Modified / Drifted**.

## Switching implementation and compatibility

Dalamud API 15 publicly exposes installed-plugin discovery and state change
events, but `IExposedPlugin` does not expose load or unload methods. Sentinel
Profiles therefore isolates switching behind `IPluginRuntime` and uses Dalamud's
native public temporary commands:

- `/xldisableplugintemp "InternalName"`
- `/xlenableplugintemp "InternalName"`

These commands apply an ephemeral override through Dalamud's own collection
manager, then load or unload the plugin. They intentionally do **not** rewrite
the user's persistent native collection definitions. Overrides reset when the
game restarts or when the plugin's state is changed in Dalamud's installer; the
stored Sentinel Profile remains available to apply again, and drift status
truthfully reflects the current live state.

Every transition is dispatched on the framework thread and must be confirmed by
Dalamud's `ActivePluginsChanged` event plus the public `IsLoaded` state within a
bounded timeout. Missing commands, API changes, rejected operations, timeouts,
and exceptions fail safely and are reported without crashing or stopping
unrelated transitions.

Plugins that declare `SupportsProfiles = false`, are banned, outdated,
decommissioned, or orphaned are visibly marked unsupported. A plugin assigned to
multiple native Dalamud collections can be rejected by Dalamud's native command;
API 15 does not expose collection membership for arbitrary installed plugins, so
Sentinel Profiles identifies that condition through a verified timeout and
directs the user to `/xllog` for Dalamud's exact native error.

The public manifest surface is marked for change in a future Dalamud API. If
Dalamud changes or removes it, the adapter is expected to fail closed until it is
updated; profile data and the rest of the UI remain isolated from that risk.

## Building

Requirements:

- .NET 10 SDK
- XIVLauncher/Dalamud API 15 development files

```powershell
dotnet restore SentinelProfiles.csproj --locked-mode
dotnet run --project SentinelProfiles.Core.Tests/SentinelProfiles.Core.Tests.csproj -c Release
dotnet build SentinelProfiles.csproj -c Release --no-restore
```

The installable package is written to:

```text
bin/Release/SentinelProfiles/latest.zip
```

## Architecture

- `Models/` — serializable profile, sparse tri-state entry, configuration, and
  installed-plugin snapshots.
- `Core/ProfileService.cs` — profile creation, capture, rename, duplicate,
  deletion, normalization, and case-insensitive name rules.
- `Core/ProfileApplicationEngine.cs` — testable planning, disable-before-enable
  execution, failure isolation, and final verification.
- `Core/DriftDetector.cs` — literal explicit-rule matching.
- `Services/InstalledPluginDiscoveryService.cs` — dynamic Dalamud inventory.
- `Services/NativeCommandPluginRuntime.cs` — isolated switching compatibility
  boundary with no reflection or internal Dalamud type references.
- `Services/ApplicationCoordinator.cs` — lifecycle, progress, results, and
  shutdown cancellation.
- `UI/MainWindow.cs` — profile list, editor, bulk controls, status, and reports.
- `.packages/SentinelCore/v0.3.1.0/` — exact release packages pinned from
  SentinelCore tag `v0.3.1.0` / commit `300703b360a58fb4b73bf7675d31fe8cab4614cd`.
  The vendored `MarshalTitan.SentinelCore.UI.0.3.1.nupkg` SHA-256 is
  `e1a9ce4e1ce36042c0fcd53f4c23874d918640be10eef16c21f1cd436c6ba747`.
- `SentinelProfiles.Core.Tests/` — dependency-free executable core test suite.

## Project boundaries

This repository owns Sentinel Profiles source, workflows, assets, release
packages, and its authoritative `repo.json`. The central `MarshalTitan/Sentinel`
catalog is reconciled by the configured `plugin-released` notification workflow.

## AI development disclosure

The initial implementation was created with substantial AI assistance. The
core logic and release package are automatically tested, but live in-game
validation is still required for the current Dalamud build and each user's
installed-plugin set.
