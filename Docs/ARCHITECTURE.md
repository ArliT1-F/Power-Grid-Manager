# Technical Architecture

Power Grid Manager is a single-assembly, zero-dependency C# RimWorld mod (`namespace PowerGridManager`) that observes RimWorld's native electrical simulation (`PowerNetManager`, `PowerNet`, `CompPowerTrader`, `CompPowerBattery`, `CompFlickable`) without replacing, patching, or altering it.

---

## 1. Design Principles

1. **Read-only monitoring by default** — Scanning, history sampling, diagnostics, and endurance forecasting never mutate any `PowerNet`, `CompPower`, or building state.
2. **No Harmony, no reflection, no external packages** — Integration relies strictly on RimWorld's public `MapComponent`, `MainTabWindow`, `Mod`, `ModSettings`, `IExposable`, and `ThingComp` extension points.
3. **Strict separation of collection and rendering** — `DoWindowContents` never scans the map. The UI reads an immutable `PowerGridSnapshot` published on a configurable tick cadence (`60–600` ticks, default `120`).
4. **Explicit epistemic labels** — Every figure in the code and UI is distinguished as **measured** (active `PowerOutput` gated by `PowerOn`, physical `StoredEnergy`), **configured** (`Props.PowerConsumption`, `Props.storedEnergyMax`), or **estimated** (daily energy, battery endurance forecast). Unknown or unsupported figures are reported as unavailable rather than guessed.
5. **Bounded memory and safe serialization** — Every persisted collection uses primitive parallel `LookMode.Value` lists with explicit format versions, hard caps, and idempotent load-time clamping in both `LoadingVars` and `PostLoadInit`. No `Thing`, `PowerNet`, `CompPower`, or Unity object reference is ever serialized.
6. **Opt-in, non-destructive control** — Both `ManagementEnabled` and `LoadSheddingEnabled` default to `false`. Every building defaults to protected (not sheddable). Switch writes go exclusively through `BuildingPowerControl.SetSwitch`, which synchronises vanilla `CompFlickable`'s public toggle gizmo, `SwitchIsOn` setter, and `FlickUtility.UpdateFlickDesignation`.

---

## 2. Repository Layout

```text
PowerGridManager/
├── About/
│   ├── About.xml                       # Mod metadata (1.5, 1.6, 1.7; no DLC/mod dependencies)
│   ├── ModIcon.png                     # Mod list icon (64x64)
│   └── Preview.png                     # Mod manager & Steam Workshop banner (640x360)
├── 1.5/Assemblies/                     # RimWorld 1.5 build output (net472 / Mono, git-ignored DLL)
├── 1.6/Assemblies/                     # RimWorld 1.6 build output (net472 / Mono, git-ignored DLL)
├── 1.7/Assemblies/                     # RimWorld 1.7 build output (net10.0 / Waylith, git-ignored DLL)
├── LoadFolders.xml                     # Strict per-version assembly folder routing
├── Defs/
│   └── MainButtonDefs/
│       └── PowerGridManager.xml        # Native "Power" main tab button definition
├── Languages/
│   └── English/Keyed/
│       └── PowerGridManager.xml        # 568 English keyed translation entries (PGM_*)
├── Textures/
│   └── UI/Icons/
│       └── PowerGridManager.png        # Main tab bar icon
├── Source/PowerGridManager/
│   ├── PowerGridManager.csproj         # Multi-version SDK project (net472 for 1.5/1.6, net10.0 for 1.7)
│   ├── Core/
│   │   ├── PowerGridManagerMod.cs      # Mod entry point & scrollable ModSettings UI
│   │   ├── PowerGridManagerSettings.cs # Global persisted preferences & load normalization
│   │   └── PowerGridManagerComponent.cs# MapComponent: refresh gate, stagger, history, advisories, control
│   ├── Power/
│   │   ├── PowerGridScanner.cs         # Linear read-only scanner over PowerNetManager & ListerBuildings
│   │   ├── PowerGridSnapshot.cs        # Immutable map, network, building & component snapshot models
│   │   └── BuildingClassifier.cs       # Conservative category classifier from verified comps/classes/defs
│   ├── Analytics/
│   │   ├── PowerHistorySample.cs       # 20-byte aggregate sample struct
│   │   ├── PowerHistoryTracker.cs      # Bounded ring/series, retention pruning & Scribe persistence
│   │   ├── PowerChartBuilder.cs        # Screen-independent bucket averaging & explicit gap builder
│   │   ├── PowerDiagnostic.cs          # Advisory model, 10 kinds, severities
│   │   ├── PowerDiagnosticTracker.cs   # Session-only episode memory, resolution window & dismissals
│   │   ├── PowerDiagnostics.cs         # Read-only diagnostic evaluator over snapshot + history
│   │   ├── PowerDiagnosticNotifier.cs  # Rate-limited on-screen Messages integration
│   │   ├── PowerForecast.cs            # Windowed historical net-balance & deficit-share math
│   │   ├── BatteryForecast.cs          # Pure endurance arithmetic, self-discharge, EMP & confidence
│   │   ├── LoadSheddingState.cs        # Persisted hysteresis state & tracked auto-shed Thing IDs
│   │   ├── LoadSheddingEngine.cs       # Read-only plan evaluation, batch execution & restoration
│   │   ├── PowerManagementEventLog.cs  # Bounded, persisted management audit log
│   │   └── PowerManagementController.cs# Orchestrates registry, shedding, manual switches & UI preview cache
│   ├── Integration/
│   │   ├── CompatibilityRegistry.cs    # Support classification (vanilla vs modded vs pending vs unsupported)
│   │   ├── BuildingPowerControl.cs     # Single switch writer via public CompFlickable gizmo contract
│   │   └── PowerManagementRegistry.cs  # Bounded per-building preferences & monitoring groups (GetUniqueLoadID)
│   ├── UI/
│   │   ├── MainTabWindow_PowerGridManager.cs # 7-tab MainTabWindow host
│   │   ├── PowerUiState.cs             # Transient window state (filters, sort, selection, scroll)
│   │   ├── PowerUiDraw.cs              # Shared IMGUI primitives, status pills, metric tiles, bars
│   │   ├── PowerOverviewPanel.cs       # 24-tile global dashboard & measurement contract notes
│   │   ├── PowerNetworkPanel.cs        # Per-network list, status evaluation & detail view
│   │   ├── BuildingListModel.cs        # Revision-cached filter, search, stable sort & grouping model
│   │   ├── BuildingPowerPanel.cs       # Building table, View/Filter menus & building detail card
│   │   ├── PowerHistoryPanel.cs        # Range selector & 4-chart historical view
│   │   ├── PowerChartWidget.cs         # Native Widgets column/cap/zero-line chart renderer
│   │   ├── PowerDiagnosticsPanel.cs    # Filterable advisory cards with mute/dismiss/locate actions
│   │   ├── PowerForecastPanel.cs       # Battery endurance headline, current-vs-history & network breakdown
│   │   └── PowerManagementPanel.cs     # Policy switches, hysteresis presets, preview, groups, table & log
│   └── Utilities/
│       ├── PowerFormatting.cs          # W / kW, Wd, %, position & in-game duration formatting
│       ├── PowerGridUtilities.cs       # Network status rules, localization key mappers & label helpers
│       └── PowerTime.cs                # RimWorld tick constants (2,500 ticks/hour; 60,000 ticks/day)
├── Tests/                              # Dependency-free static checker + C# test harness (1,700+ checks)
├── Docs/                               # Architecture, compatibility, feature inventory, Workshop & phase docs
├── build.sh                            # Version-aware compiler wrapper (1.5 / 1.6 / 1.7)
├── package-release.sh                  # Clean mod staging & .zip release packager
├── CHANGELOG.md
├── LICENSE
└── README.md
```

---

## 3. Runtime Data Flow

```text
Map.MapComponentTick() ──► PowerGridManagerComponent.MapComponentTick()
                                │
                                ├─► RefreshIfStale(tick)  [every 60–600 ticks; staggered by Map.uniqueID]
                                │      ├─► PowerGridScanner.Scan(map, tick) ──► PowerGridSnapshot (immutable)
                                │      ├─► EvaluateAdvisories(tick)
                                │      │      ├─► PowerDiagnostics.Evaluate(...)
                                │      │      ├─► ComputeForecast(LastDay)
                                │      │      └─► PowerDiagnosticNotifier.Notify(...)
                                │      └─► PowerManagementController.Tick(snapshot, settings, tick)
                                │             ├─► PowerManagementRegistry.MarkSeen / Prune
                                │             └─► LoadSheddingEngine.Evaluate / Commit / Apply (only if armed)
                                │
                                └─► PowerHistoryTracker.MaybeSample(...)  [every 250–15,000 ticks]

MainTabWindow_PowerGridManager
   ├─ PostOpen()          ──► component.RefreshIfStale(tick, force: true)
   └─ DoWindowContents()  ──► reads component.Snapshot, History, Advisories, Forecast, Management (O(1) / cached)
```

### Multi-map Staggering

When a colony has multiple active maps, `PowerGridManagerComponent` spreads initial scans across a 60-tick (one in-game second) window using `(uint)map.uniqueID % 60`. Subsequent refreshes stay naturally offset. Opening the **Power** tab passes `force: true` so the active map never displays a waiting screen during its initial stagger slot.

---

## 4. Layer Details

### 4.1 Power Scanning & Measurement Contract (`Power/`)

`PowerGridScanner` executes on the main Unity/game thread in two linear passes:
1. Indexes live `PowerNet` lists from `map.powerNetManager.AllNetsListForReading` (`powerComps`, `batteryComps`, `transmitters`, `connectors`), requiring traders and batteries to appear in their simulation lists rather than merely in `connectors`.
2. Walks spawned buildings in both `listerBuildings.allBuildingsColonist` and `allBuildingsNonColonist` (deduplicated via a reusable `HashSet<Building>`), inspecting every `CompPower` in `building.AllComps` so multi-component buildings are represented accurately.

| Metric | Source | Contract |
|---|---|---|
| Current generation (W) | `CompPowerTrader.PowerOutput > 0` when `PowerOn == true` and network membership is confirmed | **Measured** |
| Current consumption (W) | `-CompPowerTrader.PowerOutput` (`< 0`) when `PowerOn == true` and network membership is confirmed | **Measured** |
| Net balance (W) | `GenerationWatts - ConsumptionWatts` (trader balance; excludes battery self-discharge and efficiency losses) | **Measured** |
| Nominal consumer draw (W) | `CompPowerTrader.Props.PowerConsumption` (`> 0`) | **Configured** |
| Configured generation (W) | `-CompPowerTrader.Props.PowerConsumption` (`< 0`), hidden if any generator lacks a finite configured value | **Configured** |
| Physical stored energy (Wd) | `CompPowerBattery.StoredEnergy` across confirmed batteries (includes EMP-stunned batteries) | **Measured** |
| Usable stored energy (Wd) | `CompPowerBattery.StoredEnergy` excluding `StunnedByEMP` batteries (matches `PowerNet.CurrentStoredEnergy()`) | **Measured** |
| Storage capacity (Wd) | `CompPowerBattery.Props.storedEnergyMax` | **Configured** |
| Daily building energy (Wd) | Current measured draw sustained over 1 in-game day (`1 W × 1 day = 1 Wd`) | **Estimated** |
| Battery endurance | Usable energy or remaining capacity divided by net balance adjusted for self-discharge (`5 W/battery`) and charge `Props.efficiency` | **Estimated** |

### 4.2 Historical Analytics (`Analytics/PowerHistory*`, `PowerChartBuilder`)

- Each `PowerHistorySample` stores five primitive values (`Tick`, `GenerationWatts`, `ConsumptionWatts`, `StoredWattDays`, `CapacityWattDays` — 20 bytes).
- Samples are recorded at `snapshot.Tick` (strictly monotonic; duplicate ticks are skipped and tick rollbacks trim future entries).
- Bounded by `RetentionDays` (`1–30`, default `7`), a derived cap `retention / interval + 2` (default ~`243` rows per map), and `HardSampleCap = 20,000`.
- `PowerChartBuilder` bins samples into screen-independent columns (up to `2,048` columns) and leaves empty buckets as `null` so missing samples render as explicit visual gaps rather than interpolated lines.

### 4.3 Diagnostics (`Analytics/PowerDiagnostic*`)

- Evaluates 10 `PowerDiagnosticKind` conditions across networks (`PersistentDeficit`, `HighLoad`, `BatteryDischarge`, `NoGeneration`) and buildings (`IsolatedConsumer`, `PendingMembership`, `GeneratorUnderOutput`, `DrawAboveConfigured`, `SwitchedOffButDrawing`, `UnsupportedComponent`).
- `PowerDiagnosticTracker` maintains session-only episode memory keyed by live `PowerNet` or `Building`, merging flickering conditions inside `AlertCooldownTicks` and retaining cleared advisories as **resolved** for 1 in-game day (`60,000` ticks).
- `PowerDiagnosticNotifier` emits at most `2` vanilla on-screen `Messages.Message` notifications per refresh pass and enforces `AlertCooldownTicks`, gated on `Current.ProgramState == ProgramState.Playing`.

### 4.4 Battery Endurance Forecasting (`Analytics/PowerForecast`, `BatteryForecast`)

- Uses RimWorld's exact unit identity `CompPower.WattsToWattDaysPerTick = 1 / 60,000` (`1 W × 1 day = 1 Wd`).
- Subtracts vanilla's `5 W` continuous self-discharge per charged battery (`ForecastIncludeSelfDischarge = true` by default) and scales charging surplus by `CompPowerBattery.Props.efficiency` (`0.5` in vanilla).
- Compares instantaneous balance against the recorded historical average over the selected window (`1 h` to `7 d`) to assign `ForecastConfidence` (`Unavailable`, `Low`, `Medium`, `High`), saturating projections past `30` days as `"more than 30 d"`.

### 4.5 Optional Power Management (`Integration/`, `Analytics/LoadShedding*`)

- **Single control path (`BuildingPowerControl.SetSwitch`)**: verifies the building is spawned, colonist-owned, has no unsupported `CompPower`, has no pending player flick (`!WantsFlick()`), and exposes **exactly one** `Command_Toggle` gizmo from `CompFlickable.CompGetGizmosExtra()`. Invokes `toggleAction()` to sync private `wantSwitchOn`, sets `SwitchIsOn` directly, calls `FlickUtility.UpdateFlickDesignation(building)` to clear any `Flick` designation, and verifies both states match.
- **Load-shedding policy (`LoadSheddingEngine`)**: evaluates usable battery charge against `LoadShedLowThreshold` (default `25%`) and `LoadShedRestoreThreshold` (default `55%`), requiring `LoadShedStableTicks` (default `30 min`) and `LoadShedCooldownTicks` (default `1 h`) between batches of up to `LoadShedMaxActionsPerPass` (default `4`) switches. Only opted-in (`Sheddable == true`), non-excluded consumers currently drawing `> 0.01 W` are shed, ordered by user priority (`6` down to `1`) and measured draw. Disabling automation immediately restores every building tracked in `LoadSheddingState`.

---

## 5. Persistence & Save Integrity

| Persisted Owner | Scope | Format | Bounds & Recovery |
|---|---|---|---|
| `PowerGridManagerSettings` | Global (`ModSettings`) | `Scribe_Values` primitives + enum | Clamped in `PostLoadInit` and `NormalizeManagement()`; mute bitmask masked to known `PowerDiagnosticKind` bits. |
| `PowerHistoryTracker` | Per-map (`MapComponent`) | 5 parallel `LookMode.Value` primitive lists + `formatVersion` | Truncated columns keep shared prefix; non-monotonic/NaN rows dropped; capped at `20,000` rows in both `LoadingVars` and `PostLoadInit`. |
| `PowerManagementRegistry` | Per-map (`MapComponent`) | Parallel `LookMode.Value` primitive lists keyed by `Thing.GetUniqueLoadID()` | Max `2,000` building records, `16` groups; missing buildings pruned after `30` in-game days; stale group names drop `Sheddable`. |
| `LoadSheddingState` | Per-map (`MapComponent`) | Primitives + `List<string>` of auto-shed `Thing` IDs | Max `256` tracked IDs; deduplicated and clamped in `LoadingVars` and `PostLoadInit`. |
| `PowerManagementEventLog` | Per-map (`MapComponent`) | 6 parallel `LookMode.Value` primitive lists | Ring-capped at `40` events, strings trimmed to `96` chars. |

Removing Power Grid Manager from an existing save leaves no dangling `Thing` references; RimWorld logs its standard one-time unknown-`MapComponent` notice on first load and continues normally.

---

## 6. Performance Bounds

- **Zero per-tick building scans**: `MapComponentTick` checks an integer tick delta and returns immediately unless the refresh or history interval has elapsed.
- **Zero per-frame allocations of map state**: `BuildingListModel`, `PowerChartWidget`, and `PowerManagementPanel` cache their derived rows, geometry, and read-only preview plans against snapshot object identity and state revision counters.
- **Zero LINQ / Reflection / Background threads**: enforced continuously by `Tests/static_checks.py` and `Tests/CompatibilityTests.SourceHygiene` across all 37 production C# files.
