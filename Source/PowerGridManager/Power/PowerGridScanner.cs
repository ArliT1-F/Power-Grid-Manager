using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PowerGridManager
{
    /// <summary>Read-only, main-thread scanner. No simulation calls, reflection, patches or controls.</summary>
    public sealed class PowerGridScanner
    {
        private readonly Dictionary<CompPower, int> membership = new Dictionary<CompPower, int>();
        private readonly HashSet<CompPower> observed = new HashSet<CompPower>();
        private readonly HashSet<Building> buildingsSeen = new HashSet<Building>();

        public PowerGridSnapshot Scan(Map map, int tick)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            membership.Clear();
            observed.Clear();
            buildingsSeen.Clear();
            var networks = new List<PowerNetworkSnapshot>();
            var buildings = new List<BuildingPowerSnapshot>();
            var result = new PowerGridSnapshot { Tick = tick };
            try
            {
                if (map.powerNetManager == null) result.HasIncompleteData = true;
                else
                {
                    var nets = map.powerNetManager.AllNetsListForReading;
                    for (int i = 0; i < nets.Count; i++)
                    {
                        var network = new PowerNetworkSnapshot { Id = i + 1 };
                        networks.Add(network);
                        Register(nets[i].powerComps, i, networks);
                        Register(nets[i].batteryComps, i, networks);
                        Register(nets[i].transmitters, i, networks, true);
                        Register(nets[i].connectors, i, networks, true);
                    }
                }
                ScanBuildings(map.listerBuildings.allBuildingsColonist, map, buildings, networks, result);
                ScanBuildings(map.listerBuildings.allBuildingsNonColonist, map, buildings, networks, result);
                foreach (var entry in membership)
                    if (!observed.Contains(entry.Key)) networks[entry.Value].HasIncompleteData = true;
                for (int i = 0; i < networks.Count; i++)
                {
                    var n = networks[i];
                    result.GenerationWatts += n.GenerationWatts;
                    result.ConsumptionWatts += n.ConsumptionWatts;
                    result.StoredWattDays += n.StoredWattDays;
                    result.AvailableStoredWattDays += n.AvailableStoredWattDays;
                    result.CapacityWattDays += n.CapacityWattDays;
                    if (n.NetWatts < -0.01f) result.DeficitNetworkCount++;
                    result.HasIncompleteData |= n.HasIncompleteData;
                }
                result.Buildings = buildings.AsReadOnly();
                result.Networks = networks.AsReadOnly();
                return result;
            }
            finally
            {
                // Do not retain despawned objects between scans or after a failed scan.
                membership.Clear();
                observed.Clear();
                buildingsSeen.Clear();
            }
        }

        private void Register<T>(List<T> comps, int index, List<PowerNetworkSnapshot> networks, bool connectionsOnly = false) where T : CompPower
        {
            for (int i = 0; i < comps.Count; i++)
            {
                var comp = comps[i];
                if (comp == null) { networks[index].HasIncompleteData = true; continue; }
                // Traders and batteries must appear in their simulation lists, not merely connectors.
                if (connectionsOnly && (comp is CompPowerTrader || comp is CompPowerBattery)) continue;
                int previous;
                if (membership.TryGetValue(comp, out previous) && previous != index)
                {
                    networks[previous].HasIncompleteData = true;
                    networks[index].HasIncompleteData = true;
                }
                else membership[comp] = index;
            }
        }

        private void ScanBuildings(List<Building> source, Map map, List<BuildingPowerSnapshot> buildings,
            List<PowerNetworkSnapshot> networks, PowerGridSnapshot result)
        {
            for (int i = 0; i < source.Count; i++)
            {
                var building = source[i];
                if (building == null || building.Destroyed || !building.Spawned || building.Map != map || !buildingsSeen.Add(building)) continue;
                List<PowerComponentSnapshot> components = null;
                bool disconnectedConsumer = false;
                var comps = building.AllComps;
                for (int j = 0; j < comps.Count; j++)
                {
                    var power = comps[j] as CompPower;
                    if (power == null) continue;
                    if (components == null) components = new List<PowerComponentSnapshot>();
                    var sample = Observe(power, j);
                    components.Add(sample);
                    observed.Add(power);
                    int index;
                    bool registered = membership.TryGetValue(power, out index);
                    if (registered && power.PowerNet == map.powerNetManager.AllNetsListForReading[index])
                    {
                        sample.Connection = PowerConnectionState.Connected;
                        sample.NetworkId = index + 1;
                        Accumulate(networks[index], sample);
                    }
                    else
                    {
                        sample.Connection = power.PowerNet == null && !registered
                            ? PowerConnectionState.NoNetwork : PowerConnectionState.MembershipPending;
                        if (registered) networks[index].HasIncompleteData = true;
                        if (sample.Connection == PowerConnectionState.MembershipPending) result.HasIncompleteData = true;
                    }
                    disconnectedConsumer |= sample.IsConsumer && sample.Connection == PowerConnectionState.NoNetwork;
                    if (sample.Behavior == PowerBehavior.Unsupported) result.UnsupportedComponentCount++;
                    result.HasIncompleteData |= sample.HasInvalidData || sample.Behavior == PowerBehavior.Unsupported;
                }
                if (components == null) continue;
                var flick = building.GetComp<CompFlickable>();
                buildings.Add(new BuildingPowerSnapshot { Building = building,
                    SwitchIsOn = flick == null ? (bool?)null : flick.SwitchIsOn, Components = components.AsReadOnly() });
                if (disconnectedConsumer) result.DisconnectedConsumerCount++;
            }
        }

        private static PowerComponentSnapshot Observe(CompPower power, int index)
        {
            var s = new PowerComponentSnapshot { ComponentIndex = index,
                IsCustomImplementation = power.GetType().Assembly != typeof(CompPower).Assembly };
            var trader = power as CompPowerTrader;
            if (trader != null)
            {
                s.Behavior = PowerBehavior.Trader;
                s.PowerOn = trader.PowerOn;
                float output = trader.PowerOutput;
                float nominal = trader.Props.PowerConsumption;
                if (Finite(output)) s.ReportedOutputWatts = output; else s.HasInvalidData = true;
                if (Finite(nominal)) s.NominalConsumptionWatts = nominal; else s.HasInvalidData = true;
                s.IsGenerator = trader is CompPowerPlant || output > 0 || nominal < 0;
                s.IsConsumer = output < 0 || nominal > 0;
                // Match PowerNet.CurrentEnergyGainRate: off traders contribute no current draw.
                if (trader.PowerOn && s.ReportedOutputWatts.HasValue)
                {
                    s.GenerationWatts = Math.Max(0, output);
                    s.ConsumptionWatts = Math.Max(0, -output);
                }
            }
            else if (power is CompPowerBattery battery)
            {
                s.Behavior = PowerBehavior.Battery;
                float stored = battery.StoredEnergy;
                float capacity = battery.Props.storedEnergyMax;
                if (Finite(stored) && stored >= 0) s.StoredWattDays = stored; else s.HasInvalidData = true;
                if (Finite(capacity) && capacity >= 0) s.CapacityWattDays = capacity; else s.HasInvalidData = true;
                s.AvailableStoredWattDays = battery.StunnedByEMP ? 0 : s.StoredWattDays;
                float efficiency = battery.Props.efficiency;
                if (Finite(efficiency) && efficiency > 0 && efficiency <= 1) s.ChargeEfficiency = efficiency;
                else s.HasInvalidData = true;
            }
            else s.Behavior = power is CompPowerTransmitter ? PowerBehavior.ConnectionOnly : PowerBehavior.Unsupported;
            return s;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void Accumulate(PowerNetworkSnapshot n, PowerComponentSnapshot s)
        {
            if (s.IsConsumer) n.ConsumerCount++;
            if (s.IsGenerator) n.GeneratorCount++;
            if (s.Behavior == PowerBehavior.Battery) n.BatteryCount++;
            n.GenerationWatts += s.GenerationWatts;
            n.ConsumptionWatts += s.ConsumptionWatts;
            n.StoredWattDays += s.StoredWattDays;
            n.AvailableStoredWattDays += s.AvailableStoredWattDays;
            n.CapacityWattDays += s.CapacityWattDays;
            n.HasIncompleteData |= s.HasInvalidData || s.Behavior == PowerBehavior.Unsupported;
        }
    }
}
