# Phase 2: monitoring core

## Scope and evidence (2026-10-25)

Implemented a read-only map scanner, runtime snapshots, and a scheduled `MapComponent` cache. There is no dashboard yet (Phase 3), historical collection, diagnostics engine, forecasting, or management. No Harmony or new runtime dependency is used.

Before implementing this phase, re-inspected the public 1.6 reference at
[Dyyrlysh/RimworldDecompile](https://github.com/Dyyrlysh/RimworldDecompile),
commit `79ca2aa15c9e116bd43d448692b62636467f6c46`:

- `CompPower.PowerNet`, `CompPowerTrader.PowerOutput`, `PowerOn`, and `Props.PowerConsumption`;
- `CompPowerBattery.StoredEnergy`, `StunnedByEMP`, `Props.storedEnergyMax`, and `Props.efficiency`;
- `PowerNet.powerComps`, `batteryComps`, `transmitters`, `connectors`, `CurrentEnergyGainRate`, and `CurrentStoredEnergy`;
- `PowerNetManager.AllNetsListForReading`, `ListerBuildings.allBuildingsColonist` / `allBuildingsNonColonist`, `ThingWithComps.AllComps`, and `MapComponent.MapComponentTick`.

Important refinement to Phase 1: **reported negative output is not necessarily active draw**. `PowerNet.CurrentEnergyGainRate()` ignores traders with `PowerOn == false`. The scanner applies the same gate. Battery self-discharge is not part of that trader balance.

No installed game version could be determined: no game assemblies or executable are available. The repository's existing 1.5/1.6/1.7 build targets remain targets, not verified supported releases. This phase's API inspection is specifically of the public 1.6 reference, not a new 1.7 verification.

## Architecture and consumption

`PowerGridManagerComponent` is auto-discovered through the game's map-component mechanism. Its first simulation tick produces a snapshot; later refreshes use the existing saved refresh setting (default 120 ticks, clamped to 60–600). Closing a future window will not stop collection. No scan occurs during rendering. Before the first tick, `Snapshot` is null: a future UI must show a waiting state, not fabricated zero data.

Access on the game/main thread:

```csharp
PowerGridSnapshot snapshot = map.GetComponent<PowerGridManagerComponent>().Snapshot;
```

`PowerGridScanner` first indexes actual manager membership, then visits spawned buildings in both colonist and non-colonist collections. It never infers connections from distance. Traders/batteries must be present in their dedicated simulation lists; appearing only in a connector list is insufficient. Known vanilla transmitters are connection-only; other unrecognized `CompPower` implementations are unsupported. A building with several power components retains every observation in `BuildingPowerSnapshot.Components`, avoiding `GetComp`'s first-component-only limitation.

Snapshots expose read-only collection wrappers and externally read-only properties. They are replaced atomically after each successful scan. Old snapshots are not mutated. Building references support future inspection/selection but **must be checked for despawn/destruction when clicked**. No network or building references are serialized. Membership scratch collections are reused and cleared after every scan, including failed scans.

### Measurement contract

| Field | Meaning |
|---|---|
| Reported output (W) | Signed `PowerOutput`; positive generation, negative requested consumption. Retained even if off. Null means invalid/unavailable. |
| Nominal consumption (W) | `Props.PowerConsumption`, including the property's game-side research adjustments; **not actual consumption**. Negative nominal values conventionally denote generators. |
| Generation / consumption (W) | Positive/negative portions of finite output, **only when PowerOn**. Network/map totals include confirmed registered members only. Disconnected and pending rows are excluded. |
| Net balance (W) | Generation minus consumption. Excludes battery self-discharge, battery transfers, efficiency losses, and debug unlimited-power injection. Not a forecast or unmet-demand estimate. |
| Physical stored energy (Wd) | Sum of finite nonnegative battery stored energy, including EMP-stunned batteries. |
| Available stored energy (Wd) | Same sum excluding EMP-stunned batteries, matching vanilla `CurrentStoredEnergy` semantics; not a promise about custom discharge limits. |
| Capacity (Wd) | Sum of finite nonnegative configured battery capacities. Does not claim every battery is currently usable. |
| Charge fraction | Physical stored energy / configured capacity. Null if no capacity. |
| Consumer/generator/battery counts | Component counts per network. Multi-component buildings can contribute more than once. Roles use nominal sign, observed sign, and plant type, so zero-output plants remain generators. |
| Disconnected consumers | Unique buildings with a recognized consumer component that has neither network assignment nor manager membership. A momentary observation, not a persistent diagnostic. |
| Deficit networks | Networks whose active trader balance is below -0.01 W. Does not detect suppressed demand after the game powers consumers off. |

There is deliberately **no installed generation-capacity estimate**: a negative nominal consumption value is not reliable maximum generation for all plant implementations. No historical average, daily-energy extrapolation, warning count, operational-idle diagnosis, or battery endurance is fabricated by this layer.

### Unsupported and transitional cases

- `NoNetwork`: no assignment and no indexed membership. May be a temporary topology transition; no notifications are emitted.
- `MembershipPending`: assignment and membership disagree, or assignment refers to a network not yet indexed. Excluded from aggregate totals.
- `Unsupported`: unrecognized `CompPower` subtype; no invented electrical contribution. Entirely custom systems with **no CompPower** cannot be discovered by this implementation.
- `IsCustomImplementation`: type resides outside the game assembly. Compatible trader/battery subclasses still expose the public contract, but custom overrides/patches are not independently validated. This flag is informational, not a compatibility guarantee.
- Nonfinite trader data or invalid battery properties mark observations incomplete rather than poisoning aggregate sums. Unsupported components, missing registered members, and membership inconsistencies also mark incomplete data. Partial totals must be labeled as such by future UI.
- Debug unlimited-power mode is not reflected in the physical generation sum. The normal-game reconciliation procedure below must not use it.

Runtime network indices start at 1 in each snapshot. They can change after topology changes; do not retain selections across refreshes by index without revalidation and never persist them as historical identities.

## Cost and persistence

Expected scan cost is linear in building/component and network-membership counts. No every-tick full scan, LINQ, reflection, background access, external service, or simulation mutation. Full topology is rebuilt on the controlled refresh cadence, not patched. This is a provisional cadence inherited from Phase 1, **not a profiled performance claim**. Snapshots allocate once per refresh; large-colony profiling remains necessary. Maps initially refresh on their first tick; multi-map staggering remains an optimization opportunity.

The cache is transient and rebuilt after loading. No save format is added in this phase; existing mod settings continue using their existing serialization. Save/load behavior still requires an actual game test.

## Automated checks

```sh
# .NET 8 SDK, no game files or third-party test packages required:
dotnet run --project Tests/PowerGridManager.Tests.csproj --configuration Release
```

The executable links the **production scanner, snapshot, and map-component source**, with minimal game doubles. It covers empty maps, generation/consumption signs, off-state gating, flick state, batteries/EMP, deficit, independent nets, non-colonist buildings, disconnected/pending membership, destroyed/despawned infrastructure, invalid values, unsupported types, variable demand, snapshot stability, cache cadence/tick rollback, and a 10,000-building synthetic map. Doubles test logic only, not game binary compatibility, simulation accuracy, serialization or frame/tick performance. CI runs this harness on push/PR; no CI result is claimed here.

### Validation performed here

- Parsed all seven C# source/test files with the tree-sitter C# grammar: no syntax errors.
- Parsed mod/translation XML and both project files: valid XML.
- `git diff --check`: passed.
- `./build.sh`: correctly refused missing `RIMWORLD_MANAGED_DIR` (exit 2).
- **Not executed:** C# test harness, production compilation, game loading, save/load, or in-game profiling. No .NET/Mono compiler or RimWorld installation is present. Syntax parsing is not compilation.

## Mandatory game validation before Phase 2 is accepted as verified

Build using the exact installation as documented in README. Disable debug unlimited power and start a no-DLC test colony. For each scenario below, inspect the cached snapshot in a debugger/developer inspection tool; no player-facing dashboard is claimed yet.

1. Empty map: zero nets/buildings/totals; missing battery fraction is null.
2. One steady generator + one active consumer: compare signed outputs to rows; compare `NetWatts * CompPower.WattsToWattDaysPerTick` to `CurrentEnergyGainRate()` with float tolerance.
3. Multiple producers/consumers, then two independent nets: compare each network separately; sum to map totals without cross-net cancellation hiding the deficit count.
4. Charged battery: available energy agrees with `CurrentStoredEnergy()`, configured capacity with defs; EMP changes available but not physical stored energy.
5. Deficit/excess generation: verify trader balance, then watch game power shedding. Confirm off consumers retain reported demand but contribute zero active consumption.
6. Flick a consumer: confirm actual switch and `PowerOn` are distinct and refresh within the cadence.
7. Destroy generator/battery, cut/reconnect wires: no stale contribution after refresh; transient pending data is labeled, not counted as guaranteed disconnection.
8. Variable-output plant and variable-demand building: observe changed reported output, not a constant nominal estimate.
9. Load a compatible modded trader/battery and an unsupported CompPower: preserve valid contract readings, mark custom/unsupported appropriately, no fabricated output.
10. Large colony and several maps: measure refresh CPU time and allocations at 60/120/600 ticks; adjust defaults only with evidence.
11. Save/load while window closed/paused; resume: transient cache reconstructs on first tick, no exceptions or persistent runtime net IDs.
12. Check game logs and repeat with mod disabled: vanilla power simulation must be unchanged.
