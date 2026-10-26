// Phase 6 verification: battery endurance forecasting and its UI.
//
// The point of this phase is that the mod reports an *estimate*, always says what it is based
// on, and never writes anything back into the game's power simulation. Most checks below exist
// to pin those three properties down.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PowerGridManager.Tests
{
    public static class ForecastTests
    {
        public static int Run()
        {
            TestAssert.UsePhase("phase6");
            int start = TestAssert.Checks;
            HistoryAggregates();
            DepletionArithmetic();
            ChargingArithmetic();
            DegenerateInputs();
            Confidence();
            Wiring();
            PanelSmoke();
            DoesNotTouchTheSimulation();
            Console.WriteLine("Phase 6 forecast checks passed: " + (TestAssert.Checks - start));
            return TestAssert.Checks - start;
        }

        // --- Helpers ----------------------------------------------------------

        private static List<PowerHistorySample> Samples(params PowerHistorySample[] samples)
        {
            return new List<PowerHistorySample>(samples);
        }

        private static PowerHistorySample Sample(int tick, float generation, float consumption)
        {
            return new PowerHistorySample(tick, generation, consumption, 300f, 600f);
        }

        private static PowerGridManagerSettings Settings()
        {
            var settings = new PowerGridManagerSettings();
            PowerGridManagerMod.Settings = settings;
            return settings;
        }

        private static PowerGridSnapshot Scan(Map map, int tick)
        {
            return new PowerGridScanner().Scan(map, tick);
        }

        /// <summary>A map with 1000 W of generation, 1200 W of draw and a half-full 600 Wd bank.</summary>
        private static PowerScenario DrainingMap(out PowerNet net)
        {
            var scenario = new PowerScenario();
            net = scenario.AddNet();
            var plant = scenario.AddBuilding(ThingDefFactory.Simple("plant"));
            scenario.AddTrader<CompPowerPlant>(net, plant, 1000f, true, -1000f);
            var user = scenario.AddBuilding(ThingDefFactory.Simple("drill"));
            scenario.AddTrader(net, user, -1200f, true, 1200f);
            var bank = scenario.AddBuilding(ThingDefFactory.Simple("battery"));
            scenario.AddBattery(net, bank, 300f, 600f, false, 0.5f);
            return scenario;
        }

        // --- Historical aggregates --------------------------------------------

        private static void HistoryAggregates()
        {
            var samples = Samples(Sample(0, 1000f, 1200f), Sample(1000, 1000f, 1000f), Sample(2000, 800f, 1200f));

            float generation;
            float consumption;
            int count;
            TestAssert.Check(PowerForecast.TryAverage(samples, 0, 2000, out generation, out consumption, out count),
                "average over a populated window");
            TestAssert.Equal(generation, 933.333f, "average generation");
            TestAssert.Equal(consumption, 1133.333f, "average consumption");
            TestAssert.Equal(count, 3, "sample count");

            int netCount;
            float? net = PowerForecast.AverageNetWatts(samples, 0, 2000, out netCount);
            TestAssert.NotNull(net, "average net available");
            TestAssert.Equal(net.Value, -200f, "average net is generation minus consumption");
            TestAssert.Equal(netCount, 3, "net sample count");

            int shareCount;
            float share = PowerForecast.DeficitShare(samples, 0, 2000, out shareCount);
            TestAssert.Equal(shareCount, 3, "deficit share counts every sample");
            TestAssert.Equal(share, 2f / 3f, "deficit share");

            // Narrowing the window changes the answer.
            int narrowCount;
            float? narrow = PowerForecast.AverageNetWatts(samples, 1500, 2000, out narrowCount);
            TestAssert.Equal(narrowCount, 1, "windowed average respects the tick range");
            TestAssert.Equal(narrow.Value, -400f, "windowed net");

            // Empty and inverted windows are reported as unavailable rather than as zero.
            int emptyCount;
            TestAssert.Null(PowerForecast.AverageNetWatts(new List<PowerHistorySample>(), 0, 100, out emptyCount),
                "no samples means no average");
            TestAssert.Equal(emptyCount, 0, "no samples means no count");
            int invertedCount;
            TestAssert.Null(PowerForecast.AverageNetWatts(samples, 2000, 0, out invertedCount),
                "an inverted window is not averaged");
            TestAssert.Equal(PowerForecast.DeficitShare(new List<PowerHistorySample>(), 0, 100, out emptyCount), 0f,
                "deficit share of nothing is zero");
        }

        // --- Depletion ---------------------------------------------------------

        private static void DepletionArithmetic()
        {
            // 300 Wd available, 200 W deficit, one battery losing 5 W to self-discharge.
            var result = BatteryForecast.Evaluate(300f, 300f, 600f, 1000f, 1200f, 1, 0.5f, null, 0, true);
            TestAssert.Equal((int)result.Kind, (int)BatteryForecastKind.Depleting, "a deficit depletes the bank");
            TestAssert.Equal(result.TraderNetWatts, -200f, "trader balance");
            TestAssert.Equal(result.SelfDischargeWatts, 5f, "self-discharge is one battery at 5 W");
            TestAssert.Equal(result.EffectiveNetWatts, -205f, "the estimate uses the balance net of self-discharge");
            TestAssert.NotNull(result.TicksToEmpty, "a depletion time is produced");
            TestAssert.Equal(result.TicksToEmpty.Value, (int)Math.Round(300f / 205f * PowerTime.TicksPerDay),
                "depletion time is available energy over the effective rate");
            TestAssert.Null(result.TicksToFull, "no charge time while depleting");
            TestAssert.Check(!result.Saturated, "a short estimate is not clipped");

            // Turning self-discharge off removes those 5 W, which makes the estimate optimistic.
            var without = BatteryForecast.Evaluate(300f, 300f, 600f, 1000f, 1200f, 1, 0.5f, null, 0, false);
            TestAssert.Equal(without.SelfDischargeWatts, 0f, "self-discharge can be excluded");
            TestAssert.Check(without.TicksToEmpty.Value > result.TicksToEmpty.Value,
                "ignoring self-discharge overstates endurance");

            // Two batteries lose twice as much.
            var twoBatteries = BatteryForecast.Evaluate(300f, 300f, 600f, 1000f, 1200f, 2, 0.5f, null, 0, true);
            TestAssert.Equal(twoBatteries.SelfDischargeWatts, 10f, "self-discharge scales with the battery count");

            // A very slow drain is reported as "more than" instead of a misleadingly precise number.
            var slow = BatteryForecast.Evaluate(600f, 600f, 600f, 1000f, 1001f, 1, 0.5f, null, 0, false);
            TestAssert.Check(slow.Saturated, "a very long estimate is flagged as clipped");
            TestAssert.Equal(slow.TicksToEmpty.Value, BatteryForecast.MaximumReportableTicks,
                "a clipped estimate stops at the reporting horizon");

            // A battery disabled by EMP contributes no usable energy.
            var stunned = BatteryForecast.Evaluate(300f, 0f, 600f, 1000f, 1200f, 1, 0.5f, null, 0, true);
            TestAssert.Equal((int)stunned.Kind, (int)BatteryForecastKind.Balanced,
                "no usable energy means no depletion estimate");
            TestAssert.Equal(stunned.AvailableWattDays, 0f, "EMP-stunned storage is not counted as available");
        }

        // --- Charging ----------------------------------------------------------

        private static void ChargingArithmetic()
        {
            // 300 Wd of room, 200 W surplus, halved by the battery's own charge efficiency.
            var result = BatteryForecast.Evaluate(300f, 300f, 600f, 1200f, 1000f, 1, 0.5f, null, 0, true);
            TestAssert.Equal((int)result.Kind, (int)BatteryForecastKind.Charging, "a surplus charges the bank");
            TestAssert.NotNull(result.TicksToFull, "a charge time is produced");
            TestAssert.Equal(result.TicksToFull.Value,
                (int)Math.Round(300f / (195f * 0.5f) * PowerTime.TicksPerDay),
                "charge time accounts for the charge efficiency");
            TestAssert.Null(result.TicksToEmpty, "no depletion time while charging");
            TestAssert.Check(result.EfficiencyKnown, "efficiency is known when it was read");

            // An unreadable efficiency is assumed to be lossless, and says so.
            var unknown = BatteryForecast.Evaluate(300f, 300f, 600f, 1200f, 1000f, 1, null, null, 0, true);
            TestAssert.Check(!unknown.EfficiencyKnown, "efficiency is reported as unconfirmed");
            TestAssert.Check(unknown.TicksToFull.Value < result.TicksToFull.Value,
                "assuming no charging loss understates the time to full");

            // A full bank has nothing left to store.
            var full = BatteryForecast.Evaluate(600f, 600f, 600f, 1200f, 1000f, 1, 0.5f, null, 0, true);
            TestAssert.Equal((int)full.Kind, (int)BatteryForecastKind.Balanced, "a full bank is not charging");
            TestAssert.Equal(full.ChargeFraction.Value, 1f, "charge fraction of a full bank");
        }

        // --- Degenerate input --------------------------------------------------

        private static void DegenerateInputs()
        {
            var noBatteries = BatteryForecast.Evaluate(300f, 300f, 600f, 1000f, 1200f, 0, 0.5f, null, 0, true);
            TestAssert.Equal((int)noBatteries.Kind, (int)BatteryForecastKind.NoBatteries, "no batteries reported");
            TestAssert.Equal((int)noBatteries.Confidence, (int)ForecastConfidence.Unavailable,
                "no batteries means no confidence");
            TestAssert.Null(noBatteries.TicksToEmpty, "no depletion estimate without batteries");

            var noCapacity = BatteryForecast.Evaluate(300f, 300f, 0f, 1000f, 1200f, 1, 0.5f, null, 0, true);
            TestAssert.Equal((int)noCapacity.Kind, (int)BatteryForecastKind.NoCapacity, "unreadable capacity reported");
            TestAssert.Null(noCapacity.ChargeFraction, "no charge fraction without capacity");

            var balanced = BatteryForecast.Evaluate(300f, 300f, 600f, 1000f, 1000.5f, 1, 0.5f, null, 0, false);
            TestAssert.Equal((int)balanced.Kind, (int)BatteryForecastKind.Balanced, "a flat balance is reported as flat");

            // Non-finite and negative readings must not produce a number at all.
            var nan = BatteryForecast.Evaluate(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, 1, 0.5f, null, 0,
                true);
            TestAssert.Equal(nan.StoredWattDays, 0f, "NaN stored energy is sanitized");
            TestAssert.Equal((int)nan.Kind, (int)BatteryForecastKind.NoCapacity, "NaN capacity yields no forecast");

            var negative = BatteryForecast.Evaluate(-10f, -10f, -10f, -10f, -10f, 1, 0.5f, null, 0, true);
            TestAssert.Equal(negative.StoredWattDays, 0f, "negative stored energy is sanitized");
            TestAssert.Equal(negative.CapacityWattDays, 0f, "negative capacity is sanitized");

            // Stored energy above capacity is clamped rather than reported as over 100%.
            var overfull = BatteryForecast.Evaluate(900f, 900f, 600f, 1000f, 1200f, 1, 0.5f, null, 0, true);
            TestAssert.Equal(overfull.StoredWattDays, 600f, "stored energy is clamped to capacity");
            TestAssert.Equal(overfull.ChargeFraction.Value, 1f, "a clamped bank reads as full");

            var nullSnapshot = BatteryForecast.ForMap(null, null, 0, true);
            TestAssert.Equal((int)nullSnapshot.Kind, (int)BatteryForecastKind.NoBatteries,
                "a missing snapshot yields no forecast rather than throwing");
            TestAssert.Equal((int)BatteryForecast.ForNetwork(null, true).Kind, (int)BatteryForecastKind.NoBatteries,
                "a missing network yields no forecast rather than throwing");
        }

        // --- Confidence --------------------------------------------------------

        private static void Confidence()
        {
            // Current balance only: honest, but low confidence.
            var alone = BatteryForecast.Evaluate(300f, 300f, 600f, 1000f, 1200f, 1, 0.5f, null, 0, true);
            TestAssert.Equal((int)alone.Confidence, (int)ForecastConfidence.Low,
                "an estimate without history is low confidence");

            // Too few samples to describe a trend.
            var tooFew = BatteryForecast.Evaluate(300f, 300f, 600f, 1000f, 1200f, 1, 0.5f, -205f,
                BatteryForecast.MinimumHistoricalSamples - 1, true);
            TestAssert.Equal((int)tooFew.Confidence, (int)ForecastConfidence.Low,
                "a handful of samples is still low confidence");

            // History that agrees with the current balance.
            var agreed = BatteryForecast.Evaluate(300f, 300f, 600f, 1000f, 1200f, 1, 0.5f, -200f,
                BatteryForecast.MinimumHistoricalSamples, true);
            TestAssert.Equal((int)agreed.Confidence, (int)ForecastConfidence.High,
                "history that agrees with the current balance is high confidence");
            TestAssert.NotNull(agreed.HistoricalTicksToEmpty, "a historical depletion estimate is produced too");

            // History that disagrees: the current reading says deficit, the average says surplus.
            var opposed = BatteryForecast.Evaluate(300f, 300f, 600f, 1000f, 1200f, 1, 0.5f, 500f,
                BatteryForecast.MinimumHistoricalSamples, true);
            TestAssert.Equal((int)opposed.Confidence, (int)ForecastConfidence.Low,
                "history that contradicts the current balance is low confidence");

            // History in the same direction but materially different in size.
            var drifted = BatteryForecast.Evaluate(300f, 300f, 600f, 1000f, 1200f, 1, 0.5f, -400f,
                BatteryForecast.MinimumHistoricalSamples, true);
            TestAssert.Equal((int)drifted.Confidence, (int)ForecastConfidence.Medium,
                "history that only roughly agrees is medium confidence");

            // Charging with an unconfirmed efficiency can never be better than medium.
            var charging = BatteryForecast.Evaluate(300f, 300f, 600f, 1200f, 1000f, 1, null, 195f,
                BatteryForecast.MinimumHistoricalSamples, true);
            TestAssert.Equal((int)charging.Confidence, (int)ForecastConfidence.Medium,
                "an unconfirmed charge efficiency caps confidence at medium");

            // A flat historical average cannot confirm a trend either way. Self-discharge is
            // excluded here so the average really is flat; with it included, "flat" still means
            // the bank drains slowly, which is a genuine and different statement.
            var flatHistory = BatteryForecast.Evaluate(300f, 300f, 600f, 1000f, 1200f, 1, 0.5f, 0f,
                BatteryForecast.MinimumHistoricalSamples, false);
            TestAssert.Equal((int)flatHistory.Confidence, (int)ForecastConfidence.Low,
                "a flat history is low confidence");
        }

        // --- Wiring ------------------------------------------------------------

        private static void Wiring()
        {
            var settings = Settings();
            PowerNet net;
            var scenario = DrainingMap(out net);
            var snapshot = Scan(scenario.Map, 1000);

            var perNetwork = BatteryForecast.ForNetwork(snapshot.Networks[0], true);
            TestAssert.Equal((int)perNetwork.Kind, (int)BatteryForecastKind.Depleting, "per-network forecast depletes");
            TestAssert.Equal(perNetwork.BatteryCount, 1, "per-network battery count");
            TestAssert.Equal(perNetwork.CapacityWattDays, 600f, "per-network capacity");
            TestAssert.Equal(perNetwork.ChargeFraction.Value, 0.5f, "per-network charge fraction");
            TestAssert.Null(perNetwork.HistoricalNetWatts, "a per-network forecast quotes no map-wide history");

            var mapWide = BatteryForecast.ForMap(snapshot, -200f, 6, true);
            TestAssert.Equal((int)mapWide.Kind, (int)BatteryForecastKind.Depleting, "map forecast depletes");
            TestAssert.Equal(mapWide.HistoricalSampleCount, 6, "map forecast carries its sample count");
            TestAssert.Equal((int)mapWide.Confidence, (int)ForecastConfidence.High,
                "agreeing history raises the map forecast to high confidence");

            // Through the map component: history is collected first, then quoted.
            var component = new PowerGridManagerComponent(scenario.Map);
            Find.TickManager.TicksGame = 0;
            component.MapComponentTick();
            TestAssert.NotNull(component.Snapshot, "the component produced a snapshot");

            TestAssert.Equal(component.History.Count, 1, "the first refresh records one sample");
            var withoutHistory = component.ComputeForecast(PowerTimeRange.LastDay);
            TestAssert.Equal(withoutHistory.HistoricalSampleCount, 1, "one sample so far");
            TestAssert.Equal((int)withoutHistory.Confidence, (int)ForecastConfidence.Low,
                "a fresh map is low confidence");

            for (int tick = 2500; tick <= 2500 * 7; tick += 2500)
            {
                Find.TickManager.TicksGame = tick;
                component.MapComponentTick();
            }
            TestAssert.Equal(component.History.Count, 8, "eight samples recorded");
            var withHistory = component.ComputeForecast(PowerTimeRange.LastDay);
            TestAssert.Equal(withHistory.HistoricalSampleCount, 8, "the forecast quotes the recorded samples");
            TestAssert.Equal(withHistory.HistoricalNetWatts.Value, -205f,
                "the historical average is quoted net of self-discharge");
            TestAssert.Equal((int)withHistory.Confidence, (int)ForecastConfidence.High,
                "steady history gives high confidence");
            TestAssert.NotNull(component.Forecast, "the component caches a forecast after each refresh");
        }

        // --- Panel --------------------------------------------------------------

        private static void PanelSmoke()
        {
            var settings = Settings();
            PowerNet net;
            var scenario = DrainingMap(out net);
            var snapshot = Scan(scenario.Map, 1000);
            var panel = new PowerForecastPanel();
            var state = new PowerUiState();

            var sizes = new[] { 320f, 1024f, 2560f };
            foreach (float width in sizes)
            {
                WidgetsQueue.Reset();
                panel.Draw(new Rect(0f, 0f, width, Math.Min(width, 1400f)), snapshot, null, state, settings, 1000);
                TestAssert.Equal(Find.WindowStack.windows.Count, 0, "a plain draw opens no window at " + width);
            }

            // The range buttons are the first text buttons in the panel.
            WidgetsQueue.Reset();
            WidgetsQueue.ClickTextAt(2);
            panel.Draw(new Rect(0f, 0f, 1024f, 768f), snapshot, null, state, settings, 1000);
            TestAssert.Equal((int)state.ForecastRange, (int)PowerTimeRange.LastDay,
                "a range button changes the selected range");

            // With history present the comparison block renders as well.
            var history = new PowerHistoryTracker();
            AddSamples(history, 6, 1000f, 1200f);
            panel.Draw(new Rect(0f, 0f, 1024f, 768f), snapshot, history, state, settings, 2500 * 6);
            panel.Draw(new Rect(0f, 0f, 320f, 180f), snapshot, history, state, settings, 2500 * 6);
            TestAssert.Equal(Find.WindowStack.windows.Count, 0, "history-backed draw opens no window");

            // Degenerate inputs must render their explanation instead of a number.
            var empty = Scan(new PowerScenario().Map, 0);
            panel.Draw(new Rect(0f, 0f, 1024f, 768f), empty, null, state, settings, 0);
            panel.Draw(new Rect(0f, 0f, 1024f, 768f), null, null, state, settings, 0);
            TestAssert.Equal(Find.WindowStack.windows.Count, 0, "an empty map renders without windows");

            settings.ForecastIncludeSelfDischarge = false;
            panel.Draw(new Rect(0f, 0f, 1024f, 768f), snapshot, history, state, settings, 2500 * 6);
            settings.ForecastIncludeSelfDischarge = true;

            // The network panel shows the same arithmetic for a single network.
            var networkPanel = new PowerNetworkPanel();
            networkPanel.Draw(new Rect(0f, 0f, 1024f, 768f), snapshot, state, settings, null);
            networkPanel.Draw(new Rect(0f, 0f, 320f, 180f), snapshot, state, settings, null);
            TestAssert.Equal(Find.WindowStack.windows.Count, 0, "the network panel renders without windows");
        }

        private static void AddSamples(PowerHistoryTracker history, int count, float generation, float consumption)
        {
            for (int i = 1; i <= count; i++)
            {
                history.MaybeSample(new PowerGridSnapshot
                {
                    Tick = i * 2500,
                    GenerationWatts = generation,
                    ConsumptionWatts = consumption,
                    StoredWattDays = 300f,
                    CapacityWattDays = 600f
                }, i * 2500, 2500, PowerTime.TicksPerDay * 7);
            }
        }

        // --- Read-only guarantee ------------------------------------------------

        private static void DoesNotTouchTheSimulation()
        {
            var settings = Settings();
            PowerNet net;
            var scenario = DrainingMap(out net);
            var scanner = new PowerGridScanner();
            var before = scanner.Scan(scenario.Map, 1000);
            float gainBefore = net.CurrentEnergyGainRate();
            float storedBefore = net.CurrentStoredEnergy();

            BatteryForecast.ForMap(before, -200f, 6, true);
            BatteryForecast.ForNetwork(before.Networks[0], true);
            new PowerGridManagerComponent(scenario.Map).ComputeForecast(PowerTimeRange.LastDay);
            new PowerDiagnosticsPanel();
            new PowerForecastPanel().Draw(new Rect(0f, 0f, 1024f, 768f), before, null, new PowerUiState(), settings,
                1000);

            var after = scanner.Scan(scenario.Map, 1001);
            TestAssert.Equal(net.CurrentEnergyGainRate(), gainBefore, "forecasting leaves the gain rate untouched");
            TestAssert.Equal(net.CurrentStoredEnergy(), storedBefore, "forecasting leaves stored energy untouched");
            TestAssert.Equal(after.GenerationWatts, before.GenerationWatts,
                "forecasting does not change reported generation");
            TestAssert.Equal(after.ConsumptionWatts, before.ConsumptionWatts,
                "forecasting does not change reported consumption");
            TestAssert.Equal(after.StoredWattDays, before.StoredWattDays,
                "forecasting does not change reported stored energy");
        }
    }
}
