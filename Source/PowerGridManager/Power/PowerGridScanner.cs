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
                ScanBuildings(map, map.listerBuildings.allBuildingsColonist, true, buildings, networks, result);
                ScanBuildings(map, map.listerBuildings.allBuildingsNonColonist, false, buildings, networks, result);
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
                    if (n.HasIncompleteData)
                    {
                        result.IncompleteDataCount++;
                        result.HasIncompleteData = true;
                    }
                }
                ComputeNominalDemand(buildings, result);
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

        private void ScanBuildings(Map map, List<Building> source, bool colonist, List<BuildingPowerSnapshot> buildings,
            List<PowerNetworkSnapshot> networks, PowerGridSnapshot result)
        {
            for (int i = 0; i < source.Count; i++)
            {
                var building = source[i];
                if (building == null || building.Destroyed || !building.Spawned || building.Map != map || !buildingsSeen.Add(building)) continue;
                List<PowerComponentSnapshot> components = null;
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
                        Accumulate(networks[index], sample, result);
                    }
                    else
                    {
                        sample.Connection = power.PowerNet == null && !registered
                            ? PowerConnectionState.NoNetwork : PowerConnectionState.MembershipPending;
                        if (registered) networks[index].HasIncompleteData = true;
                    }
                    if (sample.Behavior == PowerBehavior.Unsupported) result.UnsupportedComponentCount++;
                }
                if (components == null) continue;
                var flick = building.GetComp<CompFlickable>();
                var buildingSnapshot = new BuildingPowerSnapshot
                {
                    Building = building,
                    IsColonistBuilding = colonist,
                    IsFlickable = flick != null,
                    SwitchIsOn = flick == null ? (bool?)null : flick.SwitchIsOn,
                    Components = components.AsReadOnly()
                };
                AggregateBuilding(buildingSnapshot, components, result);
                buildings.Add(buildingSnapshot);
            }
        }

        /// <summary>
        /// Rolls a building's components into building-level totals. Only components
        /// whose network membership is confirmed contribute to current power totals;
        /// the rest are reported through <see cref="PowerConnectionState"/>.
        /// </summary>
        private static void AggregateBuilding(BuildingPowerSnapshot building, List<PowerComponentSnapshot> components,
            PowerGridSnapshot result)
        {
            float generation = 0f, consumption = 0f, stored = 0f, capacity = 0f, nominal = 0f, configured = 0f;
            bool configuredValid = true;
            bool nominalValid = true, hasConsumer = false, hasGenerator = false, hasBattery = false;
            bool hasOff = false, unsupported = false, invalid = false, anyNoNetwork = false, anyPending = false;
            int networkId = 0;
            bool multipleNetworks = false;

            for (int i = 0; i < components.Count; i++)
            {
                PowerComponentSnapshot component = components[i];
                if (component.IsConsumer)
                {
                    hasConsumer = true;
                    if (component.NominalConsumptionWatts.HasValue) nominal += component.NominalConsumptionWatts.Value;
                    else nominalValid = false;
                }
                if (component.IsGenerator)
                {
                    hasGenerator = true;
                    if (component.ConfiguredOutputWatts.HasValue) configured += component.ConfiguredOutputWatts.Value;
                    else configuredValid = false;
                }
                if (component.Behavior == PowerBehavior.Battery) hasBattery = true;
                if (component.PowerOn == false) hasOff = true;
                if (component.Behavior == PowerBehavior.Unsupported) unsupported = true;
                if (component.HasInvalidData) invalid = true;

                switch (component.Connection)
                {
                    case PowerConnectionState.Connected:
                        generation += component.GenerationWatts;
                        consumption += component.ConsumptionWatts;
                        stored += component.StoredWattDays;
                        capacity += component.CapacityWattDays;
                        int id = component.NetworkId ?? 0;
                        if (id != 0)
                        {
                            if (networkId == 0) networkId = id;
                            else if (networkId != id) multipleNetworks = true;
                        }
                        break;
                    case PowerConnectionState.NoNetwork:
                        anyNoNetwork = true;
                        break;
                    default:
                        anyPending = true;
                        break;
                }
            }

            building.IsConsumer = hasConsumer;
            building.IsGenerator = hasGenerator;
            building.IsBattery = hasBattery;
            building.HasOffComponents = hasOff;
            building.GenerationWatts = generation;
            building.ConsumptionWatts = consumption;
            building.StoredWattDays = stored;
            building.CapacityWattDays = capacity;
            building.NominalConsumptionWatts = nominalValid && hasConsumer ? (float?)nominal : null;
            building.ConfiguredGenerationWatts = configuredValid && hasGenerator ? (float?)configured : null;
            building.HasUnsupportedComponent = unsupported;
            building.HasInvalidData = invalid;
            building.HasMultipleNetworks = multipleNetworks;
            building.NetworkId = multipleNetworks || networkId == 0 ? (int?)null : networkId;
            building.Connection = anyNoNetwork
                ? PowerConnectionState.NoNetwork
                : anyPending ? PowerConnectionState.MembershipPending : PowerConnectionState.Connected;

            if (hasConsumer && building.Connection == PowerConnectionState.NoNetwork) result.DisconnectedConsumerCount++;
            if (unsupported || invalid || building.Connection != PowerConnectionState.Connected)
            {
                result.IncompleteDataCount++;
                result.HasIncompleteData = true;
            }

            building.Category = BuildingClassifier.Classify(building.Building, components);

            // Room association is optional by design: many buildings are outdoors or
            // have no district, and not every modded map exposes one.
            building.Room = null;
            building.HasRoom = false;
            if (building.Building != null && building.Building.Spawned)
            {
                Room room = building.Building.GetRoom();
                if (room != null)
                {
                    building.Room = room;
                    building.HasRoom = room.Role != null;
                }
            }
        }

        /// <summary>
        /// Map-level configured draw. It is only reported when every consumer in the
        /// snapshot exposed a usable configured value, so the number is never a
        /// silently partial total.
        /// </summary>
        private static void ComputeNominalDemand(List<BuildingPowerSnapshot> buildings, PowerGridSnapshot result)
        {
            float total = 0f;
            bool anyConsumer = false;
            bool complete = true;
            float configuredTotal = 0f;
            bool anyGenerator = false;
            bool configuredComplete = true;
            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingPowerSnapshot building = buildings[i];
                if (building.IsConsumer)
                {
                    anyConsumer = true;
                    if (building.NominalConsumptionWatts.HasValue) total += building.NominalConsumptionWatts.Value;
                    else complete = false;
                }
                if (building.IsGenerator)
                {
                    anyGenerator = true;
                    if (building.ConfiguredGenerationWatts.HasValue) configuredTotal += building.ConfiguredGenerationWatts.Value;
                    else configuredComplete = false;
                }
            }
            result.NominalConsumptionWatts = anyConsumer && complete ? (float?)total : null;
            result.ConfiguredGenerationWatts = anyGenerator && configuredComplete ? (float?)configuredTotal : null;
        }

        private static PowerComponentSnapshot Observe(CompPower power, int index)
        {
            var s = new PowerComponentSnapshot
            {
                ComponentIndex = index,
                IsCustomImplementation = power.GetType().Assembly != typeof(CompPower).Assembly
            };
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
                // Generators declare their configured output as a negative consumption value.
                if (s.IsGenerator && nominal < 0 && Finite(nominal)) s.ConfiguredOutputWatts = -nominal;
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

        private static void Accumulate(PowerNetworkSnapshot n, PowerComponentSnapshot s, PowerGridSnapshot result)
        {
            if (s.IsConsumer)
            {
                n.ConsumerCount++;
                result.ConsumerCount++;
            }
            if (s.IsGenerator)
            {
                n.GeneratorCount++;
                result.GeneratorCount++;
            }
            if (s.Behavior == PowerBehavior.Battery)
            {
                n.BatteryCount++;
                result.BatteryCount++;
            }
            n.GenerationWatts += s.GenerationWatts;
            n.ConsumptionWatts += s.ConsumptionWatts;
            n.StoredWattDays += s.StoredWattDays;
            n.AvailableStoredWattDays += s.AvailableStoredWattDays;
            n.CapacityWattDays += s.CapacityWattDays;
            n.HasIncompleteData |= s.HasInvalidData || s.Behavior == PowerBehavior.Unsupported;
        }
    }
}
