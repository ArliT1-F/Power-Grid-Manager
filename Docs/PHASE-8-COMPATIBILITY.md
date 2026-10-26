# Phase 8 — Compatibility and optimization

Updated 2026-10-26. This is an honest verification record. **No RimWorld installation, official
game assemblies, .NET SDK, or running game are available in this workspace.** Everything below
is either a source-level audit, an API cross-check against public decompilations, or an offline
check against the project's game doubles. It is not an in-game or frame-time validation.

## What Phase 8 changed

### Serialization correctness

- Inspected real `Verse/Scribe_Deep.Look`, `ScribeLoader.FinalizeLoading`,
  `PostLoadInitter.DoAllPostLoadInits` and `ScribeExtractor.SaveableFromNode`. `Scribe_Deep.Look`
  itself returns immediately during `PostLoadInit`; nested `IExposable` objects are re-entered
  by `PostLoadInitter` **only if they were registered during `LoadingVars`**.
- Made `PowerHistoryTracker` (Phase 4) and all new Phase 7 persisted objects clamp,
  deduplicate and normalise at the end of `LoadingVars` as well as in `PostLoadInit`. Both
  passes are idempotent. This removes the dependency on the post-load registration detail and
  also makes direct `ExposeData()` calls (as in the harness) safe. It is a robustness
  improvement, not a fix for an observed in-game failure.
- All Phase 7 persistence uses parallel primitive lists with `LookMode.Value`, explicit format
  versions, length/duplicate/NaN/enum validation, and hard caps. No `Thing`, `PowerNet`,
  `CompPower` or Unity reference is serialized.
- Missing nodes (saves written before Phase 7) produce fresh empty controllers; unknown future
  format versions are downgraded; stale group names have their sheddable flag cleared.
- `LoadSheddingState` tracks only the IDs of buildings the mod itself switched off, and
  `RestoreAll` reads the live flick state so a stale cached snapshot cannot turn a player's
  manually switched-off building back on.

### Update overhead

- Evaluation still happens once per snapshot refresh (clamped 60–600 ticks, default 120), never
  per tick and never from a draw pass. `MapComponentTick` only calls the cached refresh gate.
- **Multi-map staggering:** the first scan of each map is spread over one in-game second using
  the persisted `Map.uniqueID`. Periodic refreshes then remain naturally staggered. Opening the
  tab calls `RefreshIfStale(tick, force: true)` so a player never waits on the stagger window.
- **UI caching:** the Management tab's preview plan and building row list are cached against
  the snapshot reference, a hash of the relevant settings, the group filter and a controller
  revision counter. No O(n) map walk happens per frame; each frame draws at most 8 preview rows,
  6 groups, 14 buildings and 6 events from fixed-height rows in one scroll view.
- **Fast paths:** with an empty management registry, `MarkSeen` and candidate collection return
  immediately, so a large colony with management disabled pays no per-building ID work.
- All collections are bounded: 2,000 registry rows, 16 groups, 40 events, 256 tracked switches,
  20,000 history samples (Phase 4), 80 advisories (Phase 5).
- No LINQ, reflection, `Task.Run`, threads, `Log.Error` or per-frame allocation of the snapshot.
  `Tests/CompatibilityTests.SourceHygiene` enforces this by scanning stripped production source.

### Compatibility classification

- `CompatibilityRegistry.IsCustomComponentType` now uses both assembly and namespace identity to
  label modded `CompPower` / `CompFlickable` implementations. A modded component is never
  assumed to behave like vanilla.
- New `PowerControlSupport` (Supported / ModdedImplementation / Pending / Unsupported) and
  `BuildingPowerControl.Evaluate` separate *reading* compatibility from *control* compatibility.
  An unconfirmed connection defers control; an unrecognised component refuses it.
- Control is attempted only when exactly one standard public `Command_Toggle` gizmo exists.
  Modded flick components with zero or multiple toggles are refused with an auditable reason.
- Modded `CompPowerTrader` / `CompPowerBattery` subclasses that follow the standard contract are
  measured and labelled "modded components". Components implementing none of the recognised
  contracts are counted as unsupported and excluded from totals.

### Reproducible checks

- `Tests/CompatibilityTests.cs`: source hygiene, metadata/no-DLC audit, a 10,000-building
  synthetic colony with bounded-time assertions, multi-map staggering, modded power components,
  full component save/load, missing-node upgrades, map removal and all-tab window regressions at
  three resolutions.
- `Tests/static_checks.py`: dependency-free XML/key/placeholder/source-hygiene checker, added to
  CI. With `--syntax` it additionally parses every C# file with Roslyn through the optional
  WASM package `@scalar/csharp-fmt` (see `PGM_CSHARP_FMT`), giving a real C# parser check without
  a local .NET SDK. This is a **syntax** check, not semantic compilation.

## Verification matrix

| Area | Method | Result here | Still required |
|---|---|---|---|
| C# syntax | Roslyn parser via CSharpier WASM (`@scalar/csharp-fmt`) on all 50 source/test files | 0 parse failures | Full `dotnet build` against game assemblies |
| Semantic/type correctness | Manual review + member cross-check script; `Tests/static_checks.py` | No known mismatch | Compilation (blocked here: no SDK/game DLLs) |
| Logic tests | `Tests/*.cs` source added; harness requires .NET 8 | **Not executed in this workspace** | `dotnet run --project Tests/PowerGridManager.Tests.csproj -c Release` (CI runs it) |
| Localization | `static_checks.py` + `LocalizationTests` (in harness) | 568 keys defined, 0 missing, 0 placeholder mismatches | In-game text rendering |
| XML metadata | `static_checks.py` (About/Defs/Languages parse; icon exists) | pass | In-game def load |
| Large colony | 10,000-building offline scenario in `CompatibilityTests` | Designed; timing bound generous | Real map profiling with a profiler |
| Multi-map | `Map.uniqueID` staggering test | Designed in harness | In-game multi-map session |
| Modded power components | Custom trader/battery/unknown component tests | Designed in harness | Real modded saves |
| Save/load | Scribe doubles: component, registry, events, settings, missing/old nodes | Designed in harness | Real save/reload in each game version |
| No DLC | Source audit: zero DLC namespaces, zero `PackageReference`, no Harmony | pass | Launch without DLC installed |
| 1.5 API | Public 1.5.4409 decompilation cross-check of every new member | Consistent | Compile/load with official 1.5 DLLs |
| 1.6 API | Public 1.6 decompilation cross-check (commit `79ca2aa…`) | Consistent | Compile/load with official 1.6 DLLs |
| 1.7 API | Build configuration only (`net10.0`, Waylith layout) | **Unverified** | Compile/load with official 1.7 assemblies |
| Vanilla simulation parity | No writes to power simulation; control only changes flick switches | Source audit | Control-save A/B comparison in game |

The user reported that Phases 5–6 compiled and launched in-game in their own environment. That
is user-side validation of the earlier phases, not evidence available in this workspace, and it
does not cover the new Phase 7/8 code.

## Version support

`About/About.xml` advertises 1.5, 1.6 and 1.7, and `build.sh` builds separate binaries:
`net472` against each 1.5/1.6 `*_Data/Managed` directory, and `net10.0` against a 1.7 install
root. The new Phase 7 APIs (`CompFlickable`, `Command_Toggle`, `FlickUtility`,
`GetUniqueLoadID`, `Map.uniqueID`, `Scribe_*`) were confirmed in **1.5 and 1.6** public
references. **1.7's exact signatures and runtime behaviour have not been verified here**; the
separate build target is a configuration, not a compatibility guarantee. Treat 1.7 as
"configured, untested" until compiled and launched against that version.

## Measured / configured / estimated contract (unchanged)

- Generation, consumption and net balance are sums of **current reported `PowerOutput`** from
  switched-on traders with confirmed network membership.
- Nominal draw and configured generation come from definitions and are labelled as such.
- Battery stored energy is physical; usable energy excludes EMP-stunned batteries. Forecasts
  are explicitly estimates with confidence labels.
- Phase 7's projected savings are **measured current draw**, not an extrapolation. A building
  with zero measured draw is not automatically switched off; the preview still lists it.
- No installed-capacity figure is invented from nominal values.

## Known limitations

1. No official game binaries were available: no compilation, load, Harmony-patch check,
   texture-render check or in-game exception scan could be performed here. Missing texture,
   missing key and XML failures are covered only by static checks.
2. Real frame-time profiling is impossible without the game. The large-colony test bounds
   algorithmic work with a generous wall-clock limit against doubles; it is not a performance
   claim for a real colony.
3. Priority is a user-set number per building/group. Verified `PowerCategory` is a grouping
   helper, **not** an automatic criticality classifier. There is deliberately no automatic
   "critical building" detection, because a category cannot prove safety.
4. Automatic shedding uses the current measured deficit. Rapidly varying consumers, modded
   components that change `PowerOutput` outside the vanilla contract, and buildings with zero
   current draw are handled conservatively (not shed), which can leave a shortfall uncovered.
5. Shedding can only flick buildings with a standard `CompFlickable` toggle gizmo. Mods that
   implement their own switching UI without that gizmo are reported as unsupported.
6. Removing the mod while a policy has buildings switched off leaves them off: flick state is
   vanilla save data, not something the mod can clean up after removal.
7. `Thing.GetUniqueLoadID()` is stable within a save. A building that is destroyed and replaced
   gets a new ID and is therefore protected until the player configures it again.
8. Per-building historical consumption is still not recorded; only map-wide aggregates exist.
9. The event history is bounded and map-scoped. It is a management audit, not a power chart.
10. 1.7 uses a different runtime; its power and flick APIs were not verifiable here.

## In-game validation checklist

- [ ] Build each of 1.5, 1.6 and 1.7 with its own official assemblies; fix any compiler errors.
- [ ] Launch a clean no-DLC profile with no other mods. Check the log for exceptions, repeated
      errors, Harmony patch failures, missing textures and missing translation keys.
- [ ] Open every tab at several UI scales and resolutions; scroll the settings and Management
      panels; verify no clipping or repeated alerts.
- [ ] Load a 10,000-building / heavily modded colony; watch the refresh cadence with a profiler
      and confirm no per-tick or per-frame full scan and no unbounded memory growth.
- [ ] Run two maps at once; confirm staggered first scans and independent histories/preferences.
- [ ] Save, quit to menu, reload, restart the game; confirm settings, groups, priorities,
      exclusions, event history, tracked switches and phase-4 history.
- [ ] Load a save created before Phase 7 and confirm no exception and disabled management.
- [ ] Add modded consumers/generators/batteries and a mod that changes flicking; confirm
      explicit supported/modded/unsupported labels and no silent control of unknown components.
- [ ] A/B compare a control save with the mod enabled/disabled: vanilla power totals and battery
      behaviour are identical whenever management is off.
- [ ] Verify no-DLC operation and that no DLC-only type is referenced.
- [ ] Record the results in `Docs/TESTING.md` and update this matrix with measured values.
