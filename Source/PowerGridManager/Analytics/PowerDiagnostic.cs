using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Advisory kinds produced by <see cref="PowerDiagnostics"/>.
    /// <para>
    /// The numeric values are part of the saved settings format: they index the
    /// "muted advisories" bitmask, so existing values must never be renumbered.
    /// </para>
    /// </summary>
    public enum PowerDiagnosticKind
    {
        /// <summary>A network's consumption exceeds its generation and it has no stored energy to draw on.</summary>
        PersistentDeficit = 0,

        /// <summary>Consumption is close to or above current generation (configurable threshold).</summary>
        HighLoad = 1,

        /// <summary>Batteries are covering a generation deficit and draining.</summary>
        BatteryDischarge = 2,

        /// <summary>A network has consumers but nothing is generating.</summary>
        NoGeneration = 3,

        /// <summary>A consumer has no power network at all.</summary>
        IsolatedConsumer = 4,

        /// <summary>Network membership could not be confirmed for this building.</summary>
        PendingMembership = 5,

        /// <summary>A generator is producing far below its configured output for a sustained period.</summary>
        GeneratorUnderOutput = 6,

        /// <summary>Measured draw is well above the configured draw.</summary>
        DrawAboveConfigured = 7,

        /// <summary>Flick switch is off, yet the building still reports a draw.</summary>
        SwitchedOffButDrawing = 8,

        /// <summary>At least one power component is not a recognised type.</summary>
        UnsupportedComponent = 9
    }

    public enum PowerDiagnosticSeverity
    {
        Info = 0,
        Warning = 1,
        Critical = 2
    }

    /// <summary>
    /// One advisory. The evaluator stores only numbers here; every player-facing string is
    /// produced by the panel from these values, so localization keys stay next to the code
    /// that formats them.
    /// </summary>
    public sealed class PowerDiagnostic
    {
        public PowerDiagnosticKind Kind;
        public PowerDiagnosticSeverity Severity;

        /// <summary>Snapshot-local network number, when the advisory is about a network.</summary>
        public int? NetworkId;

        /// <summary>
        /// The live network this advisory belongs to. Runtime identity only: never persisted and
        /// never used as a key outside a single play session.
        /// </summary>
        public PowerNet Net;

        /// <summary>The building an advisory refers to, when it is about one building.</summary>
        public Building Building;

        /// <summary>Used to rank advisories of the same kind (watts, or a count).</summary>
        public float SortMagnitude;

        public int FirstSeenTick;
        public int LastSeenTick;
        public bool Ongoing;
        public bool Muted;
        public bool Dismissed;

        // --- Evidence numbers -------------------------------------------------
        public float GenerationWatts;
        public float ConsumptionWatts;
        public float DeficitWatts;
        public float MeasuredWatts;
        public float ConfiguredWatts;
        public float StoredWattDays;
        public float CapacityWattDays;
        public float? ChargeFraction;
        public int AffectedCount;
        public int SustainedTicks;

        /// <summary>Ticks until the network's stored energy runs out, when that could be estimated.</summary>
        public int? DepletionTicks;

        /// <summary>Tick the condition stopped being true, or -1 while it is still ongoing.</summary>
        public int ResolvedTick = -1;

        public bool HasBuilding => Building != null;

        public int EpisodeTicks => LastSeenTick - FirstSeenTick;

        /// <summary>
        /// Copies the observation. Used when the tracker replays a resolved episode: the numbers
        /// are the ones recorded while the condition was still true.
        /// </summary>
        public PowerDiagnostic Clone()
        {
            return (PowerDiagnostic)MemberwiseClone();
        }
    }
}
