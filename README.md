# Power Grid Manager

A RimWorld mod focused on readable colony power monitoring. It will observe RimWorld's native power simulation; it will not replace it.

> **Development status:** Phase 2 monitoring code is implemented, with validation pending. A read-only map-scoped scanner now caches network, building, trader and battery observations. There is no dashboard yet. Compilation and in-game validation remain blocked by the missing game installation/toolchain; this is not a release-ready mod.

## Target and validation status

- **Compatibility targets:** RimWorld 1.5, 1.6, and 1.7. The About metadata advertises all three.
- **Build layout:** 1.5 and 1.6 get separate .NET Framework/Mono builds; 1.7 gets a separate .NET 10 / Waylith build.
- **Validation:** Power API names were cross-checked against public 1.5 and 1.6 decompilation references. A current 1.7 mod build example was reviewed for its changed runtime and assembly layout. None of these replace compiling against official game installations.
- **In-game testing:** Not performed. This environment has no RimWorld installation/game assemblies, and no .NET or Mono compiler was available during initial setup.
- **DLC:** No DLC dependency is declared.

The version support is a compatibility target, not a claim of completed compile/load testing. See [the technical research and implementation plan](Docs/TECHNICAL-RESEARCH.md) for the version matrix, evidence, limitations, and tests still required.

## Build

Build each game's assembly separately against that exact installation. The script writes version-scoped binaries under `1.5/Assemblies`, `1.6/Assemblies`, or `1.7/Assemblies`, and never downloads or bundles game assemblies.

```sh
# RimWorld 1.5 / 1.6: point at that game's *_Data/Managed directory
RIMWORLD_GAME_VERSION=1.5 RIMWORLD_MANAGED_DIR="/path/to/RimWorld-1.5/RimWorldWin64_Data/Managed" ./build.sh
RIMWORLD_GAME_VERSION=1.6 RIMWORLD_MANAGED_DIR="/path/to/RimWorld-1.6/RimWorldWin64_Data/Managed" ./build.sh

# RimWorld 1.7: this uses its installation root and .NET 10/Waylith assemblies
RIMWORLD_GAME_VERSION=1.7 RIMWORLD_DIR="/path/to/RimWorld-1.7" ./build.sh
```

The script requires `dotnet` or `msbuild` plus the target game's assemblies. The 1.7 build requires a .NET 10 SDK. Do not commit game DLLs.

## Install (once built)

Copy the repository folder (or a release folder containing `About/`, `Languages/`, and the matching version folder such as `1.6/Assemblies/`) into RimWorld's `Mods` directory, enable **Power Grid Manager** in the mod list, and restart. A release will only be called loadable after testing it with the actual target game.

## Features in this development snapshot

- Mod metadata targeting an independent, no-DLC RimWorld 1.5, 1.6, and 1.7 install.
- Settings persisted through RimWorld's `ModSettings` API: refresh cadence, sample cadence, retention, and high-load threshold.
- A build target that references game-provided assemblies without vendoring them.
- Initial implementation plan and API/research report.
- Scheduled map-scoped monitoring with active-vs-reported power separation, real network membership, battery energy/capacity, and unsupported/pending-data flags.
- Source-linked monitoring test harness and CI workflow (not executed locally).

See [Phase 2 measurement contracts, architecture, limitations and validation](Docs/PHASE-2-MONITORING.md). Run logic tests with a .NET 8 SDK: `dotnet run --project Tests/PowerGridManager.Tests.csproj --configuration Release`. These tests use game doubles and do not verify compatibility with game binaries.

## Planned

See the phase checklist in `Docs/TECHNICAL-RESEARCH.md`. Features will be marked complete only after implementation and validation.
