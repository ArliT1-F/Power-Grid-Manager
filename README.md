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
# RimWorld 1.5 / 1.6: point at that game's *_Data/Managed directory
RIMWORLD_GAME_VERSION=1.6 RIMWORLD_MANAGED_DIR="/path/to/RimWorld/RimWorldWin64_Data/Managed" ./build.sh

# RimWorld 1.7: uses the installation root and .NET 10 / Waylith assemblies
RIMWORLD_GAME_VERSION=1.7 RIMWORLD_DIR="/path/to/RimWorld-1.7" ./build.sh
```

Requires `dotnet` or `msbuild` plus the target game's assemblies (and a .NET 10 SDK for the
1.7 build). Do not commit game DLLs.

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
