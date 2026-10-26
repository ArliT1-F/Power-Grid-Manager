// Phase 7 verification: the optional control layer. Everything runs against the game
// doubles, so it proves the decision logic, persistence and headless UI paths, not
// RimWorld binary compatibility or in-game rendering.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PowerGridManager.Tests
{
    public static class ManagementTests
    {
        public static int Run()
        {
            TestAssert.UsePhase("phase7");
            int start = TestAssert.Checks;
            SettingsAndDefaults();
            ControlApi();
            RegistryPersistence();
            EventLogBounds();
            DecisionLogic();
            HysteresisAndRecovery();
            ControllerIntegration();
            ComponentSerialization();
            PanelSmoke();
            Console.WriteLine("Phase 7 management checks passed: " + (TestAssert.Checks - start));
            return TestAssert.Checks - start;
        }

        // --- Helpers ----------------------------------------------------------

        private static PowerGridManagerSettings Settings(bool management = true, bool automation = true)
        {
            var settings = new PowerGridManagerSettings
            {
                ManagementEnabled = management,
                LoadSheddingEnabled = automation,
                LoadShedStableTicks = 0,
                LoadShedCooldownTicks = 0,
                LoadShedMaxActionsPerPass = 4,
                LoadShedRecovery = LoadSheddingRecoveryMode.All
            };
            PowerGridManagerMod.Settings = settings;
            return settings;
        }

        private static PowerGridSnapshot Scan(Map map, int tick)
        {
            return new PowerGridScanner().Scan(map, tick);
        }

        private static Building Consumer(PowerScenario scenario, PowerNet net, string name, float output,
            float nominal, out CompFlickable flick)
        {
            Building building = scenario.AddBuilding(ThingDefFactory.Simple(name));
            scenario.AddTrader(net, building, output, true, nominal);
            flick = scenario.AddFlickable(building, true);
            return building;
        }

        /// <summary>One network with 100 W generation, a 20/100 Wd battery, and optional consumers.</summary>
        private static PowerScenario BuildScenario(out PowerNet net, out CompPowerBattery battery)
        {
            var scenario = new PowerScenario();
            net = scenario.AddNet();
            Building generator = scenario.AddBuilding(ThingDefFactory.Simple("generator"));
            scenario.AddTrader<CompPowerPlant>(net, generator, 100f, true, -100f);
            Building batteryBuilding = scenario.AddBuilding(ThingDefFactory.Simple("battery"));
            battery = scenario.AddBattery(net, batteryBuilding, 20f, 100f);
            return scenario;
        }

        private static void MarkSheddable(PowerManagementRegistry registry, Building building, int priority,
            int tick = 0)
        {
            registry.SetSheddable(building, true, tick);
            registry.SetPriority(building, priority, tick);
        }

        private static LoadSheddingState FreshState()
        {
            return new LoadSheddingState();
        }

        private static SaveNode Save(IExposable exposable)
        {
            SaveNode node = Scribe.BeginSaving();
            exposable.ExposeData();
            Scribe.Finish();
            return node;
        }

        private static void Load(IExposable exposable, SaveNode node)
        {
            Scribe.BeginLoading(node);
            exposable.ExposeData();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            exposable.ExposeData();
            Scribe.Finish();
        }

        // --- Settings ---------------------------------------------------------

        private static void SettingsAndDefaults()
        {
            var defaults = new PowerGridManagerSettings();
            TestAssert.Check(!defaults.ManagementEnabled, "management is off by default");
            TestAssert.Check(!defaults.LoadSheddingEnabled, "load shedding is off by default");
            TestAssert.Check(defaults.LoadShedRestoreThreshold > defaults.LoadShedLowThreshold,
                "recovery threshold is above the shed threshold by default");
            TestAssert.Equal(defaults.LoadShedRecovery, LoadSheddingRecoveryMode.Staged,
                "staged recovery is the default");
            TestAssert.Check(defaults.LoadShedRequireDeficit, "deficit is required by default");

            var settings = Settings();
            settings.LoadShedLowThreshold = 0.99f;
            settings.LoadShedRestoreThreshold = 0.01f;
            settings.LoadShedMaxActionsPerPass = 900;
            settings.LoadShedStableTicks = -10;
            settings.LoadShedRecovery = (LoadSheddingRecoveryMode)99;
            settings.NormalizeManagement();
            TestAssert.Check(settings.LoadShedLowThreshold <= 0.90f, "low threshold is clamped");
            TestAssert.Check(settings.LoadShedRestoreThreshold > settings.LoadShedLowThreshold,
                "hysteresis band cannot be inverted");
            TestAssert.Equal(settings.LoadShedMaxActionsPerPass, 32, "action batch is bounded");
            TestAssert.Equal(settings.LoadShedStableTicks, 0, "negative stability is clamped");
            TestAssert.Equal(settings.LoadShedRecovery, LoadSheddingRecoveryMode.Staged,
                "unknown recovery enum falls back to staged");

            settings.ManagementEnabled = true;
            settings.LoadSheddingEnabled = false;
            settings.LoadShedLowThreshold = 0.12f;
            settings.LoadShedRecovery = LoadSheddingRecoveryMode.Manual;
            SaveNode node = Save(settings);
            var loaded = new PowerGridManagerSettings();
            Load(loaded, node);
            TestAssert.Check(loaded.ManagementEnabled, "management setting survives save/load");
            TestAssert.Check(!loaded.LoadSheddingEnabled, "automation setting survives save/load");
            TestAssert.Equal(loaded.LoadShedLowThreshold, 0.12f, "threshold survives save/load");
            TestAssert.Equal(loaded.LoadShedRecovery, LoadSheddingRecoveryMode.Manual,
                "recovery enum survives save/load");

            // A save written by an older mod version has none of the new nodes.
            SaveNode legacy = Scribe.BeginSaving();
            Scribe.Node.values["refreshIntervalTicks"] = 240;
            Scribe.Finish();
            var legacySettings = new PowerGridManagerSettings();
            Load(legacySettings, legacy);
            TestAssert.Check(!legacySettings.ManagementEnabled && !legacySettings.LoadSheddingEnabled,
                "missing management nodes load as disabled");
            TestAssert.Equal(legacySettings.LoadShedLowThreshold, 0.25f, "missing threshold uses default");

            // Monitoring settings keep working independently of management.
            legacySettings.HistoryEnabled = true;
            legacySettings.DiagnosticsEnabled = true;
            TestAssert.Check(legacySettings.HistoryEnabled && legacySettings.DiagnosticsEnabled,
                "history and diagnostics do not depend on management");
        }

        // --- Control API ------------------------------------------------------

        private static void ControlApi()
        {
            var scenario = new PowerScenario();
            PowerNet net = scenario.AddNet();
            CompFlickable flick;
            Building building = Consumer(scenario, net, "lamp", -50f, 50f, out flick);
            PowerGridSnapshot snapshot = Scan(scenario.Map, 0);
            BuildingPowerSnapshot row = snapshot.Buildings[0];
            var registry = new PowerManagementRegistry();

            PowerControlCapability capability = BuildingPowerControl.Evaluate(row, registry);
            TestAssert.Equal(capability, PowerControlCapability.Controllable,
                "a vanilla flickable colonist consumer is controllable");
            TestAssert.Check(BuildingPowerControl.CanControl(row, registry), "capability helper agrees");

            string detail;
            PowerControlResult result = BuildingPowerControl.SetSwitch(building, false, out detail);
            TestAssert.Equal(result, PowerControlResult.Success, "switch off succeeds");
            TestAssert.Check(!flick.SwitchIsOn, "switch is actually off");
            TestAssert.Check(!flick.WantsSwitchOn, "the private desired state was synchronised");
            TestAssert.Check(!flick.WantsFlick(), "no flick designation is left pending");
            TestAssert.Check(!scenario.Map.flickDesignations.Contains(building),
                "no stale flick designation remains");
            TestAssert.Equal(FlickUtility.WantsToBeOn(building), false, "vanilla wants-to-be-on agrees");

            result = BuildingPowerControl.SetSwitch(building, true, out detail);
            TestAssert.Equal(result, PowerControlResult.Success, "switch on succeeds");
            TestAssert.Check(flick.SwitchIsOn && flick.WantsSwitchOn, "switch and desired state are both on");
            result = BuildingPowerControl.SetSwitch(building, true, out detail);
            TestAssert.Equal(result, PowerControlResult.AlreadyInState, "repeating a state is a no-op");

            // A queued player flick wins.
            flick.SwitchIsOn = false;
            flick.SetWantsSwitchOn(true);
            result = BuildingPowerControl.SetSwitch(building, true, out detail);
            TestAssert.Equal(result, PowerControlResult.PendingFlick, "pending player flick is respected");
            TestAssert.Check(!flick.SwitchIsOn, "pending flick is not overridden");
            flick.SetWantsSwitchOn(false);

            // Non-player buildings expose no toggle gizmo.
            building.factionInt = new Faction();
            result = BuildingPowerControl.SetSwitch(building, true, out detail);
            TestAssert.Equal(result, PowerControlResult.NoSupportedToggle,
                "a building without a public toggle is refused");
            building.factionInt = Faction.OfPlayer;

            // Destroyed and non-flickable buildings.
            Building plain = scenario.AddBuilding(ThingDefFactory.Simple("wall"));
            TestAssert.Equal(BuildingPowerControl.SetSwitch(plain, false, out detail),
                PowerControlResult.NoFlickable, "a building without a flick comp is refused");
            building.Destroyed = true;
            TestAssert.Equal(BuildingPowerControl.SetSwitch(building, false, out detail),
                PowerControlResult.NotSpawned, "a destroyed building is refused");
            building.Destroyed = false;

            // Capability reasons from the snapshot.
            snapshot = Scan(scenario.Map, 1);
            row = null;
            for (int i = 0; i < snapshot.Buildings.Count; i++)
                if (snapshot.Buildings[i].Building == building) row = snapshot.Buildings[i];
            TestAssert.NotNull(row, "controlled building is still in the snapshot");
            registry.SetExcludedFromManagement(building, true, 1);
            TestAssert.Equal(BuildingPowerControl.Evaluate(row, registry),
                PowerControlCapability.ExcludedByUser, "user exclusion wins");
            registry.SetExcludedFromManagement(building, false, 1);
            TestAssert.Equal(CompatibilityRegistry.EvaluateControl(row, registry),
                PowerControlSupport.Supported, "vanilla control support is reported");

            // Modded flick implementations: exactly one toggle is accepted, two are ambiguous,
            // and a throwing implementation is reported once rather than crashing the tick.
            var modded = new PowerScenario();
            PowerNet moddedNet = modded.AddNet();
            Building customBuilding = modded.AddBuilding(ThingDefFactory.Simple("modded lamp"));
            modded.AddTrader(moddedNet, customBuilding, -60f, true, 60f);
            var customFlick = new ModdedFlick { parent = customBuilding };
            customBuilding.AllComps.Add(customFlick);
            result = BuildingPowerControl.SetSwitch(customBuilding, false, out detail);
            TestAssert.Equal(result, PowerControlResult.Success, "modded flick with one toggle is controlled");
            TestAssert.Check(!customFlick.SwitchIsOn, "modded switch changed");

            customFlick.DoubleToggles = true;
            result = BuildingPowerControl.SetSwitch(customBuilding, true, out detail);
            TestAssert.Equal(result, PowerControlResult.AmbiguousToggle, "ambiguous toggles are refused");
            TestAssert.Check(!customFlick.SwitchIsOn, "ambiguous building is left untouched");

            int warnings = Log.Warnings.Count;
            customFlick.ThrowOnGizmos = true;
            result = BuildingPowerControl.SetSwitch(customBuilding, true, out detail);
            TestAssert.Equal(result, PowerControlResult.ControlFailed, "throwing gizmo is contained");
            TestAssert.Check(Log.Warnings.Count > warnings, "the incompatibility is logged once");
            Log.Warnings.Clear();
        }

        private sealed class ModdedFlick : CompFlickable
        {
            public bool DoubleToggles;
            public bool ThrowOnGizmos;

            private Command_Toggle MakeToggle()
            {
                return new Command_Toggle
                {
                    isActive = () => WantsSwitchOn,
                    toggleAction = () => SetWantsSwitchOn(!WantsSwitchOn)
                };
            }

            public override IEnumerable<Gizmo> CompGetGizmosExtra()
            {
                if (ThrowOnGizmos) throw new InvalidOperationException("broken modded gizmo");
                yield return MakeToggle();
                if (DoubleToggles) yield return MakeToggle();
            }
        }

        // --- Registry ---------------------------------------------------------

        private static void RegistryPersistence()
        {
            var scenario = new PowerScenario();
            PowerNet net = scenario.AddNet();
            CompFlickable flickA, flickB;
            Building a = Consumer(scenario, net, "cooler", -200f, 200f, out flickA);
            Building b = Consumer(scenario, net, "lamp", -40f, 40f, out flickB);
            string idA = PowerManagementRegistry.StableId(a);
            TestAssert.Check(!string.IsNullOrEmpty(idA), "stable save ID is available");
            TestAssert.Check(idA.StartsWith("Thing_", StringComparison.Ordinal),
                "stable ID is the game's own load-reference ID");

            var registry = new PowerManagementRegistry();
            TestAssert.Check(registry.AddGroup("Freezers", 2), "group created");
            TestAssert.Check(!registry.AddGroup("Freezers", 3), "duplicate group rejected");
            TestAssert.Check(registry.AddGroup("Workshop", 5), "second group created");
            registry.SetGroup(a, "Freezers", 100);
            registry.SetSheddable(a, true, 100);
            registry.SetPriority(a, 99, 100);
            registry.SetExcludedFromDiagnostics(b, true, 100);
            TestAssert.Equal(registry.Count, 2, "two configured rows");
            TestAssert.Equal(registry.PriorityOf(a), PowerManagementRegistry.MaxPriority,
                "priority is clamped");
            TestAssert.Check(registry.IsSheddable(a), "sheddable flag set");
            TestAssert.Check(registry.IsExcludedFromDiagnostics(b), "diagnostics exclusion set");
            TestAssert.Check(!registry.IsSheddable(b), "unmarked building is protected");

            SaveNode node = Save(registry);
            var loaded = new PowerManagementRegistry();
            Load(loaded, node);
            TestAssert.Equal(loaded.Count, 2, "rows survive save/load");
            TestAssert.Equal(loaded.GroupCount, 2, "groups survive save/load");
            TestAssert.Check(loaded.IsSheddable(a), "sheddable status is bound to the stable ID");
            TestAssert.Equal(loaded.PriorityOf(a), PowerManagementRegistry.MaxPriority,
                "priority survives save/load");
            TestAssert.Check(loaded.IsExcludedFromDiagnostics(b), "exclusion survives save/load");
            TestAssert.Equal(loaded.GroupPriority("Freezers"), 2, "group priority survives");

            // A new Building object with the same save ID (a reloaded map) resolves to the row.
            var rebuiltScenario = new PowerScenario();
            PowerNet rebuiltNet = rebuiltScenario.AddNet();
            Building reloaded = rebuiltScenario.AddBuilding(ThingDefFactory.Simple("cooler"));
            reloaded.thingIDNumber = a.thingIDNumber;
            rebuiltScenario.AddTrader(rebuiltNet, reloaded, -200f, true, 200f);
            TestAssert.Check(loaded.IsSheddable(reloaded), "new object instance with the same ID matches");

            // Destroyed buildings are marked missing and eventually pruned, never dereferenced.
            loaded.MarkSeen(Scan(rebuiltScenario.Map, 200), 200);
            TestAssert.Check(loaded.Find(idA) != null && !loaded.Find(idA).IsMissing,
                "live row is seen after reload");
            TestAssert.Check(loaded.Find(PowerManagementRegistry.StableId(b)).IsMissing,
                "destroyed/removed building is marked missing");
            loaded.MarkSeen(Scan(rebuiltScenario.Map, PowerManagementRegistry.OrphanRetentionTicks + 500),
                PowerManagementRegistry.OrphanRetentionTicks + 500);
            loaded.Prune(PowerManagementRegistry.OrphanRetentionTicks + 500);
            TestAssert.Null(loaded.Find(PowerManagementRegistry.StableId(b)),
                "long-missing row is pruned");

            // Bulk group actions are explicit and bounded.
            TestAssert.Check(loaded.SetGroupPriority("Freezers", 4), "group priority changed");
            TestAssert.Equal(loaded.ApplyGroupPriority("Freezers", 4), 1,
                "bulk priority applies only to live members");
            TestAssert.Equal(loaded.PriorityOf(reloaded), 4, "live member received the group priority");
            TestAssert.Check(loaded.RemoveGroup("Workshop"), "group removed");

            // Corrupt/partial serialized lists must not throw or create phantom rows.
            SaveNode broken = Scribe.BeginSaving();
            registry.ExposeData();
            Scribe.Finish();
            broken.values["ids"] = new List<string> { "Thing_a", "Thing_a", "" };
            broken.values["priorities"] = new List<int> { 999, 7, 8 };
            var corrupt = new PowerManagementRegistry();
            Load(corrupt, broken);
            TestAssert.Equal(corrupt.Count, 1, "duplicate and empty IDs are discarded");
            TestAssert.Equal(corrupt.Find("Thing_a").ShedPriority, PowerManagementRegistry.MaxPriority,
                "out-of-range priority is clamped");

            SaveNode missing = Scribe.BeginSaving();
            Scribe.Finish();
            var empty = new PowerManagementRegistry();
            Load(empty, missing);
            TestAssert.Equal(empty.Count, 0, "missing management node loads as an empty registry");
        }

        private static void EventLogBounds()
        {
            var log = new PowerManagementEventLog();
            for (int i = 0; i < PowerManagementEventLog.MaxEvents + 12; i++)
                log.Add(i, PowerManagementEventKind.ManualOff, "Thing_" + i, "lamp " + i, true, "detail");
            TestAssert.Equal(log.Count, PowerManagementEventLog.MaxEvents, "event log is bounded");
            TestAssert.Check(log.DroppedEvents > 0, "dropped events are counted");
            TestAssert.Equal(log[log.Count - 1].Label, "lamp " + (PowerManagementEventLog.MaxEvents + 11),
                "newest event is kept");

            SaveNode node = Save(log);
            var loaded = new PowerManagementEventLog();
            Load(loaded, node);
            TestAssert.Equal(loaded.Count, PowerManagementEventLog.MaxEvents, "events survive save/load");
            TestAssert.Equal(loaded[loaded.Count - 1].Kind, PowerManagementEventKind.ManualOff,
                "event kind survives save/load");

            SaveNode corrupt = Scribe.BeginSaving();
            log.ExposeData();
            Scribe.Finish();
            corrupt.values["kinds"] = new List<int> { 900, (int)PowerManagementEventKind.AutomaticOff };
            var repaired = new PowerManagementEventLog();
            Load(repaired, corrupt);
            TestAssert.Equal(repaired[0].Kind, PowerManagementEventKind.Refused,
                "unknown event kind falls back to refused");
        }

        // --- Decision logic ---------------------------------------------------

        private static void DecisionLogic()
        {
            PowerNet net;
            CompPowerBattery battery;
            PowerScenario scenario = BuildScenario(out net, out battery);
            CompFlickable lowFlick, highFlick, criticalFlick;
            Building low = Consumer(scenario, net, "recreation", -30f, 30f, out lowFlick);
            Building high = Consumer(scenario, net, "workshop", -120f, 120f, out highFlick);
            Building critical = Consumer(scenario, net, "medical", -70f, 70f, out criticalFlick);
            var registry = new PowerManagementRegistry();
            var events = new PowerManagementEventLog();
            PowerGridManagerSettings settings = Settings();

            // 100 W generation, 220 W consumption => 120 W deficit, 20% usable charge.
            PowerGridSnapshot snapshot = Scan(scenario.Map, 0);
            TestAssert.Equal(snapshot.NetWatts, -120f, "test map has a deficit");
            TestAssert.Equal(LoadSheddingEngine.EffectiveCharge(snapshot), 0.2f,
                "effective charge excludes no energy here");

            LoadSheddingPlan offPlan = LoadSheddingEngine.Evaluate(snapshot, Settings(false, false),
                registry, FreshState(), 0, false);
            TestAssert.Equal(offPlan.Phase, LoadSheddingPhase.Disabled, "disabled policy does nothing");
            TestAssert.Check(!offPlan.Ready && offPlan.ActionCount == 0, "disabled plan has no actions");

            MarkSheddable(registry, low, 6);
            MarkSheddable(registry, high, 2);
            // 'critical' is deliberately NOT marked: it must never appear in a plan.
            settings.LoadShedRequireDeficit = true;
            LoadSheddingPlan plan = LoadSheddingEngine.Evaluate(snapshot, settings, registry, FreshState(), 0, false);
            TestAssert.Equal(plan.ChargeFraction, 0.2f, "plan reports usable charge");
            TestAssert.Equal(plan.DeficitWatts, 120f, "plan reports measured deficit");
            TestAssert.Equal(plan.CandidateCount, 2, "only opted-in consumers are candidates");
            TestAssert.Check(plan.Ready, "conditions and stability are satisfied with zero thresholds");
            TestAssert.Equal(plan.ToShed.Count, 2, "both opted-in consumers are needed for this deficit");
            TestAssert.Same(plan.ToShed[0].Building, low,
                "higher shed priority is selected first (P6 before P2)");
            TestAssert.Equal(plan.ProjectedSavingsWatts, 150f,
                "projected savings are the measured draw, not configured draw");

            // The evaluation itself never changed a switch.
            TestAssert.Check(lowFlick.SwitchIsOn && highFlick.SwitchIsOn && criticalFlick.SwitchIsOn,
                "evaluation is read-only");

            LoadSheddingState state = FreshState();
            LoadSheddingEngine.Commit(plan, state, 0);
            int changed = LoadSheddingEngine.Apply(plan, state, registry, events, 0);
            TestAssert.Equal(changed, 2, "apply changes exactly the planned switches");
            TestAssert.Check(!lowFlick.SwitchIsOn && !highFlick.SwitchIsOn, "selected buildings are off");
            TestAssert.Check(criticalFlick.SwitchIsOn,
                "the protected building stays on");
            TestAssert.Equal(state.AutoShedCount, 2, "both switched-off buildings are tracked");
            TestAssert.Check(state.IsShedding, "state enters shedding");
            TestAssert.Check(events.Count > 0, "action is recorded");

            // A preview is read-only and lists every opted-in building, even ones already off.
            LoadSheddingPlan preview = LoadSheddingEngine.Evaluate(snapshot, Settings(false, false),
                registry, state, 100, true);
            TestAssert.Check(preview.IsPreview, "preview is marked as such");
            TestAssert.Check(!preview.Ready, "preview never authorises actions");
            TestAssert.Equal(preview.ToShed.Count, 2, "preview lists all opted-in buildings");
            TestAssert.Check(lowFlick.WantsFlick() == false, "preview did not queue a flick");
            TestAssert.Equal(LoadSheddingEngine.Apply(preview, state, registry, events, 100), 0,
                "applying a preview is a no-op");

            // No batteries: no charge-based policy, but recovery still clears tracking.
            battery.Props.storedEnergyMax = 0f;
            PowerGridSnapshot noBattery = Scan(scenario.Map, 101);
            TestAssert.Equal(LoadSheddingEngine.EffectiveCharge(noBattery), -1f,
                "charge is unknown without capacity");
            LoadSheddingPlan unknown = LoadSheddingEngine.Evaluate(noBattery, settings, registry, state, 101, false);
            TestAssert.Equal(unknown.ReasonKey, "PGM_Mgmt_Reason_NoBatteries",
                "unknown charge is reported honestly");
            TestAssert.Equal(unknown.ToRestore.Count, 2, "unknown capacity triggers restoration");
            battery.Props.storedEnergyMax = 100f;

            // Above the low threshold nothing is shed.
            battery.StoredEnergy = 90f;
            PowerGridSnapshot charged = Scan(scenario.Map, 102);
            LoadSheddingPlan idle = LoadSheddingEngine.Evaluate(charged, settings, registry,
                FreshState(), 102, false);
            TestAssert.Equal(idle.Phase, LoadSheddingPhase.Idle, "charged battery keeps policy idle");
            TestAssert.Equal(idle.ActionCount, 0, "no actions while charged");

            // EMP-stunned batteries have zero available energy even though physical charge exists.
            battery.StoredEnergy = 90f;
            battery.StunnedByEMP = true;
            PowerGridSnapshot emp = Scan(scenario.Map, 103);
            TestAssert.Equal(LoadSheddingEngine.EffectiveCharge(emp), 0f,
                "EMP-stunned energy is not counted as usable");
            battery.StunnedByEMP = false;

            // Generators and batteries are never shedding candidates, and unsupported components
            // are never controlled.
            var mixed = new PowerScenario();
            PowerNet mixedNet = mixed.AddNet();
            Building generator = mixed.AddBuilding(ThingDefFactory.Simple("generator"));
            mixed.AddTrader<CompPowerPlant>(mixedNet, generator, 100f, true, -100f);
            CompFlickable genFlick = mixed.AddFlickable(generator, true);
            Building generatorBattery = mixed.AddBuilding(ThingDefFactory.Simple("battery"));
            mixed.AddBattery(mixedNet, generatorBattery, 10f, 100f);
            mixed.AddFlickable(generatorBattery, true);
            var mixedRegistry = new PowerManagementRegistry();
            mixedRegistry.SetSheddable(generator, true, 0);
            mixedRegistry.SetSheddable(generatorBattery, true, 0);
            PowerGridSnapshot mixedSnapshot = Scan(mixed.Map, 0);
            LoadSheddingPlan mixedPlan = LoadSheddingEngine.Evaluate(mixedSnapshot, settings,
                mixedRegistry, FreshState(), 0, false);
            TestAssert.Equal(mixedPlan.CandidateCount, 0, "generators and batteries are never shed");
            TestAssert.Check(mixedPlan.ReasonKey == "PGM_Mgmt_Reason_NoBatteries" ||
                             mixedPlan.ActionCount == 0, "no control action for storage/generation");
            TestAssert.Check(genFlick.SwitchIsOn, "generator switch is untouched");

            // Diagnostics exclusions are visible to the diagnostics engine.
            mixedRegistry.SetExcludedFromDiagnostics(generator, true, 0);
            TestAssert.Check(mixedRegistry.IsExcludedFromDiagnostics(generator),
                "per-building diagnostic exclusion is honoured");
        }

        // --- Hysteresis and recovery -----------------------------------------

        private static void HysteresisAndRecovery()
        {
            PowerNet net;
            CompPowerBattery battery;
            PowerScenario scenario = BuildScenario(out net, out battery);
            CompFlickable f1, f2, f3;
            Building b1 = Consumer(scenario, net, "bench", -80f, 80f, out f1);
            Building b2 = Consumer(scenario, net, "light", -60f, 60f, out f2);
            Building b3 = Consumer(scenario, net, "tv", -40f, 40f, out f3);
            var registry = new PowerManagementRegistry();
            MarkSheddable(registry, b1, 6);
            MarkSheddable(registry, b2, 4);
            MarkSheddable(registry, b3, 2);
            var events = new PowerManagementEventLog();
            var state = FreshState();
            PowerGridManagerSettings settings = Settings();
            settings.LoadShedStableTicks = 600;
            settings.LoadShedCooldownTicks = 300;
            settings.LoadShedMaxActionsPerPass = 1;
            settings.LoadShedRecovery = LoadSheddingRecoveryMode.All;
            PowerGridManagerMod.Settings = settings;

            PowerGridSnapshot snapshot = Scan(scenario.Map, 0);
            // First pass: trigger met but not stable.
            LoadSheddingPlan watching = LoadSheddingEngine.Evaluate(snapshot, settings, registry, state, 0, false);
            LoadSheddingEngine.Commit(watching, state, 0);
            TestAssert.Equal(watching.Phase, LoadSheddingPhase.Watching, "first pass watches");
            TestAssert.Check(!watching.Ready, "stability is not yet satisfied");
            TestAssert.Equal(LoadSheddingEngine.Apply(watching, state, registry, events, 0), 0,
                "unstable plan performs no actions");
            TestAssert.Check(f1.SwitchIsOn && f2.SwitchIsOn && f3.SwitchIsOn, "no switch moved early");

            // Still unstable one tick later.
            LoadSheddingPlan waiting = LoadSheddingEngine.Evaluate(snapshot, settings, registry, state, 100, false);
            LoadSheddingEngine.Commit(waiting, state, 100);
            TestAssert.Check(!waiting.Stable, "short trigger does not count as stable");

            // Stable: exactly one switch changes because of the per-pass cap.
            LoadSheddingPlan firstBatch = LoadSheddingEngine.Evaluate(snapshot, settings, registry, state, 600, false);
            LoadSheddingEngine.Commit(firstBatch, state, 600);
            TestAssert.Check(firstBatch.Ready, "stable trigger is ready");
            LoadSheddingEngine.Apply(firstBatch, state, registry, events, 600);
            TestAssert.Check(!f1.SwitchIsOn && f2.SwitchIsOn && f3.SwitchIsOn,
                "one priority-6 building is shed first");
            TestAssert.Equal(state.AutoShedCount, 1, "one building tracked");

            // The cooldown blocks an immediate second batch.
            LoadSheddingPlan cooldown = LoadSheddingEngine.Evaluate(snapshot, settings, registry, state, 700, false);
            LoadSheddingEngine.Commit(cooldown, state, 700);
            TestAssert.Check(!cooldown.CooldownReady, "cooldown is active");
            TestAssert.Check(cooldown.ReasonKey == "PGM_Mgmt_Reason_Cooldown" ||
                             cooldown.ReasonKey == "PGM_Mgmt_Reason_Stabilising",
                "cooldown or stability is explained");

            // After the cooldown and a fresh stability window another batch is allowed.
            LoadSheddingPlan second = LoadSheddingEngine.Evaluate(snapshot, settings, registry, state, 1500, false);
            LoadSheddingEngine.Commit(second, state, 1500);
            TestAssert.Check(second.Ready, "additional shedding is allowed after the delay");
            LoadSheddingEngine.Apply(second, state, registry, events, 1500);
            TestAssert.Equal(state.AutoShedCount, 2, "two buildings are now tracked");

            // Charge between the two thresholds must NOT restore (hysteresis band).
            battery.StoredEnergy = 40f;
            PowerGridSnapshot midway = Scan(scenario.Map, 1600);
            LoadSheddingPlan hold = LoadSheddingEngine.Evaluate(midway, settings, registry, state, 1600, false);
            TestAssert.Check(!hold.ConditionsMet, "mid-band charge is not a recovery condition");
            TestAssert.Equal(hold.ToRestore.Count, 0, "no restoration inside the hysteresis band");
            TestAssert.Check(f1.SwitchIsOn == false, "shed building stays off in the band");

            // Recovery: all tracked buildings are restored.
            battery.StoredEnergy = 80f;
            PowerGridSnapshot recovered = Scan(scenario.Map, 2200);
            for (int i = 0; i < recovered.Buildings.Count; i++)
            {
                if (recovered.Buildings[i].Building == b1) f1.SwitchIsOn = false;
                if (recovered.Buildings[i].Building == b2) f2.SwitchIsOn = false;
            }
            LoadSheddingPlan recoveryStart = LoadSheddingEngine.Evaluate(recovered, settings, registry,
                state, 2200, false);
            LoadSheddingEngine.Commit(recoveryStart, state, 2200);
            LoadSheddingPlan recovery = LoadSheddingEngine.Evaluate(recovered, settings, registry,
                state, 2900, false);
            LoadSheddingEngine.Commit(recovery, state, 2900);
            TestAssert.Check(recovery.Ready, "recovery condition is ready after its own stability window");
            TestAssert.Check(recovery.ToRestore.Count >= 1, "tracked buildings are queued for restoration");
            LoadSheddingEngine.Apply(recovery, state, registry, events, 2900);
            TestAssert.Equal(state.AutoShedCount, 0, "tracking is cleared after restoration");
            TestAssert.Check(!state.IsShedding, "policy leaves shedding");

            // Manual recovery never restores automatically. ResetToOn also clears the desired
            // switch flag, like a player flicking the building back on.
            f1.ResetToOn();
            f2.ResetToOn();
            battery.StoredEnergy = 10f;
            var manualState = FreshState();
            PowerGridManagerSettings manualSettings = Settings();
            manualSettings.LoadShedRecovery = LoadSheddingRecoveryMode.Manual;
            PowerGridSnapshot low = Scan(scenario.Map, 3000);
            LoadSheddingPlan shed = LoadSheddingEngine.Evaluate(low, manualSettings, registry, manualState, 3000, false);
            LoadSheddingEngine.Commit(shed, manualState, 3000);
            LoadSheddingEngine.Apply(shed, manualState, registry, events, 3000);
            TestAssert.Check(manualState.AutoShedCount > 0, "manual mode still sheds");
            battery.StoredEnergy = 90f;
            PowerGridSnapshot high = Scan(scenario.Map, 3600);
            LoadSheddingPlan manualRecovery = LoadSheddingEngine.Evaluate(high, manualSettings, registry,
                manualState, 3600, false);
            TestAssert.Equal(manualRecovery.ToRestore.Count, 0, "manual mode does not auto-restore");
            LoadSheddingEngine.Commit(manualRecovery, manualState, 3600);
            LoadSheddingEngine.Apply(manualRecovery, manualState, registry, events, 3600);
            TestAssert.Check(manualState.AutoShedCount > 0, "manual mode keeps tracking");
            TestAssert.Check(LoadSheddingEngine.RestoreAll(high, registry, manualState, events, 3600) > 0,
                "explicit restore works in manual mode");
            TestAssert.Equal(manualState.AutoShedCount, 0, "explicit restore clears tracking");

            // Destroyed buildings are dropped from tracking without exceptions.
            manualState.Track(PowerManagementRegistry.StableId(b3));
            b3.Destroyed = true;
            PowerGridSnapshot afterDestruction = Scan(scenario.Map, 3700);
            LoadSheddingEngine.RestoreAll(afterDestruction, registry, manualState, events, 3700);
            TestAssert.Equal(manualState.AutoShedCount, 0, "destroyed tracked building is untracked");
            TestAssert.Check(events.Count > 0, "cleanup is recorded in the event log");
        }

        // --- Controller -------------------------------------------------------

        private static void ControllerIntegration()
        {
            PowerNet net;
            CompPowerBattery battery;
            PowerScenario scenario = BuildScenario(out net, out battery);
            CompFlickable flick, protectedFlick;
            Building target = Consumer(scenario, net, "bench", -80f, 80f, out flick);
            Building protectedBuilding = Consumer(scenario, net, "medical", -60f, 60f, out protectedFlick);
            var controller = new PowerManagementController();
            PowerGridManagerSettings settings = Settings();
            PowerGridSnapshot snapshot = Scan(scenario.Map, 0);

            controller.SetSheddable(target, true, 0);
            controller.SetPriority(target, 6, 0);
            controller.Tick(snapshot, settings, 0);
            TestAssert.Check(!flick.SwitchIsOn, "controller sheds an opted-in building");
            TestAssert.Check(protectedFlick.SwitchIsOn, "protected building is untouched");
            TestAssert.NotNull(controller.LastPlan, "controller caches its decision");
            TestAssert.Check(controller.Events.Count > 0, "controller records events");

            int beforeRevision = controller.Revision;
            LoadSheddingPlan preview = controller.Preview(snapshot, settings, 0);
            LoadSheddingPlan cached = controller.Preview(snapshot, settings, 1);
            TestAssert.Same(preview, cached, "preview is cached instead of re-scanned per frame");
            controller.SetGroup(target, "Freezers", 1);
            TestAssert.Check(controller.Revision > beforeRevision, "changes invalidate the preview cache");

            // Disabling automation restores what the policy switched off.
            controller.SetPolicy(settings, true, false, snapshot, 2);
            TestAssert.Check(flick.SwitchIsOn, "disabling automation restores switches");
            TestAssert.Equal(controller.AutoShedCount, 0, "tracking is cleared on disable");
            TestAssert.Null(controller.LastPlan, "cached plan is dropped when disabled");

            // Manual control is refused while management is off, and works when on.
            controller.SetPolicy(settings, false, false, snapshot, 3);
            string detail;
            PowerControlResult refused = controller.ManualSwitch(snapshot, target, false, settings, 3, out detail);
            TestAssert.Equal(refused, PowerControlResult.ControlFailed, "manual control needs the master switch");
            controller.SetPolicy(settings, true, false, snapshot, 4);
            PowerControlResult manual = controller.ManualSwitch(snapshot, target, false, settings, 4, out detail);
            TestAssert.Equal(manual, PowerControlResult.Success, "manual switch works when enabled");
            TestAssert.Check(!flick.SwitchIsOn, "manual switch changed the building");
            manual = controller.ManualSwitch(snapshot, target, true, settings, 5, out detail);
            TestAssert.Equal(manual, PowerControlResult.Success, "manual restore works");
            TestAssert.Check(flick.SwitchIsOn, "manual restore switched it back on");

            // Produce a real building-level advisory, then confirm the exclusion filters it.
            protectedFlick.SwitchIsOn = false;
            PowerGridSnapshot diagSnapshot = Scan(scenario.Map, 7);
            settings.DiagnosticsEnabled = true;
            settings.ShowInfoDiagnostics = true;
            var allAdvisories = new List<PowerDiagnostic>();
            PowerDiagnostics.Evaluate(diagSnapshot, null, settings, new PowerDiagnosticTracker(),
                allAdvisories, null);
            bool produced = false;
            for (int i = 0; i < allAdvisories.Count; i++)
                if (allAdvisories[i].Building == protectedBuilding) produced = true;
            TestAssert.Check(produced, "unexcluded building produces a building-level advisory");

            controller.SetExcludedFromDiagnostics(protectedBuilding, true, 7);
            var advisories = new List<PowerDiagnostic>();
            PowerDiagnostics.Evaluate(diagSnapshot, null, settings, new PowerDiagnosticTracker(),
                advisories, controller.Registry);
            for (int i = 0; i < advisories.Count; i++)
                TestAssert.Check(advisories[i].Building != protectedBuilding,
                    "excluded building raises no advisory");

            controller.Reset();
            TestAssert.Equal(controller.Registry.Count, 0, "map removal clears the registry");
            TestAssert.Equal(controller.Events.Count, 0, "map removal clears the event log");
        }

        // --- Component save/load ---------------------------------------------

        private static void ComponentSerialization()
        {
            PowerNet net;
            CompPowerBattery battery;
            PowerScenario scenario = BuildScenario(out net, out battery);
            CompFlickable flick;
            Building target = Consumer(scenario, net, "bench", -80f, 80f, out flick);
            PowerGridManagerSettings settings = Settings();
            var component = new PowerGridManagerComponent(scenario.Map);
            component.Management.SetSheddable(target, true, 0);
            component.Management.SetPriority(target, 5, 0);
            component.Management.Events.Add(0, PowerManagementEventKind.PolicyChanged, "",
                "policy", true, "enabled");
            component.History.MaybeSample(Scan(scenario.Map, 0), 0, 2500,
                settings.RetentionDays * PowerTime.TicksPerDay);

            SaveNode node = Save(component);
            var loaded = new PowerGridManagerComponent(scenario.Map);
            Load(loaded, node);
            TestAssert.Check(loaded.Management.Registry.IsSheddable(target),
                "management preferences survive a component save/load");
            TestAssert.Equal(loaded.Management.Events.Count, 1, "event log survives component save/load");
            TestAssert.Equal(loaded.History.Count, 1, "history still survives the same save");

            // A save from before Phase 7 has no management node but must load cleanly.
            SaveNode legacy = Scribe.BeginSaving();
            var oldComponent = new PowerGridManagerComponent(scenario.Map);
            oldComponent.ExposeData();
            Scribe.Finish();
            legacy.children.RemoveAll(c => c.name == "powerManagement");
            var upgraded = new PowerGridManagerComponent(scenario.Map);
            Load(upgraded, legacy);
            TestAssert.NotNull(upgraded.Management, "missing management node gets a fresh controller");
            TestAssert.Equal(upgraded.Management.Registry.Count, 0, "upgraded registry is empty");
            TestAssert.Equal(upgraded.Management.AutoShedCount, 0, "upgraded policy is inert");
        }

        // --- UI ---------------------------------------------------------------

        private static void PanelSmoke()
        {
            PowerNet net;
            CompPowerBattery battery;
            PowerScenario scenario = BuildScenario(out net, out battery);
            CompFlickable flick;
            Building target = Consumer(scenario, net, "bench", -80f, 80f, out flick);
            var component = new PowerGridManagerComponent(scenario.Map);
            scenario.Map.RegisterComponent(component);
            PowerGridManagerSettings settings = Settings(true, false);
            component.Management.SetSheddable(target, true, 0);
            PowerGridSnapshot snapshot = Scan(scenario.Map, 0);
            var state = new PowerUiState { LastSnapshot = snapshot };
            var panel = new PowerManagementPanel();
            Find.WindowStack.windows.Clear();

            Rect[] sizes =
            {
                new Rect(0f, 0f, 1010f, 660f),
                new Rect(0f, 0f, 320f, 180f),
                new Rect(0f, 0f, 640f, 420f),
                new Rect(0f, 0f, 2560f, 1400f)
            };
            foreach (Rect size in sizes)
            {
                panel.Draw(size, snapshot, state, settings, component.Management, 0);
                TestAssert.NotNull(panel.LastPlan, "panel produced a preview at " + size.width + "px");
                TestAssert.Check(panel.LastPlan.IsPreview, "panel plan is a preview");
            }

            // Window dispatch with the new tab must not throw.
            Find.TickManager.TicksGame = 0;
            var window = new MainTabWindow_PowerGridManager();
            window.PostOpen();
            window.UiState.Tab = PowerGridTab.Management;
            window.DoWindowContents(sizes[0]);
            window.UiState.Tab = PowerGridTab.Buildings;
            window.UiState.SelectBuilding(target);
            window.DoWindowContents(sizes[0]);

            // Click the master/enable button and confirm it changes settings (scripted clicks).
            var controller = component.Management;
            PowerGridManagerSettings toggleSettings = new PowerGridManagerSettings
            {
                ManagementEnabled = true,
                LoadSheddingEnabled = false
            };
            PowerGridManagerMod.Settings = toggleSettings;
            WidgetsQueue.Reset();
            WidgetsQueue.ClickTextAt(0);
            panel.Draw(sizes[0], snapshot, state, toggleSettings, controller, 0);
            TestAssert.Check(!toggleSettings.ManagementEnabled, "header button toggles the master switch");
            Find.WindowStack.windows.Clear();
            WidgetsQueue.Reset();
        }
    }
}
