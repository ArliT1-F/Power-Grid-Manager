// Phase 4 verification: bounded sampling, retention, save/load round-trips, corrupt data
// tolerance, chart geometry and the map component's sampling integration.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using PowerGridManager.Tests;
using PowerScenario = PowerGridManager.Tests.PowerScenario;

namespace PowerGridManager
{
    public static class HistoryTests
    {
        public static int Run()
        {
            TestAssert.UsePhase("phase4");
            int start = TestAssert.Checks;
            Sampling();
            RetentionAndCap();
            Rollback();
            SaveLoad();
            CorruptData();
            ComponentIntegration();
            Charts();
            Console.WriteLine("Phase 4 history checks passed: " + (TestAssert.Checks - start));
            return TestAssert.Checks - start;
        }

        private static PowerGridSnapshot SnapshotAt(int tick, float generation, float consumption, float stored,
            float capacity)
        {
            return new PowerGridSnapshot
            {
                Tick = tick,
                GenerationWatts = generation,
                ConsumptionWatts = consumption,
                StoredWattDays = stored,
                CapacityWattDays = capacity
            };
        }

        private static void Sampling()
        {
            var tracker = new PowerHistoryTracker();
            TestAssert.Check(!tracker.HasSamples, "new tracker is empty");
            TestAssert.Check(tracker.MaybeSample(SnapshotAt(0, 100f, 50f, 10f, 20f), 0, 2500, 60000), "first sample");
            TestAssert.Equal(tracker.Count, 1, "one sample stored");
            TestAssert.Check(!tracker.MaybeSample(SnapshotAt(100, 100f, 50f, 10f, 20f), 100, 2500, 60000),
                "interval not elapsed");
            TestAssert.Check(tracker.MaybeSample(SnapshotAt(2500, 120f, 60f, 12f, 20f), 2500, 2500, 60000),
                "interval elapsed");
            TestAssert.Equal(tracker.Count, 2, "two samples stored");
            TestAssert.Equal(tracker.LastSampleTick, 2500, "last sample tick");
            TestAssert.Equal(tracker.LastSampleIntervalTicks, 2500, "interval recorded");
            TestAssert.Equal(tracker.NewestTick, 2500, "newest sample tick");
            TestAssert.Equal(tracker.OldestTick, 0, "oldest sample tick");
            TestAssert.Check(tracker.CanDrawTrend, "two samples allow a trend");

            PowerHistorySample sample = tracker[1];
            TestAssert.Equal(sample.GenerationWatts, 120f, "sample generation");
            TestAssert.Equal(sample.NetWatts, 60f, "sample net");
            TestAssert.Equal(sample.ChargeFraction.Value, 0.6f, "sample charge fraction");

            // Interval clamping: absurd values must not break sampling.
            var clamped = new PowerHistoryTracker();
            TestAssert.Check(clamped.MaybeSample(SnapshotAt(0, 1f, 1f, 0f, 0f), 0, 1, 60000), "clamped interval first");
            TestAssert.Check(!clamped.MaybeSample(SnapshotAt(30, 1f, 1f, 0f, 0f), 30, 1, 60000),
                "clamped interval floor is 60 ticks");
            TestAssert.Check(clamped.MaybeSample(SnapshotAt(60, 1f, 1f, 0f, 0f), 60, 1, 60000),
                "samples after the clamped floor");

            TestAssert.Check(!tracker.MaybeSample(null, 5000, 2500, 60000), "null snapshot ignored");

            // A cached snapshot is recorded at its own measurement tick, not at "now".
            var stale = new PowerHistoryTracker();
            TestAssert.Check(stale.MaybeSample(SnapshotAt(200, 5f, 5f, 0f, 0f), 260, 60, 60000),
                "stale snapshot sampled");
            TestAssert.Equal(stale.NewestTick, 200, "sample keeps the snapshot's measurement tick");
            TestAssert.Check(stale.MaybeSample(SnapshotAt(320, 5f, 5f, 0f, 0f), 380, 60, 60000),
                "next stale snapshot sampled");
            TestAssert.Equal(stale.NewestTick, 320, "series follows measurement ticks");
            TestAssert.Check(stale.MaybeSample(SnapshotAt(700, 5f, 5f, 0f, 0f), 740, 60, 60000),
                "interval is judged against the last sample tick");
            TestAssert.Check(!stale.MaybeSample(SnapshotAt(700, 5f, 5f, 0f, 0f), 800, 60, 60000),
                "the same snapshot tick is never stored twice");
            for (int i = 1; i < stale.Count; i++)
            {
                TestAssert.Check(stale.Samples[i].Tick > stale.Samples[i - 1].Tick,
                    "in-memory series stays strictly monotonic");
            }
        }

        private static void RetentionAndCap()
        {
            var tracker = new PowerHistoryTracker();
            for (int i = 0; i <= 10; i++)
            {
                tracker.MaybeSample(SnapshotAt(i * 2500, 10f, 5f, 1f, 2f), i * 2500, 2500, 6 * 2500);
            }
            TestAssert.Equal(tracker.Count, 7, "retention window bounds the sample count");
            TestAssert.Equal(tracker.NewestTick, 25000, "newest retained");
            TestAssert.Check(tracker.OldestTick >= 25000 - 15000, "oldest sample inside the window");

            TestAssert.Equal(PowerHistoryTracker.ComputeSampleCap(60000, 2500), 26, "cap for one day");
            TestAssert.Check(PowerHistoryTracker.ComputeSampleCap(60000, 60000) >= 16, "minimum cap");
            TestAssert.Check(PowerHistoryTracker.ComputeSampleCap(int.MaxValue / 2, 60) <=
                             PowerHistoryTracker.HardSampleCap, "hard cap respected");

            var large = new PowerHistoryTracker();
            for (int i = 0; i < 5000; i++)
            {
                large.MaybeSample(SnapshotAt(i * 60, 1f, 1f, 0f, 0f), i * 60, 60, 60000);
            }
            TestAssert.Check(large.Count <= PowerHistoryTracker.ComputeSampleCap(60000, 60),
                "very short intervals stay bounded");

            large.Clear();
            TestAssert.Equal(large.Count, 0, "clear empties history");
            TestAssert.Equal(large.LastSampleTick, -1, "clear resets sampling cursor");
        }

        private static void Rollback()
        {
            var tracker = new PowerHistoryTracker();
            tracker.MaybeSample(SnapshotAt(1000, 1f, 1f, 0f, 0f), 1000, 2500, 60000);
            tracker.MaybeSample(SnapshotAt(4000, 2f, 2f, 0f, 0f), 4000, 2500, 60000);
            TestAssert.Equal(tracker.Count, 2, "two samples before rollback");

            // A save reloaded at an earlier tick drops the impossible future sample
            // instead of writing a backwards time series.
            TestAssert.Check(tracker.MaybeSample(SnapshotAt(500, 3f, 3f, 0f, 0f), 500, 2500, 60000),
                "rollback stores a fresh sample");
            TestAssert.Equal(tracker.Count, 1, "future samples trimmed");
            TestAssert.Equal(tracker.NewestTick, 500, "series stays monotonic");

            TestAssert.Check(!tracker.MaybeSample(SnapshotAt(600, 3f, 3f, 0f, 0f), 600, 2500, 60000),
                "interval still enforced after rollback");
        }

        private static void SaveLoad()
        {
            var original = new PowerHistoryTracker();
            original.MaybeSample(SnapshotAt(0, 1000f, 400f, 250f, 600f), 0, 2500, 7 * 60000);
            original.MaybeSample(SnapshotAt(2500, 900f, 500f, 200f, 600f), 2500, 2500, 7 * 60000);
            original.MaybeSample(SnapshotAt(5000, 800f, 600f, 150f, 600f), 5000, 2500, 7 * 60000);

            SaveNode tree = Scribe.BeginSaving();
            original.ExposeData();
            Scribe.Finish();

            var loaded = new PowerHistoryTracker();
            Scribe.BeginLoading(tree);
            loaded.ExposeData();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            loaded.ExposeData();
            Scribe.Finish();

            TestAssert.Equal(loaded.Count, 3, "all samples survive save/load");
            TestAssert.Equal(loaded.NewestTick, 5000, "newest tick survives");
            TestAssert.Equal(loaded.LastSampleIntervalTicks, 2500, "interval survives");
            TestAssert.Equal(loaded[0].GenerationWatts, 1000f, "values survive");
            TestAssert.Equal(loaded[2].StoredWattDays, 150f, "stored energy survives");
            TestAssert.Equal(loaded.DiscardedSampleCount, 0, "no rows discarded on a clean load");
            TestAssert.Check(loaded.MaybeSample(SnapshotAt(7500, 1f, 1f, 0f, 0f), 7500, 2500, 60000),
                "sampling continues after load");
            TestAssert.Equal(loaded.Count, 4, "post-load sample appended");

            // A save written by a future version must not break loading.
            var future = new PowerHistoryTracker();
            future.MaybeSample(SnapshotAt(0, 1f, 1f, 0f, 0f), 0, 2500, 60000);
            SaveNode futureTree = Scribe.BeginSaving();
            future.ExposeData();
            Scribe.Finish();
            futureTree.values["formatVersion"] = 99;
            var downgraded = new PowerHistoryTracker();
            Scribe.BeginLoading(futureTree);
            downgraded.ExposeData();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            downgraded.ExposeData();
            Scribe.Finish();
            TestAssert.Equal(downgraded.Count, 1, "future format still loads samples");
            TestAssert.Equal(downgraded.FormatVersion, PowerHistoryTracker.CurrentFormatVersion,
                "future format version is clamped");

            // An empty tracker round-trips without creating bogus samples.
            var empty = new PowerHistoryTracker();
            SaveNode emptyTree = Scribe.BeginSaving();
            empty.ExposeData();
            Scribe.Finish();
            var emptyLoaded = new PowerHistoryTracker();
            Scribe.BeginLoading(emptyTree);
            emptyLoaded.ExposeData();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            emptyLoaded.ExposeData();
            Scribe.Finish();
            TestAssert.Equal(emptyLoaded.Count, 0, "empty history loads as empty");
        }

        private static void CorruptData()
        {
            var tracker = new PowerHistoryTracker();
            tracker.MaybeSample(SnapshotAt(0, 1f, 1f, 0f, 0f), 0, 2500, 60000);
            tracker.MaybeSample(SnapshotAt(2500, 2f, 2f, 0f, 0f), 2500, 2500, 60000);
            SaveNode tree = Scribe.BeginSaving();
            tracker.ExposeData();
            Scribe.Finish();

            // Truncated column.
            var generation = (List<float>)tree.values["generationWatts"];
            generation.RemoveAt(1);
            var loaded = new PowerHistoryTracker();
            Scribe.BeginLoading(tree);
            loaded.ExposeData();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            loaded.ExposeData();
            Scribe.Finish();
            TestAssert.Equal(loaded.Count, 1, "mismatched column length keeps the valid prefix");
            TestAssert.Check(loaded.DiscardedSampleCount > 0, "dropped rows are reported");

            // Non-monotonic ticks and NaN values are rejected rather than charted.
            var second = new PowerHistoryTracker();
            second.MaybeSample(SnapshotAt(0, 1f, 1f, 0f, 0f), 0, 2500, 60000);
            SaveNode tree2 = Scribe.BeginSaving();
            second.ExposeData();
            Scribe.Finish();
            var ticks = (List<int>)tree2.values["ticks"];
            ticks.Add(0);
            var gen2 = (List<float>)tree2.values["generationWatts"];
            gen2.Add(float.NaN);
            var cons2 = (List<float>)tree2.values["consumptionWatts"];
            cons2.Add(1f);
            var stored2 = (List<float>)tree2.values["storedWattDays"];
            stored2.Add(0f);
            var cap2 = (List<float>)tree2.values["capacityWattDays"];
            cap2.Add(0f);
            var loaded2 = new PowerHistoryTracker();
            Scribe.BeginLoading(tree2);
            loaded2.ExposeData();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            loaded2.ExposeData();
            Scribe.Finish();
            TestAssert.Equal(loaded2.Count, 1, "duplicate tick and NaN row rejected");
            TestAssert.Equal(loaded2.DiscardedSampleCount, 1, "rejection counted");

            // Missing keys (older or hand-edited save) must not throw.
            var third = new PowerHistoryTracker();
            Scribe.BeginLoading(new SaveNode("root"));
            third.ExposeData();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            third.ExposeData();
            Scribe.Finish();
            TestAssert.Equal(third.Count, 0, "missing data loads as empty");
            TestAssert.Check(third.MaybeSample(SnapshotAt(0, 1f, 1f, 0f, 0f), 0, 2500, 60000),
                "tracker still usable after a failed load");
        }

        private static void ComponentIntegration()
        {
            var scenario = new PowerScenario();
            var net = scenario.AddNet();
            var plantBuilding = scenario.CreateBuilding(ThingDefFactory.Simple("plant"));
            scenario.AddTrader<CompPowerPlant>(net, plantBuilding, 1500f);
            var lampBuilding = scenario.CreateBuilding(ThingDefFactory.Simple("lamp", typeof(CompGlower)));
            scenario.AddTrader(net, lampBuilding, -500f, true, 500f);

            PowerGridManagerSettings settings = new PowerGridManagerSettings
            {
                RefreshIntervalTicks = 120,
                SampleIntervalTicks = 2500,
                RetentionDays = 2,
                HistoryEnabled = true
            };
            new PowerGridManagerMod(new ModContentPack());
            PowerGridManagerMod.Settings.RefreshIntervalTicks = settings.RefreshIntervalTicks;
            PowerGridManagerMod.Settings.SampleIntervalTicks = settings.SampleIntervalTicks;
            PowerGridManagerMod.Settings.RetentionDays = settings.RetentionDays;
            PowerGridManagerMod.Settings.HistoryEnabled = settings.HistoryEnabled;

            var component = new PowerGridManagerComponent(scenario.Map);
            Find.TickManager.TicksGame = 0;
            component.MapComponentTick();
            TestAssert.Equal(component.History.Count, 1, "component records a sample on the first tick");
            Find.TickManager.TicksGame = 100;
            component.MapComponentTick();
            TestAssert.Equal(component.History.Count, 1, "no sample before the interval");
            Find.TickManager.TicksGame = 2500;
            component.MapComponentTick();
            TestAssert.Equal(component.History.Count, 2, "sample at the interval");
            PowerHistorySample latest = component.History[1];
            TestAssert.Equal(latest.GenerationWatts, 1500f, "component samples the snapshot generation");
            TestAssert.Equal(latest.ConsumptionWatts, 500f, "component samples the snapshot consumption");

            // History must not require the dashboard: it is driven by the map tick only.
            PowerGridManagerMod.Settings.HistoryEnabled = false;
            Find.TickManager.TicksGame = 5000;
            component.MapComponentTick();
            TestAssert.Equal(component.History.Count, 2, "disabled history stops sampling");
            PowerGridManagerMod.Settings.HistoryEnabled = true;
            Find.TickManager.TicksGame = 50000;
            component.MapComponentTick();
            TestAssert.Equal(component.History.Count, 3, "re-enabling history resumes sampling");

            // Retention uses the configured windows.
            TestAssert.Check(component.History.Count <=
                             PowerHistoryTracker.ComputeSampleCap(settings.RetentionTicks, settings.SampleIntervalTicks),
                "component history respects the retention cap");

            // Map component save/load.
            SaveNode tree = Scribe.BeginSaving();
            component.ExposeData();
            Scribe.Finish();
            var reloaded = new PowerGridManagerComponent(scenario.Map);
            Scribe.BeginLoading(tree);
            reloaded.ExposeData();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            reloaded.ExposeData();
            Scribe.Finish();
            TestAssert.Equal(reloaded.History.Count, component.History.Count, "map component history survives");
            TestAssert.Null(reloaded.Snapshot, "runtime cache is not restored from the save");
            Find.TickManager.TicksGame = 60000;
            reloaded.MapComponentTick();
            TestAssert.NotNull(reloaded.Snapshot, "cache rebuilt after loading");

            // RefreshIfStale is what the window calls: it must not rescan while fresh.
            PowerGridSnapshot first = reloaded.Snapshot;
            reloaded.RefreshIfStale(60010);
            TestAssert.Same(reloaded.Snapshot, first, "opening the window reuses the fresh snapshot");
            reloaded.RefreshIfStale(60000 + 121);
            TestAssert.Check(!ReferenceEquals(reloaded.Snapshot, first), "stale snapshot is refreshed once");
        }

        private static void Charts()
        {
            var samples = new List<PowerHistorySample>
            {
                new PowerHistorySample(0, 1000f, 400f, 300f, 600f),
                new PowerHistorySample(2500, 1200f, 500f, 250f, 600f),
                new PowerHistorySample(5000, 800f, 900f, 200f, 600f)
            };

            PowerChartData generation = PowerChartBuilder.Build(samples, 0, 5000, 10,
                PowerChartMetric.GenerationAndConsumption);
            TestAssert.Check(generation.HasData, "generation chart has data");
            TestAssert.Equal(generation.Columns, 10, "column count preserved");
            TestAssert.Check(generation.HasSecondary, "generation chart has two series");
            TestAssert.Check(generation.Max >= 1200f, "axis covers the maximum");
            TestAssert.Check(generation.Min <= 400f, "axis covers the minimum");
            TestAssert.Check(generation.MissingColumnCount > 0, "sparse columns are reported as missing");
            TestAssert.Check(generation.Primary[0].HasValue, "first column has a value");
            TestAssert.Equal(generation.Primary[0].Value, 1000f, "single sample column keeps its value");

            // One column per sample so each bucket maps to exactly one recorded sample.
            PowerChartData net = PowerChartBuilder.Build(samples, 0, 5000, 3, PowerChartMetric.NetBalance);
            TestAssert.Check(net.Min <= 0f && net.Max >= 0f, "net chart always shows zero");
            TestAssert.Equal(net.Primary[2].Value, -100f, "deficit bucket negative");
            TestAssert.Equal(net.Primary[0].Value, 600f, "surplus bucket positive");

            PowerChartData charge = PowerChartBuilder.Build(samples, 0, 5000, 3, PowerChartMetric.BatteryCharge);
            TestAssert.Equal(charge.Min, 0f, "charge axis starts at zero");
            TestAssert.Equal(charge.Max, 100f, "charge axis ends at 100");
            TestAssert.Equal(charge.Primary[0].Value, 50f, "charge converted to percent");
            TestAssert.Equal(charge.Primary[2].Value, 33.333f, "charge of the third sample");

            PowerChartData empty = PowerChartBuilder.Build(new List<PowerHistorySample>(), 0, 5000, 10,
                PowerChartMetric.NetBalance);
            TestAssert.Check(!empty.HasData, "empty chart has no data");
            TestAssert.Equal(empty.MissingColumnCount, 10, "all columns missing");
            TestAssert.Check(empty.Max > empty.Min, "empty chart still has a usable axis");

            PowerChartData outOfRange = PowerChartBuilder.Build(samples, 20000, 30000, 10,
                PowerChartMetric.NetBalance);
            TestAssert.Check(!outOfRange.HasData, "range without samples reports no data");

            // Averages within a bucket rather than showing only the last sample.
            var bucket = new List<PowerHistorySample>
            {
                new PowerHistorySample(0, 100f, 0f, 0f, 0f),
                new PowerHistorySample(100, 300f, 0f, 0f, 0f)
            };
            PowerChartData averaged = PowerChartBuilder.Build(bucket, 0, 1000, 1,
                PowerChartMetric.GenerationAndConsumption);
            TestAssert.Equal(averaged.Primary[0].Value, 200f, "bucket averages its samples");
            TestAssert.Equal(averaged.Secondary[0].Value, 0f, "secondary series averaged too");

            // Charge series ignores samples without a battery instead of drawing zero.
            var noBattery = new List<PowerHistorySample>
            {
                new PowerHistorySample(0, 1f, 1f, 0f, 0f),
                new PowerHistorySample(2500, 1f, 1f, 5f, 10f)
            };
            PowerChartData charge2 = PowerChartBuilder.Build(noBattery, 0, 2500, 2, PowerChartMetric.BatteryCharge);
            TestAssert.Check(!charge2.Primary[0].HasValue, "no-capacity sample is not drawn as zero charge");
            TestAssert.Equal(charge2.Primary[1].Value, 50f, "capacity sample drawn");

            // Demand relative to generation: percentages, with gaps where generation is zero.
            var mix = new List<PowerHistorySample>
            {
                new PowerHistorySample(0, 1000f, 500f, 0f, 0f),
                new PowerHistorySample(1000, 1000f, 1500f, 0f, 0f),
                new PowerHistorySample(2000, 0f, 100f, 0f, 0f)
            };
            PowerChartData load = PowerChartBuilder.Build(mix, 0, 2000, 3, PowerChartMetric.LoadRatio);
            TestAssert.Equal(load.Primary[0].Value, 50f, "half load");
            TestAssert.Equal(load.Primary[1].Value, 150f, "overload above 100%");
            TestAssert.Check(!load.Primary[2].HasValue, "no ratio when generation is zero");
            TestAssert.Equal(load.Min, 0f, "load axis starts at zero");
            TestAssert.Equal(load.Max, 150f, "load axis covers the overload peak");

            PowerChartData lightLoad = PowerChartBuilder.Build(mix, 0, 999, 2, PowerChartMetric.LoadRatio);
            TestAssert.Equal(lightLoad.Max, 100f, "load axis keeps the 100% reference visible");

            PowerChartData noLoadData = PowerChartBuilder.Build(new List<PowerHistorySample>(), 0, 1000, 2,
                PowerChartMetric.LoadRatio);
            TestAssert.Check(!noLoadData.HasData, "load chart reports missing data");

            TestAssert.Equal(PowerChartBuilder.TicksFor(PowerTimeRange.LastHour), PowerTime.TicksPerHour,
                "hour range");
            TestAssert.Equal(PowerChartBuilder.TicksFor(PowerTimeRange.LastSevenDays), PowerTime.TicksPerDay * 7,
                "week range");
            PowerChartData clampedColumns = PowerChartBuilder.Build(samples, 0, 5000, 99999,
                PowerChartMetric.NetBalance);
            TestAssert.Equal(clampedColumns.Columns, PowerChartBuilder.MaximumColumns, "column cap");
        }
    }
}
