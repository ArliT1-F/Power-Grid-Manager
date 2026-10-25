using System;
using RimWorld;
using Verse;
using PowerGridManager;

static class MonitoringTests
{
    static int checks;
    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception("[phase2] " + message); }
    static void Equal(float actual, float expected, string message)
    { Check(Math.Abs(actual - expected) < 0.001f, message + ": " + actual); }
    static PowerNet Net(Map map)
    { var n = new PowerNet(); map.powerNetManager.AllNetsListForReading.Add(n); return n; }
    static T Add<T>(Map map, PowerNet net, T comp, bool colonist = true) where T : CompPower
    {
        var b = new Building { Map = map }; b.AllComps.Add(comp); comp.parent = b; comp.PowerNet = net;
        (colonist ? map.listerBuildings.allBuildingsColonist : map.listerBuildings.allBuildingsNonColonist).Add(b);
        if (net != null)
        {
            if (comp is CompPowerTrader t) net.powerComps.Add(t);
            else if (comp is CompPowerBattery battery) net.batteryComps.Add(battery);
            else net.transmitters.Add(comp);
        }
        return comp;
    }
    sealed class UnknownPower : CompPower { }
    public static int Run()
    {
        var scan = new PowerGridScanner(); var map = new Map();
        var empty = scan.Scan(map, 0);
        Check(empty.Networks.Count == 0 && empty.Buildings.Count == 0 && !empty.HasIncompleteData, "empty map");
        var n = Net(map);
        var plant = Add(map, n, new CompPowerPlant { PowerOn = true, PowerOutput = 1000 });
        var user = Add(map, n, new CompPowerTrader { PowerOn = true, PowerOutput = -400 }, false);
        user.Props.PowerConsumption = 400;
        var battery = Add(map, n, new CompPowerBattery { StoredEnergy = 300 });
        battery.Props.storedEnergyMax = 600;
        var s = scan.Scan(map, 120);
        Equal(s.GenerationWatts, 1000, "generation"); Equal(s.ConsumptionWatts, 400, "consumption");
        Equal(s.NetWatts, 600, "balance"); Equal(s.StoredWattDays, 300, "energy in Wd");
        Equal(s.Networks[0].ChargeFraction.Value, 0.5f, "charge");
        Check(s.Networks[0].ConsumerCount == 1 && s.Networks[0].GeneratorCount == 1, "role counts");
        user.PowerOn = false; user.parent.AllComps.Add(new CompFlickable { SwitchIsOn = false });
        s = scan.Scan(map, 240);
        Equal(s.ConsumptionWatts, 0, "off traders are excluded");
        Equal(s.Buildings[2].Components[0].ReportedOutputWatts.Value, -400, "reported demand retained");
        Check(s.Buildings[2].SwitchIsOn == false, "flick state independent");
        user.PowerOn = true; user.PowerOutput = -1500;
        battery.StunnedByEMP = true;
        s = scan.Scan(map, 360);
        Check(s.DeficitNetworkCount == 1, "deficit");
        Equal(s.AvailableStoredWattDays, 0, "EMP energy unavailable"); Equal(s.StoredWattDays, 300, "physical energy retained");
        var other = Net(map);
        Add(map, other, new CompPowerTrader { PowerOn = true, PowerOutput = -100 });
        var orphan = Add(map, null, new CompPowerTrader { PowerOutput = -200 });
        s = scan.Scan(map, 480);
        Check(s.Networks.Count == 2 && s.DisconnectedConsumerCount == 1, "independent nets and orphan");
        Equal(s.ConsumptionWatts, 1600, "orphans excluded from totals");
        orphan.PowerNet = other; // Not registered yet: do not guess membership.
        s = scan.Scan(map, 600);
        Check(s.DisconnectedConsumerCount == 0 && s.HasIncompleteData, "pending topology");
        plant.parent.Destroyed = true;
        s = scan.Scan(map, 720);
        Equal(s.GenerationWatts, 0, "destroyed generator excluded"); Check(s.HasIncompleteData, "stale membership flagged");
        n.powerComps.Remove(plant);
        battery.parent.Spawned = false; n.batteryComps.Remove(battery);
        s = scan.Scan(map, 840); Equal(s.CapacityWattDays, 0, "despawned battery removed");
        user.PowerOutput = float.NaN;
        Add(map, other, new UnknownPower());
        s = scan.Scan(map, 960);
        Check(s.UnsupportedComponentCount == 1 && s.HasIncompleteData, "unsupported and invalid data");
        Check(!float.IsNaN(s.ConsumptionWatts), "invalid data cannot poison totals");
        user.PowerOutput = -230;
        Equal(scan.Scan(map, 1080).ConsumptionWatts, 330, "variable demand refreshed");
        Equal(empty.ConsumptionWatts, 0, "old snapshots unchanged");
        var cache = new PowerGridManagerComponent(map);
        Find.TickManager.TicksGame = 10; cache.MapComponentTick(); var first = cache.Snapshot;
        Find.TickManager.TicksGame = 129; cache.MapComponentTick(); Check(ReferenceEquals(first, cache.Snapshot), "cache cadence");
        Find.TickManager.TicksGame = 130; cache.MapComponentTick(); Check(!ReferenceEquals(first, cache.Snapshot), "scheduled refresh");
        Find.TickManager.TicksGame = 1; cache.MapComponentTick(); Check(cache.Snapshot.Tick == 1, "tick rollback");
        var compoundMap = new Map(); var compoundNet = Net(compoundMap);
        var a = Add(compoundMap, compoundNet, new CompPowerTrader { PowerOn = true, PowerOutput = -50 });
        var b = new CompPowerTrader { parent = a.parent, PowerNet = compoundNet, PowerOn = true, PowerOutput = -75 };
        a.parent.AllComps.Add(b); compoundNet.powerComps.Add(b);
        compoundMap.listerBuildings.allBuildingsNonColonist.Add(a.parent); // Duplicate lister entry.
        s = scan.Scan(compoundMap, 0);
        Check(s.Buildings.Count == 1 && s.Buildings[0].Components.Count == 2, "compound building and deduplication");
        Equal(s.ConsumptionWatts, 125, "all components counted once");
        compoundNet.powerComps.Remove(b); compoundNet.connectors.Add(b);
        s = scan.Scan(compoundMap, 1);
        Equal(s.ConsumptionWatts, 50, "connector-only trader is not simulated membership");
        Check(s.Buildings[0].Components[1].Connection == PowerConnectionState.MembershipPending, "connector-only trader pending");
        var large = new Map(); var largeNet = Net(large);
        for (int i = 0; i < 10000; i++) Add(large, largeNet, new CompPowerTrader { PowerOn = true, PowerOutput = -1 });
        s = scan.Scan(large, 0); Equal(s.ConsumptionWatts, 10000, "large map totals");
        Check(s.Buildings.Count == 10000, "large map discovery");
        Console.WriteLine("Phase 2 monitoring checks passed: " + checks);
        return checks;
    }
}
