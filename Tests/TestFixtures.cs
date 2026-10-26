// Shared assertion helpers and scenario builders for the offline verification harness.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PowerGridManager.Tests
{
    public static class TestAssert
    {
        public static int Checks;
        private static string phase = "";

        public static void UsePhase(string name)
        {
            phase = name;
        }

        public static void Check(bool condition, string message)
        {
            Checks++;
            if (!condition) throw new Exception("[" + phase + "] " + message);
        }

        public static void Equal(float actual, float expected, string message)
        {
            Check(Math.Abs(actual - expected) < 0.001f, message + ": expected " + expected + " but was " + actual);
        }

        public static void Equal(int actual, int expected, string message)
        {
            Check(actual == expected, message + ": expected " + expected + " but was " + actual);
        }

        public static void Equal(string actual, string expected, string message)
        {
            Check(actual == expected, message + ": expected '" + expected + "' but was '" + actual + "'");
        }

        public static void Equal<T>(T actual, T expected, string message) where T : struct
        {
            Check(EqualityComparer<T>.Default.Equals(actual, expected),
                message + ": expected " + expected + " but was " + actual);
        }

        public static void Same(object actual, object expected, string message)
        {
            Check(ReferenceEquals(actual, expected), message + ": expected the same instance");
        }

        public static void Null(object value, string message)
        {
            Check(value == null, message + ": expected null");
        }

        public static void NotNull(object value, string message)
        {
            Check(value != null, message + ": expected non-null");
        }

        public static void Throws(Action action, string message)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                Checks++;
                return;
            }
            throw new Exception("[" + phase + "] " + message + ": expected an exception");
        }
    }

    /// <summary>Fluent-ish builder for map/power fixtures.</summary>
    public sealed class PowerScenario
    {
        public readonly Map Map = new Map();
        public readonly List<PowerNet> Nets = new List<PowerNet>();
        private static int nextThingId = 1;

        public PowerScenario()
        {
            Find.CurrentMap = Map;
        }

        /// <summary>Provides a persisted map ID for the multi-map staggering tests.</summary>
        public PowerScenario WithUniqueId(int id)
        {
            Map.uniqueID = id;
            return this;
        }

        public PowerNet AddNet()
        {
            var net = new PowerNet();
            Map.powerNetManager.AllNetsListForReading.Add(net);
            Nets.Add(net);
            return net;
        }

        public Building CreateBuilding(ThingDef def)
        {
            return new Building { def = def, Map = Map, thingIDNumber = nextThingId++ };
        }

        public Building AddBuilding(ThingDef def)
        {
            return CreateBuilding(def);
        }

        public T AddTrader<T>(PowerNet net, Building building, float output, bool powerOn = true, float nominal = 0f,
            bool colonist = true) where T : CompPowerTrader, new()
        {
            var comp = new T { PowerOn = powerOn, PowerOutput = output };
            comp.Props.PowerConsumption = nominal;
            Attach(building, comp, net, colonist);
            if (net != null) net.powerComps.Add(comp);
            return comp;
        }

        public CompPowerTrader AddTrader(PowerNet net, Building building, float output, bool powerOn = true,
            float nominal = 0f, bool colonist = true)
        {
            return AddTrader<CompPowerTrader>(net, building, output, powerOn, nominal, colonist);
        }

        public CompPowerBattery AddBattery(PowerNet net, Building building, float stored, float capacity,
            bool stunned = false, float efficiency = 0.5f, bool colonist = true)
        {
            var comp = new CompPowerBattery { StoredEnergy = stored, StunnedByEMP = stunned };
            comp.Props.storedEnergyMax = capacity;
            comp.Props.efficiency = efficiency;
            Attach(building, comp, net, colonist);
            if (net != null) net.batteryComps.Add(comp);
            return comp;
        }

        public CompFlickable AddFlickable(Building building, bool on)
        {
            var flick = new CompFlickable { SwitchIsOn = on };
            flick.parent = building;
            building.AllComps.Add(flick);
            return flick;
        }

        public void Attach(Building building, CompPower comp, PowerNet net, bool colonist)
        {
            if (building.Map == null) building.Map = Map;
            if (!building.Spawned) building.Spawned = true;
            comp.parent = building;
            comp.PowerNet = net;
            if (building.def == null) building.def = ThingDefFactory.Simple("TestBuilding");
            if (!building.AllComps.Contains(comp)) building.AllComps.Add(comp);
            var list = colonist ? Map.listerBuildings.allBuildingsColonist : Map.listerBuildings.allBuildingsNonColonist;
            if (!list.Contains(building)) list.Add(building);
        }
    }

    public static class ThingDefFactory
    {
        private static int counter;

        public static ThingDef Simple(string label, params Type[] comps)
        {
            var def = new ThingDef
            {
                defName = label,
                label = label,
                thingClass = typeof(Building),
                building = new BuildingProperties()
            };
            counter++;
            if (comps != null) def.compTypes.AddRange(comps);
            return def;
        }

        public static ThingDef WithCategory(string label, string categoryDefName)
        {
            var def = Simple(label);
            var category = new ThingCategoryDef { defName = categoryDefName };
            def.thingCategories.Add(category);
            return def;
        }
    }
}
