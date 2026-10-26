# Testing

Two separate levels exist, and they must not be confused:

1. **Offline logic harness** — runs here, compiles the production sources against minimal
   game doubles. Proves measurement, filtering, history, chart and localization logic.
   It cannot prove compatibility with the real game assemblies, rendering, or performance.
2. **In-game validation** — requires an installed RimWorld build. Everything in this
   document's second half is still outstanding.

## 1. Offline harness

```sh
# Requires a .NET 8 SDK only; no game files, no NuGet packages, no test framework.
dotnet run --project Tests/PowerGridManager.Tests.csproj --configuration Release
```

Exit code is 0 only when every check passes. The project links
`../Source/PowerGridManager/**/*.cs` plus `Tests/Doubles/*.cs`, so a compile error in the
mod also fails the harness.

What it covers (493 checks at the time of writing):

- **Phase 2 monitoring (32):** empty maps, signed trader output, `PowerOn` gating, flick
  state, batteries and EMP, deficit counting, independent networks, non-colonist buildings,
  disconnected vs pending membership, destroyed/despawned infrastructure, invalid values,
  unsupported components, variable demand, snapshot immutability, cache cadence, tick
  rollback, multi-component buildings, deduplicated lister entries, connector-only traders,
  and a 10,000-building map.
- **Phase 3 dashboard (108):** building roll-ups and component counts, configured
  generation, load ratio, room association, category classification, all network statuses,
  every filter, searching, sort direction and stability, grouping labels, number/unit
  formatting (watts vs kilowatts vs watt-days vs durations), support levels, and a headless
  UI pass that draws every panel and every tab at 320x180, 1010x640, 2560x1400 and with
  real data, clicks network rows, building rows and toolbar buttons, and asserts that no
  drawing path opens a window unexpectedly.
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
- **Localization and metadata (41):** every `PGM_` key referenced in source exists in the
  English keyed file, format-placeholder counts are sufficient, the keyed/def/About XML
  parse, the def wires the right tab window type, the icon texture exists, and the window
  type derives from `MainTabWindow`.

The harness proves nothing about the draw calls themselves: `Widgets`, `GUI` and rendering
are inert doubles.

## 2. In-game validation checklist (outstanding)

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

### Compatibility and performance

- [ ] A modded consumer, a modded generator and a modded battery behave sanely; custom
      components are labelled as modded rather than as vanilla.
- [ ] A power system that bypasses `CompPowerTrader` shows as an unsupported/missing
      notice and never contributes invented numbers.
- [ ] Large colony (100+ powered buildings): measure frame time with the dashboard open and
      closed; the open dashboard must not drop frames.
- [ ] Several maps: each map keeps its own history; switching maps does not mix them.
- [ ] No repeated log spam over a long session.

### Regression

- [ ] Power simulation behaves identically with the mod enabled and disabled (generation,
      battery charging, power-down behaviour).
- [ ] No Harmony patches are present: the mod ships none, so no patch errors are possible.
- [ ] UI scale 1x/1.5x/2x and 1280x720 through 2560x1440: nothing clipped or overlapping.

## 3. Static checks performed in this environment

- Full compile of all production sources plus the harness against the local .NET 10
  runtime (Roslyn), then execution of all 493 checks.
- API signatures cross-checked against a public RimWorld 1.6 decompilation reference:
  `MainTabWindow`, `MainButtonDef`, `TabDrawer`, `TabRecord.GetTip`, `Widgets.*`,
  `Text.*`, `Map.FillComponents` auto-instantiation, `Map.GetComponent<T>`,
  `MapComponent` hooks, `Scribe_*`, `CompPower*`, `CompFlickable`, `Room`/`RoomRoleDef`,
  `CameraJumper`, `Dialog_InfoCard`, `FloatMenu`.
- XML parsing of `About.xml`, the `MainButtonDef` and the English keyed file.
- `git diff --check` for whitespace errors.

**Not performed:** compiling against the real game assemblies, launching the game, real
save/load, real rendering, profiling.
