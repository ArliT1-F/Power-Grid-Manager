// Phase 5 verification: the diagnostics engine, episode memory, throttling and the
// diagnostics tab. Everything runs against the game doubles, so it proves the logic and the
// headless draw path but not RimWorld API compatibility.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PowerGridManager.Tests
{
    public static class DiagnosticsTests
    {
        public static int Run()
        {
            TestAssert.UsePhase("phase5");
            int start = TestAssert.Checks;
            SettingsAndMuting();
            EpisodeTracking();
            NetworkAdvisories();
            BuildingAdvisories();
            BoundsAndToggles();
            ResolutionReporting();
            Notifications();
            PanelSmoke();
            Console.WriteLine("Phase 5 diagnostics checks passed: " + (TestAssert.Checks - start));
            return TestAssert.Checks - start;
        }

        // --- Helpers ----------------------------------------------------------

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

        private static List<PowerDiagnostic> Evaluate(PowerGridSnapshot snapshot, PowerGridManagerSettings settings,
            PowerDiagnosticTracker tracker, PowerHistoryTracker history = null)
        {
            var output = new List<PowerDiagnostic>();
            PowerDiagnostics.Evaluate(snapshot, history, settings, tracker, output);
            return output;
        }

        private static PowerDiagnostic FindAdvisory(List<PowerDiagnostic> list, PowerDiagnosticKind kind)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].Kind == kind) return list[i];
            return null;
        }

        private static int Count(List<PowerDiagnostic> list, PowerDiagnosticKind kind)
        {
            int count = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Kind == kind) count++;
            return count;
        }

        /// <summary>A network that is 200 W short with no storage.</summary>
        private static PowerScenario DeficitMap(out PowerNet net, out CompPowerTrader consumer)
        {
            var scenario = new PowerScenario();
            net = scenario.AddNet();
            var plant = scenario.AddBuilding(ThingDefFactory.Simple("plant"));
            scenario.AddTrader<CompPowerPlant>(net, plant, 1000f, true, -1000f);
            var user = scenario.AddBuilding(ThingDefFactory.Simple("drill"));
            consumer = scenario.AddTrader(net, user, -1200f, true, 1200f);
            return scenario;
        }

        private sealed class UnknownPower : CompPower { }

        // --- Settings ---------------------------------------------------------

        private static void SettingsAndMuting()
        {
            var settings = Settings();
            TestAssert.Check(settings.DiagnosticsEnabled, "diagnostics on by default");
            TestAssert.Equal(settings.DeficitAlertWatts, 50f, "default deficit threshold");
            TestAssert.Equal(settings.LowBatteryWarningFraction, 0.25f, "default low battery threshold");
            TestAssert.Equal(settings.PersistentDeficitTicks, PowerTime.TicksPerHour, "default persistence window");
            TestAssert.Equal(settings.GeneratorUnderOutputTicks, PowerTime.TicksPerDay, "default generator window");
            TestAssert.Equal(settings.AlertCooldownTicks, PowerTime.TicksPerDay, "default alert cooldown");
            TestAssert.Check(settings.ShowPowerMessages, "messages on by default");
            TestAssert.Check(settings.ForecastIncludeSelfDischarge, "self discharge counted by default");

            TestAssert.Equal(settings.MutedKindCount, 0, "nothing muted initially");
            settings.SetMuted(PowerDiagnosticKind.PersistentDeficit, true);
            settings.SetMuted(PowerDiagnosticKind.HighLoad, true);
            TestAssert.Check(settings.IsMuted(PowerDiagnosticKind.PersistentDeficit), "mute is recorded");
            TestAssert.Check(!settings.IsMuted(PowerDiagnosticKind.BatteryDischarge), "mute does not leak to other kinds");
            TestAssert.Equal(settings.MutedKindCount, 2, "muted count");
            settings.SetMuted(PowerDiagnosticKind.PersistentDeficit, false);
            TestAssert.Equal(settings.MutedKindCount, 1, "unmute is recorded");

            // A full save/load round trip: the game calls ExposeData once per mode, and the
            // PostLoadInit pass is what clamps values written by an older or newer build.
            var saved = Scribe.BeginSaving();
            settings.MutedDiagnosticKinds = ~0;
            settings.DeficitAlertWatts = -500f;
            settings.LowBatteryWarningFraction = 5f;
            settings.PersistentDeficitTicks = 1;
            settings.AlertCooldownTicks = -1;
            settings.ExposeData();
            Scribe.Finish();

            Scribe.BeginLoading(saved);
            settings.ExposeData();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            settings.ExposeData();
            Scribe.Finish();
            TestAssert.Equal(settings.MutedDiagnosticKinds, 0, "load clamps the mute mask to known kinds");
            TestAssert.Equal(settings.MutedKindCount, 0, "a clamped mask mutes nothing");
            TestAssert.Equal(settings.DeficitAlertWatts, 1f, "deficit threshold clamped");
            TestAssert.Equal(settings.LowBatteryWarningFraction, 0.99f, "low battery threshold clamped");
            TestAssert.Equal(settings.PersistentDeficitTicks, 60, "persistence window clamped");
            TestAssert.Equal(settings.AlertCooldownTicks, 0, "cooldown clamped");
        }

        // --- Episode memory ---------------------------------------------------

        private static void EpisodeTracking()
        {
            var scenario = new PowerScenario();
            var net = scenario.AddNet();
            var tracker = new PowerDiagnosticTracker();

            TestAssert.Equal(tracker.Register(net, PowerDiagnosticKind.PersistentDeficit, true, 0, 1000), 0,
                "first observation starts at zero");
            TestAssert.Check(tracker.IsNewEpisode(net, PowerDiagnosticKind.PersistentDeficit), "new episode flagged");
            TestAssert.Equal(tracker.Register(net, PowerDiagnosticKind.PersistentDeficit, true, 3000, 1000), 3000,
                "sustained duration");

            tracker.BeginEvaluation();
            tracker.MarkAnnounced(net, PowerDiagnosticKind.PersistentDeficit);
            TestAssert.Equal(tracker.Register(net, PowerDiagnosticKind.PersistentDeficit, false, 3100, 1000), 0,
                "cleared conditions report no duration");
            TestAssert.Check(!tracker.IsNewEpisode(net, PowerDiagnosticKind.PersistentDeficit),
                "episode is no longer new once it has been seen");
            TestAssert.Check(tracker.JustResolvedLong(net, PowerDiagnosticKind.PersistentDeficit, 1500),
                "an announced long episode reports that it resolved");
            TestAssert.Check(!tracker.JustResolvedLong(net, PowerDiagnosticKind.PersistentDeficit, 5000),
                "a long-enough threshold is required");

            tracker.EndEvaluation(null, 3100);
            TestAssert.Equal(tracker.TrackedEpisodeCount, 1, "resolved episode retained for reporting");

            // Returning inside the cooldown is the same episode; returning after it is a new one.
            int resumed = tracker.Register(net, PowerDiagnosticKind.PersistentDeficit, true, 3200, 1000);
            TestAssert.Check(!tracker.IsNewEpisode(net, PowerDiagnosticKind.PersistentDeficit),
                "return inside the cooldown is the same episode");
            TestAssert.Equal(resumed, 3200, "a resumed episode keeps its original start, so the duration spans the gap");

            tracker.Register(net, PowerDiagnosticKind.PersistentDeficit, false, 4000, 1000);
            tracker.Register(net, PowerDiagnosticKind.PersistentDeficit, true, 100000, 1000);
            TestAssert.Check(tracker.IsNewEpisode(net, PowerDiagnosticKind.PersistentDeficit),
                "return after the cooldown is a new episode");

            tracker.Reset();
            TestAssert.Equal(tracker.TrackedEpisodeCount, 0, "reset clears episodes");
            TestAssert.Equal(tracker.DismissedCount, 0, "reset clears dismissals");
        }

        // --- Network advisories ------------------------------------------------

        private static void NetworkAdvisories()
        {
            var settings = Settings();
            var tracker = new PowerDiagnosticTracker();

            PowerNet net;
            CompPowerTrader consumer;
            var scenario = DeficitMap(out net, out consumer);
            var first = Evaluate(Scan(scenario.Map, 0), settings, tracker);
            PowerDiagnostic deficit = FindAdvisory(first, PowerDiagnosticKind.PersistentDeficit);
            TestAssert.NotNull(deficit, "deficit is reported");
            TestAssert.Equal(deficit.DeficitWatts, 200f, "deficit magnitude");
            TestAssert.Equal(deficit.GenerationWatts, 1000f, "evidence carries generation");
            TestAssert.Equal(deficit.ConsumptionWatts, 1200f, "evidence carries consumption");
            TestAssert.Equal(deficit.AffectedCount, 1, "evidence carries the consumer count");
            TestAssert.Equal((int)deficit.Severity, (int)PowerDiagnosticSeverity.Warning,
                "a deficit is a warning until it persists");
            TestAssert.Same(deficit.Net, net, "advisory is bound to the live network");

            // Same condition one hour later: the configured persistence window has elapsed.
            var later = Evaluate(Scan(scenario.Map, PowerTime.TicksPerHour + 1), settings, tracker);
            PowerDiagnostic escalated = FindAdvisory(later, PowerDiagnosticKind.PersistentDeficit);
            TestAssert.NotNull(escalated, "deficit still reported");
            TestAssert.Equal((int)escalated.Severity, (int)PowerDiagnosticSeverity.Critical,
                "a deficit that outlasts the window becomes critical");

            // Muting removes it without touching the underlying reading.
            settings.SetMuted(PowerDiagnosticKind.PersistentDeficit, true);
            var muted = Evaluate(Scan(scenario.Map, PowerTime.TicksPerHour + 2), settings, tracker);
            TestAssert.Equal(Count(muted, PowerDiagnosticKind.PersistentDeficit), 0, "muted kinds are not listed");
            settings.SetMuted(PowerDiagnosticKind.PersistentDeficit, false);

            // A deficit below the configured threshold is noise, not an advisory.
            // A deficit below the configured threshold stops being an active fault. It is still
            // listed as resolved, so the player can see that the threshold, not the grid, changed.
            settings.DeficitAlertWatts = 500f;
            var ignored = Evaluate(Scan(scenario.Map, PowerTime.TicksPerHour + 3), settings, tracker);
            PowerDiagnostic ignoredAdvisory = FindAdvisory(ignored, PowerDiagnosticKind.PersistentDeficit);
            TestAssert.NotNull(ignoredAdvisory, "the episode is still visible after it stops qualifying");
            TestAssert.Check(!ignoredAdvisory.Ongoing, "a deficit below the threshold is no longer ongoing");
            settings.DeficitAlertWatts = 50f;

            // Batteries covering the gap are reported as discharge, not as a bare deficit.
            var withBattery = new PowerScenario();
            var batteryNet = withBattery.AddNet();
            var batteryPlant = withBattery.AddBuilding(ThingDefFactory.Simple("plant"));
            withBattery.AddTrader<CompPowerPlant>(batteryNet, batteryPlant, 1000f, true, -1000f);
            var batteryUser = withBattery.AddBuilding(ThingDefFactory.Simple("drill"));
            withBattery.AddTrader(batteryNet, batteryUser, -1200f, true, 1200f);
            var batteryBuilding = withBattery.AddBuilding(ThingDefFactory.Simple("battery"));
            withBattery.AddBattery(batteryNet, batteryBuilding, 300f, 600f);
            var dischargeTracker = new PowerDiagnosticTracker();
            var discharge = Evaluate(Scan(withBattery.Map, 0), settings, dischargeTracker);
            PowerDiagnostic draining = FindAdvisory(discharge, PowerDiagnosticKind.BatteryDischarge);
            TestAssert.NotNull(draining, "batteries covering a deficit are reported as discharge");
            TestAssert.Equal(Count(discharge, PowerDiagnosticKind.PersistentDeficit), 0,
                "a network with reserve is not reported as a bare deficit");
            TestAssert.Equal(draining.ChargeFraction.Value, 0.5f, "discharge evidence carries the charge fraction");
            TestAssert.NotNull(draining.DepletionTicks, "discharge evidence carries a depletion estimate");
            TestAssert.Equal((int)draining.Severity, (int)PowerDiagnosticSeverity.Warning,
                "a half-full draining network is a warning");
            TestAssert.Check(draining.DepletionTicks.Value > PowerTime.TicksPerDay,
                "a half-full network draining 200 W lasts more than a day");

            // The same network with a nearly empty bank is critical.
            var lowBattery = new PowerScenario();
            var lowNet = lowBattery.AddNet();
            var lowPlant = lowBattery.AddBuilding(ThingDefFactory.Simple("plant"));
            lowBattery.AddTrader<CompPowerPlant>(lowNet, lowPlant, 1000f, true, -1000f);
            var lowUser = lowBattery.AddBuilding(ThingDefFactory.Simple("drill"));
            lowBattery.AddTrader(lowNet, lowUser, -1200f, true, 1200f);
            var lowBank = lowBattery.AddBuilding(ThingDefFactory.Simple("battery"));
            lowBattery.AddBattery(lowNet, lowBank, 60f, 600f);
            var low = Evaluate(Scan(lowBattery.Map, 0), settings, new PowerDiagnosticTracker());
            PowerDiagnostic lowAdvisory = FindAdvisory(low, PowerDiagnosticKind.BatteryDischarge);
            TestAssert.NotNull(lowAdvisory, "a nearly empty draining network is reported");
            TestAssert.Equal((int)lowAdvisory.Severity, (int)PowerDiagnosticSeverity.Critical,
                "a nearly empty draining network is critical");

            // High load: generation still covers demand, so it is an advisory, not a fault.
            var loaded = new PowerScenario();
            var loadNet = loaded.AddNet();
            var loadPlant = loaded.AddBuilding(ThingDefFactory.Simple("plant"));
            loaded.AddTrader<CompPowerPlant>(loadNet, loadPlant, 1000f, true, -1000f);
            var loadUser = loaded.AddBuilding(ThingDefFactory.Simple("drill"));
            loaded.AddTrader(loadNet, loadUser, -950f, true, 950f);
            var loadTracker = new PowerDiagnosticTracker();
            var loadFirst = Evaluate(Scan(loaded.Map, 0), settings, loadTracker);
            PowerDiagnostic highLoad = FindAdvisory(loadFirst, PowerDiagnosticKind.HighLoad);
            TestAssert.NotNull(highLoad, "high load is reported");
            TestAssert.Equal(highLoad.DeficitWatts, 0.95f, "high load evidence carries the load ratio");
            TestAssert.Equal((int)highLoad.Severity, (int)PowerDiagnosticSeverity.Info,
                "brief high load is informational");
            var loadLater = Evaluate(Scan(loaded.Map, PowerTime.TicksPerHour + 1), settings, loadTracker);
            TestAssert.Equal((int)FindAdvisory(loadLater, PowerDiagnosticKind.HighLoad).Severity,
                (int)PowerDiagnosticSeverity.Warning, "sustained high load is a warning");

            // No generation at all: critical without a reserve, a warning while running on batteries.
            var dark = new PowerScenario();
            var darkNet = dark.AddNet();
            var darkUser = dark.AddBuilding(ThingDefFactory.Simple("lamp"));
            dark.AddTrader(darkNet, darkUser, -300f, true, 300f);
            var darkList = Evaluate(Scan(dark.Map, 0), settings, new PowerDiagnosticTracker());
            PowerDiagnostic noGeneration = FindAdvisory(darkList, PowerDiagnosticKind.NoGeneration);
            TestAssert.NotNull(noGeneration, "a network with consumers and no generation is reported");
            TestAssert.Equal((int)noGeneration.Severity, (int)PowerDiagnosticSeverity.Critical,
                "no generation without a reserve is critical");
            TestAssert.Equal(noGeneration.AffectedCount, 1, "consumer count is reported");

            var onBattery = new PowerScenario();
            var onNet = onBattery.AddNet();
            var onUser = onBattery.AddBuilding(ThingDefFactory.Simple("lamp"));
            onBattery.AddTrader(onNet, onUser, -300f, true, 300f);
            var onBank = onBattery.AddBuilding(ThingDefFactory.Simple("battery"));
            onBattery.AddBattery(onNet, onBank, 300f, 600f);
            var onList = Evaluate(Scan(onBattery.Map, 0), settings, new PowerDiagnosticTracker());
            PowerDiagnostic running = FindAdvisory(onList, PowerDiagnosticKind.NoGeneration);
            TestAssert.NotNull(running, "no generation with stored energy is still reported");
            TestAssert.Equal((int)running.Severity, (int)PowerDiagnosticSeverity.Warning,
                "running on batteries is a warning, not a critical");

            // A healthy network produces nothing at all.
            var healthy = new PowerScenario();
            var healthyNet = healthy.AddNet();
            var healthyPlant = healthy.AddBuilding(ThingDefFactory.Simple("plant"));
            healthy.AddTrader<CompPowerPlant>(healthyNet, healthyPlant, 1000f, true, -1000f);
            var healthyUser = healthy.AddBuilding(ThingDefFactory.Simple("lamp"));
            healthy.AddTrader(healthyNet, healthyUser, -200f, true, 200f);
            var healthyList = Evaluate(Scan(healthy.Map, 0), settings, new PowerDiagnosticTracker());
            TestAssert.Equal(healthyList.Count, 0, "a healthy network raises no advisory");
        }

        // --- Building advisories ----------------------------------------------

        private static void BuildingAdvisories()
        {
            var settings = Settings();
            settings.GeneratorUnderOutputTicks = 1000;

            // Isolated consumer.
            var isolatedMap = new PowerScenario();
            var orphan = isolatedMap.AddBuilding(ThingDefFactory.Simple("cooler"));
            isolatedMap.AddTrader(null, orphan, -200f, true, 200f);
            var isolated = Evaluate(Scan(isolatedMap.Map, 0), settings, new PowerDiagnosticTracker());
            PowerDiagnostic orphanAdvisory = FindAdvisory(isolated, PowerDiagnosticKind.IsolatedConsumer);
            TestAssert.NotNull(orphanAdvisory, "a consumer with no network is reported");
            TestAssert.Equal((int)orphanAdvisory.Severity, (int)PowerDiagnosticSeverity.Warning,
                "an isolated consumer is a warning");
            TestAssert.Equal(orphanAdvisory.ConfiguredWatts, 200f, "the potential draw is reported");
            TestAssert.Same(orphanAdvisory.Building, orphan, "the advisory points at the building");

            // Generator far below its configured output, but only after it has stayed that way.
            var idle = new PowerScenario();
            var idleNet = idle.AddNet();
            var idlePlantBuilding = idle.AddBuilding(ThingDefFactory.Simple("solar"));
            var idlePlant = idle.AddTrader<CompPowerPlant>(idleNet, idlePlantBuilding, 100f, true, -1000f);
            var idleUser = idle.AddBuilding(ThingDefFactory.Simple("lamp"));
            idle.AddTrader(idleNet, idleUser, -50f, true, 50f);
            var idleTracker = new PowerDiagnosticTracker();
            var early = Evaluate(Scan(idle.Map, 0), settings, idleTracker);
            TestAssert.Equal(Count(early, PowerDiagnosticKind.GeneratorUnderOutput), 0,
                "a generator is not reported the moment it dips");
            var sustained = Evaluate(Scan(idle.Map, 1500), settings, idleTracker);
            PowerDiagnostic idleAdvisory = FindAdvisory(sustained, PowerDiagnosticKind.GeneratorUnderOutput);
            TestAssert.NotNull(idleAdvisory, "a generator is reported after it stays low");
            TestAssert.Equal(idleAdvisory.ConfiguredWatts, 1000f, "configured output is reported");
            TestAssert.Equal(idleAdvisory.MeasuredWatts, 100f, "measured output is reported");

            // A switched-off generator producing nothing is expected, not a fault.
            idle.AddFlickable(idlePlantBuilding, false);
            var switchedOff = Evaluate(Scan(idle.Map, 1600), settings, idleTracker);
            PowerDiagnostic stopped = FindAdvisory(switchedOff, PowerDiagnosticKind.GeneratorUnderOutput);
            TestAssert.Check(stopped == null || !stopped.Ongoing,
                "a switched-off generator is no longer an active advisory");

            // Switched off but still drawing.
            var ghost = new PowerScenario();
            var ghostNet = ghost.AddNet();
            var ghostPlant = ghost.AddBuilding(ThingDefFactory.Simple("plant"));
            ghost.AddTrader<CompPowerPlant>(ghostNet, ghostPlant, 1000f, true, -1000f);
            var ghostBuilding = ghost.AddBuilding(ThingDefFactory.Simple("modded"));
            ghost.AddTrader(ghostNet, ghostBuilding, -100f, true, 100f);
            ghost.AddFlickable(ghostBuilding, false);
            var ghostList = Evaluate(Scan(ghost.Map, 0), settings, new PowerDiagnosticTracker());
            PowerDiagnostic ghostAdvisory = FindAdvisory(ghostList, PowerDiagnosticKind.SwitchedOffButDrawing);
            TestAssert.NotNull(ghostAdvisory, "a switched-off building that still draws is reported");
            TestAssert.Equal(ghostAdvisory.MeasuredWatts, 100f, "the reported draw is the measured one");

            // Draw well above the configured draw: informational, and suppressed when info is off.
            var hungry = new PowerScenario();
            var hungryNet = hungry.AddNet();
            var hungryPlant = hungry.AddBuilding(ThingDefFactory.Simple("plant"));
            hungry.AddTrader<CompPowerPlant>(hungryNet, hungryPlant, 1000f, true, -1000f);
            var hungryBuilding = hungry.AddBuilding(ThingDefFactory.Simple("variable"));
            hungry.AddTrader(hungryNet, hungryBuilding, -300f, true, 100f);
            var hungryList = Evaluate(Scan(hungry.Map, 0), settings, new PowerDiagnosticTracker());
            PowerDiagnostic above = FindAdvisory(hungryList, PowerDiagnosticKind.DrawAboveConfigured);
            TestAssert.NotNull(above, "a draw well above the configured draw is reported");
            TestAssert.Equal((int)above.Severity, (int)PowerDiagnosticSeverity.Info,
                "an above-configured draw is informational");
            settings.ShowInfoDiagnostics = false;
            var noInfo = Evaluate(Scan(hungry.Map, 1), settings, new PowerDiagnosticTracker());
            TestAssert.Equal(Count(noInfo, PowerDiagnosticKind.DrawAboveConfigured), 0,
                "informational advisories can be turned off");
            settings.ShowInfoDiagnostics = true;

            // Unsupported power component.
            var odd = new PowerScenario();
            var oddNet = odd.AddNet();
            var oddBuilding = odd.AddBuilding(ThingDefFactory.Simple("odd"));
            var oddComp = new UnknownPower();
            oddComp.parent = oddBuilding;
            oddComp.PowerNet = oddNet;
            oddBuilding.AllComps.Add(oddComp);
            oddBuilding.Spawned = true;
            odd.Map.listerBuildings.allBuildingsColonist.Add(oddBuilding);
            oddNet.transmitters.Add(oddComp);
            var oddList = Evaluate(Scan(odd.Map, 0), settings, new PowerDiagnosticTracker());
            PowerDiagnostic unsupported = FindAdvisory(oddList, PowerDiagnosticKind.UnsupportedComponent);
            TestAssert.NotNull(unsupported, "an unrecognised power component is reported");
            TestAssert.Equal((int)unsupported.Severity, (int)PowerDiagnosticSeverity.Info,
                "an unsupported component is informational");
        }

        // --- Bounds and toggles ------------------------------------------------

        private static void BoundsAndToggles()
        {
            var settings = Settings();

            // Diagnostics are opt-out; monitoring keeps working without them.
            PowerNet net;
            CompPowerTrader consumer;
            var scenario = DeficitMap(out net, out consumer);
            var tracker = new PowerDiagnosticTracker();
            Evaluate(Scan(scenario.Map, 0), settings, tracker);
            TestAssert.Check(tracker.TrackedEpisodeCount > 0, "episodes tracked while diagnostics are on");

            settings.DiagnosticsEnabled = false;
            var off = Evaluate(Scan(scenario.Map, 100), settings, tracker);
            TestAssert.Equal(off.Count, 0, "diagnostics off produces no advisories");
            TestAssert.Equal(tracker.TrackedEpisodeCount, 0, "diagnostics off releases episode memory");
            settings.DiagnosticsEnabled = true;

            // Many failing networks must not produce an unbounded list.
            var crowded = new PowerScenario();
            for (int i = 0; i < 90; i++)
            {
                var crowdedNet = crowded.AddNet();
                var plant = crowded.AddBuilding(ThingDefFactory.Simple("plant"));
                crowded.AddTrader<CompPowerPlant>(crowdedNet, plant, 1000f, true, -1000f);
                var user = crowded.AddBuilding(ThingDefFactory.Simple("drill"));
                crowded.AddTrader(crowdedNet, user, -1200f, true, 1200f);
            }
            var crowdedList = Evaluate(Scan(crowded.Map, 0), settings, new PowerDiagnosticTracker());
            TestAssert.Equal(crowdedList.Count, PowerDiagnostics.MaxAdvisories,
                "the advisory list is capped");

            // Dismissal hides one advisory for the rest of the session.
            var dismissMap = DeficitMap(out net, out consumer);
            var dismissTracker = new PowerDiagnosticTracker();
            var before = Evaluate(Scan(dismissMap.Map, 0), settings, dismissTracker);
            TestAssert.Equal(before.Count, 1, "one advisory before dismissing");
            dismissTracker.Dismiss(before[0], 0);
            var after = Evaluate(Scan(dismissMap.Map, 100), settings, dismissTracker);
            TestAssert.Equal(after.Count, 0, "a dismissed advisory is hidden");
            TestAssert.Equal(dismissTracker.DismissedCount, 1, "dismissal is remembered");
            dismissTracker.ClearDismissals();
            var restored = Evaluate(Scan(dismissMap.Map, 200), settings, dismissTracker);
            TestAssert.Equal(restored.Count, 1, "restoring hidden advisories brings it back");
        }

        // --- Resolution --------------------------------------------------------

        private static void ResolutionReporting()
        {
            var settings = Settings();
            PowerNet net;
            CompPowerTrader consumer;
            var scenario = DeficitMap(out net, out consumer);
            var tracker = new PowerDiagnosticTracker();

            var failing = Evaluate(Scan(scenario.Map, 0), settings, tracker);
            TestAssert.Equal(failing.Count, 1, "advisory while the deficit is live");
            TestAssert.Check(failing[0].Ongoing, "the advisory is ongoing");
            TestAssert.Equal(failing[0].ResolvedTick, -1, "an ongoing advisory has no resolution tick");

            // Fix the network: the consumer stops drawing.
            consumer.PowerOutput = 0f;
            var cleared = Evaluate(Scan(scenario.Map, 1000), settings, tracker);
            TestAssert.Equal(cleared.Count, 1, "a resolved advisory is still listed");
            TestAssert.Check(!cleared[0].Ongoing, "the resolved advisory is marked resolved");
            TestAssert.Equal(cleared[0].ResolvedTick, 1000, "the resolution tick is recorded");
            TestAssert.Equal(cleared[0].Kind, PowerDiagnosticKind.PersistentDeficit, "the resolved kind is preserved");
            TestAssert.Equal(cleared[0].DeficitWatts, 200f, "the resolved advisory keeps its evidence");
            TestAssert.Same(cleared[0].Net, net, "the resolved advisory keeps its network binding");

            // Long after the fact it stops being listed.
            var forgotten = Evaluate(Scan(scenario.Map,
                1000 + PowerDiagnosticTracker.ResolvedRetentionTicks + 1), settings, tracker);
            TestAssert.Equal(forgotten.Count, 0, "resolved advisories expire");
        }

        // --- Notifications -----------------------------------------------------

        private static void Notifications()
        {
            var settings = Settings();
            settings.AlertCooldownTicks = PowerTime.TicksPerDay;

            var scenario = new PowerScenario();
            for (int i = 0; i < 3; i++)
            {
                var net = scenario.AddNet();
                var plant = scenario.AddBuilding(ThingDefFactory.Simple("plant"));
                scenario.AddTrader<CompPowerPlant>(net, plant, 1000f, true, -1000f);
                var user = scenario.AddBuilding(ThingDefFactory.Simple("drill"));
                scenario.AddTrader(net, user, -1200f, true, 1200f);
            }
            var tracker = new PowerDiagnosticTracker();
            var advisories = Evaluate(Scan(scenario.Map, 0), settings, tracker);
            TestAssert.Equal(advisories.Count, 3, "three deficit advisories");

            Messages.Clear();
            PowerDiagnosticNotifier.Notify(advisories, settings, tracker, 0);
            TestAssert.Equal(Messages.Sent.Count, PowerDiagnosticNotifier.MaxMessagesPerPass,
                "at most two messages per refresh");

            Messages.Clear();
            var second = Evaluate(Scan(scenario.Map, 10), settings, tracker);
            PowerDiagnosticNotifier.Notify(second, settings, tracker, 10);
            TestAssert.Equal(Messages.Sent.Count, 0, "the same episode is not announced twice");

            Messages.Clear();
            settings.ShowPowerMessages = false;
            var quiet = Evaluate(Scan(scenario.Map, 20), settings, new PowerDiagnosticTracker());
            PowerDiagnosticNotifier.Notify(quiet, settings, new PowerDiagnosticTracker(), 20);
            TestAssert.Equal(Messages.Sent.Count, 0, "messages can be turned off");
            settings.ShowPowerMessages = true;

            // Informational advisories are never announced.
            Messages.Clear();
            var infoList = new List<PowerDiagnostic>
            {
                new PowerDiagnostic
                {
                    Kind = PowerDiagnosticKind.DrawAboveConfigured,
                    Severity = PowerDiagnosticSeverity.Info,
                    NetworkId = 1,
                    Ongoing = true
                }
            };
            PowerDiagnosticNotifier.Notify(infoList, settings, new PowerDiagnosticTracker(), 30);
            TestAssert.Equal(Messages.Sent.Count, 0, "informational advisories are not announced");

            // Resolved advisories are not announced either.
            Messages.Clear();
            var resolvedList = new List<PowerDiagnostic>
            {
                new PowerDiagnostic
                {
                    Kind = PowerDiagnosticKind.PersistentDeficit,
                    Severity = PowerDiagnosticSeverity.Critical,
                    NetworkId = 1,
                    Ongoing = false
                }
            };
            PowerDiagnosticNotifier.Notify(resolvedList, settings, new PowerDiagnosticTracker(), 40);
            TestAssert.Equal(Messages.Sent.Count, 0, "resolved advisories are not announced");

            // Nothing is queued before the game is actually playing.
            Messages.Clear();
            ProgramState previous = Current.ProgramState;
            Current.ProgramState = ProgramState.MapInitializing;
            var startup = Evaluate(Scan(scenario.Map, 50), settings, new PowerDiagnosticTracker());
            PowerDiagnosticNotifier.Notify(startup, settings, new PowerDiagnosticTracker(), 50);
            Current.ProgramState = previous;
            TestAssert.Equal(Messages.Sent.Count, 0, "no messages before the game is playing");
            Messages.Clear();
        }

        // --- Panel -------------------------------------------------------------

        private static void PanelSmoke()
        {
            var settings = Settings();
            PowerNet net;
            CompPowerTrader consumer;
            var scenario = DeficitMap(out net, out consumer);
            var tracker = new PowerDiagnosticTracker();
            Evaluate(Scan(scenario.Map, 0), settings, tracker);
            var snapshot = Scan(scenario.Map, PowerTime.TicksPerHour + 1);
            var advisories = Evaluate(snapshot, settings, tracker);
            TestAssert.Equal((int)advisories[0].Severity, (int)PowerDiagnosticSeverity.Critical,
                "the sample advisory is critical");
            var panel = new PowerDiagnosticsPanel();
            var state = new PowerUiState();
            int shown = -1;

            var sizes = new[] { 320f, 1024f, 2560f };
            foreach (float width in sizes)
            {
                WidgetsQueue.Reset();
                panel.Draw(new Rect(0f, 0f, width, Math.Min(width, 1400f)), snapshot, state, settings, tracker,
                    advisories, id => shown = id);
                TestAssert.Equal(Find.WindowStack.windows.Count, 0, "a plain draw opens no window at " + width);
            }
            TestAssert.Equal(panel.VisibleCount, 1, "the advisory is visible");

            state.DiagnosticView = DiagnosticFilter.ResolvedOnly;
            panel.Draw(new Rect(0f, 0f, 1024f, 768f), snapshot, state, settings, tracker, advisories, null);
            TestAssert.Equal(panel.VisibleCount, 0, "a resolved-only filter hides an ongoing advisory");

            state.DiagnosticView = DiagnosticFilter.CriticalOnly;
            panel.Draw(new Rect(0f, 0f, 1024f, 768f), snapshot, state, settings, tracker, advisories, null);
            TestAssert.Equal(panel.VisibleCount, 1, "a critical-only filter keeps the critical advisory");

            state.DiagnosticView = DiagnosticFilter.All;

            // The filter button (first text button in the header) opens a float menu.
            WidgetsQueue.Reset();
            WidgetsQueue.ClickTextAt(0);
            panel.Draw(new Rect(0f, 0f, 1024f, 768f), snapshot, state, settings, tracker, advisories, null);
            TestAssert.Equal(Find.WindowStack.windows.Count, 1, "the filter button opens a menu");
            Find.WindowStack.windows.Clear();

            // Row buttons, in draw order: mute, dismiss, locate, show buildings.
            WidgetsQueue.Reset();
            WidgetsQueue.ClickTextAt(1);
            panel.Draw(new Rect(0f, 0f, 1024f, 768f), snapshot, state, settings, tracker, advisories, null);
            TestAssert.Check(settings.IsMuted(PowerDiagnosticKind.PersistentDeficit), "the mute button mutes the kind");
            settings.SetMuted(PowerDiagnosticKind.PersistentDeficit, false);

            WidgetsQueue.Reset();
            WidgetsQueue.ClickTextAt(2);
            panel.Draw(new Rect(0f, 0f, 1024f, 768f), snapshot, state, settings, tracker, advisories, null);
            TestAssert.Equal(tracker.DismissedCount, 1, "the dismiss button dismisses the advisory");

            // With a dismissal recorded the restore button appears as the second header button,
            // which shifts every row button along by one.
            WidgetsQueue.Reset();
            WidgetsQueue.ClickTextAt(1);
            panel.Draw(new Rect(0f, 0f, 1024f, 768f), snapshot, state, settings, tracker, advisories, null);
            TestAssert.Equal(tracker.DismissedCount, 0, "the restore button clears dismissals");

            WidgetsQueue.Reset();
            WidgetsQueue.ClickTextAt(3);
            panel.Draw(new Rect(0f, 0f, 1024f, 768f), snapshot, state, settings, tracker, advisories, null);
            TestAssert.Equal(Find.WindowStack.windows.Count, 0, "locating does not open a window");

            WidgetsQueue.Reset();
            WidgetsQueue.ClickTextAt(4);
            shown = -1;
            panel.Draw(new Rect(0f, 0f, 1024f, 768f), snapshot, state, settings, tracker, advisories, id => shown = id);
            TestAssert.Equal(shown, 1, "the show-buildings button selects the advisory's network");

            // Empty states must still render, and diagnostics-off must say so.
            panel.Draw(new Rect(0f, 0f, 320f, 180f), snapshot, state, settings, tracker, new List<PowerDiagnostic>(),
                null);
            settings.DiagnosticsEnabled = false;
            panel.Draw(new Rect(0f, 0f, 1024f, 768f), snapshot, state, settings, tracker, new List<PowerDiagnostic>(),
                null);
            settings.DiagnosticsEnabled = true;
            panel.Draw(new Rect(0f, 0f, 1024f, 768f), null, state, null, null, null, null);
            TestAssert.Equal(Find.WindowStack.windows.Count, 0, "degenerate input opens no window");

            // The settings window gained a diagnostics, forecast and mute section; it must draw
            // without throwing and without opening a window.
            var mod = new PowerGridManagerMod(new ModContentPack());
            mod.DoSettingsWindowContents(new Rect(0f, 0f, 620f, 720f));
            mod.DoSettingsWindowContents(new Rect(0f, 0f, 320f, 400f));
            TestAssert.Equal(Find.WindowStack.windows.Count, 0, "the settings window opens no window");

            // The overview tiles wired to diagnostics and the forecast must render too.
            var forecast = BatteryForecast.ForMap(snapshot, null, 0, true);
            var overview = new PowerOverviewPanel();
            overview.Draw(new Rect(0f, 0f, 1024f, 768f), snapshot, 1000, settings, forecast, 1, 2);
            overview.Draw(new Rect(0f, 0f, 320f, 180f), snapshot, 1000, settings, null, 0, 0);
            TestAssert.Equal(Find.WindowStack.windows.Count, 0, "the overview renders without windows");
        }
    }
}
