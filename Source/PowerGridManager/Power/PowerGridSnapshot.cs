using System.Collections.Generic;
using Verse;

namespace PowerGridManager
{
    public enum PowerConnectionState { Connected, NoNetwork, MembershipPending }
    public enum PowerBehavior { Trader, Battery, ConnectionOnly, Unsupported }

    // Runtime-only observations. Never serialize network indices or game object references.
    public sealed class PowerComponentSnapshot
    {
        public int ComponentIndex { get; internal set; }
        public int? NetworkId { get; internal set; }
        public PowerConnectionState Connection { get; internal set; }
        public PowerBehavior Behavior { get; internal set; }
        public bool IsCustomImplementation { get; internal set; }
        public bool HasInvalidData { get; internal set; }
        public bool? PowerOn { get; internal set; }
        public float? ReportedOutputWatts { get; internal set; }
        public float? NominalConsumptionWatts { get; internal set; }
        public float GenerationWatts { get; internal set; }
        public float ConsumptionWatts { get; internal set; }
        public float StoredWattDays { get; internal set; }
        public float AvailableStoredWattDays { get; internal set; }
        public float CapacityWattDays { get; internal set; }
        public float? ChargeEfficiency { get; internal set; }
        public bool IsConsumer { get; internal set; }
        public bool IsGenerator { get; internal set; }
    }

    public sealed class BuildingPowerSnapshot
    {
        public Building Building { get; internal set; }
        public bool? SwitchIsOn { get; internal set; }
        public IReadOnlyList<PowerComponentSnapshot> Components { get; internal set; }
    }

    public sealed class PowerNetworkSnapshot
    {
        // Snapshot-local index, NOT a persistent network identity.
        public int Id { get; internal set; }
        public int ConsumerCount { get; internal set; }
        public int GeneratorCount { get; internal set; }
        public int BatteryCount { get; internal set; }
        public float GenerationWatts { get; internal set; }
        public float ConsumptionWatts { get; internal set; }
        public float NetWatts => GenerationWatts - ConsumptionWatts;
        public float StoredWattDays { get; internal set; }
        public float AvailableStoredWattDays { get; internal set; }
        public float CapacityWattDays { get; internal set; }
        public float? ChargeFraction => CapacityWattDays > 0 ? (float?)(StoredWattDays / CapacityWattDays) : null;
        public bool HasIncompleteData { get; internal set; }
    }

    public sealed class PowerGridSnapshot
    {
        public int Tick { get; internal set; }
        public IReadOnlyList<BuildingPowerSnapshot> Buildings { get; internal set; }
        public IReadOnlyList<PowerNetworkSnapshot> Networks { get; internal set; }
        public float GenerationWatts { get; internal set; }
        public float ConsumptionWatts { get; internal set; }
        public float NetWatts => GenerationWatts - ConsumptionWatts;
        public float StoredWattDays { get; internal set; }
        public float AvailableStoredWattDays { get; internal set; }
        public float CapacityWattDays { get; internal set; }
        public int DisconnectedConsumerCount { get; internal set; }
        public int DeficitNetworkCount { get; internal set; }
        public int UnsupportedComponentCount { get; internal set; }
        public bool HasIncompleteData { get; internal set; }
    }
}
