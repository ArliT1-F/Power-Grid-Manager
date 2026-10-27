# Power Grid Manager — Integration API

**Package ID:** `ArliT1.PowerGridManager`  
**Supported RimWorld versions:** 1.5, 1.6, 1.7  
**DLC required:** none — base game only  
**Harmony:** not used; Power Grid Manager applies no Harmony patches of its own and
provides none for other mods to rely on.

This document describes the public, read-first API surface that other mods may query
or, where explicitly marked, control. Everything here is additive to the existing
mod; no existing API is removed or changed between minor releases without a major
version bump.

---

## 1. Getting the mod instance

```csharp
using PowerGridManager;

// Global settings (persisted across saves).
PowerGridManagerSettings settings = PowerGridManagerMod.Settings;

// Per-map component (null on maps the mod is not active on, e.g. caravan maps).
PowerGridManagerComponent component = Find.Maps?
    .FirstOrDefault(m => m != null)?.GetComponent<PowerGridManagerComponent>();
```

The component owns the snapshot, the history/forecast engines, the recommendation
tracker and the management controller. All reads are safe from any thread at any
time — the snapshot is rebuilt on a refresh interval and never mutated per frame.

---

## 2. Read-only query API

All of the following are **read-only** and never change the simulation.

### 2.1 Live snapshot (`component.Snapshot`)

`PowerGridSnapshot` exposes:

| Member | Meaning |
|---|---|
| `Tick` | Tick the snapshot was built. |
| `Buildings` | `IReadOnlyList<BuildingPowerSnapshot>` — every power building on the map. |
| `Networks` | `IReadOnlyList<PowerNetworkSnapshot>` — every powered network. |
| `GenerationWatts` / `ConsumptionWatts` / `NetWatts` | Map-wide totals. |
| `StoredWattDays` | Total stored battery energy. |

`BuildingPowerSnapshot` (one per power building):

| Member | Meaning |
|---|---|
| `Building` | The vanilla `Building` (Thing) — may be despawned; check `Spawned` before use. |
| `IsConsumer / IsGenerator / IsBattery` | Role flags. |
| `ConsumptionWatts / GenerationWatts` | Measured draw / output. |
| `NominalConsumptionWatts` | Configured draw when complete, null if unknown. |
| `StoredWattDays / CapacityWattDays` | Battery energy and capacity. |
| `Connection` | `PowerConnectionState` (Connected / NoNetwork / …). |
| `NetworkId` | Network id the building belongs to, null when none. |
| `IsFlickable / SwitchIsOn` | Whether a standard toggle gizmo exists and its state. |
| `Category` | `PowerCategory` (Generation, Storage, TemperatureControl, …). |
| `HasAnyDraw` | `ConsumptionWatts > 0.01f`. |

`PowerNetworkSnapshot`:

| Member | Meaning |
|---|---|
| `Id` | Stable network id. |
| `ConsumerCount / GeneratorCount / BatteryCount` | Counts by role. |
| `GenerationWatts / ConsumptionWatts / NetWatts` | Network totals. |
| `StoredWattDays / CapacityWattDays / ChargeFraction` | Battery state. |

### 2.2 Recommendations (`component.Recommendations` / `component.RecommendationTracker`)

`PowerRecommendation` carries `Kind`, `Severity`, `Title`, `Description`,
`Building`, `NetworkId`, `Watts`, `WattDays`, `ExplanationKey`, `ActionKey` and an
`Apply()` callback that the UI invokes — recommendations never act automatically.

### 2.3 Forecasts & history

`component.Forecast` and `component.History` expose battery endurance projections,
charge curves and bounded retention history (see `PowerForecast`, `PowerHistoryTracker`,
`BatteryForecast.Evaluate()`).

### 2.4 Scenario planner (`PowerScenarioPlanner`)

`PowerScenarioPlanner.Project(snapshot, changes)` returns a projected result reusing
the same battery model as the live forecast — purely arithmetic, nothing is built or
changed. See `PowerScenario` for the change model.

---

## 3. Management control API (opt-in, off by default)

Management is **disabled by default** and must be enabled in Mod Settings before any
of the following can act. These are the only write paths the mod exposes.

### 3.1 Building control (`component.Management`)

`PowerManagementController.Registry` tracks opted-in buildings. `PowerManagementRecord`
exposes `Sheddable`, `DefaultPriority`, `Group` and related fields. Buildings are
added/removed through the Management tab; other mods should not write the registry
directly unless they intend to own a building's power policy.

`BuildingControl.SetSwitch(building, wantOn, tick)` is the sole switch writer. It
verifies the building is spawned, colonist-owned, has no unsupported component, no
pending flick, exactly one standard `Command_Toggle` gizmo, then applies the switch and
re-verifies. It logs via the management event log and never forces gizmos.

### 3.2 Presets (`PowerPresetManager`)

`PowerPresetManager` holds named load-shedding policy presets (thresholds, hysteresis,
recovery mode, default priority, optional charge-based auto-activation). Activating a
preset applies its policy values to the live settings; it never switches a building by
itself. One preset may be active at a time.

---

## 4. Stability guarantees & constraints

| Guarantee | Detail |
|---|---|
| No Harmony | Power Grid Manager applies no Harmony patches; depend on public C# only. |
| No background threads | All work runs on the main thread during refresh ticks. |
| No per-frame work | The snapshot rebuilds on a configurable interval (default 120 ticks). |
| Save compatibility | Persisted data uses versioned parallel primitive lists; orphans pruned after 30 days. |
| No automation by default | Load shedding, presets and emergency override are all opt-in and off by default. |
| Never acts automatically | Recommendations and troubleshooting advise only; the player applies every action. |

### 4.1 What other mods must not assume

- Do not reflect on private fields; the layout may change.
- Do not cast to internal types outside the assembly.
- Do not rely on a specific refresh interval; read `Snapshot.Tick` for freshness.
- Do not serialize `Thing`/`PowerNet` references; use stable ids (`thing.thingIDNumber`,
  network id) if you need to persist cross-save references.

---

## 5. Example: shed all high-draw consumers on a network

```csharp
var component = map.GetComponent<PowerGridManagerComponent>();
if (component == null || !component.Management.AutomationEnabled) return;

foreach (var building in component.Snapshot.Buildings)
{
    if (!building.IsConsumer || building.ConsumptionWatts < 1000f) continue;
    var record = component.Management.Registry.Find(building.Building);
    if (record == null || !record.Sheddable) continue;
    // Advise only: the player applies the switch through the Management tab.
}
```

---

## 6. Questions

Open an issue on the repository for API additions, deprecations, or compatibility
concerns. Breaking changes are avoided within a major version line.
