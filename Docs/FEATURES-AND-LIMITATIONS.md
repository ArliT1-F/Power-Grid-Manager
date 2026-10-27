# Implemented Features & Remaining Limitations

This document provides a complete checklist of implemented functionality mapped against Power Grid Manager's ten primary objectives, together with all deliberate scope boundaries and remaining verification items as of `0.9.0-rc.1`.

---

## 1. Implemented Features by Objective

### Objective 1 — Total Power Generation, Consumption & Storage (`Overview` Tab)
- **24-tile overview grid** displaying:
  - *Measured power:* Current generation (`W`/`kW`), Current consumption (`W`/`kW`), Net trader balance (signed `W`/`kW`), Consumption / generation load ratio (`%`).
  - *Configured & storage metrics:* Configured def-level generation (muted/qualified), Physical stored battery energy (`Wd`), Configured storage capacity (`Wd`), Battery charge (`%`).
  - *Topology counts:* Power networks, Connected consumers, Connected generators, Connected batteries.
  - *Health & data quality:* Networks in deficit, High-load networks, Disconnected consumers, Data notices.
  - *Diagnostics & endurance:* Active advisories (critical + warning), Critical advisories, Battery endurance summary (explicitly marked `est.`), Forecast confidence.
  - *Reference & cadence:* Nominal consumer draw (`W`/`kW`), Unsupported components, Switched-off consumers, History sampling interval.
- **Inline measurement contract panel** ("What these numbers include") explaining exact inclusions/exclusions and keeping power (`W`/`kW`) strictly distinct from energy (`Wd`).
- **Adaptive layout** that shrinks tile heights and font sizes cleanly on compact resolutions without clipping.

### Objective 2 — Individual Building Power Consumption (`Buildings` Tab)
- **Searchable, stably sorted table** covering all spawned colonist and non-colonist buildings with `CompPower` components (including multi-component buildings).
- **Columns:** Building label (with `[on]`/`[off]` switch indicator), conservative Category, Measured draw, Measured output, Network ID, and explicit text+colour Status (`Unsupported`, `Multiple networks`, `No network`, `Pending`, `Switched off`, `Powered down`, `High consumption`, `OK`).
- **Sorting:** Highest current draw, Highest generation, Building name, Category, Operational status, plus ascending/descending toggle with deterministic tie-breaking.
- *(Note on sorting by estimated daily consumption: because daily energy is `CurrentConsumptionWatts × 1 day`, sorting by current draw produces the exact same ordering without a redundant duplicate sort key.)*
- **Selected-building detail card:** Measured draw vs Nominal (configured) draw, Generation, Trader state (`On`/`Off`/`Mixed`/`N/A`), Category, Network, Map coordinates `(x, z)`, Room label, Estimated daily energy (`Wd`, explicitly marked as an estimate), explicit `"Per-building history: not recorded"` disclosure, Battery stored/capacity (when applicable), Component compatibility badge, **Locate** (`CameraJumper.TryJumpAndSelect`), and **Inspect** (`Dialog_InfoCard`).

### Objective 3 — Grouping & Filtering by Type, Room, and Network (`Networks` & `Buildings` Tabs)
- **Dedicated `Networks` tab:** Lists every `PowerNet` with ID, net balance, status pill (`Stable`, `High load`, `Power deficit`, `Battery discharging`, `Battery charging`, `No generation`, `Unknown`), consumer/generator/battery counts, stored/capacity `Wd`, charge bar, endurance line, incomplete-data notice, top active consumers, **Show buildings on network**, and **Locate** (jumps to a confirmed member building rather than inventing wire geometry).
- **Grouping modes in `Buildings`:** None, Category (`Generation`, `Storage`, `Production`, `Temperature control`, `Lighting`, `Research`, `Medical`, `Security`, `Recreation`, `Uncategorized`), Room (`Thing.GetRoom()`, with an explicit `"No room assigned"` group), and Network.
- **Combinable filters:** Consumers, Generators, Batteries, Specific network, Specific category, Flickable only, Switched-off only, High consumption (`>= HighConsumptionWatts`), Data notices only, Unknown/unsupported behaviour only, Colonist buildings only, Unassigned-room buildings only, plus instant case-insensitive text search across label, `defName`, and category.

### Objective 4 & 8 — Power Diagnostics & Configurable Notifications (`Diagnostics` Tab)
- **10 evidence-based advisory kinds:**
  - *Network-level:* `PersistentDeficit`, `HighLoad`, `BatteryDischarge`, `NoGeneration`.
  - *Building-level:* `IsolatedConsumer`, `PendingMembership`, `GeneratorUnderOutput` (default 1-day window so nightly solar idle is ignored), `DrawAboveConfigured`, `SwitchedOffButDrawing`, `UnsupportedComponent`.
- **Auditable presentation:** Every card displays a severity pill (`Critical`, `Warning`, `Info`), affected network/building, numeric description, supporting evidence (duration + sample count where applicable), recommended action, and ongoing vs **resolved** status (retained for 1 in-game day after clearing).
- **Episode deduplication & throttling:** Session-scoped episode memory merges flickering faults inside `AlertCooldownTicks`; optional vanilla on-screen messages (`ShowPowerMessages`) are capped at 2 per refresh pass and rate-limited by `AlertCooldownTicks`.
- **User controls:** Severity/resolved filter bar, per-advisory **Dismiss** + **Restore hidden**, persistent per-kind **Mute** toggles (synced to Mod Settings), **Locate**, and **Show buildings**.

### Objective 5 — Historical Power Monitoring (`History` Tab)
- **Map-scoped, persistent time series:** Samples Generation, Consumption, Physical Stored Energy, and Storage Capacity at a configurable interval (`250–15,000` ticks UI slider, `60–15,000` internal clamp; default `2,500` ticks = 1 in-game hour) with configurable retention (`1–30` days, default `7` days ≈ `243` samples/map, hard-capped at `20,000` samples).
- **Four native charts** across 5 in-game ranges (`Last hour`, `Last 6 hours`, `Last day`, `Last 3 days`, `Last 7 days`):
  1. Total generation vs total consumption (dual series).
  2. Net trader power balance (signed, zero line always visible).
  3. Battery charge percentage (`0–100%`).
  4. Power demand relative to current generation (`%`, `100%` reference line).
- **Honest gap rendering:** Missing buckets (map unloaded, history paused, newly started colony) render as blank gaps with an explicit `"N of M columns have no sample"` notice — never interpolated or zero-filled.

### Objective 6 — Battery Endurance Forecasting (`Forecast` Tab)
- **Depletion & charging time estimates** accounting for usable energy (excluding EMP-stunned batteries), remaining capacity, vanilla `5 W`/battery continuous self-discharge (`ForecastIncludeSelfDischarge`), and battery charging `Props.efficiency`.
- **Historical comparison & confidence levels:** Compares current balance against recorded history over the chosen range and assigns `High`, `Medium`, `Low`, or `Unavailable` confidence with plain-language basis notes. Projections past 30 days saturate as `"more than 30 d"`.
- **Per-network endurance breakdown** and an explicit **"What this estimate assumes"** disclosure panel.

### Objective 7 & 9 — Shutdown Candidates & Optional Power Management (`Management` Tab)
- **Shutdown identification without risky guessing:** High-consumption filters, switched-off filters, room/category grouping, and per-network top-consumer lists surface heavy loads while keeping every building **Protected** by default.
- **Disabled by default:** Both `ManagementEnabled` and `LoadSheddingEnabled` default to `false` and are completely independent of monitoring.
- **Manual building controls:** Compatible colonist buildings with a single standard `CompFlickable` toggle gizmo can be switched On/Off from the `Management` tab or the `Buildings` detail card, assigned to up to `16` user-defined monitoring groups, given shed priorities (`1–6`), marked **Sheddable** or **Protected**, excluded from control, or excluded from building-level diagnostics.
- **Hysteresis load-shedding policy:** Configurable low-charge trigger (`5–60%`, default `25%`), restore threshold (`20–100%`, default `55%`), stability window, cooldown, per-pass action cap, optional deficit requirement, and `Staged` / `All` / `Manual` recovery.
- **Read-only preview & audit log:** Displays the exact opted-in buildings and measured draw a policy would affect *before* automation is enabled, and persists a bounded 40-entry per-map event log of all actions and refusals. Disabling automation immediately restores all auto-shed buildings.

### Objective 10 — Performance, Save Integrity & Mod Compatibility
- Periodic refresh (`60–600` ticks) with multi-map staggering (`Map.uniqueID % 60`); zero per-tick scans, zero per-frame snapshot rebuilds, zero LINQ, zero reflection, zero Harmony patches, zero DLC dependencies.
- Defensive parallel-list `Scribe` serialization validated against missing nodes, truncated lists, duplicate keys, `NaN`/`Infinity`, tick rollbacks, and pre-Phase-7 saves.

---

## 2. Deliberate Scope Boundaries & Remaining Limitations

### Deliberate Design Boundaries
1. **No per-building historical consumption recording** — Only map-wide aggregates are stored to keep tick time and save size tiny on 1,000+ building colonies; per-building daily energy is clearly marked as an estimate from current draw.
2. **No per-network historical charts** — Runtime `PowerNet` objects split, merge, and re-index whenever conduits break or switches toggle; fabricating persistent network IDs across saves would misattribute historical data.
3. **No fabricated "installed capacity" guarantee** — Def-level generator ratings are shown only as muted **Configured generation** and excluded from load-ratio and forecast math.
4. **No automatic criticality guessing** — Categories (`Medical`, `Temperature`, etc.) never auto-enroll or auto-protect buildings for load shedding; every building is protected unless the player explicitly opts it in.
5. **Standard `CompFlickable` control boundary** — Modded buildings without a single public `Command_Toggle` gizmo or with pending colonist flick designations are refused with an explicit reason rather than forced via private-field reflection.

## 1.5 Implemented Features Added During `0.9.0-rc.1` Development

These extend the baseline objectives above and are implemented, statically checked, and
committed on `arena/38cf92f4-power-grid-manager`:

- **Objective 7 — Emergency Override (load-shedding automation):** `AutomationStatus`
  plus `ActivateEmergencyOverride()` / `ClearEmergencyOverride()`; `Tick()` suspends
  automatic switching while the override is active; header pill + toggle button on the
  Management tab; runtime-only state that resets on save/load (no serialization change).
- **Objective 8 — Recommendation engine (`Optimization` tab):** Ranked, actionable
  recommendations (`PersistentDeficit`, `LowBatteryReserve`, `BatteryDischarge`,
  `HighDemand`, `NearCapacity`, `IdleConsumer`, `HighDraw`, `Uncontrollable`,
  `ImbalancedGeneration`). Never acts automatically; never recommends transferring power
  between unconnected networks; `NearCapacityThreshold` setting.
- **Objective 9 — Scenario planner & power budgeting (`Planning` tab):** Pure-arithmetic
  projector reusing `BatteryForecast.Evaluate()`; models added generation, battery,
  consumer and switched-off changes; endurance + reserve-margin vs `ReserveMarginWatts`;
  assumptions documented in-panel. Nothing is built or changed in the colony.
- **Objective 8 — Guided troubleshooting assistant:** Rule-based `PowerTroubleshooting
  Assistant` for whole-map / selected-network / selected-building scope; bounded
  diagnostic report with cause + evidence + recommended action; **Diagnose Power
  Problem** button on the Diagnostics tab; full localization keys.
- **Performance modes (`Lightweight` / `Balanced` / `Detailed`):** `ApplyPerformanceMode()`
  reapplies refresh interval, history sample interval, retention and diagnostic-depth
  presets (defaults drive real collector differences; `Balanced` default). Picker in Mod
  Settings; enum validation on load.
- **Power presets:** Named, persisted load-shedding policy configurations (thresholds,
  hysteresis, recovery mode, default priority, mark-sheddable, optional charge-based
  auto-activation). One active preset at a time; activating applies policy values, never
  switches a building; create / delete / duplicate / activate in the Management panel.
- **Dashboard heatmap (`Heatmap` tab):** Colour-coded building heatmap with modes for
  consumption, generation, battery charge, network stability, isolation, idle draw and
  critical protection; search; hover tooltips; Locate / Inspect. Dashboard view only — an
  on-map cell overlay is not implemented because it would require Harmony and RimWorld
  exposes no public map-drawing hook.
- **Integration API:** `Docs/INTEGRATION.md` documents the read-only query API, the
  opt-in management control API, preset API, stability guarantees and constraints.

Counts updated: **50 production C# files** (was 37 at baseline). Local static checks pass
**2,538 checks, 0 failures** (was 1,931 at baseline). CI check counts and the official
RimWorld build against 1.5 / 1.6 / 1.7 remain pending as noted below.

### Remaining Environment / Pre-Release Verification Limitations
1. **Official RimWorld assembly compilation pending:** This repository was developed and verified in an environment without RimWorld binaries. All 37 production C# files compile and pass **1,744 automated checks** in `.NET 8` GitHub Actions CI against minimal game doubles, and pass **1,931 static XML/key/placeholder/link/Roslyn syntax checks** locally, but `./build.sh` still needs to be run against official RimWorld `1.5`, `1.6`, and `1.7` installations.
2. **RimWorld 1.7 API verification pending:** `1.5` and `1.6` APIs were verified line-by-line against public decompilations; `1.7` (`net10.0` / Waylith) is configured in `PowerGridManager.csproj`, `LoadFolders.xml`, and `build.sh`, but remains untested until built against official 1.7 assemblies.
3. **In-game QA & live screenshots pending:** Frame-time profiling on a live colony, real RimWorld save/reload testing, UI scale visual inspection (`1x`–`2x`), and live in-game Workshop screenshots remain open items on the pre-release checklist in [`Docs/TESTING.md`](TESTING.md) before promoting `0.9.0-rc.1` to `1.0.0`.
