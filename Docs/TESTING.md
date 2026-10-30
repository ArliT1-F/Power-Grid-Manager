# Testing

Two separate levels exist, and they must not be confused:

1. **Offline logic harness** — compiles the production sources against minimal game doubles
   and checks measurement, filtering, history, diagnostics, forecasting, management and UI
   logic. It cannot prove compatibility with real game assemblies, rendering or performance.
   The `Offline logic tests` GitHub Actions job is configured to run on .NET 8 for each push
   and pull request; check the CI result for the revision being released.
2. **In-game validation** — requires installed RimWorld builds. The checklist below was not
   run for the `1.0.1` update in this development environment.

## 1. Offline harness

```sh
# Requires a .NET 8 SDK only; no game files, no NuGet packages, no test framework.
dotnet run --project Tests/PowerGridManager.Tests.csproj --configuration Release
```

Exit code is 0 only when every check passes. The project links
`../Source/PowerGridManager/**/*.cs` plus `Tests/Doubles/*.cs`, so a compile error in the
mod also fails the harness.

What it covers (the harness prints the exact total when run; the `1.0.1` local doubles-based
run passed 2,465 checks):

- **Phase 2 monitoring (32):** empty maps, signed trader output, `PowerOn` gating, flick
  state, batteries and EMP, deficit counting, independent networks, non-colonist buildings,
  disconnected vs pending membership, destroyed/despawned infrastructure, invalid values,
  unsupported components, variable demand, snapshot immutability, cache cadence, tick
  rollback, multi-component buildings, deduplicated lister entries, connector-only traders,
  and a 10,000-building map.
- **Phase 3 dashboard (333):** building roll-ups and component counts, configured
  generation, load ratio, room association, category classification, all network statuses,
  every filter, searching, sort direction and stability, grouping labels, number/unit
  formatting (watts vs kilowatts vs watt-days vs durations), support levels, and a headless
  UI pass that draws every panel and every tab at 320x180, 1010x640, 2560x1400 and with
  real data, clicks network rows, building rows and toolbar buttons, and asserts that no
  drawing path opens a window unexpectedly.
- **Heatmap performance (116):** a 10,000-building fixture counts snapshot reads and submitted
  row widgets to check that repeated draws reuse rankings and submit only viewport rows and
  a small edge buffer. It exercises all modes, filtering, management-policy changes, scroll
  boundaries, partial rows, resized lists, tooltips, selection, Inspect and Locate. These
  counts are not FPS measurements, and the doubles do not simulate real GUI clipping or input.
- **Phase 4 history (103):** sampling cadence and clamping, retention, caps, rollback,
  save/load round-trip, future and truncated formats, corrupt rows, missing nodes, map
  component integration, chart binnings/averages/gaps/axes, and `RefreshIfStale` reuse.
- **Phase 5 diagnostics (119):** settings defaults and save/load clamping including the mute
  mask; episode registration, duration, resolution, cooldown and reset; every network advisory
  kind with its severity and evidence; every building advisory kind; muting; the info toggle;
  the diagnostics master switch releasing episode memory; the advisory cap with 90 failing
  networks; dismissal and restore; resolved-advisory reporting and expiry; notification
  throttling (burst cap, no repeat announcements, messages off, Info and resolved skipped,
  nothing announced before the game is playing); and a headless pass over the diagnostics tab
  at three sizes that clicks the filter, mute, dismiss, restore, locate and show-buildings
  buttons.
- **Phase 6 forecast (90):** windowed averages and deficit shares including empty and inverted
  windows; depletion and charging arithmetic against hand-computed values; self-discharge
  scaling with battery count; EMP; saturation at the reporting horizon; NaN, negative and
  over-capacity inputs; every confidence band; `ForNetwork`, `ForMap` and the map component's
  `ComputeForecast` with real samples; headless passes over the forecast and network panels
  at three sizes; and the read-only guarantee that forecasting leaves the simulation and the
  reported measurements untouched.
- **Phase 7 management (new):** settings defaults and clamping; settings save/load and older
  saves without management nodes; control API success, no-op, pending-flick refusal, missing
  gizmo, ambiguous toggles, throwing modded gizmo, destroyed buildings and unsupported
  components; stable load IDs across reload; registry corruption, duplicates, bounds,
  long-missing pruning and group bulk actions; bounded event log save/load; disabled policy,
  no batteries, EMP, generators/batteries excluded, priority ordering, measured-savings
  selection, per-pass caps, stability, cooldown, mid-band hysteresis, staged/all/manual
  recovery, destroyed buildings, controller integration, component serialization and
  per-building diagnostics exclusion; headless Management tab draw at four sizes.
- **Phase 8 compatibility/performance (new):** production source hygiene (no LINQ,
  reflection, Harmony, threads, `Log.Error` or DLC namespaces), metadata/no-DLC audit,
  a 10,000-building synthetic colony with bounded-time assertions, multi-map staggering,
  modded trader/battery/unknown components, full component save/load, older saves, map
  removal, and an all-tab window regression at three resolutions.
- **Localization and metadata (41 in Phase 1–6, now 568 keys):** every `PGM_` key referenced
  in source exists in the English keyed file, format-placeholder counts are sufficient, the
  keyed/def/About XML parse, the def wires the right tab window type, the icon texture exists,
  and the window type derives from `MainTabWindow`.

The instrumented doubles can count submitted widget calls, but `Widgets` and `GUI` do not
perform real clipping or rendering; the harness cannot validate the resulting in-game view.

## 2. In-game validation checklist for the update

Build against each target installation (see README), install into a **no-DLC** profile,
and check the log after each step. Record the results next to each item.

### Loading

- [ ] Game starts with the mod enabled; no exceptions, no missing-texture warnings.
- [ ] The Power tab appears next to vanilla main tabs, with its icon, and opens.
- [ ] Settings window opens, all controls render (the panel now scrolls), values persist
      across a restart.
- [ ] Disabling the mod restores the vanilla tab bar and leaves the save loadable (expect
      RimWorld's standard unknown-map-component message for this component).

### Overview and networks

- [ ] Empty map: zero totals, "no power networks" state, no fabricated charge percentage.
- [ ] One generator + one consumer: generation, consumption and net balance match the
      vanilla power overlay; per-network numbers match the map totals.
- [ ] Two independent networks: no cross-network aggregation; deficit count matches.
- [ ] Battery network: stored/capacity/charge match the battery's vanilla inspect string,
      including EMP (charge drops in the dashboard but physical storage stays).
- [ ] Deficit network: labelled "Power deficit" (or "Battery discharging" while batteries
      cover it) with a negative net balance.
- [ ] High-load threshold: changing the setting in mod settings changes the labels after
      the next refresh without a restart.
- [ ] Location jumps: "Locate" in the network pane and building detail pan the camera and
      select the right building.

### Building table

- [ ] Search narrows rows as text is typed, without stutter.
- [ ] Every filter toggles the expected rows; the indicator appears on the Filters button.
- [ ] Sorting by each key, both directions, keeps ties stable between refreshes.
- [ ] Grouping by category, room and network produces correct headers, including
      "No room assigned".
- [ ] Flicking a building off in game flips its row to "Switched off", shows zero measured
      draw and keeps the nominal draw.
- [ ] A destroyed building disappears after the next refresh; a selected building that is
      destroyed clears the selection instead of throwing.
- [ ] "Inspect" opens the vanilla info card.

### History

- [ ] Samples appear at the configured interval (compare sample count with elapsed days).
- [ ] Charts show labelled axes, units, legend, and the visible time range.
- [ ] Switching ranges redraws correctly, including a range with no samples.
- [ ] Missing samples appear as gaps, never as interpolated values or zeros.
- [ ] Save, quit, reload: history survives and continues from where it left off.
- [ ] Changing network topology (cutting wires, adding a battery) does not corrupt history.
- [ ] Turning history off stops new samples; re-enabling resumes.
- [ ] Long session (7+ in-game days): sample count stays at the retention cap.

### Diagnostics

- [ ] A network in deficit produces an advisory whose generation/consumption figures match the
      vanilla power overlay, and whose severity escalates after the configured window.
- [ ] A deficit covered by batteries is reported as battery discharge, not as a bare deficit;
      it turns critical as the bank approaches the configured low-charge threshold.
- [ ] Cutting a generator at night does not raise an underperforming-generator advisory before
      the configured delay; a genuinely broken or unfuelled generator does.
- [ ] Fixing a condition shows it as *resolved*, and it stops being listed after a day.
- [ ] Muting a kind removes it and survives a restart; dismissing one advisory hides only that
      one, and "restore hidden" brings it back.
- [ ] Two new warnings at once produce at most two on-screen messages; a third appears on a
      later refresh, not immediately.

### Forecast

- [ ] With no history the endurance figure matches the current balance arithmetic and is
      labelled low confidence.
- [ ] After several in-game days of history the figure quotes the recorded average and the
      confidence rises.
- [ ] Depleting batteries: the estimate is in the right order of magnitude compared with
      actually running the colony down (it will differ; demand changes).
- [ ] EMP-stunned batteries are excluded from usable energy and the estimate shortens.
- [ ] Nothing the forecast does changes the vanilla power simulation (compare a control save
      without the mod).

### Management (Phase 7)

- [ ] Default settings: the Management tab says OFF; no building is ever switched.
- [ ] Enable controls only: on/off buttons work and the vanilla gizmo checkbox, switch and
      flick designation stay in sync; colonists do not re-flick the building.
- [ ] Enable automation with no sheddable buildings: preview is empty and nothing switches.
- [ ] Mark two non-critical consumers sheddable with different priorities: the preview lists
      both, the higher shed-priority number goes first and the event log records it.
- [ ] Drain batteries below the threshold with a deficit: stability and cooldown are visible,
      then a bounded batch is applied; charge between thresholds does not restore.
- [ ] Recharge above the recovery threshold: staged / all / manual recovery behave as set.
- [ ] Disable automation mid-shedding: every auto-shed building is restored.
- [ ] Destroy, uninstall and replace a configured building: no stale control, no exception;
      the replacement stays protected until configured again.
- [ ] Exclude a building from diagnostics and confirm its building-level advisory disappears
      while network advisories remain.
- [ ] Save/reload and restart with a policy armed: settings, groups, priorities, exclusions,
      event history and tracked switches survive.

### Compatibility and performance

- [ ] A modded consumer, a modded generator and a modded battery behave sanely; custom
      components are labelled as modded rather than as vanilla.
- [ ] A power system that bypasses `CompPowerTrader` shows as an unsupported/missing
      notice and never contributes invented numbers.
- [ ] Large colony (100+ powered buildings): measure frame time with the dashboard open and
      closed; the open dashboard must not drop frames.
- [ ] Heatmap: on the same large save, compare frame time before and after this change at the
      same resolution, UI scale and game speed. Scroll to the top, middle and bottom, including
      partial rows; verify selection, tooltips, Inspect and Locate. Try every mode and search
      while paused, shrink the results while scrolled down, change critical policies, and
      switch maps or remove buildings to verify refreshed rankings and scroll positions.
- [ ] Several maps: each map keeps its own history; switching maps does not mix them.
- [ ] No repeated log spam over a long session.

### Regression

- [ ] Power simulation behaves identically with the mod enabled and disabled (generation,
      battery charging, power-down behaviour).
- [ ] No Harmony patches are present: the mod ships none, so no patch errors are possible.
- [ ] UI scale 1x/1.5x/2x and 1280x720 through 2560x1440: nothing clipped or overlapping.

## 3. Verification recorded for 1.0.1

The following were run in this workspace on 2026-10-30:

- Compiled all production and test sources with Roslyn 5.9 against game doubles and ran
  the harness on .NET 10: **2,465 checks passed**, including **116 Heatmap checks**.
  This was a temporary local toolchain; the project's standard CI target is .NET 8.
- `python3 Tests/static_checks.py` — checks XML, localization, source hygiene, release
  metadata, project/mod version consistency, assets and Markdown links.
- `python3 Tests/build_version_test.py` — checks standalone compiler version attributes
  using a fake compiler and placeholder references; this is not a real game build.
- `python3 Tests/package_release_test.py` — verifies versioned archive naming, the assembly
  gate, optional Workshop-ID preservation and clean restaging using fake DLLs only.
- `./package-release.sh --allow-unbuilt` — verifies the folder and zip layout. Its output
  contains `UNBUILT-PACKAGE.txt` and **must not be distributed**.
- `git diff --check` and shell syntax checks.

Compilation against official game assemblies, launching RimWorld, real save/load, real
rendering and live frame-time profiling were **not performed for this update here**. The
`CompatibilityTests` timing assertions and GUI-double counts are regression guards, not
performance measurements in the game.
