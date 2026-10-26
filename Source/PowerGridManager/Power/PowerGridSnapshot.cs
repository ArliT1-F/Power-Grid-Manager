using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PowerGridManager
{
    /// <summary>How a power component relates to the map's simulated power networks.</summary>
    public enum PowerConnectionState
    {
        Connected,
        NoNetwork,
        MembershipPending
    }

    /// <summary>The power behaviour a component exposes through supported public API.</summary>
    public enum PowerBehavior
    {
        Trader,
        Battery,
        ConnectionOnly,
        Unsupported
    }

    /// <summary>
    /// Purpose-based building grouping used by the dashboard filters. Assignment is
    /// deliberately conservative: anything that cannot be classified safely stays
    /// <see cref="Unassigned"/> instead of being guessed.
    /// </summary>
    public enum PowerCategory
    {
        Unassigned = 0,
        Generation,
        Storage,
        Production,
        TemperatureControl,
        Lighting,
        Research,
        Medical,
        Security,
        Recreation
    }

    /// <summary>Network status labels the dashboard can justify from observed data.</summary>
    public enum NetworkStatus
    {
        Unknown = 0,
        Stable,
        HighLoad,
        Deficit,
        BatteryDischarging,
        BatteryCharging,
        NoGeneration
    }

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

        /// <summary>
        /// Generation configured by the building definition for this component, in watts.
        /// Taken from the def, not measured: variable-output plants regularly produce less.
        /// </summary>
        public float? ConfiguredOutputWatts { get; internal set; }

        public float GenerationWatts { get; internal set; }
        public float ConsumptionWatts { get; internal set; }
        public float StoredWattDays { get; internal set; }
        public float AvailableStoredWattDays { get; internal set; }
        public float CapacityWattDays { get; internal set; }
        public float? ChargeEfficiency { get; internal set; }
        public bool IsConsumer { get; internal set; }
        public bool IsGenerator { get; internal set; }
    }

    /// <summary>
    /// One building's power observation. Aggregates are the sum of this building's
    /// observed components; they are not inferred from the building's def.
    /// </summary>
    public sealed class BuildingPowerSnapshot
    {
        public Building Building { get; internal set; }
        public IReadOnlyList<PowerComponentSnapshot> Components { get; internal set; }
        public bool IsColonistBuilding { get; internal set; }

        public bool IsFlickable { get; internal set; }
        public bool? SwitchIsOn { get; internal set; }

        /// <summary>
        /// True when the flick component's type lives outside the game assembly. Such a building
        /// can still be controlled if it exposes the standard public toggle gizmo; the UI labels
        /// it as a modded implementation rather than claiming verified vanilla behaviour.
        /// </summary>
        public bool IsCustomFlickable { get; internal set; }

        public bool IsConsumer { get; internal set; }
        public bool IsGenerator { get; internal set; }
        public bool IsBattery { get; internal set; }

        /// <summary>Current reported draw in watts (only components that are switched on).</summary>
        public float ConsumptionWatts { get; internal set; }

        /// <summary>Current reported output in watts.</summary>
        public float GenerationWatts { get; internal set; }

        public float StoredWattDays { get; internal set; }
        public float CapacityWattDays { get; internal set; }

        /// <summary>
        /// Sum of configured consumption for this building's consumer components, in watts.
        /// Null when any relevant component did not expose a usable value.
        /// This is the def-level configuration, not a measurement of current draw.
        /// </summary>
        public float? NominalConsumptionWatts { get; internal set; }

        /// <summary>
        /// Sum of definition-configured generator output for this building, in watts.
        /// Null when a generator component did not expose a usable configured value.
        /// </summary>
        public float? ConfiguredGenerationWatts { get; internal set; }

        /// <summary>True when at least one trader component is currently switched off.</summary>
        public bool HasOffComponents { get; internal set; }

        public bool HasUnsupportedComponent { get; internal set; }
        public bool HasInvalidData { get; internal set; }
        public PowerConnectionState Connection { get; internal set; }

        /// <summary>Set only when every connected component agrees on a single network.</summary>
        public int? NetworkId { get; internal set; }

        public bool HasMultipleNetworks { get; internal set; }
        public PowerCategory Category { get; internal set; }
        public Room Room { get; internal set; }
        public bool HasRoom { get; internal set; }

        public bool HasAnyDraw => ConsumptionWatts > 0.01f;
    }

    public sealed class PowerNetworkSnapshot
    {
        // Snapshot-local index, NOT a persistent network identity.
        public int Id { get; internal set; }

        /// <summary>
        /// The live network this row describes. Runtime identity only: it lets advisories follow a
        /// network across refreshes for as long as the game keeps the same object. Never persisted.
        /// </summary>
        public PowerNet Net { get; internal set; }
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

        /// <summary>Scratch accumulator: charge efficiency weighted by battery capacity.</summary>
        public float EfficiencyWeightedSum { get; internal set; }

        /// <summary>How many batteries reported a usable charge efficiency.</summary>
        public int EfficiencySamples { get; internal set; }

        /// <summary>
        /// Capacity-weighted average charge efficiency, or null when any battery on the network did
        /// not report one. Charging estimates are labelled as assuming no loss when this is null.
        /// </summary>
        public float? ChargeEfficiency => BatteryCount > 0 && CapacityWattDays > 0f &&
                                          EfficiencySamples >= BatteryCount
            ? (float?)(EfficiencyWeightedSum / CapacityWattDays)
            : null;
    }

    /// <summary>
    /// A complete, immutable map-wide observation produced by <see cref="PowerGridScanner"/>.
    /// The dashboard reads only this object; it never scans while rendering.
    /// </summary>
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

        /// <summary>Scratch accumulator: charge efficiency weighted by battery capacity.</summary>
        public float EfficiencyWeightedSum { get; internal set; }

        /// <summary>How many batteries reported a usable charge efficiency.</summary>
        public int EfficiencySamples { get; internal set; }

        /// <summary>Map-wide capacity-weighted charge efficiency, or null when not fully known.</summary>
        public float? ChargeEfficiency => BatteryCount > 0 && CapacityWattDays > 0f &&
                                          EfficiencySamples >= BatteryCount
            ? (float?)(EfficiencyWeightedSum / CapacityWattDays)
            : null;

        /// <summary>Configured consumer draw for connected components; not a measurement.</summary>
        public float? NominalConsumptionWatts { get; internal set; }

        /// <summary>
        /// Sum of definition-configured generator output. Null when any generator does not
        /// expose a usable configured value, so a partial total is never presented as whole.
        /// Separate from measured generation in every calculation.
        /// </summary>
        public float? ConfiguredGenerationWatts { get; internal set; }

        public int ConsumerCount { get; internal set; }
        public int GeneratorCount { get; internal set; }
        public int BatteryCount { get; internal set; }

        public int DisconnectedConsumerCount { get; internal set; }
        public int DeficitNetworkCount { get; internal set; }
        public int UnsupportedComponentCount { get; internal set; }

        /// <summary>Count of networks/buildings whose data is incomplete or unsupported.</summary>
        public int IncompleteDataCount { get; internal set; }

        public bool HasIncompleteData { get; internal set; }

        public float? ChargeFraction => CapacityWattDays > 0 ? (float?)(StoredWattDays / CapacityWattDays) : null;

        /// <summary>
        /// Current consumption as a fraction of current generation. Null when nothing is
        /// being generated, because a ratio with a zero denominator is meaningless.
        /// </summary>
        public float? LoadRatio => GenerationWatts > 0.01f ? (float?)(ConsumptionWatts / GenerationWatts) : null;

        /// <summary>
        /// Monitoring notices that are backed by observed data quality (unsupported
        /// components, pending membership, disconnected consumers). Diagnostic alerts
        /// are a separate, later feature.
        /// </summary>
        public int DataNoticeCount =>
            UnsupportedComponentCount + IncompleteDataCount + DisconnectedConsumerCount;
    }
}
