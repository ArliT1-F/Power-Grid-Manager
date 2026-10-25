# Changelog

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
