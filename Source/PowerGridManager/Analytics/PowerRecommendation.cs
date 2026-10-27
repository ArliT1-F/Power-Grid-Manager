using System;
using RimWorld;
using Verse;

namespace PowerGridManager
{
    /// <summary>Kinds of optimization recommendation the engine can produce.</summary>
    /// <remarks>
    /// Numeric values are part of the saved dismissal format: never renumber existing values.
    /// </remarks>
    public enum RecommendationKind
    {
        PersistentDeficit = 0,
        HighDemand,
        LowBatteryReserve,
        BatteryDischarge,
        IdleConsumer,
        UnnecessaryConsumption,
        ImbalancedGeneration,
        UncontrollableBuilding,
        NearCapacity
    }

    public enum RecommendationSeverity
    {
        Info = 0,
        Warning = 1,
        Critical = 2
    }

    /// <summary>
    /// One actionable optimization recommendation. Facts (what was measured) are kept
    /// separate from estimates (potential benefit), and every estimate is labelled.
    /// The engine only proposes; it never acts. Applying a recommendation always requires
    /// an explicit player action or an explicitly enabled automation policy.
    /// </summary>
    public sealed class PowerRecommendation
    {
        public RecommendationKind Kind;
        public RecommendationSeverity Severity;

        /// <summary>Snapshot-local network number, when the recommendation is about a network.</summary>
        public int? NetworkId;

        /// <summary>The live network this recommendation belongs to. Runtime identity only.</summary>
        public PowerNet Net;

        /// <summary>The building this recommendation is about, when it is about one building.</summary>
        public Building Building;

        public string TitleKey;
        public string ExplanationKey;
        public string EvidenceKey;
        public object[] EvidenceArgs;

        /// <summary>Estimated demand reduction in watts if the suggested action is taken.</summary>
        public float EstimatedBenefitWatts;

        /// <summary>True when the benefit is modelled rather than directly measured.</summary>
        public bool BenefitIsEstimate;

        /// <summary>Suggested player actions, as localization keys.</summary>
        public string[] ActionKeys;

        /// <summary>True when the underlying problem is still present.</summary>
        public bool Ongoing;

        /// <summary>True when the player dismissed this specific recommendation.</summary>
        public bool Dismissed;

        public int FirstSeenTick;
        public int LastSeenTick;

        public bool CanInspectBuilding => Building != null;
        public bool CanInspectNetwork => NetworkId.HasValue;

        /// <summary>True when this recommendation should no longer be shown.</summary>
        public bool ShouldHide => !Ongoing || Dismissed;

        public PowerRecommendation Clone() => (PowerRecommendation)MemberwiseClone();
    }
}
