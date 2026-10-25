// Phase 3 verification: snapshots feeding the dashboard, building list filtering and
// sorting, status evaluation, classification, formatting and a headless UI smoke run.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using PowerGridManager.Tests;

namespace PowerGridManager
{
    public static class DashboardTests
    {
        public static int Run()
        {
            TestAssert.UsePhase("phase3");
            int start = TestAssert.Checks;
            SnapshotAggregates();
            StatusEvaluation();
            FilteringAndSearch();
            Sorting();
            Grouping();
            Classification();
            Formatting();
            Compatibility();
            UiSmoke();
            Console.WriteLine("Phase 3 dashboard checks passed: " + (TestAssert.Checks - start));
            return TestAssert.Checks - start;
        }

        private static void SnapshotAggregates()
        {
            var scenario = new PowerScenario();
            var net = scenario.AddNet();
            var plantDef = ThingDefFactory.Simple("plant");
            var plantBuilding = scenario.AddBuilding(plantDef);
            scenario.AddTrader<CompPowerPlant>(net, plantBuilding, 1200f, true, -1200f);

            var consumerDef = ThingDefFactory.Simple("lamp", typeof(CompGlower));
            var consumerBuilding = scenario.AddBuilding(consumerDef);
            scenario.AddTrader(net, consumerBuilding, -150f, true, 150f);
            scenario.AddFlickable(consumerBuilding, true);

            var batteryDef = ThingDefFactory.Simple("battery");
            var batteryBuilding = scenario.AddBuilding(batteryDef);
            scenario.AddBattery(net, batteryBuilding, 400f, 600f);

            var offDef = ThingDefFactory.Simple("heater", typeof(CompTempControl));
            var offBuilding = scenario.AddBuilding(offDef);
            scenario.AddTrader(net, offBuilding, -80f, false, 80f);
            scenario.AddFlickable(offBuilding, false);

            var room = new Room { Role = new RoomRoleDef { defName = "Workshop", label = "workshop" } };
            consumerBuilding.RoomRef = room;

            var snapshot = new PowerGridScanner().Scan(scenario.Map, 100);

            TestAssert.Equal(snapshot.ConsumerCount, 2, "consumer components counted");
            TestAssert.Equal(snapshot.GeneratorCount, 1, "generator components counted");
            TestAssert.Equal(snapshot.BatteryCount, 1, "battery components counted");
            TestAssert.Equal(snapshot.GenerationWatts, 1200f, "generation total");
            TestAssert.Equal(snapshot.ConsumptionWatts, 150f, "consumption excludes switched-off trader");
            TestAssert.Equal(snapshot.StoredWattDays, 400f, "stored energy");
            TestAssert.Equal(snapshot.CapacityWattDays, 600f, "capacity");
            TestAssert.NotNull(snapshot.NominalConsumptionWatts, "nominal demand available");
            TestAssert.Equal(snapshot.NominalConsumptionWatts.Value, 230f, "nominal demand sums configured draw");

            BuildingPowerSnapshot lamp = FindBuilding(snapshot, consumerBuilding);
            TestAssert.NotNull(lamp, "lamp present in snapshot");
            TestAssert.Equal(lamp.ConsumptionWatts, 150f, "building draw");
            TestAssert.Equal(lamp.NominalConsumptionWatts.Value, 150f, "building nominal draw");
            TestAssert.Check(lamp.IsFlickable && lamp.SwitchIsOn == true, "flick state captured");
            TestAssert.Check(lamp.HasRoom, "room association captured");
            TestAssert.Equal(PowerGridUtilities.RoomLabel(lamp.Room), "Workshop", "room label");
            TestAssert.Equal(lamp.Category, PowerCategory.Lighting, "glower classified as lighting");

            BuildingPowerSnapshot heater = FindBuilding(snapshot, offBuilding);
            TestAssert.Check(heater.HasOffComponents, "switched-off component detected");
            TestAssert.Check(heater.SwitchIsOn == false, "switch state false");
            TestAssert.Equal(heater.ConsumptionWatts, 0f, "off building draws nothing");
            TestAssert.Equal(heater.NominalConsumptionWatts.Value, 80f, "configured draw retained while off");
            TestAssert.Equal(heater.Category, PowerCategory.TemperatureControl, "cooler classified");

            BuildingPowerSnapshot battery = FindBuilding(snapshot, batteryBuilding);
            TestAssert.Equal(battery.Category, PowerCategory.Storage, "battery classified as storage");
            TestAssert.Equal(battery.CapacityWattDays, 600f, "battery capacity on building");

            BuildingPowerSnapshot generator = FindBuilding(snapshot, plantBuilding);
            TestAssert.Equal(generator.Category, PowerCategory.Generation, "plant classified as generation");
            TestAssert.Equal(generator.ConfiguredGenerationWatts.Value, 1200f,
                "configured generation read from the negative configured consumption");
            TestAssert.Equal(snapshot.ConfiguredGenerationWatts.Value, 1200f, "map configured generation");
            TestAssert.Equal(snapshot.LoadRatio.Value, 0.125f, "load ratio is consumption over generation");

            // A generator without a declared configured output must not be guessed.
            var undeclared = new PowerScenario();
            var undeclaredNet = undeclared.AddNet();
            var undeclaredBuilding = undeclared.CreateBuilding(ThingDefFactory.Simple("solar"));
            undeclared.AddTrader<CompPowerPlant>(undeclaredNet, undeclaredBuilding, 600f, true, 0f);
            var undeclaredSnapshot = new PowerGridScanner().Scan(undeclared.Map, 0);
            TestAssert.Null(undeclaredSnapshot.ConfiguredGenerationWatts,
                "undeclared configured output stays unknown");
            TestAssert.Equal(undeclaredSnapshot.LoadRatio.Value, 0f,
                "load ratio is zero when a generator runs with no consumption");

            // No generation at all: the ratio must be undefined rather than infinite.
            var idle = new PowerScenario();
            idle.AddNet();
            var idleBuilding = idle.CreateBuilding(ThingDefFactory.Simple("idle lamp"));
            idle.AddTrader(null, idleBuilding, 0f, false, 40f);
            var idleSnapshot = new PowerGridScanner().Scan(idle.Map, 0);
            TestAssert.Null(idleSnapshot.LoadRatio, "load ratio undefined when nothing is generated");

            // Disconnected consumer must be counted and flagged, not folded into totals.
            var orphanDef = ThingDefFactory.Simple("orphan");
            var orphanBuilding = scenario.AddBuilding(orphanDef);
            scenario.AddTrader(null, orphanBuilding, -40f);
            snapshot = new PowerGridScanner().Scan(scenario.Map, 200);
            TestAssert.Equal(snapshot.DisconnectedConsumerCount, 1, "disconnected consumer counted");
            TestAssert.Equal(snapshot.ConsumptionWatts, 150f, "disconnected draw excluded from totals");
            TestAssert.Check(snapshot.IncompleteDataCount > 0, "incomplete data flagged");
            PowerGridUtilities.CollectNetworkBuildings(snapshot, 1, new List<BuildingPowerSnapshot>());
        }

        private static BuildingPowerSnapshot FindBuilding(PowerGridSnapshot snapshot, Building building)
        {
            for (int i = 0; i < snapshot.Buildings.Count; i++)
            {
                if (snapshot.Buildings[i].Building == building) return snapshot.Buildings[i];
            }
            return null;
        }

        private static void StatusEvaluation()
        {
            var stable = MakeNetwork(1, 1000f, 200f, 0f, 0f);
            TestAssert.Equal(PowerGridUtilities.EvaluateStatus(stable, 0.9f), NetworkStatus.Stable, "stable net");

            var highLoad = MakeNetwork(2, 1000f, 950f, 0f, 0f);
            TestAssert.Equal(PowerGridUtilities.EvaluateStatus(highLoad, 0.9f), NetworkStatus.HighLoad, "high load");

            var deficit = MakeNetwork(3, 500f, 900f, 0f, 0f);
            TestAssert.Equal(PowerGridUtilities.EvaluateStatus(deficit, 0.9f), NetworkStatus.Deficit, "deficit");

            var discharging = MakeNetwork(4, 100f, 400f, 250f, 600f);
            TestAssert.Equal(PowerGridUtilities.EvaluateStatus(discharging, 0.9f), NetworkStatus.BatteryDischarging,
                "battery covering deficit");

            var charging = MakeNetwork(5, 1000f, 100f, 200f, 600f);
            TestAssert.Equal(PowerGridUtilities.EvaluateStatus(charging, 0.9f), NetworkStatus.BatteryCharging,
                "battery charging");

            var noGeneration = MakeNetwork(6, 0f, 40f, 0f, 0f, 1, 0);
            TestAssert.Equal(PowerGridUtilities.EvaluateStatus(noGeneration, 0.9f), NetworkStatus.NoGeneration,
                "no generation");

            var empty = new PowerNetworkSnapshot { Id = 1 };
            TestAssert.Equal(PowerGridUtilities.EvaluateStatus(empty, 0.9f), NetworkStatus.Unknown, "empty network");

            TestAssert.Equal(PowerGridUtilities.StatusLabelKey(NetworkStatus.HighLoad), "PGM_Status_HighLoad",
                "status label key");
        }

        private static PowerNetworkSnapshot MakeNetwork(int id, float generation, float consumption,
            float stored, float capacity, int consumers = 1, int generators = 1)
        {
            return new PowerNetworkSnapshot
            {
                Id = id,
                GenerationWatts = generation,
                ConsumptionWatts = consumption,
                StoredWattDays = stored,
                CapacityWattDays = capacity,
                ConsumerCount = consumers,
                GeneratorCount = generators,
                BatteryCount = capacity > 0f ? 1 : 0
            };
        }

        private static void FilteringAndSearch()
        {
            var scenario = new PowerScenario();
            var net = scenario.AddNet();
            var otherNet = scenario.AddNet();

            var lampBuilding = scenario.CreateBuilding(ThingDefFactory.Simple("standing lamp", typeof(CompGlower)));
            scenario.AddTrader(net, lampBuilding, -75f, true, 75f);
            var heaterBuilding = scenario.CreateBuilding(ThingDefFactory.Simple("heater", typeof(CompTempControl)));
            scenario.AddTrader(net, heaterBuilding, -900f, true, 900f);
            scenario.AddFlickable(heaterBuilding, false);
            var turretBuilding = scenario.CreateBuilding(new ThingDef
            {
                defName = "Turret",
                label = "mini-turret",
                thingClass = typeof(Building_TurretGun),
                building = new BuildingProperties()
            });
            scenario.AddTrader(otherNet, turretBuilding, -250f, true, 250f);
            var batteryBuilding = scenario.CreateBuilding(ThingDefFactory.Simple("battery bank"));
            scenario.AddBattery(otherNet, batteryBuilding, 100f, 500f);
            var unsupportedBuilding = scenario.CreateBuilding(ThingDefFactory.Simple("odd device"));
            var custom = new StrangeComponent();
            scenario.Attach(unsupportedBuilding, custom, net, true);

            var snapshot = new PowerGridScanner().Scan(scenario.Map, 10);
            var filter = new BuildingFilterSettings();
            var model = new BuildingListModel();

            model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 500f);
            TestAssert.Equal(model.Count, 5, "all power buildings visible by default");

            filter.OnlyFlickable = true;
            filter.MarkChanged();
            model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 500f);
            TestAssert.Equal(model.Count, 1, "flickable filter");
            TestAssert.Same(model.Items[0].Building, heaterBuilding, "flickable row is the heater");

            filter.Reset();
            filter.OnlySwitchedOff = true;
            model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 500f);
            TestAssert.Equal(model.Count, 1, "switched-off filter");

            filter.Reset();
            filter.OnlyHighConsumption = true;
            model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 500f);
            TestAssert.Equal(model.Count, 1, "high-consumption filter");
            TestAssert.Same(model.Items[0].Building, heaterBuilding, "high consumer identified");

            filter.Reset();
            filter.NetworkId = 1;
            filter.MarkChanged();
            model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 500f);
            TestAssert.Equal(model.Count, 2, "network filter excludes pending membership");

            filter.Reset();
            filter.Category = PowerCategory.Lighting;
            filter.MarkChanged();
            model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 500f);
            TestAssert.Equal(model.Count, 1, "category filter");
            TestAssert.Same(model.Items[0].Building, lampBuilding, "lighting row");

            filter.Reset();
            filter.ShowBatteries = false;
            filter.MarkChanged();
            model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 500f);
            TestAssert.Equal(model.Count, 4, "role filter hides batteries");

            filter.Reset();
            filter.ShowConsumers = false;
            filter.MarkChanged();
            model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 500f);
            TestAssert.Equal(model.Count, 1, "only battery remains when consumers are hidden");

            filter.Reset();
            filter.OnlyUnknownBehavior = true;
            filter.MarkChanged();
            model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 500f);
            TestAssert.Equal(model.Count, 1, "unknown behavior filter");

            filter.Reset();
            filter.SearchText = "lamp";
            filter.MarkChanged();
            model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 500f);
            TestAssert.Equal(model.Count, 1, "search by label");

            filter.SearchText = "TURRET";
            filter.MarkChanged();
            model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 500f);
            TestAssert.Equal(model.Count, 1, "search is case-insensitive on defName");

            filter.SearchText = "zzz";
            filter.MarkChanged();
            model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 500f);
            TestAssert.Equal(model.Count, 0, "no matches");

            filter.Reset();
            filter.OnlyWithoutRoom = true;
            filter.MarkChanged();
            model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 500f);
            TestAssert.Equal(model.Count, 5, "buildings without a room");
        }

        private static void Sorting()
        {
            var scenario = new PowerScenario();
            var net = scenario.AddNet();
            var alpha = scenario.CreateBuilding(ThingDefFactory.Simple("alpha"));
            var beta = scenario.CreateBuilding(ThingDefFactory.Simple("beta"));
            var gamma = scenario.CreateBuilding(ThingDefFactory.Simple("gamma"));
            scenario.AddTrader(net, alpha, -100f, true, 100f);
            scenario.AddTrader(net, beta, -300f, true, 300f);
            scenario.AddTrader(net, gamma, -300f, true, 300f);
            var snapshot = new PowerGridScanner().Scan(scenario.Map, 0);
            var filter = new BuildingFilterSettings();
            var model = new BuildingListModel();

            model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 0f);
            TestAssert.Equal(model.Count, 3, "three rows");
            TestAssert.Same(model.Items[0].Building, beta, "descending draw, tie broken by name");
            TestAssert.Same(model.Items[1].Building, gamma, "tie order follows name");
            TestAssert.Same(model.Items[2].Building, alpha, "lowest draw last");

            // Stability: repeated rebuilds with identical inputs keep the order.
            string first = model.Items[0].Building.def.label + model.Items[1].Building.def.label;
            model.Update(snapshot, filter, BuildingSortKey.Name, false, BuildingGrouping.None, 0f);
            TestAssert.Same(model.Items[0].Building, alpha, "name ascending");
            model.Update(snapshot, filter, BuildingSortKey.Name, true, BuildingGrouping.None, 0f);
            TestAssert.Same(model.Items[0].Building, gamma, "name descending");
            model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 0f);
            string second = model.Items[0].Building.def.label + model.Items[1].Building.def.label;
            TestAssert.Equal(first, second, "sort order is stable across rebuilds");

            // Unchanged inputs must not rebuild (returns false).
            bool rebuilt = model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 0f);
            TestAssert.Check(!rebuilt, "no rebuild without changes");
            filter.MarkChanged();
            rebuilt = model.Update(snapshot, filter, BuildingSortKey.CurrentDraw, true, BuildingGrouping.None, 0f);
            TestAssert.Check(rebuilt, "filter revision forces rebuild");
        }

        private static void Grouping()
        {
            var scenario = new PowerScenario();
            var net = scenario.AddNet();
            var lamp = scenario.CreateBuilding(ThingDefFactory.Simple("lamp", typeof(CompGlower)));
            scenario.AddTrader(net, lamp, -10f, true, 10f);
            var turret = scenario.CreateBuilding(new ThingDef
            {
                defName = "Turret",
                label = "turret",
                thingClass = typeof(Building_TurretGun),
                building = new BuildingProperties()
            });
            scenario.AddTrader(net, turret, -20f, true, 20f);
            var snapshot = new PowerGridScanner().Scan(scenario.Map, 0);
            var model = new BuildingListModel();
            model.Update(snapshot, new BuildingFilterSettings(), BuildingSortKey.Name, false,
                BuildingGrouping.Category, 0f);
            TestAssert.Equal(model.Count, 2, "grouped rows still listed");
            TestAssert.Equal(model.GroupLabelAt(0), "PGM_Category_Lighting", "first group is lighting");
            TestAssert.Equal(model.GroupLabelAt(1), "PGM_Category_Security", "second group is security");
        }

        private static void Classification()
        {
            var scenario = new PowerScenario();
            var net = scenario.AddNet();

            var research = scenario.CreateBuilding(new ThingDef
            {
                defName = "bench",
                label = "bench",
                thingClass = typeof(Building_ResearchBench),
                building = new BuildingProperties()
            });
            scenario.AddTrader(net, research, -100f, true, 100f);

            var medicalBed = scenario.CreateBuilding(new ThingDef
            {
                defName = "hospital bed",
                label = "hospital bed",
                thingClass = typeof(Building_Bed),
                building = new BuildingProperties { bed_defaultMedical = true, bed_healPerDay = 10f }
            });
            scenario.AddTrader(net, medicalBed, -50f, true, 50f);

            var plainBed = scenario.CreateBuilding(new ThingDef
            {
                defName = "bed",
                label = "bed",
                thingClass = typeof(Building_Bed),
                building = new BuildingProperties()
            });
            scenario.AddTrader(net, plainBed, -5f, true, 5f);

            var production = scenario.CreateBuilding(ThingDefFactory.WithCategory("smithy", "BuildingsProduction"));
            scenario.AddTrader(net, production, -200f, true, 200f);

            var nested = scenario.CreateBuilding(ThingDefFactory.WithCategory("nested", "BuildingsJoy"));
            scenario.AddTrader(net, nested, -30f, true, 30f);

            var unknown = scenario.CreateBuilding(ThingDefFactory.Simple("mystery"));
            scenario.AddTrader(net, unknown, -70f, true, 70f);

            var snapshot = new PowerGridScanner().Scan(scenario.Map, 0);
            TestAssert.Equal(FindBuilding(snapshot, research).Category, PowerCategory.Research, "research bench");
            TestAssert.Equal(FindBuilding(snapshot, medicalBed).Category, PowerCategory.Medical, "medical bed");
            TestAssert.Equal(FindBuilding(snapshot, plainBed).Category, PowerCategory.Unassigned, "plain bed uncategorized");
            TestAssert.Equal(FindBuilding(snapshot, production).Category, PowerCategory.Production, "production category");
            TestAssert.Equal(FindBuilding(snapshot, nested).Category, PowerCategory.Recreation, "recreation category");
            TestAssert.Equal(FindBuilding(snapshot, unknown).Category, PowerCategory.Unassigned, "unknown stays unassigned");
        }

        private static void Formatting()
        {
            // Translation doubles echo the key, so unit assertions compare against keys.
            TestAssert.Equal(PowerFormatting.FormatWatts(0f), "0.00 PGM_Unit_Watt", "zero watts");
            TestAssert.Equal(PowerFormatting.FormatWatts(9.5f), "9.50 PGM_Unit_Watt", "small watt value");
            TestAssert.Equal(PowerFormatting.FormatWatts(85.4f), "85.4 PGM_Unit_Watt", "tens of watts");
            TestAssert.Equal(PowerFormatting.FormatWatts(850f), "850 PGM_Unit_Watt", "hundreds of watts");
            TestAssert.Equal(PowerFormatting.FormatWatts(1500f), "1.50 PGM_Unit_Kilowatt", "kilowatt switch");
            TestAssert.Equal(PowerFormatting.FormatWattsSigned(-1500f), "-1.50 PGM_Unit_Kilowatt", "signed negative");
            TestAssert.Equal(PowerFormatting.FormatWattsSigned(1500f), "+1.50 PGM_Unit_Kilowatt", "signed positive");
            TestAssert.Equal(PowerFormatting.FormatWattsSigned(0f), "0.00 PGM_Unit_Watt", "signed zero has no sign");
            TestAssert.Equal(PowerFormatting.EnergyUnit(), "PGM_Unit_WattDay", "energy carries its own unit");
            TestAssert.Check(PowerFormatting.FormatEnergy(12.5f) != PowerFormatting.FormatWatts(12.5f),
                "energy and power render differently");
            TestAssert.Equal(PowerFormatting.FormatCharge(null), "PGM_Value_Unknown", "unknown charge");
            TestAssert.Equal(PowerFormatting.FormatCharge(0.456f), "46%", "charge percent");
            TestAssert.Equal(PowerFormatting.FormatCharge(1.4f), "100%", "charge clamped");
            TestAssert.Equal(PowerFormatting.FormatDuration(GenDate.TicksPerHour), "1.0 PGM_Unit_Hour", "one hour");
            TestAssert.Equal(PowerFormatting.FormatDuration(GenDate.TicksPerDay), "1.0 PGM_Unit_Day", "one day");
            TestAssert.Equal(PowerFormatting.FormatDuration(GenDate.TicksPerHour / 2), "30 PGM_Unit_Minute", "half hour");
            TestAssert.Equal(PowerFormatting.EstimateDailyWattDays(250f), 250f, "one day at constant draw");
            TestAssert.Equal(PowerFormatting.EstimateDailyWattDays(-5f), 0f, "no negative daily energy");
            TestAssert.Equal(PowerFormatting.FormatPosition(-3, 12), "(-3, 12)", "position formatting");
        }

        private static void Compatibility()
        {
            var scenario = new PowerScenario();
            var net = scenario.AddNet();
            var building = scenario.CreateBuilding(ThingDefFactory.Simple("thing"));
            scenario.AddTrader(net, building, -10f, true, 10f);
            var snapshot = new PowerGridScanner().Scan(scenario.Map, 0);
            TestAssert.Equal(CompatibilityRegistry.Evaluate(FindBuilding(snapshot, building)), PowerSupportLevel.Supported,
                "vanilla component supported");
            TestAssert.Equal(CompatibilityRegistry.LabelKey(PowerSupportLevel.Supported), "PGM_Support_Supported",
                "support label key");

            var custom = scenario.CreateBuilding(ThingDefFactory.Simple("custom"));
            scenario.Attach(custom, new StrangeComponent(), net, true);
            snapshot = new PowerGridScanner().Scan(scenario.Map, 1);
            TestAssert.Equal(CompatibilityRegistry.Evaluate(FindBuilding(snapshot, custom)), PowerSupportLevel.Unsupported,
                "unrecognised component is unsupported");
        }

        private static void UiSmoke()
        {
            var scenario = new PowerScenario();
            var net = scenario.AddNet();
            var plantBuilding = scenario.CreateBuilding(ThingDefFactory.Simple("plant"));
            scenario.AddTrader<CompPowerPlant>(net, plantBuilding, 2000f);
            var lampBuilding = scenario.CreateBuilding(ThingDefFactory.Simple("lamp", typeof(CompGlower)));
            scenario.AddTrader(net, lampBuilding, -120f, true, 120f);
            scenario.AddFlickable(lampBuilding, true);
            lampBuilding.RoomRef = new Room { Role = new RoomRoleDef { defName = "Bedroom", label = "bedroom" } };
            var batteryBuilding = scenario.CreateBuilding(ThingDefFactory.Simple("battery"));
            scenario.AddBattery(net, batteryBuilding, 300f, 600f);

            var component = new PowerGridManagerComponent(scenario.Map);
            scenario.Map.RegisterComponent(component);
            new PowerGridManagerMod(new ModContentPack());
            Find.TickManager.TicksGame = 500;
            component.MapComponentTick();
            TestAssert.NotNull(component.Snapshot, "component produced a snapshot for the UI");

            // Accumulate a few history samples so the charts render real data.
            var settings = PowerGridManagerMod.Settings;
            settings.SampleIntervalTicks = 500;
            for (int i = 1; i <= 4; i++)
            {
                Find.TickManager.TicksGame = 500 + i * settings.SampleIntervalTicks;
                component.MapComponentTick();
            }
            TestAssert.Check(component.History.Count >= 3, "history recorded for the chart smoke test");

            var window = new MainTabWindow_PowerGridManager();
            var rect = new Rect(0f, 0f, 1010f, 640f);
            window.PostOpen();
            window.DoWindowContents(rect);
            TestAssert.Equal(Find.WindowStack.windows.Count, 0, "drawing does not open windows");

            var state = new PowerUiState { LastSnapshot = component.Snapshot };
            var overview = new PowerOverviewPanel();
            overview.Draw(rect, component.Snapshot, 500, settings);

            var networks = new PowerNetworkPanel();
            int requestedNetwork = -1;
            networks.Draw(rect, component.Snapshot, state, settings, id => requestedNetwork = id);
            state.SelectedNetworkId = 1;
            networks.Draw(rect, component.Snapshot, state, settings, id => requestedNetwork = id);

            var buildings = new BuildingPowerPanel();
            buildings.Draw(rect, component.Snapshot, state, settings);
            state.SelectBuilding(lampBuilding);
            buildings.Draw(rect, component.Snapshot, state, settings);
            TestAssert.Equal(buildings.Model.Count, 3, "model built for the drawn table");

            var history = new PowerHistoryPanel();
            int now = Find.TickManager.TicksGame;
            foreach (PowerTimeRange range in new[]
                     {
                         PowerTimeRange.LastHour, PowerTimeRange.LastSixHours, PowerTimeRange.LastDay,
                         PowerTimeRange.LastThreeDays, PowerTimeRange.LastSevenDays
                     })
            {
                state.Range = range;
                history.Draw(rect, component.History, state, settings, now);
                history.Draw(new Rect(0f, 0f, 640f, 320f), component.History, state, settings, now);
            }
            state.Range = PowerTimeRange.LastDay;

            // Window-level dispatch: every tab must render without throwing.
            foreach (PowerGridTab tab in new[]
                     {
                         PowerGridTab.Overview, PowerGridTab.Networks, PowerGridTab.Buildings,
                         PowerGridTab.History
                     })
            {
                window.UiState.Tab = tab;
                window.DoWindowContents(rect);
            }

            // Clicking a network row selects that network.
            WidgetsQueue.Reset();
            WidgetsQueue.ClickInvisibleAt(0);
            var clickState = new PowerUiState { LastSnapshot = component.Snapshot };
            networks.Draw(rect, component.Snapshot, clickState, settings, id => { });
            TestAssert.Equal(clickState.SelectedNetworkId ?? -1, 1, "clicking a network row selects it");

            // Buttons in the network detail: "show buildings" then "locate".
            WidgetsQueue.Reset();
            WidgetsQueue.ClickTextAt(0);
            int shownNetwork = -1;
            clickState.SelectedNetworkId = 1;
            networks.Draw(rect, component.Snapshot, clickState, settings, id => shownNetwork = id);
            TestAssert.Equal(shownNetwork, 1, "show-buildings button reports the network");
            WidgetsQueue.Reset();
            WidgetsQueue.ClickTextAt(1);
            networks.Draw(rect, component.Snapshot, clickState, settings, id => { });

            // Clicking a building row selects that building and shows its detail card.
            WidgetsQueue.Reset();
            WidgetsQueue.ClickInvisibleAt(0);
            var buildingClickState = new PowerUiState { LastSnapshot = component.Snapshot };
            buildings.Draw(rect, component.Snapshot, buildingClickState, settings);
            TestAssert.NotNull(buildingClickState.SelectedBuilding, "clicking a building row selects it");
            buildings.Draw(rect, component.Snapshot, buildingClickState, settings);
            WidgetsQueue.Reset();

            // Very small and very large rectangles must not throw (resolution independence).
            var tiny = new Rect(0f, 0f, 320f, 180f);
            overview.Draw(tiny, component.Snapshot, 500, settings);
            networks.Draw(tiny, component.Snapshot, state, settings, id => { });
            buildings.Draw(tiny, component.Snapshot, state, settings);
            history.Draw(tiny, component.History, state, settings, 500);
            var large = new Rect(0f, 0f, 2560f, 1400f);
            overview.Draw(large, component.Snapshot, 500, settings);
            networks.Draw(large, component.Snapshot, state, settings, id => { });
            buildings.Draw(large, component.Snapshot, state, settings);

            // Empty map state.
            var emptyScenario = new PowerScenario();
            var emptyComponent = new PowerGridManagerComponent(emptyScenario.Map);
            emptyComponent.MapComponentTick();
            var emptyState = new PowerUiState { LastSnapshot = emptyComponent.Snapshot };
            overview.Draw(rect, emptyComponent.Snapshot, 0, settings);
            networks.Draw(rect, emptyComponent.Snapshot, emptyState, settings, id => { });
            buildings.Draw(rect, emptyComponent.Snapshot, emptyState, settings);
            TestAssert.Equal(buildings.Model.Count, 0, "empty map yields no rows");

            // Filter menu opens from the toolbar (guards against a dead control).
            var toolbarRect = new Rect(0f, 0f, 1010f, 400f);
            Find.WindowStack.windows.Clear();
            var menuState = new PowerUiState { LastSnapshot = component.Snapshot };
            WidgetsQueue.ClickTextAt(2); // search-free toolbar: sort, then filter button
            buildings.Draw(toolbarRect, component.Snapshot, menuState, settings);
            TestAssert.Check(Find.WindowStack.windows.Count > 0, "filter button opens a menu");
            var menu = Find.WindowStack.windows[0] as FloatMenu;
            TestAssert.NotNull(menu, "filter menu is a FloatMenu");
            TestAssert.Check(menu.options.Count >= 10, "filter menu lists the documented filters");
            Find.WindowStack.windows.Clear();
            WidgetsQueue.Reset();
        }

        /// <summary>Unrecognised power component used to exercise the unsupported path.</summary>
        private sealed class StrangeComponent : CompPower { }
    }

    /// <summary>Scripted widget results so click handlers can be exercised headlessly.</summary>
    public static class WidgetsQueue
    {
        private static readonly Queue<bool> ButtonInvisibleResults = new Queue<bool>();
        private static readonly Queue<bool> ButtonTextResults = new Queue<bool>();

        /// <summary>Clicks the (zero-based) invisible-button index in the next draw pass.</summary>
        public static void ClickInvisibleAt(int index)
        {
            for (int i = 0; i < index; i++) ButtonInvisibleResults.Enqueue(false);
            ButtonInvisibleResults.Enqueue(true);
        }

        /// <summary>Clicks the (zero-based) text-button index in the next draw pass.</summary>
        public static void ClickTextAt(int index)
        {
            for (int i = 0; i < index; i++) ButtonTextResults.Enqueue(false);
            ButtonTextResults.Enqueue(true);
        }

        public static void Reset()
        {
            ButtonInvisibleResults.Clear();
            ButtonTextResults.Clear();
        }

        public static bool NextButtonInvisible()
        {
            return ButtonInvisibleResults.Count > 0 && ButtonInvisibleResults.Dequeue();
        }

        public static bool NextButtonText()
        {
            return ButtonTextResults.Count > 0 && ButtonTextResults.Dequeue();
        }
    }
}
