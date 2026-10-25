// Minimal contracts for scanner logic tests, NOT proof of game API compatibility.
using System.Collections.Generic;
namespace Verse
{
    public class ThingComp { public Building parent; }
    public class Building
    {
        public bool Destroyed, Spawned = true;
        public Map Map;
        public List<ThingComp> AllComps = new List<ThingComp>();
        public T GetComp<T>() where T : ThingComp
        { foreach (var c in AllComps) if (c is T t) return t; return null; }
    }
    public class ListerBuildings
    {
        public List<Building> allBuildingsColonist = new List<Building>();
        public List<Building> allBuildingsNonColonist = new List<Building>();
    }
    public class Map
    {
        public RimWorld.PowerNetManager powerNetManager = new RimWorld.PowerNetManager();
        public ListerBuildings listerBuildings = new ListerBuildings();
    }
    public class MapComponent
    {
        public readonly Map map;
        public MapComponent(Map map) { this.map = map; }
        public virtual void MapComponentTick() { }
    }
    public class TickManager { public int TicksGame; }
    public static class Find { public static TickManager TickManager = new TickManager(); }
}
namespace RimWorld
{
    public class CompProperties_Power { public float PowerConsumption; }
    public class CompProperties_Battery { public float storedEnergyMax, efficiency = 0.5f; }
    public abstract class CompPower : Verse.ThingComp { public PowerNet PowerNet; }
    public class CompPowerTrader : CompPower
    {
        public bool PowerOn;
        public float PowerOutput;
        public CompProperties_Power Props = new CompProperties_Power();
    }
    public class CompPowerPlant : CompPowerTrader { }
    public class CompPowerBattery : CompPower
    {
        public float StoredEnergy;
        public bool StunnedByEMP;
        public CompProperties_Battery Props = new CompProperties_Battery();
    }
    public class CompPowerTransmitter : CompPower { }
    public class CompFlickable : Verse.ThingComp { public bool SwitchIsOn = true; }
    public class PowerNet
    {
        public List<CompPowerTrader> powerComps = new List<CompPowerTrader>();
        public List<CompPowerBattery> batteryComps = new List<CompPowerBattery>();
        public List<CompPower> transmitters = new List<CompPower>();
        public List<CompPower> connectors = new List<CompPower>();
    }
    public class PowerNetManager { public List<PowerNet> AllNetsListForReading = new List<PowerNet>(); }
}
namespace PowerGridManager
{
    public class PowerGridManagerSettings { public int RefreshIntervalTicks = 120; }
    public static class PowerGridManagerMod { public static PowerGridManagerSettings Settings; }
}
