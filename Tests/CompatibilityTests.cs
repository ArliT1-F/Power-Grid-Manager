// Phase 8 verification: compatibility and performance regressions.
//
// These checks are static source audits plus large synthetic maps against the game doubles.
// They cannot measure real frame times, prove binary compatibility with any game version, or
// replace an in-game save/load test; those limits are recorded in Docs/PHASE-8-COMPATIBILITY.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PowerGridManager.Tests
{
    public static class CompatibilityTests
    {
        public static int Run()
        {
            TestAssert.UsePhase("phase8");
            int start = TestAssert.Checks;
            SourceHygiene();
            MetadataAndNoDlc();
            LargeColony();
            MultiMapStaggering();
            ModdedPowerComponents();
            SaveLoadAndRemoval();
            WindowRegression();
            Console.WriteLine("Phase 8 compatibility/performance checks passed: " +
                              (TestAssert.Checks - start));
            return TestAssert.Checks - start;
        }

        private static PowerGridManagerSettings BasicSettings(bool management = false,
            bool automation = false)
        {
            var settings = new PowerGridManagerSettings
            {
                ManagementEnabled = management,
                LoadSheddingEnabled = automation,
                LoadShedStableTicks = 0,
                LoadShedCooldownTicks = 0,
                RefreshIntervalTicks = 120,
                HistoryEnabled = false
            };
            PowerGridManagerMod.Settings = settings;
            return settings;
        }

        // --- Static audits ----------------------------------------------------

        private static void SourceHygiene()
        {
            string root = FindRepositoryRoot();
            TestAssert.NotNull(root, "repository root located");
            string source = Path.Combine(root, "Source", "PowerGridManager");
            string[] files = Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories);
            TestAssert.Check(files.Length >= 20, "all production sources are present: " + files.Length);

            string[] forbidden =
            {
                "using System.Linq", ".Where(", ".Select(", ".OrderBy(", ".ToArray()",
                "System.Reflection", "BindingFlags", "GetField(", "GetProperty(",
                "using HarmonyLib", "[HarmonyPatch", "HarmonyLib.",
                "new Thread", "Task.Run", "ThreadPool", "BackgroundWorker",
                "Log.Error", "Log.ErrorOnce", "Log.WarningOnce"
            };
            string[] dlc =
            {
                "RimWorld.Biotech", "RimWorld.Ideology", "RimWorld.Royalty",
                "RimWorld.Anomaly", "RimWorld.Odyssey", "Verse.AI.Group"
            };

            for (int i = 0; i < files.Length; i++)
            {
                string text = StripComments(File.ReadAllText(files[i]));
                string name = Path.GetFileName(files[i]);
                for (int j = 0; j < forbidden.Length; j++)
                    TestAssert.Check(!text.Contains(forbidden[j]),
                        name + " avoids " + forbidden[j]);
                for (int j = 0; j < dlc.Length; j++)
                    TestAssert.Check(!text.Contains(dlc[j]), name + " has no DLC dependency");
            }

            // The tick hook must not scan the map directly, and the UI must never call the scanner.
            string componentText = File.ReadAllText(Path.Combine(source, "Core", "PowerGridManagerComponent.cs"));
            TestAssert.Check(componentText.Contains("RefreshIfStale(tick)"),
                "map tick uses the cached refresh gate");
            TestAssert.Check(!componentText.Contains("DoWindowContents"),
                "map component has no UI draw path");
            string windowText = File.ReadAllText(Path.Combine(source, "UI",
                "MainTabWindow_PowerGridManager.cs"));
            TestAssert.Check(!windowText.Contains("new PowerGridScanner"),
                "window never constructs a scanner");
            string controlText = File.ReadAllText(Path.Combine(source, "Integration",
                "BuildingPowerControl.cs"));
            TestAssert.Check(controlText.Contains("CompGetGizmosExtra") &&
                             controlText.Contains("FlickUtility.UpdateFlickDesignation"),
                "control path verifies and normalises the vanilla flick gizmo");
        }

        /// <summary>Removes line and block comments so documentation words are not mistaken for code.</summary>
        private static string StripComments(string text)
        {
            var result = new System.Text.StringBuilder(text.Length);
            bool block = false, line = false, str = false, verbatim = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (line)
                {
                    if (c == '\n') { line = false; result.Append('\n'); }
                    continue;
                }
                if (block)
                {
                    if (c == '*' && next == '/') { block = false; i++; }
                    continue;
                }
                if (str)
                {
                    if (verbatim && c == '"' && next == '"') { result.Append(c); i++; continue; }
                    if (c == '"' && (i == 0 || text[i - 1] != '\\')) str = false;
                    result.Append(c);
                    continue;
                }
                if (c == '/' && next == '/') { line = true; i++; continue; }
                if (c == '/' && next == '*') { block = true; i++; continue; }
                if (c == '@' && next == '"') { verbatim = true; str = true; result.Append(c); continue; }
                if (c == '"') { verbatim = false; str = true; result.Append(c); continue; }
                result.Append(c);
            }
            return result.ToString();
        }

        private static void MetadataAndNoDlc()
        {
            string root = FindRepositoryRoot();
            string aboutPath = Path.Combine(root, "About", "About.xml");
            XDocument about = XDocument.Load(aboutPath);
            var versions = new List<string>();
            foreach (XElement element in about.Root.Element("supportedVersions").Elements("li"))
                versions.Add(element.Value);
            TestAssert.Check(versions.Contains("1.5") && versions.Contains("1.6") &&
                             versions.Contains("1.7"), "all three builds are advertised");
            TestAssert.Null(about.Root.Element("dependencies"), "no hard mod dependencies");
            TestAssert.Null(about.Root.Element("modDependencies"), "no DLC/mod dependency list");
            TestAssert.Null(about.Root.Element("loadAfter"), "no forced load order");

            string defsPath = Path.Combine(root, "Defs", "MainButtonDefs", "PowerGridManager.xml");
            XDocument defs = XDocument.Load(defsPath);
            TestAssert.Check(defs.Root.Element("MainButtonDef") != null,
                "main button def is present and readable");

            // The build configuration must not require a DLC assembly or a Harmony package.
            string project = File.ReadAllText(Path.Combine(root, "Source", "PowerGridManager",
                "PowerGridManager.csproj"));
            TestAssert.Check(!project.ToLowerInvariant().Contains("harmony"),
                "production project has no Harmony reference");
            TestAssert.Check(!project.Contains("PackageReference"),
                "production project has no external NuGet packages");
            string build = File.ReadAllText(Path.Combine(root, "build.sh"));
            TestAssert.Check(build.Contains("Assembly-CSharp.dll"), "build references the game assembly");
        }

        // --- Performance ------------------------------------------------------

        private static void LargeColony()
        {
            PowerGridManagerSettings settings = BasicSettings(true, true);
            settings.LoadShedStableTicks = 0;
            settings.LoadShedCooldownTicks = 0;
            settings.LoadShedMaxActionsPerPass = 4;
            settings.LoadShedLowThreshold = 1f;
            settings.NormalizeManagement();

            var scenario = new PowerScenario();
            PowerNet net = scenario.AddNet();
            Building generator = scenario.AddBuilding(ThingDefFactory.Simple("generator"));
            scenario.AddTrader<CompPowerPlant>(net, generator, 50000f, true, -50000f);
            Building batteryBuilding = scenario.AddBuilding(ThingDefFactory.Simple("battery"));
            scenario.AddBattery(net, batteryBuilding, 100f, 1000f);

            const int count = 10000;
            var flickables = new List<Building>(count);
            for (int i = 0; i < count; i++)
            {
                Building building = scenario.AddBuilding(ThingDefFactory.Simple("lamp"));
                scenario.AddTrader(net, building, -10f, true, 10f);
                scenario.AddFlickable(building, true);
                flickables.Add(building);
            }

            var watch = Stopwatch.StartNew();
            PowerGridSnapshot snapshot = new PowerGridScanner().Scan(scenario.Map, 0);
            watch.Stop();
            TestAssert.Equal(snapshot.Buildings.Count, count + 2, "large map discovered every building");
            TestAssert.Check(watch.ElapsedMilliseconds < 5000,
                "large scan is linear and bounded: " + watch.ElapsedMilliseconds + "ms");

            var registry = new PowerManagementRegistry();
            for (int i = 0; i < count; i++)
                registry.SetSheddable(flickables[i], true, 0);
            TestAssert.Equal(registry.Count, PowerManagementRegistry.MaxRecords,
                "registry is hard-capped for very large colonies");

            var events = new PowerManagementEventLog();
            var state = FreshState();
            watch.Restart();
            LoadSheddingPlan plan = LoadSheddingEngine.Evaluate(snapshot, settings, registry, state, 0, false);
            watch.Stop();
            TestAssert.Check(watch.ElapsedMilliseconds < 5000,
                "policy evaluation over 10k buildings is bounded: " + watch.ElapsedMilliseconds + "ms");
            TestAssert.Check(plan.Ready, "large-colony plan is ready with zero stability");
            TestAssert.Check(plan.ToShed.Count <= settings.LoadShedMaxActionsPerPass,
                "action batch is capped on a large map");
            TestAssert.Equal(plan.CandidateCount, PowerManagementRegistry.MaxRecords,
                "candidate count respects the registry cap");

            // Preview caching: repeated requests must not repeat the O(n) work.
            var controller = new PowerManagementController();
            LoadSheddingPlan first = controller.Preview(snapshot, settings, 0);
            LoadSheddingPlan second = controller.Preview(snapshot, settings, 1);
            TestAssert.Same(first, second, "UI preview is cached for large colonies");

            // A disabled policy takes the constant-time path.
            BasicSettings(false, false);
            watch.Restart();
            LoadSheddingPlan disabled = LoadSheddingEngine.Evaluate(snapshot,
                PowerGridManagerMod.Settings, registry, state, 0, false);
            watch.Stop();
            TestAssert.Equal(disabled.Phase, LoadSheddingPhase.Disabled,
                "disabled policy produces no work");
            TestAssert.Check(watch.ElapsedMilliseconds < 100,
                "disabled policy is O(1): " + watch.ElapsedMilliseconds + "ms");
        }

        private static LoadSheddingState FreshState()
        {
            return new LoadSheddingState();
        }

        private static void MultiMapStaggering()
        {
            PowerGridManagerSettings settings = BasicSettings();
            var first = new PowerScenario().WithUniqueId(0);
            var second = new PowerScenario().WithUniqueId(37);
            var a = new PowerGridManagerComponent(first.Map);
            var b = new PowerGridManagerComponent(second.Map);
            first.Map.RegisterComponent(a);
            second.Map.RegisterComponent(b);

            Find.TickManager.TicksGame = 0;
            a.MapComponentTick();
            b.MapComponentTick();
            TestAssert.NotNull(a.Snapshot, "map with zero offset scans on the first tick");
            TestAssert.Null(b.Snapshot, "map with a non-zero offset is staggered");

            Find.TickManager.TicksGame = 36;
            a.MapComponentTick();
            b.MapComponentTick();
            TestAssert.Null(b.Snapshot, "staggered map still waits below its offset");

            Find.TickManager.TicksGame = 37;
            b.MapComponentTick();
            TestAssert.NotNull(b.Snapshot, "staggered map scans within one in-game second");

            // Opening the window forces a scan without waiting for the stagger window.
            var third = new PowerScenario().WithUniqueId(59);
            var c = new PowerGridManagerComponent(third.Map);
            third.Map.RegisterComponent(c);
            c.RefreshIfStale(0, true);
            TestAssert.NotNull(c.Snapshot, "window open forces an immediate scan");

            // The refresh gate still prevents a scan on every tick.
            PowerGridSnapshot cached = c.Snapshot;
            for (int tick = 1; tick < 60; tick++)
            {
                Find.TickManager.TicksGame = tick;
                c.MapComponentTick();
            }
            TestAssert.Same(c.Snapshot, cached, "no rescan within the refresh interval");
            Find.TickManager.TicksGame = 121;
            c.MapComponentTick();
            TestAssert.Check(!ReferenceEquals(c.Snapshot, cached), "rescan happens at the interval");
            TestAssert.Check(!settings.HistoryEnabled, "history stays off in this scenario");
        }

        // --- Modded components -----------------------------------------------

        private static void ModdedPowerComponents()
        {
            BasicSettings();
            var scenario = new PowerScenario();
            PowerNet net = scenario.AddNet();

            // A custom trader that follows the vanilla contract is measurable but labelled custom.
            Building traderBuilding = scenario.AddBuilding(ThingDefFactory.Simple("modded consumer"));
            var trader = new ModdedTrader { PowerOn = true, PowerOutput = -250f };
            trader.Props.PowerConsumption = 250f;
            scenario.Attach(traderBuilding, trader, net, true);
            net.powerComps.Add(trader);

            // A component that implements none of the recognised contracts is unsupported.
            Building strangeBuilding = scenario.AddBuilding(ThingDefFactory.Simple("modded reactor"));
            var strange = new StrangePower();
            scenario.Attach(strangeBuilding, strange, net, true);
            net.transmitters.Add(strange);

            // A modded battery is still a battery if it derives from CompPowerBattery.
            Building batteryBuilding = scenario.AddBuilding(ThingDefFactory.Simple("modded battery"));
            var battery = new ModdedBattery { StoredEnergy = 25f };
            battery.Props.storedEnergyMax = 100f;
            scenario.Attach(batteryBuilding, battery, net, true);
            net.batteryComps.Add(battery);

            PowerGridSnapshot snapshot = new PowerGridScanner().Scan(scenario.Map, 0);
            TestAssert.Equal(snapshot.ConsumptionWatts, 250f,
                "modded trader output is measured through the standard contract");
            TestAssert.Equal(snapshot.CapacityWattDays, 100f, "modded battery capacity is readable");
            TestAssert.Equal(snapshot.UnsupportedComponentCount, 1,
                "unrecognised component is counted, not guessed");
            TestAssert.Check(snapshot.HasIncompleteData, "unknown behaviour is surfaced");

            BuildingPowerSnapshot custom = null, unknown = null;
            for (int i = 0; i < snapshot.Buildings.Count; i++)
            {
                if (snapshot.Buildings[i].Building == traderBuilding) custom = snapshot.Buildings[i];
                if (snapshot.Buildings[i].Building == strangeBuilding) unknown = snapshot.Buildings[i];
            }
            TestAssert.NotNull(custom, "modded trader has a row");
            TestAssert.NotNull(unknown, "unsupported component has a row");
            TestAssert.Equal(CompatibilityRegistry.Evaluate(custom),
                PowerSupportLevel.CustomImplementation, "modded trader is labelled custom");
            TestAssert.Equal(CompatibilityRegistry.Evaluate(unknown),
                PowerSupportLevel.Unsupported, "unrecognised component is labelled unsupported");
            TestAssert.Equal(BuildingPowerControl.Evaluate(unknown, null),
                PowerControlCapability.UnsupportedComponents,
                "unsupported component is never a control target");

            // Invalid data must not poison totals or the policy.
            trader.PowerOutput = float.NaN;
            PowerGridSnapshot invalid = new PowerGridScanner().Scan(scenario.Map, 1);
            TestAssert.Check(!float.IsNaN(invalid.ConsumptionWatts),
                "NaN output cannot corrupt aggregate consumption");
            TestAssert.Check(invalid.HasIncompleteData, "invalid data is reported");
            trader.PowerOutput = -250f;

            // Destroyed modded infrastructure does not throw or leave a control target.
            strangeBuilding.Destroyed = true;
            PowerGridSnapshot afterDestruction = new PowerGridScanner().Scan(scenario.Map, 2);
            TestAssert.Check(afterDestruction.HasIncompleteData,
                "stale network membership is reported after destruction");
        }

        private sealed class ModdedTrader : CompPowerTrader { }
        private sealed class ModdedBattery : CompPowerBattery { }
        private sealed class StrangePower : CompPower { }

        // --- Save/load and removal -------------------------------------------

        private static void SaveLoadAndRemoval()
        {
            BasicSettings();
            var scenario = new PowerScenario();
            PowerNet net = scenario.AddNet();
            Building generator = scenario.AddBuilding(ThingDefFactory.Simple("generator"));
            scenario.AddTrader<CompPowerPlant>(net, generator, 1000f, true, -1000f);
            var component = new PowerGridManagerComponent(scenario.Map);
            scenario.Map.RegisterComponent(component);
            component.History.MaybeSample(new PowerGridScanner().Scan(scenario.Map, 0), 0,
                2500, 7 * PowerTime.TicksPerDay);
            component.Management.SetGroup(generator, "Generation", 0);
            component.Management.Events.Add(0, PowerManagementEventKind.PolicyChanged, "",
                "policy", true, "test");
            // SetGroup writes an audit event too; assert against the real saved length.
            int expectedEvents = component.Management.Events.Count;

            SaveNode node = Scribe.BeginSaving();
            component.ExposeData();
            Scribe.Finish();
            var loaded = new PowerGridManagerComponent(scenario.Map);
            Scribe.BeginLoading(node);
            loaded.ExposeData();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            loaded.ExposeData();
            Scribe.Finish();
            TestAssert.Equal(loaded.History.Count, 1, "history survives a full component round trip");
            TestAssert.Equal(loaded.Management.Events.Count, expectedEvents,
                "event log survives the round trip");
            TestAssert.Equal(loaded.Management.Events[expectedEvents - 1].Detail, "test",
                "the newest event detail survives the round trip");
            TestAssert.Null(loaded.Snapshot, "runtime snapshot is not persisted");

            // Removing the mod's data node must not break the map component or its history.
            SaveNode trimmed = Scribe.BeginSaving();
            component.ExposeData();
            Scribe.Finish();
            trimmed.children.RemoveAll(c => c.name == "powerManagement");
            trimmed.children.RemoveAll(c => c.name == "powerHistory");
            var clean = new PowerGridManagerComponent(scenario.Map);
            Scribe.BeginLoading(trimmed);
            clean.ExposeData();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            clean.ExposeData();
            Scribe.Finish();
            TestAssert.NotNull(clean.Management, "missing management node is recreated");
            TestAssert.NotNull(clean.History, "missing history node is recreated");
            TestAssert.Equal(clean.Management.Registry.Count, 0, "no phantom preferences");

            // Map removal clears everything without touching game objects.
            clean.MapRemoved();
            TestAssert.Null(clean.Snapshot, "map removal drops the snapshot");
            TestAssert.Equal(clean.History.Count, 0, "map removal drops history");
            TestAssert.Equal(clean.Management.Events.Count, 0, "map removal drops management events");
        }

        // --- Window regression -----------------------------------------------

        private static void WindowRegression()
        {
            BasicSettings(true, false);
            var scenario = new PowerScenario();
            PowerNet net = scenario.AddNet();
            Building generator = scenario.AddBuilding(ThingDefFactory.Simple("generator"));
            scenario.AddTrader<CompPowerPlant>(net, generator, 1000f, true, -1000f);
            var component = new PowerGridManagerComponent(scenario.Map);
            scenario.Map.RegisterComponent(component);
            Find.TickManager.TicksGame = 0;
            component.MapComponentTick();

            var window = new MainTabWindow_PowerGridManager();
            window.PostOpen();
            PowerGridTab[] tabs =
            {
                PowerGridTab.Overview, PowerGridTab.Networks, PowerGridTab.Buildings,
                PowerGridTab.History, PowerGridTab.Diagnostics, PowerGridTab.Forecast,
                PowerGridTab.Management
            };
            Rect[] sizes =
            {
                new Rect(0f, 0f, 320f, 180f),
                new Rect(0f, 0f, 1010f, 660f),
                new Rect(0f, 0f, 2560f, 1400f)
            };
            foreach (Rect size in sizes)
            {
                foreach (PowerGridTab tab in tabs)
                {
                    window.UiState.Tab = tab;
                    window.DoWindowContents(size);
                    TestAssert.Check(true, "tab " + tab + " renders at " + size.width + "px");
                }
            }
            TestAssert.Equal(Find.WindowStack.windows.Count, 0,
                "rendering never opens an unexpected window");
        }

        private static string FindRepositoryRoot()
        {
            var candidates = new List<string>
            {
                Environment.GetEnvironmentVariable("PGM_REPO_ROOT"),
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory()
            };
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                DirectoryInfo directory;
                try { directory = new DirectoryInfo(Path.GetFullPath(candidate)); }
                catch (ArgumentException) { continue; }
                for (int i = 0; i < 10 && directory != null; i++)
                {
                    if (File.Exists(Path.Combine(directory.FullName, "About", "About.xml")))
                        return directory.FullName;
                    directory = directory.Parent;
                }
            }
            return null;
        }
    }
}
