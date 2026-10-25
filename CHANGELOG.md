# Changelog

## Unreleased — Phase 2 monitoring core

- Added read-only network discovery and map building/component snapshots, including non-colonist infrastructure and multiple power components per building.
- Separated nominal/reported demand from PowerOn-gated current consumption and generation.
- Added physical/EMP-available battery energy, configured capacity and charge observations in watt-days.
- Added disconnected/pending membership, custom component and unsupported/invalid-data flags; no inferred connections or simulation changes.
- Added a configurable periodic map cache independent of UI and transient across saves.
- Added source-linked logic tests and a CI workflow; documented measurement contracts and manual reconciliation scenarios.
- Validation here: C# syntax parsing, XML parsing and diff checks only. No compiler, game installation, executed C# tests or in-game validation available. No dashboard, history, diagnostics, forecasting or control added.

## Phase 1 setup

- Added mod metadata targeting RimWorld 1.5, 1.6, and 1.7.
- Added separate build configurations and version-scoped assembly output: net472/Mono for 1.5 and 1.6; net10.0/Waylith for 1.7.
- Added persisted, bounded monitoring/alert settings and English settings labels.
- Added a technical research report, cross-version API notes, runtime risks, existing-mod overlap review, and manual test scenarios.
- Build and in-game compatibility remain unverified because no game installs or .NET toolchain are available in this environment.
- No dashboard, power scanner, history, diagnostic alerts, forecasting, or automatic control is implemented in this snapshot.
