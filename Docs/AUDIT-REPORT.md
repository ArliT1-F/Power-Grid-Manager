# Power Grid Manager — Audit Report

**Repository:** `ArliT1-F/Power-Grid-Manager`
**Branch:** `arena/38cf92f4-power-grid-manager` (from `main` @ `457afe9d`)
**Current version:** `0.9.0-rc.1` (About.xml)
**Supported game versions:** RimWorld 1.5, 1.6, 1.7 (About.xml `<supportedVersions>`)
**DLC required:** None. Core only.
**External dependencies:** None (no Harmony, no HugsLib, no reflection, no background threads).

## 1. Supported versions — confirmed from project files

- `About/About.xml` lists `<li>1.5</li> <li>1.6</li> <li>1.7</li>`.
- `Source/PowerGridManager/PowerGridManager.csproj` multi-targets:
  - `1.5` / `1.6` → `net472` (Mono), refs `Assembly-CSharp`, `UnityEngine.CoreModule/IMGUIModule/TextRenderingModule` from `RIMWORLD_MANAGED_DIR`.
  - `1.7` → `net10.0` (Waylith), refs from `RIMWORLD_DIR`.
- `LoadFolders.xml` routes each version to its isolated `1.X/Assemblies/` folder.
- `build.sh` builds one version-scoped DLL at a time.

**Do not assume a single version.** The mod targets all three; 1.5/1.6 APIs were verified against public decompilations, 1.7 is configured but untested against official assemblies.

## 2. Features already implemented

| Area | What exists |
|---|---|
| **Power discovery** | `PowerGridScanner` linearly indexes `map.powerNetManager.AllNetsListForReading` and all spawned buildings' `CompPower` components; confirms network membership via sim lists, flags pending/no-network. |
| **Building-level stats** | `BuildingPowerSnapshot` with measured draw/output, stored/capacity Wd, nominal/configured draw, flick state, category, network ID, connection state. |
| **UI / dashboard** | `MainTabWindow_PowerGridManager` with 7 tabs: Overview (24 tiles), Networks, Buildings (search/sort/filter/group), History (4 charts), Diagnostics, Forecast, Management. |
| **History** | `PowerHistoryTracker`: bounded map-wide aggregates (gen/consumption/stored/capacity), configurable interval & retention, hard cap 20,000 samples, save-persisted, honest gap rendering. |
| **Forecasting** | `BatteryForecast`: depletion/charge time, self-discharge (5 W/battery), charge efficiency, current + historical balance, confidence levels (High/Medium/Low/Unavailable), per-network breakdown. |
| **Diagnostics** | `PowerDiagnostics`: 10 advisory kinds (PersistentDeficit, HighLoad, BatteryDischarge, NoGeneration, IsolatedConsumer, PendingMembership, GeneratorUnderOutput, DrawAboveConfigured, SwitchedOffButDrawing, UnsupportedComponent) with severity, evidence, duration, mute/dismiss/locate. |
| **Load shedding** | `PowerManagementController` + `LoadSheddingEngine`: priorities 1–6, up to 16 groups, hysteresis (low/restore thresholds, stable window, cooldown), protected-by-default, read-only preview, audit log, restore-on-disable. |
| **Control boundary** | `BuildingPowerControl.SetSwitch` writes only via public `CompFlickable` toggle gizmo + `SwitchIsOn` + `FlickUtility.UpdateFlickDesignation`; refuses ambiguous/unsupported components. |
| **Compatibility classification** | `CompatibilityRegistry`: Supported / CustomImplementation / Pending / Unsupported; no reflection. |
| **Settings** | `PowerGridManagerSettings`: refresh/sample/retention, diagnostic thresholds, forecast, management thresholds, mute bitmask; clamped on load. |
| **Save/load** | Settings, history, management registry, event log, shedding state all `IExposable` with primitive parallel lists, format versions, clamping, no object references persisted. |
| **Build/CI** | `Tests/static_checks.py` (1,929 checks, 0 failures) enforces no LINQ/reflection/Harmony/background threads; C# harness (1,744 checks) runs in GitHub Actions with .NET 8 against game doubles. |

## 3. Features partially implemented

| Feature | State | Gap |
|---|---|---|
| **Network topology view (4.1)** | `Networks` tab lists each `PowerNet` with balance, status, counts, top consumers, Locate camera jump. | No map-based topology/graph view; network identity is snapshot-local index (documented limitation — `PowerNet` objects split/merge). |
| **Interactive network inspection (4.2)** | Select network → detail + largest consumers + Locate. | No combined "select network → inspect all its generators/batteries/consumers" drill-down beyond existing detail; no per-network filter chip on the map. |
| **Map heatmap (4.3–4.4)** | **Not implemented.** No map overlay, no hover panel, no consumption/generation/battery/stability/isolation/idle visualization on the map itself. | See §7. |
| **Load shedding (6.x)** | Priorities, groups, hysteresis, preview, protected, restore all implemented. | **Emergency Override (6.7) missing** — no prominent control to suspend automation with enabled/suspended/unavailable status indicator. |
| **Compatibility (11.x)** | `CompatibilityRegistry` classifies vanilla vs modded components. | No documented integration API for other modders; no specific optional mod integrations; integration claims unverified against specific mods. |
| **Performance modes (9)** | Refresh/sample/retention settings exist, but no named Lightweight/Balanced/Detailed modes with actual behavioral differences. | Not implemented. |

## 4. Features missing entirely

- **Recommendation engine with savings estimates (Feature Two):** no formal recommendations, deduplication, per-item dismiss, configurable severity thresholds, or estimated-savings calculations.
- **Scenario planner (Feature Four 7.3):** no hypothetical generation/battery/consumer change interface; nothing guarantees non-mutation of live state beyond the existing read-only preview for load shedding.
- **Power budgeting / reserve targets (Feature Five):** no configurable reserve targets (margin, charge, endurance, utilization) per network, no budget presentation, no expansion-planning evaluation.
- **Power presets (Feature Seven):** no create/edit/duplicate/activate/import/export presets, no automatic activation conditions, no precedence system.
- **Troubleshooting assistant (Feature Eleven):** no "Diagnose Power Problem" guided report for map/network/building.
- **Documented mod integration API (Feature Eight 11.3):** no public extension surface for other modders (stats, classification, diagnostics, control, forecasting, visualization).

## 5. Existing bugs / architectural weaknesses

- **1.7 unverified:** `1.7` target compiles in the project file but cannot be compiled or tested here (no official assemblies in this environment). Treat as experimental until built against real 1.7 DLLs.
- **Snapshot-local network identity:** `PowerNetworkSnapshot.Id` is a list index, not persistent; selection is revalidated each draw (already handled in `PowerNetworkPanel.FindNetwork`), but anything persisting a network ID across saves would be wrong. Documented as a deliberate boundary.
- **History is map-wide only:** per-building and per-network history deliberately omitted (documented). Network-level historical evidence in `PowerDiagnostics.EvaluateNetworks` is only used when the map has exactly one network.
- **Building-level "history" field:** the detail card shows `PGM_Value_NotRecorded` with a `PGM_Tip_HistoryPerBuilding` tooltip — an explicit disclosure, not a bug.
- **`PowerDiagnostics` uses static scratch state** (`Candidates`, `KindCounts`, `Exclusions`) guarded by try/finally; single-threaded main-thread use only, which matches the architecture.
- **`PowerUiState`** is transient window state, not persisted — filter/sort selection resets on reopen (acceptable, matches "runtime-only state" convention).

## 6. Potential performance bottlenecks

- `PowerGridScanner.Scan` is O(buildings × comps + net comps) per refresh. Refresh is gated to 60–600 ticks (default 120) and staggered across maps by `map.uniqueID % 60`. A 10,000-building map is covered by an existing test. This is the largest per-refresh cost and is already amortized.
- `BuildingListModel` rebuilds filter/sort/group on each draw but caches against snapshot + filter + sort + revision.
- `LoadSheddingEngine.CollectCandidates` walks all buildings each evaluation but short-circuits when `registry.Count == 0` (management off by default).
- History sampling is interval-driven, never per-tick, and bounded.
- UI never scans; it reads the cached immutable snapshot.

No obvious unbounded allocations, LINQ, reflection, or background-thread access (enforced by `static_checks.py`).

## 7. Compatibility risks

- `BuildingPowerControl` relies on public `CompFlickable.CompGetGizmosExtra()` + exactly one `Command_Toggle` with `toggleAction`/`isActive`. Modded flickables with multiple/no toggles or pending colonist flicks are refused cleanly — safe but may refuse control the player expects.
- Identity uses `Thing.GetUniqueLoadID()`; stable across save/load for the same thing, but orphaned records are pruned after 30 days.
- No Harmony patches means the mod cannot hook map drawing or alter `PowerNet` simulation — consistent with its "observe only" contract, but limits on-map visualization (see §7 recommendation).
- Optional integrations are not hard dependencies; unsupported components fail gracefully with a reason.

## 8. Recommended implementation order

1. **Emergency Override** (Feature Three 6.7) — small, high value, fully testable.
2. **Recommendation engine** (Feature Two) — testable diagnostics extension.
3. **Troubleshooting assistant** (Feature Eleven) — reuses diagnostics + navigation.
4. **Scenario planner + battery forecast refinement** (Feature Four 7.3) — pure arithmetic.
5. **Power budgeting / reserve targets** (Feature Five) — builds on scenario planner.
6. **Performance modes** (Feature Nine) — wires into existing refresh/sample/retention settings.
7. **Power presets** (Feature Seven) — builds on priorities/groups/management.
8. **Dashboard-based "heatmap" view** (Feature One 4.3–4.4) — color-coded building/network list inside the existing Power tab (on-map cell overlay would require Harmony, which the mod explicitly avoids; documented).
9. **Documented integration API** (Feature Eight 11.3) + localization, docs, changelog.

## 9. Files likely to require modification

**Core:**
- `Source/PowerGridManager/Core/PowerGridManagerComponent.cs` — add recommendation/troubleshooting/performance-mode hooks + forecast exposure.
- `Source/PowerGridManager/Core/PowerGridManagerSettings.cs` — new thresholds/modes/preset/performance settings.
- `Source/PowerGridManager/Core/PowerGridManagerMod.cs` — settings UI sections.

**Analytics (new or extended):**
- `Source/PowerGridManager/Analytics/PowerRecommendation.cs` + `PowerRecommendationEngine.cs` (new)
- `Source/PowerGridManager/Analytics/PowerScenarioPlanner.cs` (new)
- `Source/PowerGridManager/Analytics/PowerTroubleshootingAssistant.cs` (new)
- `Source/PowerGridManager/Analytics/PowerPerformanceMode.cs` (new)
- `Source/PowerGridManager/Analytics/BatteryForecast.cs` — scenario entry points (extend)

**Integration:**
- `Source/PowerGridManager/Analytics/PowerManagementController.cs` — Emergency Override state.
- `Source/PowerGridManager/Integration/CompatibilityRegistry.cs` — documented extension points (extend).

**UI:**
- `Source/PowerGridManager/UI/MainTabWindow_PowerGridManager.cs` — new tabs/buttons.
- `Source/PowerGridManager/UI/PowerManagementPanel.cs` — Emergency Override button + status.
- `Source/PowerGridManager/UI/PowerDiagnosticsPanel.cs` — recommendation surface.
- New panel files for recommendations, scenario, budget, presets, assistant.

**Metadata / docs:**
- `Languages/English/Keyed/PowerGridManager.xml` — new `PGM_*` keys.
- `About/About.xml` — version/description bump as features land.
- `README.md`, `CHANGELOG.md`, `Docs/FEATURES-AND-LIMITATIONS.md`, `Docs/COMPATIBILITY.md`, `Docs/TESTING.md`.

## 10. Verification notes for this environment

- No .NET SDK / RimWorld assemblies are present, so C# cannot be compiled and the in-game QA checklist cannot be run here.
- `python3 Tests/static_checks.py` passes (1,929 checks, 0 failures) and enforces source hygiene; I will re-run it after every change.
- All new C# will follow existing file patterns exactly and avoid LINQ, reflection, Harmony, `Task.Run`/`new Thread`, and `Log.Error/ErrorOnce/WarningOnce` so the static checks keep passing.
- In-game validation (compilation against official 1.5/1.6/1.7 assemblies, save/reload, live UI) remains the responsibility of a RimWorld installation and is noted as a pre-release item consistent with the existing `0.9.0-rc.1` status.
