# Phase 4: historical monitoring

Status: implemented and verified against the offline harness only. Save/load was simulated
with serialization doubles, not with a real RimWorld save. No in-game validation was
possible in this environment.

## Data model

`PowerHistorySample` (struct, 20 bytes) holds one aggregate observation:

| Field | Meaning |
|---|---|
| `Tick` | Game tick the sample was taken on. |
| `GenerationWatts` | Map-wide measured generation at that moment. |
| `ConsumptionWatts` | Map-wide measured consumption at that moment. |
| `StoredWattDays` | Physical battery energy (watt-days). |
| `CapacityWattDays` | Configured battery capacity (watt-days). |

`NetWatts` and `ChargeFraction` are derived, never stored. No building, network or `Thing`
reference is persisted: network identity is snapshot-local and game objects are not
serialization-safe.

Samples are **map-wide aggregates**. This is deliberate: a network can be split, merged or
renumbered by the game at any time, and a per-network series keyed by list position would
fabricate continuity that does not exist. Per-network history is therefore recorded as
part of the map series, and the UI does not offer per-network history.

## Collection

`PowerGridManagerComponent` (a `MapComponent`, discovered by `Map.FillComponents`) owns a
`PowerHistoryTracker`:

- `MapComponentTick` refreshes the snapshot on the configured interval and then calls
  `MaybeSample(snapshot, tick, sampleIntervalTicks, retentionTicks)`.
- Sampling runs on the map tick only. It does not depend on the dashboard being open, so
  closing the window never stops collection. Pausing the game stops ticks, hence sampling.
- One sample per interval regardless of how many ticks were skipped; the tick is recorded
  so gaps remain visible in the charts.
- A sample stores the **snapshot's own measurement tick**, not the tick of the call: the
  cached snapshot can be one refresh interval old, and writing "now" would shift every
  chart point by that lag. Re-sampling an already-recorded tick is skipped, so the series
  is strictly monotonic in memory, matching what the loader accepts.
- `HistoryEnabled` (settings) turns recording off without discarding existing history.
- Fresh samples from a snapshot with non-finite values are sanitized to zero rather than
  poisoning the series.

## Bounds and retention

Three independent bounds keep storage small:

1. **Retention window** — samples older than `RetentionDays` (1–30, default 7) are dropped.
2. **Derived cap** — `ComputeSampleCap(retention, interval) = retention / interval + 2`,
   with a floor of 16 rows and a hard ceiling of 20,000 rows (`HardSampleCap`).
3. **Interval clamp** — 60 to 15,000 ticks, so a pathological setting cannot produce a
   per-tick series.

At the defaults (7 days, 2,500-tick interval) that is **243 samples per map**, i.e. a few
kilobytes of save data. Even the hard ceiling (20,000 rows of five primitives) stays in
the low hundreds of kilobytes, and it is unreachable with the allowed settings
(7 days at 60 ticks would be 10,080 rows, still under the ceiling).

## Save/load behaviour

`PowerHistoryTracker.ExposeData` writes five parallel primitive lists (`ticks`,
`generationWatts`, `consumptionWatts`, `storedWattDays`, `capacityWattDays`) plus
`formatVersion`, `sampleInterval` and `lastSampleTick`, using `Scribe_Columns.Look` with
`LookMode.Value` and `Scribe_Values`. Parallel lists were chosen over a list of exposable
objects because each sample is a plain aggregate: there is no object identity to preserve
and no cross-reference to resolve.

Loading is defensive:

- Missing keys (older save, hand-edited save, mod downgraded) load as empty rather than throwing.
- Columns shorter than `ticks` keep the valid shared prefix and record
  `DiscardedSampleCount`; the history panel warns when rows were ignored.
- Rows with negative or non-increasing ticks, or non-finite values, are dropped — the
  loaded series is always monotonic.
- The hard cap is applied on load as well, so an oversized save cannot blow up memory.
- `formatVersion` is clamped down to the current version after loading; unknown fields are
  simply absent for future versions.
- `PostLoadInit` resets the runtime cache (`Snapshot = null`, `lastRefreshTick = -1`). The
  first tick rebuilds it, so a save never contains stale snapshot state.

`MapRemoved` clears the history and the cache, handling map unloading/reloading. If the
mod is removed from an existing save, RimWorld reports the usual unknown-map-component
message for this component and no vanilla data is referenced or modified; the mod stores
no reference to game objects that could dangle.

Tick rollback (debug tools, or a save loaded behind a recorded sample) trims samples newer
than the current tick instead of writing a backwards time series, then resumes normally.

## Charts

`PowerChartBuilder` is pure logic (no Unity/RimWorld drawing types) so it can be verified
offline. It bins samples into screen-independent columns:

- One column per sample bucket; a bucket with several samples shows their **average**.
- A bucket with no sample is `null`. Nothing is interpolated or carried forward, and the
  panel reports "N of M columns have no sample" in warning colour.
- Ranges: 1 hour, 6 hours, 1 day, 3 days, 7 days of in-game time (`PowerTime.TicksPerHour`
  = 2,500, `PowerTime.TicksPerDay` = 60,000).
- Metrics: generation and consumption (two series), net balance (axis always includes
  zero, negative values rendered in a distinct colour), battery charge (0–100 %),
  consumption relative to generation (0 % upward, axis keeps the 100 % line visible, and a
  sample with no generation becomes a gap instead of infinity or a fake 0 %).
- Column count is capped at `MaximumColumns` (2,048); the panel asks for roughly one column
  per three pixels.

`PowerChartWidget` renders with vanilla widgets only (`DrawBoxSolid`, `DrawLineHorizontal`,
`Label`): filled columns plus a solid value cap, three horizontal guides with axis values,
a legend, the range start/end labels ("x before the newest sample" / "newest sample y ago")
and the missing-data warning. No third-party charting dependency.

Chart geometry is cached and rebuilt only when the sample count, newest tick, selected
range or pixel size changes, so redrawing does not re-bin 243 samples every frame.

## What the charts are not

- They are **not** a forecast. Battery endurance and demand forecasting are Phase 6 and
  are not implemented.
- They do not imply that a gap in the past was a power cut: sampling only happens while
  the map is running.
- They show aggregates for the whole map; they cannot attribute an average to a single
  building or network.
- A recorded deficit is not a diagnosis; interpreting deficits is Phase 5.

## Verification performed offline

`Tests/HistoryTests.cs` covers: first-sample handling, interval enforcement and clamping,
retention pruning, the derived cap and hard cap, tick rollback and monotonicity, clear,
save/load round-trip, loading a save written by a "future" format version, empty-history
round-trip, truncated columns, duplicate ticks, NaN values, a completely missing node, map
component integration (sampling cadence, disabling history, resuming, retention, save/load,
cache rebuild, `RefreshIfStale` reuse), and chart building (buckets, averages, gaps, axis
bounds, lookups outside the range, column cap).

All of this uses serialization doubles that mirror the `Scribe` signatures; it proves the
logic, not RimWorld's save pipeline or file format.
