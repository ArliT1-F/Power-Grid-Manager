# Technical research, implementation plan, and validation record

Updated 2026-10-26. This is a development record, not a claim of in-game validation.

## Environment findings

- The checkout initially contained only `README.md` and Git metadata.
- Searches under `/home/user` and a shallow filesystem search found no `Assembly-CSharp.dll` or `Verse.dll`; no RimWorld installation was available to inspect.
- `dotnet`, `msbuild`, `xbuild`, `csc`, and `mono` were unavailable. Compilation and loading therefore cannot be demonstrated in this workspace.
- Public source repositories were used as *references for API research only*: [Dyyrlysh/RimworldDecompile](https://github.com/Dyyrlysh/RimworldDecompile) identifies itself as a RimWorld 1.6 decompilation, and [kasirii/RimWorld1.5.4409Decompiled](https://github.com/kasirii/RimWorld1.5.4409Decompiled) identifies itself as 1.5.4409. Their source is not included in this project and is not a substitute for official installed assemblies.
- RimWorld 1.7's newer runtime/layout was cross-checked against the public [PrepareLanding multi-version build configuration](https://github.com/aemonke/PrepareLanding), and 1.5/1.6/1.7 version directories against [More Power Info](https://github.com/angelolocritani/Rimworld-MorePowerInfo). These are evidence of current mod build conventions, not official API validation.

## Verified against the public 1.6 reference (not locally installed binaries)

The following names and behaviors were inspected in that reference: `RimWorld/CompPower.cs`, `CompPowerTrader.cs`, `CompPowerPlant.cs`, `CompPowerBattery.cs`, `PowerNet.cs`, `PowerNetManager.cs`, `Verse/Map.cs`, `Verse/MapComponent.cs`, and `RimWorld/MainTabWindow.cs`.

- `Map.powerNetManager` is initialized by `Map` and exposes `AllNetsListForReading`. Network processing is scheduled by the map; `PowerNetManager` processes topology changes and ticks its own nets. Mod monitoring must not call or replace that simulation.
- `CompPower.PowerNet` resolves either the component's `transNet` or its `connectParent`'s network. A missing `PowerNet` is observable for a spawned power component, but needs careful interpretation (topology can be pending; not all powered components behave like vanilla).
- Each `PowerNet` has live `powerComps`, `batteryComps`, `transmitters`, and `connectors` lists. `powerComps` contains `CompPowerTrader` objects (consumers and conventional generators); batteries are separately represented. These live lists are the reliable starting point for network aggregates, rather than inferring a network from building proximity.
- `CompPowerTrader.PowerOutput` is signed: positive values produce power and negative values consume it. It returns zero when EMP-stunned, and the backing output is updated by the component's simulation. Its value is therefore the current reported output, not a building's configured nominal draw. `PowerOn` is an operational state, not identical to the player's flick switch state. `CompFlickable.SwitchIsOn` and `FlickUtility.WantsToBeOn` provide separate switch/desire signals.
- `CompPowerPlant` is a `CompPowerTrader` subclass. Its `UpdateDesiredPowerOutput()` checks breakdown, fuel, flick state, auto-power state, pollution-related availability, and `PowerOn`; some plants override `DesiredPowerOutput`. Treating the def's nominal output as current generation would be inaccurate.
- `PowerNet.CurrentEnergyGainRate()` sums energy-per-tick from connected traders whose `PowerOn` is true. The source uses `CompPower.WattsToWattDaysPerTick = 1.6666667E-05f`; divide the gain rate by this factor to display watts. The value is net (signed), not independently calculated total generation and total consumption, so a dashboard should sum positive and negative trader outputs separately when reporting those categories.
- `PowerNet.CurrentStoredEnergy()` sums battery `StoredEnergy` except EMP-stunned batteries. Battery storage properties include `StoredEnergy`, `StoredEnergyPct`, `Props.storedEnergyMax`, `Props.efficiency`, and `AmountCanAccept`; energy is watt-days in the game's inspect text. Vanilla batteries self-discharge at a fixed 5 W while charged and lose energy on breakdown. `AmountCanAccept` accounts for efficiency and unavailable/broken/stunned state.
- Vanilla `PowerNet.PowerNetTick()` balances signed generation/consumption against stored battery energy, handles power restart/shutdown behavior and battery charge/discharge. `CurrentEnergyGainRate()` is a current snapshot, not a guarantee of sustained supply, and the forecast must disclose its assumptions.
- `CompPowerTrader.PostExposeData()` persists `PowerOn`; `CompPowerBattery.PostExposeData()` persists stored energy. Component network linkage is reconstructed/managed with connection callbacks. Runtime `PowerNet` object identity is not an appropriate persistent historical network identifier.
- `MapComponent` offers map-scoped tick and `ExposeData()` hooks. This supports map-scoped sampling in principle; persisted history should contain primitive aggregate samples only and remain bounded.
- `MainTabWindow` is the native map-tab extension point, configured via a `MainButtonDef` `tabWindowClass`. It sizes itself against screen resolution. The window should consume cached snapshots, never perform a full scan in `DoWindowContents`.

## Cross-version compatibility research

### RimWorld 1.5

The 1.5.4409 reference was checked for the power subsystem types central to this mod. `CompPower.PowerNet`, `CompPowerTrader.PowerOutput`, `CompPowerBattery.StoredEnergy` / `StoredEnergyPct`, `PowerNet.powerComps` / `batteryComps`, `PowerNet.CurrentEnergyGainRate()` / `CurrentStoredEnergy()`, `PowerNetManager.AllNetsListForReading`, and `Map.powerNetManager` are present. Their signed-output and watt-day behavior align with the inspected 1.6 reference. This is sufficient to plan a shared source implementation, but still requires compilation against official 1.5 assemblies.

### RimWorld 1.6

The API notes above apply directly to the public 1.6 reference. The 1.6 assembly is compiled separately from 1.5 so each binary is linked to its own game assemblies.

### RimWorld 1.7

1.7 requires a **separate runtime build**, rather than reusing the 1.5/1.6 Mono assembly. Public current mod-build examples target `net10.0` and reference the installation-root assemblies `Assembly-CSharp.dll`, `UnityStubs.dll`, `Waylith.dll`, and `Waylith.UnityCompat.dll`. The project now has a separate 1.7 build path and version-scoped output. The exact power API signatures and runtime behavior have not been checked against 1.7 game assemblies in this workspace; they must be verified before this version can be called tested.

### Build matrix

| Game | Target runtime | Reference inputs | Output |
|---|---|---|---|
| 1.5 | `net472` / Mono | That 1.5 install's `*_Data/Managed` | `1.5/Assemblies/PowerGridManager.dll` |
| 1.6 | `net472` / Mono | That 1.6 install's `*_Data/Managed` | `1.6/Assemblies/PowerGridManager.dll` |
| 1.7 | `net10.0` / Waylith | That 1.7 install root | `1.7/Assemblies/PowerGridManager.dll` |

## Key uncertainties / compatibility risks

1. The player's installed game versions are unknown. No on-disk game metadata or assemblies were found. The mod metadata and build configuration now target **1.5, 1.6, and 1.7**, but none has been locally compiled or launched.
2. RimWorld 1.7 uses a different .NET/Waylith runtime and assembly layout. A separate `net10.0` binary target is configured based on current mod build references, but exact 1.7 API signatures remain unverified.
3. The 1.5/1.6 power API overlap is supported by two public decompilation references, not official installed DLLs. Compilation against the official assemblies for each version remains mandatory.
4. Third-party power systems may bypass vanilla `CompPowerTrader`, use custom `CompPower` derivatives, alter meaning/timing of `PowerOutput`, or manage their own network. Initial support should detect vanilla-compatible components and label unsupported behavior instead of guessing.
5. Network membership may change asynchronously through manager callbacks. Refresh snapshots on a controlled cadence and tolerate a component being despawned/destroyed between refreshes.
6. Aggregate current output is observable, but exact per-building historical consumption is not supplied by vanilla statistics. Historical per-building averages can only be estimates from periodic observations; mark these accordingly.

## Existing-mod overlap review

This was a repository/README discovery pass, not an exhaustive Steam Workshop survey. Candidate projects and their stated scopes:

- [More Power Info](https://github.com/angelolocritani/Rimworld-MorePowerInfo) states it displays total power usage and current autonomy, including a stored-energy-only autonomy figure. Avoid duplicating it as a feature claim; the distinctive target here is a unified per-network/per-building explorer, historical time series, and diagnostics.
- [LightsOut](https://github.com/NathanIkola/LightsOut) describes automatic management of lights and benches to conserve power. This highlights compatibility risk around optional control/automation; it must remain opt-in and never assume control of another mod's consumers.
- [PowerControl](https://github.com/Epicguru/PowerControl), [RT_PowerSwitch](https://github.com/Ratysz/RT_PowerSwitch), and [Turn It On and Off - RePowered](https://github.com/emipa606/TurnOnOffRePowered) concern control/switching or demand behavior. They are not reasons to abandon the project; this mod's initial priority remains observation and transparency.
- [PowerView](https://github.com/GonDragon/PowerView) is an older small power-related mod; its current scope/version support was not established in this pass.

These links establish overlapping categories, not a complete feature-by-feature comparison or compatibility guarantee. No hard dependencies or patches against those mods are planned.

## Technical implementation plan

1. **Phase 1 — Setup (current):** mod metadata, game-assembly-only build configuration, settings, report, and reproducible environment requirements. Do not claim a loadable build without compiling and launching against a real game.
2. **Phase 2 — Core:** map-scoped cached scanner over `AllNetsListForReading`; use trader's current signed `PowerOutput`, separately track positive production/negative demand, inspect batteries, and use component/network object refs only in transient snapshots. Classify disconnected/unknown conservatively.
3. **Phase 3 — UI:** native main tab, cached overview/network/building tables, search and stable sorting, map selection/inspection, and localized strings. Provide explicit empty/unsupported data states.
4. **Phase 4 — History:** map component samples primitive totals at a configurable interval, bounded by retention; serialize only data needed to chart and forecast. Treat changed topology as aggregate map history, not a falsely persistent network identity.
5. **Phases 5–6 — Diagnostics/forecast:** simple evidence-based deficit/high-load conditions and battery energy ÷ observed/current net-deficit estimate; indicate uncertainty, ignore zero/positive deficits, include battery constraints where verifiable, deduplicate warnings.
6. **Phase 7 — Optional control:** only after monitoring validation. Use supported flick APIs, disabled by default, whitelists and hysteresis, auditable actions; no assumptions about generic third-party consumers.
7. **Phase 8–9 — Validation/release:** compile against each claimed version; install without DLC; test save/load, UI, unsupported mods, topology changes, and scenarios in the project checklist; document anything not testable.

## Build and test workflow

1. Install separate copies of each target game and use the matching `*_Data/Managed` directory for 1.5/1.6 and the installation root for 1.7.
2. Install .NET SDK/MSBuild and the matching reference/runtime packs (including a .NET 10 SDK for 1.7).
3. Build each version separately using the commands in the README. Confirm the binary appears in that version's `1.X/Assemblies/` folder and inspect all compiler errors/warnings.
4. Install in a clean no-DLC profile for each game version. Check the game's log after launch and a new colony before loading any existing save.
5. Use the reproducible scenarios below, compare the UI with vanilla inspect strings and the power overlay, save/reload, change topology, and review logs. Repeat the complete matrix for all three versions.

### Scenario checklist (not run; requires game installation)

- [ ] Empty map: empty-state UI and zero totals.
- [ ] One generator + one consumer: signed output, net balance, network membership match game inspect.
- [ ] Multiple producers/consumers on one network: current totals separated from net balance.
- [ ] Two independent networks: no cross-network aggregation.
- [ ] Partial/full batteries: stored energy, max capacity, and percent.
- [ ] Persistent deficit and excess generation: diagnostics/forecast labels match signs and storage.
- [ ] Flicked-off consumer: distinguish switch state from current reported draw.
- [ ] Destroyed generator or battery: no stale reference/crash.
- [ ] Unconnected trader: report connection absence only if component is spawned and not awaiting a topology refresh; avoid asserting isolated semantics for unsupported components.
- [ ] Variable load and a modded trader: identify present reported draw as a sample, not exact long-term use.
- [ ] Large map: bounded refresh cost, no full scan on every tick or UI frame.
- [ ] History: interval, retention, missing samples, topology change, save/reload.
- [ ] UI: search, sort, filtering, scrolling, selection, resize, localization, large row count, UI scale.

## Phase checklist

- [x] Phase 1: initial research, project/build skeleton and settings established.
- [x] Add 1.5/1.6/1.7 metadata and separate build targets/runtime layouts.
- [ ] Phase 1: compile/load against official 1.5, 1.6, and 1.7 installations (blocked here by absent game installs/toolchain).
- [x] Phase 2: monitoring core implementation and source-linked test harness.
- [ ] Phase 2: compile against game assemblies and reconcile in-game statistics (environment-blocked).
- [x] Phase 2: execute the C# harness (252 checks, see the note below on how it was run here).
- [x] Phase 3: dashboard (overview, networks, buildings, classification, filtering, sorting, grouping, localization).
- [ ] Phase 3: in-game rendering/interaction validation (environment-blocked).
- [x] Phase 4: history (bounded sampling, retention, save/load, charts).
- [ ] Phase 4: in-game save/load and chart validation (environment-blocked).
- [x] Phase 5: diagnostics (source + offline checks; in-game validation still open).
- [x] Phase 6: battery forecasting (source + offline checks; in-game validation still open).
- [x] Phase 7: optional management (source + offline checks; in-game validation still open).
- [x] Phase 8: compatibility/performance (source audits + offline checks; real profiling and
      official-assembly compilation still open).
- [ ] Phase 9: release preparation (license, packaged release, final in-game sign-off).


## Phase 3-4 implementation update (2026-10-25)

The dashboard and history layers are implemented. See
[PHASE-3-DASHBOARD.md](PHASE-3-DASHBOARD.md), [PHASE-4-HISTORY.md](PHASE-4-HISTORY.md) and
[TESTING.md](TESTING.md).

Additional API evidence gathered for these phases, again from the public 1.6 reference
(`Dyyrlysh/RimworldDecompile`, commit `79ca2aa15c9e116bd43d448692b62636467f6c46`):

- `MainTabWindow` is the documented tab extension point: `MainButtonDef.tabWindowClass`
  is instantiated by `MainButtonDef.TabWindow` and receives `def`; `RequestedTabSize` is
  virtual and `Window.InitialSize` clamps it to the screen.
- `TabDrawer.DrawTabs<TTabRecord>(Rect, List<TTabRecord>, float maxTabWidth = 200f)` draws
  the strip 32 px above the rect it is given; `TabRecord.GetTip()` is virtual.
- `Map.FillComponents()` instantiates every non-`CustomMapComponent` subclass of
  `MapComponent` that has a `(Map)` constructor, which is why the monitoring component needs
  no patch or def. `Map.GetComponent<T>()` scans that list.
- Def `label`/`description` are translated through `DefInjected`, not `Keyed`; the
  `MainButtonDef` therefore carries display text while C#-generated strings use keyed keys.
- `Widgets.DrawBoxSolid`/`DrawLineHorizontal`/`Label`, `Text.Font`/`Anchor`/`WordWrap`,
  `Text.ClampTextWithEllipsis`, `Widgets.BeginScrollView`, `Widgets.ButtonInvisible`,
  `Widgets.ButtonText`, `Widgets.TextField`, `TooltipHandler.TipRegion`, `FloatMenu`,
  `FloatMenuOption`, `Dialog_InfoCard(Thing)` and `CameraJumper.TryJumpAndSelect` all exist
  with the signatures used by the dashboard.
- `Scribe_Values.Look`, `Scribe_Collections.Look(ref List<T>, …)` and
  `Scribe_Deep.Look(ref T, …)` support the bounded history format; `Map.ExposeComponents`
  serializes the `components` list with `LookMode.Deep`, so a map component can persist data
  and is dropped cleanly when absent.
- `Thing.GetRoom()` (`RegionAndRoomQuery`) returns a `Room` whose `Role` may be null, so
  room grouping must handle "no room" and "unnamed room" explicitly.
- `ThingDef.HasComp(Type)`, `BuildingProperties.isTrap` / `bed_defaultMedical` /
  `bed_healPerDay`, `ThingCategoryDef.parent` and the classes `Building_ResearchBench`,
  `Building_TurretGun`, `Building_Bed`, `CompTempControl` and `CompGlower` support the
  conservative category classifier.

### How the harness was executed without a game SDK

The environment has no `dotnet`, NuGet access or game assemblies, but Roslyn and a
self-contained .NET 10 runtime were obtainable from packages already reachable here
(`registry.npmjs.org`). The production sources plus the harness were compiled with Roslyn
and executed on that runtime, which is how the 252 checks were run. This validates C#/logic
only: the game assemblies were still replaced by doubles, so *no* claim is made about real
game API compatibility, rendering, save format or performance.

### Deliberate scope boundaries carried into Phases 3-4

- No installed-capacity figure is presented as production; only a def-level "configured
  generation", clearly qualified and excluded from all calculations.
- No per-building history exists, so per-building historical averages are reported as not
  recorded, and the daily-energy figure is labelled an estimate from current draw.
- Network identity stays snapshot-local; history is map-aggregate only, because network
  objects have no persistent identity.
- Diagnostics/forecasting/management remain Phase 5-7; "data notices" are data-quality
  facts, not advisories.

## Phase 2 implementation update (2026-10-25)

See [PHASE-2-MONITORING.md](PHASE-2-MONITORING.md) for the implemented scanner/cache, exact measurement contracts, source reinspection evidence, tests and limitations. Phase 2 code is implemented; acceptance remains gated on compilation and in-game reconciliation. In particular, current draw must gate signed trader output by `PowerOn`. Historical collection, UI, diagnostics, forecasts and management remain later phases.


## Phase 7-8 implementation update (2026-10-26)

The optional management layer and the compatibility work are implemented in source. See
[PHASE-7-MANAGEMENT.md](PHASE-7-MANAGEMENT.md) and
[PHASE-8-COMPATIBILITY.md](PHASE-8-COMPATIBILITY.md) for the full design, API evidence,
verification matrix and limitations. Key additions to this research record:

- **Flick control**: `CompFlickable` keeps a private desired-state flag. Setting only
  `SwitchIsOn` leaves the vanilla gizmo inconsistent and can make a colonist flick the
  building back. The supported path used here is the component's own public
  `Command_Toggle.toggleAction` (from `CompGetGizmosExtra`) to synchronise that flag, followed
  by the public `SwitchIsOn` setter and `FlickUtility.UpdateFlickDesignation` to clear the
  designation. Verified in both the 1.5.4409 and 1.6 public decompilations.
- **Stable identity**: `Thing.GetUniqueLoadID()` returns `"Thing_" + defName +
  thingIDNumber`, and the game itself persists it as the `id` node. It is the key used for
  management preferences and tracked switches. `Map.uniqueID` is persisted and used only to
  stagger first scans across maps.
- **Serialization**: `Scribe_Deep.Look` itself does nothing in `PostLoadInit`; the separate
  `PostLoadInitter` re-enters objects registered during `LoadingVars`. Loading-time clamping
  now also happens at the end of `LoadingVars` for the Phase 4 history tracker and every new
  persisted object, so validation does not depend on that registration detail.
- **Performance**: evaluation stays on the 60–600 tick refresh cadence; the Management tab
  caches its plan and rows rather than walking the snapshot each frame; empty registries take
  a constant-time path. No LINQ, reflection, threads or Harmony are used anywhere in the
  production source. Synthetic 10,000-building and multi-map checks were added to the harness.
- **Version status**: the new APIs were cross-checked against 1.5 and 1.6 public references.
  The 1.7 build target remains configured but unverified: its runtime/assembly layout differs
  and no official 1.7 assemblies were available here.
- **Validation honesty**: no .NET SDK, game assemblies or profiler were available in the local
  development workspace. A Roslyn-based *syntax* parse, XML/key checks and source hygiene
  checks were run locally with zero failures. The `Offline logic tests` GitHub Actions job
  (Ubuntu, .NET 8) then compiled all production sources against the test doubles and ran the
  full C# harness successfully, so semantic/type correctness against the doubles and all
  offline logic checks are verified. Compilation against the official RimWorld assemblies,
  in-game launch, save/load, rendering and profiling remain open checklist items.
