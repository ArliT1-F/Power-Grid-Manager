# Power Grid Manager

A RimWorld mod focused on readable colony power monitoring. It observes RimWorld's native
power simulation; it does not replace or patch it.

> **Development status:** Phases 1–4 are implemented (project skeleton, monitoring core,
> dashboard, historical monitoring). The offline logic harness passes all 252 checks, but
> nothing has been compiled against, loaded into, or profiled in an actual RimWorld
> installation — none is available in this environment. Treat this as a development build,
> not a release.

## What it does today

Open the **Power** tab to get a dashboard over the map's electrical infrastructure:

- **Overview** — 20 tiles covering measured generation, measured consumption, net balance,
  consumption/generation ratio, configured (def-level) generation, stored energy, storage
  capacity, charge, network/component counts, deficit and high-load networks, disconnected
  consumers, data notices, nominal draw, unsupported components, switched-off consumers and
  the history cadence. A panel underneath states exactly what those numbers include.
- **Networks** — every simulated power network with its own generation, consumption,
  balance, counts, battery energy and charge, a status label (stable, high load, deficit,
  battery charging/discharging, no generation, unknown), its largest consumers, and buttons
  to filter the building table or jump to a real building on that network.
- **Buildings** — searchable, sortable, groupable table of every powered building with
  measured draw, output, network, status and flick state; filters for role, network,
  category, flickable, switched-off, high consumption, data notices and unknown behaviour;
  a detail card with nominal vs measured draw, trader state, room, position, an explicitly
  labelled daily-energy *estimate*, and locate/inspect buttons.
- **History** — in-game-time charts (hour, 6 hours, day, 3 days, 7 days) of generation vs
  consumption, net balance, battery charge and consumption relative to generation, sampled
  at a configurable interval, bounded by a retention window and stored in the save.

Everything the mod reports is labelled as **measured**, **configured** or **estimated**.
Figures that cannot be verified (installed capacity, per-building history, unsupported
custom power systems) are reported as unavailable with an explanation instead of a guess.

Not implemented yet: diagnostics advisories (Phase 5), battery/load forecasting (Phase 6),
and optional load management (Phase 7). The dashboard's "data notices" are observed
data-quality facts, not diagnostics.

## Requirements

- RimWorld 1.5, 1.6 or 1.7 (each needs its own binary; see below).
- No DLC required, no HugsLib, no Harmony patches, no external service or account.

## Build

The mod ships no DLLs in this repository: build against your own installation. The script
writes version-scoped binaries to `1.5/Assemblies`, `1.6/Assemblies` or `1.7/Assemblies`
and never downloads or bundles game assemblies.

```sh
# RimWorld 1.5 / 1.6 (bundled Mono, net472) — version is read from Version.txt
RIMWORLD_MANAGED_DIR="$HOME/.steam/steam/steamapps/common/RimWorld/RimWorldLinux_Data/Managed" ./build.sh

# Windows equivalent
# set RIMWORLD_MANAGED_DIR=C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed

# RimWorld 1.7 (.NET 10 / Waylith) — pass the installation root instead
RIMWORLD_DIR="/path/to/RimWorld-1.7" ./build.sh
```

- The version is detected from `Version.txt` next to the `Managed` directory; set
  `RIMWORLD_GAME_VERSION=1.5` (or `1.6`/`1.7`) to override it.
- 1.5/1.6 need `Assembly-CSharp.dll`, `UnityEngine.CoreModule.dll` and
  `UnityEngine.IMGUIModule.dll` from that install; the script verifies them first.
- The build uses `dotnet` or `msbuild`. On Linux the .NET SDK restores the .NET Framework
  4.7.2 reference assemblies from NuGet, so the first build needs network access. If no
  `dotnet`/`msbuild` is present, the script falls back to `mcs`/`csc` against the game's own
  assemblies (Mono: `sudo apt install mono-devel`).
- Output: `1.6/Assemblies/PowerGridManager.dll` (adjust the folder for your version). It is
  git-ignored — never commit or redistribute game DLLs.

## Try it in game

Link the repository into the game's `Mods` folder so a rebuild takes effect immediately:

```sh
ln -s "/path/to/Power-Grid-Manager" "$HOME/.steam/steam/steamapps/common/RimWorld/Mods/PowerGridManager"
```

Then enable **Power Grid Manager** in the mod list, restart, and open the **Power** tab.
If the tab is missing or nothing happens, check the log for load errors:

```sh
tail -n 80 "$HOME/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log"
```

## Test

```sh
dotnet run --project Tests/PowerGridManager.Tests.csproj --configuration Release   # .NET 8 SDK
```

The harness links the production sources against minimal game doubles and runs 252 checks
across monitoring, dashboard, history and localization logic. It proves logic only; see
[Testing](Docs/TESTING.md) for the in-game checklist that still has to be executed.

## Install (once built)

Copy this folder (or a release folder containing `About/`, `Defs/`, `Languages/`,
`Textures/` and the matching `1.X/Assemblies/`) into RimWorld's `Mods` directory, enable
**Power Grid Manager**, and restart.

## Documentation

- [Technical research, version matrix and implementation plan](Docs/TECHNICAL-RESEARCH.md)
- [Phase 2: monitoring core, measurement contracts](Docs/PHASE-2-MONITORING.md)
- [Phase 3: dashboard, UI API verification, layout](Docs/PHASE-3-DASHBOARD.md)
- [Phase 4: historical monitoring, retention, save/load](Docs/PHASE-4-HISTORY.md)
- [Testing: what the harness proves and the in-game checklist](Docs/TESTING.md)
- [Changelog](CHANGELOG.md)
