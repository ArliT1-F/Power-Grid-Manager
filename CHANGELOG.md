# Changelog

## Unreleased — Phase 8: compatibility and optimization

- Hardened save/load after inspecting `Verse/Scribe_Deep.Look`, `PostLoadInitter` and
  `ScribeExtractor`: `Scribe_Deep.Look` does nothing in `PostLoadInit`, and nested objects are
  re-entered there only when `PostLoadInitter` registered them during `LoadingVars`.
  `PowerHistoryTracker` and all Phase 7 persisted objects now clamp/normalise at the end of
  `LoadingVars` as well (both passes are idempotent), so bounds, format versions and
  deduplication do not depend on that registration detail.
- Staggered first scans across multiple maps using the persisted `Map.uniqueID`; opening the
  tab forces an immediate scan so the stagger never shows a waiting state.
- Cached the Management tab's preview plan and row list against the snapshot, settings and a
  revision counter; added fast paths so an empty registry costs no per-building ID work.
  No per-frame full scan, no LINQ, no reflection, no background work.
- Extended compatibility classification with assembly *and* namespace detection for modded
  `CompPower` / `CompFlickable` types, plus a separate `PowerControlSupport` level. Modded
  flick components with zero or multiple public toggle gizmos are refused explicitly.
- Added `Tests/CompatibilityTests.cs` (source hygiene, no-DLC/metadata audit, 10,000-building
  synthetic colony, multi-map staggering, modded trader/battery/unknown component, full
  component save/load, older saves without management data, map removal, all-tab window
  regression) and `Tests/static_checks.py` (dependency-free XML/key/placeholder/source check
  with an optional Roslyn syntax parse via `@scalar/csharp-fmt`). CI now runs the static checker.
- Added `Docs/PHASE-8-COMPATIBILITY.md`: verification matrix, version status (1.5/1.6 API
  research, 1.7 configured but unverified), measured/configured/estimated contract, known
  limitations and an in-game validation checklist.
- The offline harness now emits GitHub Actions annotations for failures and a notice with the
  total check count. The `Offline logic tests` CI job (Ubuntu, .NET 8) compiles all production
  sources against the doubles and runs the full harness successfully (1,719 checks); the
  static checker runs in the same job. This is a doubles-based compile, not a build against the game assemblies.
- No official game binaries or profiler were available here: no compilation against the real
  RimWorld assemblies, frame-time profiling, Harmony patch check or in-game exception scan was
  performed.

## Unreleased — Phase 7: optional management (off by default)

- Added `ManagementEnabled` and `LoadSheddingEnabled`, both false by default and saved with the
  mod settings. Monitoring, history, diagnostics and forecasting are independent of them.
- Added `Integration/BuildingPowerControl.cs`: the only writer to a building's power state.
  It syncs the private desired state through the vanilla `Command_Toggle.toggleAction`, applies
  the public `CompFlickable.SwitchIsOn` setter, clears any flick designation, and verifies the
  result. No reflection, no Harmony, no simulation mutation. Pending player flicks,
  unsupported components, unconfirmed connections and ambiguous gizmos are refused.
- Added a bounded, map-scoped `PowerManagementRegistry` keyed by `Thing.GetUniqueLoadID()`:
  monitoring groups, per-building shed priorities (1–6), explicit sheddable/protected flags,
  exclusion from control and exclusion from building-level diagnostics. Loads tolerate missing,
  truncated, duplicate and future-version data; destroyed/replaced buildings lose no safety.
- Added `LoadSheddingEngine`: read-only decisions over the cached snapshot with usable charge,
  measured deficit, a low/recovery hysteresis band, a stability window, a cooldown, a per-pass
  cap, priority ordering and configurable staged/all/manual recovery. Only opted-in, currently
  drawing consumers are shed; generators, batteries and unknown components never are.
- Added `LoadSheddingState` (tracked switches, condition memory) and a bounded
  `PowerManagementEventLog`. Disabling automation restores everything the policy switched off;
  missing buildings are untracked and recorded.
- Added the **Management** tab: status pill, separate enable/automation switches, threshold and
  recovery presets, a read-only "buildings this policy would affect" preview, group management,
  a paged building table with on/off/group/priority/exclude/locate actions, and an event
  history. Building detail cards gain on/off, group and diagnostics-exclusion controls when
  management is enabled.
- Added 162 English keyed strings; every referenced key and placeholder count is checked.
- Extended `Tests/Doubles` with the verified flick/gizmo/load-ID contracts and added
  `Tests/ManagementTests.cs`. See `Docs/PHASE-7-MANAGEMENT.md` for API evidence and the
  in-game checklist. Not compiled against game assemblies or validated in game here.

## Unreleased — build compatibility

- Removed the compile-time dependency on `Verse.GenDate`: durations, retention and chart
  ranges now use the mod's own `PowerTime.TicksPerHour` / `PowerTime.TicksPerDay`
  (2,500 / 60,000 ticks), so the sources no longer fail to compile with `CS0103` against a
  game assembly that does not expose that type.
- History saving now checks `LoadSaveMode.Saving` only, instead of also referencing
  `LoadSaveMode.SavingReference` (`CS0117`). The five parallel value lists are written in
  the plain saving pass, which every supported game build performs.
- Offline harness: dropped the now-unused `GenDate` double and pointed the duration/range
  assertions at `PowerTime`.

## Unreleased — Phase 6: battery endurance forecasting

- Added a battery endurance estimate: time until the map's stored energy runs out, or until it
  is full, from the current balance and — when enough samples exist — the recorded average over
  a selectable range (hour to seven days).
- Added `Analytics/PowerForecast.cs` (windowed averages, average net balance, deficit share)
  and `Analytics/BatteryForecast.cs` (the arithmetic, result kinds and confidence bands). Both
  are pure functions over numbers: no game objects, no simulation calls, no side effects.
- The estimate subtracts battery self-discharge (a verified 5 W per battery that the trader
  balance does not include), accounts for charge efficiency when it can be read, excludes
  EMP-stunned batteries from usable energy, and saturates at 30 in-game days so a long
  projection is reported as "more than" rather than as a precise number.
- No discharge rate limit is applied: a public per-battery limit is not confirmed for the
  supported builds, so inventing one was rejected. The estimate is conservative as a result.
- Added the **Forecast** tab: range buttons, four metric tiles (stored energy, capacity,
  charge, net including losses), the headline estimate with its basis and confidence, a
  current-vs-historical comparison, per-network endurance, and a "what this estimate assumes"
  block.
- Added **Battery endurance** and **Forecast confidence** tiles to the overview, and an
  endurance line to the network detail pane.
- Added the `ForecastIncludeSelfDischarge` setting.
- Validation: 90 new offline checks, including a read-only guarantee that forecasting leaves
  the power simulation and the reported measurements untouched. Not validated in game.

## Unreleased — Phase 5: power diagnostics

- Added a diagnostics engine that turns the readings into advisories: power deficit, high load,
  battery discharge, network with no generation, isolated consumer, unconfirmed membership,
  underperforming generator, draw above the configured amount, switched off but still drawing,
  and unsupported component.
- Every advisory carries a severity, a description, the evidence behind it (generation,
  consumption, charge, how long it has been true, how many samples support it) and a suggested
  action. Nothing is acted on automatically.
- Added `Analytics/PowerDiagnosticTracker.cs`: session-only episode memory keyed by live
  network or building. A flickering condition is one episode, not a new alert; a condition that
  clears is reported as resolved for one in-game day; episodes are released when their object
  disappears or their resolution ages out.
- Added the **Diagnostics** tab: scrollable advisory cards with severity pill, title,
  description, evidence, action and status, plus locate / show-buildings / mute / dismiss
  buttons and a filter (everything, critical only, warning and above, resolved only).
- Added the **Active advisories** and **Critical advisories** tiles to the overview.
- Added optional, rate-limited on-screen messages for new warning and critical advisories
  (at most two per refresh, then nothing until the cooldown expires), suppressed entirely
  unless `Current.ProgramState == ProgramState.Playing`.
- Added settings: `DiagnosticsEnabled`, `DeficitAlertWatts`, `LowBatteryWarningFraction`,
  `PersistentDeficitTicks`, `GeneratorUnderOutputTicks`, `ShowInfoDiagnostics`,
  `ShowPowerMessages`, `AlertCooldownTicks` and a per-kind mute bitmask. All are clamped on
  load, and the mute mask is masked to the kinds this build defines.
- Advisory kinds are never renumbered: the enum values index the persisted mute mask.
- Validation: 119 new offline checks. Not validated in game.

## Unreleased — Phase 4: historical monitoring

- Added bounded, map-scoped aggregate history: generation, consumption, stored energy and
  capacity sampled at a configurable interval (60–15,000 ticks; default 2,500).
- Added retention (1–30 in-game days), a derived sample cap and a 20,000-row hard ceiling;
  defaults store about 243 samples per map.
- Added save/load for history through five parallel primitive lists plus a format version.
  Loading tolerates missing keys, truncated columns, duplicate/non-monotonic ticks and
  non-finite values, and reports how many rows were discarded.
- Added tick-rollback handling that trims impossible future samples instead of writing a
  backwards series, and cache reset on load so no stale snapshot is persisted.
- Added `MapRemoved` cleanup for map unload/reload.
- Added the History tab: hour / 6 h / day / 3 days / 7 days of in-game time, with charts for
  generation vs consumption, net balance (zero line always shown), battery charge and
  consumption relative to generation.
- Added a pure-logic chart builder with screen-independent columns, per-bucket averaging,
  explicit gaps for missing samples (never interpolated), column cap and fixed 0–100 %
  axes for percentage metrics.
- Added a vanilla-widget chart renderer with labelled axes, units, legend, time range and a
  missing-data indicator; geometry is cached and only rebuilt when inputs change.
- Added settings for enabling history recording.
- Samples are recorded at the snapshot's own measurement tick, and a repeat of an
  already-recorded tick is skipped, so the in-memory series is always strictly monotonic.
- Validation: 103 new offline checks. Not validated in game.

## Unreleased — Phase 3: dashboard

- Added a native **Power** main tab (`MainButtonDef` + `MainTabWindow`) with mod icon.
- Added the overview dashboard: measured generation/consumption/balance, load ratio,
  configured def-level generation, storage and charge, topology counts, deficit/high-load
  network counts, disconnected consumers, data notices, nominal draw, unsupported
  components, switched-off consumers, sampling cadence, and an explanation of what each
  aggregate includes.
- Added the network view: list with status, counts, battery energy and charge; detail pane
  with full statistics, charge bar, incomplete-data warning, largest consumers, and
  show-buildings / locate buttons.
- Added the building table: search, sort (draw, generation, name, category, status, plus
  direction), grouping (category, room, network), filters (role, network, category,
  flickable, switched-off, high consumption, data notices, unknown behaviour,
  colonist-only, no-room) and a stable sort with deterministic tie-breaks.
- Added a building detail card (measured vs nominal draw, trader state, category, network,
  room, position, estimated daily energy, explicit "history not recorded", support level)
  with locate / inspect / clear actions.
- Added conservative purpose classification from verified components, classes and four
  vanilla thing categories, with an uncategorized fallback.
- Added room association through `Thing.GetRoom()` with a separate "no room" group.
- Added compatibility levels (supported, modded components, pending, unsupported) derived
  from component types; unsupported components never contribute numbers.
- Added adaptive layout: tiles shrink on small windows, the toolbar collapses to a View
  menu below 760 px, the detail card becomes single-column below 700 px, labels ellipsize
  instead of bleeding into the next column.
- Added `MainButtonDef` wiring, `Textures/UI/Icons/PowerGridManager.png`, `About/ModIcon.png`
  and ~120 English keyed strings.
- Added localization verification: every referenced key must exist and format placeholders
  must match the argument count.
- Validation: 108 new offline checks, including a headless draw pass of every panel and tab
  at three sizes. No in-game validation.

## Unreleased — Phase 2 monitoring core

- Added read-only network discovery and map building/component snapshots, including
  non-colonist infrastructure and multiple power components per building.
- Separated nominal/reported demand from PowerOn-gated current consumption and generation.
- Added physical/EMP-available battery energy, configured capacity and charge observations
  in watt-days.
- Added disconnected/pending membership, custom component and unsupported/invalid-data
  flags; no inferred connections or simulation changes.
- Added a configurable periodic map cache independent of UI and transient across saves.
- Added source-linked logic tests and a CI workflow; documented measurement contracts and
  manual reconciliation scenarios.
- Validation here: C# syntax parsing, XML parsing and diff checks only. No compiler, game
  installation, executed C# tests or in-game validation available.

## Phase 1 setup

- Added mod metadata targeting RimWorld 1.5, 1.6, and 1.7.
- Added separate build configurations and version-scoped assembly output: net472/Mono for
  1.5 and 1.6; net10.0/Waylith for 1.7.
- Added persisted, bounded monitoring/alert settings and English settings labels.
- Added a technical research report, cross-version API notes, runtime risks, existing-mod
  overlap review, and manual test scenarios.
- Build and in-game compatibility remain unverified because no game installs or .NET
  toolchain are available in this environment.
