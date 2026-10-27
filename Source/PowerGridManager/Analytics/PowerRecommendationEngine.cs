using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Produces actionable optimization recommendations from a snapshot, history and the
    /// management registry. Read-only: it never changes a switch, never scans the map and
    /// never proposes transferring power between networks that are not actually connected.
    /// Facts are separated from estimates; every benefit is labelled.
    /// </summary>
    public static class PowerRecommendationEngine
    {
        /// <summary>Hard cap so a heavily modded colony cannot flood the tab.</summary>
        public const int MaxRecommendations = 60;

        /// <summary>Below this, a negative balance is rounding noise rather than a deficit.</summary>
        public const float MinimumDeficitWatts = 1f;

        /// <summary>Smallest benefit worth reporting, in watts.</summary>
        public const float MinimumBenefitWatts = 5f;

        /// <summary>Hard cap on per-kind rows so one condition cannot dominate the list.</summary>
        public const int MaxRowsPerKind = 8;

        private static readonly List<PowerRecommendation> Candidates = new List<PowerRecommendation>(MaxRecommendations * 2);
        private static readonly HashSet<string> Emitted = new HashSet<string>();
        private static readonly Comparison<PowerRecommendation> RankComparison = Rank;

        private static PowerManagementRegistry Registry;
        private static PowerRecommendationTracker Tracker;
        private static PowerGridManagerSettings Settings;

        /// <summary>Rebuilds <paramref name="output"/>. Called on refresh, never per frame.</summary>
        public static void Evaluate(PowerGridSnapshot snapshot, PowerGridManagerSettings settings,
            PowerManagementRegistry registry, PowerRecommendationTracker tracker,
            List<PowerRecommendation> output, int now)
        {
            output.Clear();
            Settings = settings;
            Registry = registry;
            Tracker = tracker;
            Emitted.Clear();
            try
            {
                if (snapshot == null || settings == null || tracker == null) return;
                tracker.BeginEvaluation();
                Candidates.Clear();

                EvaluateNetworks(snapshot, now);
                EvaluateBuildings(snapshot, now);
                EvaluateImbalance(snapshot, now);

                tracker.CollectResolved(output, now);

                Candidates.Sort(RankComparison);
                int limit = Candidates.Count < MaxRecommendations ? Candidates.Count : MaxRecommendations;
                for (int i = 0; i < limit; i++) output.Add(Candidates[i]);

                tracker.EndEvaluation(snapshot, now);
            }
            finally
            {
                Settings = null;
                Registry = null;
                Tracker = null;
                Candidates.Clear();
                Emitted.Clear();
            }
        }

        private static int Rank(PowerRecommendation left, PowerRecommendation right)
        {
            if (left.Ongoing != right.Ongoing) return left.Ongoing ? -1 : 1;
            int severity = ((int)right.Severity).CompareTo((int)left.Severity);
            if (severity != 0) return severity;
            float benefit = right.EstimatedBenefitWatts.CompareTo(left.EstimatedBenefitWatts) == 0 ? 0f :
                right.EstimatedBenefitWatts > left.EstimatedBenefitWatts ? 1f : -1f;
            int b = benefit.CompareTo(0f);
            if (b != 0) return b;
            return ((int)left.Kind).CompareTo((int)right.Kind);
        }

        // --- Networks ---------------------------------------------------------

        private static void EvaluateNetworks(PowerGridSnapshot snapshot, int now)
        {
            IReadOnlyList<PowerNetworkSnapshot> networks = snapshot.Networks;
            if (networks == null) return;

            float minimumDeficit = Settings.DeficitAlertWatts;
            if (minimumDeficit < MinimumDeficitWatts) minimumDeficit = MinimumDeficitWatts;

            for (int i = 0; i < networks.Count; i++)
            {
                PowerNetworkSnapshot network = networks[i];
                if (network == null) continue;

                float deficit = network.ConsumptionWatts - network.GenerationWatts;
                bool inDeficit = deficit >= minimumDeficit && network.ConsumerCount > 0;
                float charge = network.ChargeFraction ?? 0f;
                bool lowReserve = network.BatteryCount > 0 && charge <= Settings.LowBatteryWarningFraction;
                float loadRatio = snapshot.GenerationWatts > 0.01f
                    ? network.ConsumptionWatts / network.GenerationWatts : 0f;
                bool nearCapacity = loadRatio >= Settings.NearCapacityThreshold;

                int deficitSustained = Tracker.Register(network.Net, RecommendationKind.PersistentDeficit,
                    inDeficit, now);
                int lowSustained = Tracker.Register(network.Net, RecommendationKind.LowBatteryReserve,
                    lowReserve, now);
                int dischargeSustained = Tracker.Register(network.Net, RecommendationKind.BatteryDischarge,
                    inDeficit && network.BatteryCount > 0 && network.StoredWattDays > 0.001f, now);
                int demandSustained = Tracker.Register(network.Net, RecommendationKind.HighDemand,
                    loadRatio >= Settings.HighLoadThreshold && network.GenerationWatts > 0.01f, now);
                int capacitySustained = Tracker.Register(network.Net, RecommendationKind.NearCapacity,
                    nearCapacity && network.GenerationWatts > 0.01f, now);

                if (inDeficit && deficitSustained > 0)
                {
                    Add(new PowerRecommendation
                    {
                        Kind = RecommendationKind.PersistentDeficit,
                        Severity = SeverityForDeficit(deficit, deficitSustained, charge),
                        Net = network.Net,
                        NetworkId = network.Id,
                        TitleKey = "PGM_Rec_Title_Deficit".Translate(network.Id).ToString(),
                        ExplanationKey = "PGM_Rec_Expl_Deficit".ToString(),
                        EvidenceKey = "PGM_Rec_Ev_Deficit".ToString(),
                        EvidenceArgs = new object[]
                        {
                            PowerFormatting.FormatWatts(network.GenerationWatts),
                            PowerFormatting.FormatWatts(network.ConsumptionWatts),
                            PowerFormatting.FormatWatts(deficit),
                            PowerFormatting.FormatDuration(deficitSustained)
                        },
                        EstimatedBenefitWatts = deficit,
                        BenefitIsEstimate = true,
                        ActionKeys = new[]
                        {
                            "PGM_Rec_Act_InspectGenerators",
                            "PGM_Rec_Act_InspectConsumers",
                            "PGM_Rec_Act_PreviewLoadShedding",
                            "PGM_Rec_Act_AddGeneration"
                        },
                        Ongoing = true,
                        FirstSeenTick = now - deficitSustained,
                        LastSeenTick = now
                    }, deficit, network.Id, null);
                }

                if (lowReserve && lowSustained > 0)
                {
                    Add(new PowerRecommendation
                    {
                        Kind = RecommendationKind.LowBatteryReserve,
                        Severity = charge <= 0.10f ? RecommendationSeverity.Critical : RecommendationSeverity.Warning,
                        Net = network.Net,
                        NetworkId = network.Id,
                        TitleKey = "PGM_Rec_Title_LowBattery".Translate(network.Id).ToString(),
                        ExplanationKey = "PGM_Rec_Expl_LowBattery".ToString(),
                        EvidenceKey = "PGM_Rec_Ev_LowBattery".ToString(),
                        EvidenceArgs = new object[]
                        {
                            PowerFormatting.FormatCharge(charge),
                            PowerFormatting.FormatWatts(deficit),
                            PowerFormatting.FormatDuration(lowSustained)
                        },
                        EstimatedBenefitWatts = 0f,
                        BenefitIsEstimate = false,
                        ActionKeys = new[]
                        {
                            "PGM_Rec_Act_AddGeneration",
                            "PGM_Rec_Act_ReduceDemand",
                            "PGM_Rec_Act_AddBatteries"
                        },
                        Ongoing = true,
                        FirstSeenTick = now - lowSustained,
                        LastSeenTick = now
                    }, 0f, network.Id, null);
                }

                if ((inDeficit && network.BatteryCount > 0) && dischargeSustained > 0)
                {
                    Add(new PowerRecommendation
                    {
                        Kind = RecommendationKind.BatteryDischarge,
                        Severity = charge <= Settings.LowBatteryWarningFraction
                            ? RecommendationSeverity.Critical : RecommendationSeverity.Warning,
                        Net = network.Net,
                        NetworkId = network.Id,
                        TitleKey = "PGM_Rec_Title_Discharge".Translate(network.Id).ToString(),
                        ExplanationKey = "PGM_Rec_Expl_Discharge".ToString(),
                        EvidenceKey = "PGM_Rec_Ev_Discharge".ToString(),
                        EvidenceArgs = new object[]
                        {
                            PowerFormatting.FormatWatts(deficit),
                            PowerFormatting.FormatCharge(charge),
                            PowerFormatting.FormatDuration(dischargeSustained)
                        },
                        EstimatedBenefitWatts = deficit,
                        BenefitIsEstimate = true,
                        ActionKeys = new[]
                        {
                            "PGM_Rec_Act_InspectConsumers",
                            "PGM_Rec_Act_PreviewLoadShedding",
                            "PGM_Rec_Act_AddGeneration"
                        },
                        Ongoing = true,
                        FirstSeenTick = now - dischargeSustained,
                        LastSeenTick = now
                    }, deficit, network.Id, null);
                }

                if (demandSustained > 0)
                {
                    Add(new PowerRecommendation
                    {
                        Kind = RecommendationKind.HighDemand,
                        Severity = RecommendationSeverity.Warning,
                        Net = network.Net,
                        NetworkId = network.Id,
                        TitleKey = "PGM_Rec_Title_HighDemand".Translate(network.Id).ToString(),
                        ExplanationKey = "PGM_Rec_Expl_HighDemand".ToString(),
                        EvidenceKey = "PGM_Rec_Ev_HighDemand".ToString(),
                        EvidenceArgs = new object[]
                        {
                            PowerFormatting.FormatCharge(loadRatio),
                            PowerFormatting.FormatWatts(network.ConsumptionWatts),
                            PowerFormatting.FormatWatts(network.GenerationWatts)
                        },
                        EstimatedBenefitWatts = Math.Max(0f, network.ConsumptionWatts -
                            network.GenerationWatts * Settings.HighLoadThreshold),
                        BenefitIsEstimate = true,
                        ActionKeys = new[]
                        {
                            "PGM_Rec_Act_InspectConsumers",
                            "PGM_Rec_Act_PreviewLoadShedding"
                        },
                        Ongoing = true,
                        FirstSeenTick = now - demandSustained,
                        LastSeenTick = now
                    }, Math.Max(0f, network.ConsumptionWatts -
                        network.GenerationWatts * Settings.HighLoadThreshold), network.Id, null);
                }

                if (capacitySustained > 0)
                {
                    Add(new PowerRecommendation
                    {
                        Kind = RecommendationKind.NearCapacity,
                        Severity = RecommendationSeverity.Info,
                        Net = network.Net,
                        NetworkId = network.Id,
                        TitleKey = "PGM_Rec_Title_NearCapacity".Translate(network.Id).ToString(),
                        ExplanationKey = "PGM_Rec_Expl_NearCapacity".ToString(),
                        EvidenceKey = "PGM_Rec_Ev_NearCapacity".ToString(),
                        EvidenceArgs = new object[]
                        {
                            PowerFormatting.FormatCharge(loadRatio),
                            PowerFormatting.FormatCharge(Settings.NearCapacityThreshold)
                        },
                        EstimatedBenefitWatts = 0f,
                        BenefitIsEstimate = false,
                        ActionKeys = new[]
                        {
                            "PGM_Rec_Act_AddGeneration",
                            "PGM_Rec_Act_AddBatteries"
                        },
                        Ongoing = true,
                        FirstSeenTick = now - capacitySustained,
                        LastSeenTick = now
                    }, 0f, network.Id, null);
                }
            }
        }

        // --- Buildings --------------------------------------------------------

        private static void EvaluateBuildings(PowerGridSnapshot snapshot, int now)
        {
            IReadOnlyList<BuildingPowerSnapshot> buildings = snapshot.Buildings;
            if (buildings == null) return;

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingPowerSnapshot building = buildings[i];
                if (building == null || building.Building == null) continue;

                // Idle consumer: switch is off yet the building still draws power.
                bool idle = building.IsFlickable && building.SwitchIsOn == false &&
                            building.ConsumptionWatts > 0.01f;
                int idleSustained = Tracker.Register(building.Building, RecommendationKind.IdleConsumer,
                    idle, now);
                if (idle && idleSustained > 0)
                {
                    Add(new PowerRecommendation
                    {
                        Kind = RecommendationKind.IdleConsumer,
                        Severity = RecommendationSeverity.Warning,
                        Building = building.Building,
                        TitleKey = "PGM_Rec_Title_IdleConsumer".ToString(),
                        ExplanationKey = "PGM_Rec_Expl_IdleConsumer".ToString(),
                        EvidenceKey = "PGM_Rec_Ev_IdleConsumer".ToString(),
                        EvidenceArgs = new object[]
                        {
                            PowerGridUtilities.BuildingLabel(building.Building),
                            PowerFormatting.FormatWatts(building.ConsumptionWatts)
                        },
                        EstimatedBenefitWatts = building.ConsumptionWatts,
                        BenefitIsEstimate = false,
                        ActionKeys = new[] { "PGM_Rec_Act_SwitchOff", "PGM_Rec_Act_InspectBuilding" },
                        Ongoing = true,
                        FirstSeenTick = now - idleSustained,
                        LastSeenTick = now
                    }, building.ConsumptionWatts, null, building.Building);
                }

                // Unnecessary consumption: high draw consumer that is not marked sheddable.
                bool unnecessary = building.IsConsumer && building.Connection == PowerConnectionState.Connected &&
                                   building.ConsumptionWatts >= Settings.HighConsumptionWatts &&
                                   Settings.HighConsumptionWatts > 0f &&
                                   (Registry == null || !Registry.IsSheddable(building.Building));
                int unnecessarySustained = Tracker.Register(building.Building,
                    RecommendationKind.UnnecessaryConsumption, unnecessary, now);
                if (unnecessary && unnecessarySustained > 0)
                {
                    Add(new PowerRecommendation
                    {
                        Kind = RecommendationKind.UnnecessaryConsumption,
                        Severity = RecommendationSeverity.Info,
                        Building = building.Building,
                        TitleKey = "PGM_Rec_Title_HighDraw".ToString(),
                        ExplanationKey = "PGM_Rec_Expl_HighDraw".ToString(),
                        EvidenceKey = "PGM_Rec_Ev_HighDraw".ToString(),
                        EvidenceArgs = new object[]
                        {
                            PowerGridUtilities.BuildingLabel(building.Building),
                            PowerFormatting.FormatWatts(building.ConsumptionWatts),
                            PowerFormatting.FormatWatts(Settings.HighConsumptionWatts)
                        },
                        EstimatedBenefitWatts = building.ConsumptionWatts,
                        BenefitIsEstimate = true,
                        ActionKeys = new[] { "PGM_Rec_Act_MarkSheddable", "PGM_Rec_Act_InspectBuilding" },
                        Ongoing = true,
                        FirstSeenTick = now - unnecessarySustained,
                        LastSeenTick = now
                    }, building.ConsumptionWatts, null, building.Building);
                }

                // Uncontrollable building: a consumer the management system cannot safely switch.
                bool uncontrollable = building.IsConsumer && building.Connection == PowerConnectionState.Connected &&
                                      building.ConsumptionWatts > 0.01f &&
                                      CompatibilityRegistry.EvaluateControl(building, Registry) ==
                                      PowerControlSupport.Unsupported;
                int uncontrollableSustained = Tracker.Register(building.Building,
                    RecommendationKind.UncontrollableBuilding, uncontrollable, now);
                if (uncontrollable && uncontrollableSustained > 0)
                {
                    Add(new PowerRecommendation
                    {
                        Kind = RecommendationKind.UncontrollableBuilding,
                        Severity = RecommendationSeverity.Info,
                        Building = building.Building,
                        TitleKey = "PGM_Rec_Title_Uncontrollable".ToString(),
                        ExplanationKey = "PGM_Rec_Expl_Uncontrollable".ToString(),
                        EvidenceKey = "PGM_Rec_Ev_Uncontrollable".ToString(),
                        EvidenceArgs = new object[]
                        {
                            PowerGridUtilities.BuildingLabel(building.Building)
                        },
                        EstimatedBenefitWatts = 0f,
                        BenefitIsEstimate = false,
                        ActionKeys = new[] { "PGM_Rec_Act_InspectBuilding" },
                        Ongoing = true,
                        FirstSeenTick = now - uncontrollableSustained,
                        LastSeenTick = now
                    }, 0f, null, building.Building);
                }
            }
        }

        // --- Cross-network ----------------------------------------------------

        private static void EvaluateImbalance(PowerGridSnapshot snapshot, int now)
        {
            IReadOnlyList<PowerNetworkSnapshot> networks = snapshot.Networks;
            if (networks == null || networks.Count < 2) return;

            PowerNetworkSnapshot deficitNet = null;
            float deficitNetDeficit = 0f;
            PowerNetworkSnapshot surplusNet = null;
            float surplusNetSurplus = 0f;

            for (int i = 0; i < networks.Count; i++)
            {
                PowerNetworkSnapshot network = networks[i];
                if (network == null || network.ConsumerCount == 0) continue;
                float net = network.NetWatts;
                if (net < -MinimumDeficitWatts && (deficitNet == null || -net > deficitNetDeficit))
                {
                    deficitNet = network;
                    deficitNetDeficit = -net;
                }
                else if (net > MinimumDeficitWatts && (surplusNet == null || net > surplusNetSurplus))
                {
                    surplusNet = network;
                    surplusNetSurplus = net;
                }
            }

            if (deficitNet != null && surplusNet != null)
            {
                int sustained = Tracker.Register(deficitNet.Net, RecommendationKind.ImbalancedGeneration,
                    true, now);
                if (sustained > 0)
                {
                    Add(new PowerRecommendation
                    {
                        Kind = RecommendationKind.ImbalancedGeneration,
                        Severity = RecommendationSeverity.Info,
                        Net = deficitNet.Net,
                        NetworkId = deficitNet.Id,
                        TitleKey = "PGM_Rec_Title_Imbalanced".ToString(),
                        ExplanationKey = "PGM_Rec_Expl_Imbalanced".ToString(),
                        EvidenceKey = "PGM_Rec_Ev_Imbalanced".ToString(),
                        EvidenceArgs = new object[]
                        {
                            deficitNet.Id, PowerFormatting.FormatWatts(deficitNetDeficit),
                            surplusNet.Id, PowerFormatting.FormatWatts(surplusNetSurplus)
                        },
                        EstimatedBenefitWatts = 0f,
                        BenefitIsEstimate = false,
                        ActionKeys = new[]
                        {
                            "PGM_Rec_Act_InspectDeficitNetwork",
                            "PGM_Rec_Act_AddGeneration",
                            "PGM_Rec_Act_PreviewLoadShedding"
                        },
                        Ongoing = true,
                        FirstSeenTick = now - sustained,
                        LastSeenTick = now
                    }, 0f, deficitNet.Id, null);
                }
            }
        }

        // --- Helpers ----------------------------------------------------------

        private static RecommendationSeverity SeverityForDeficit(float deficit, int sustained, float charge)
        {
            if (deficit >= 1000f || sustained >= PowerTime.TicksPerDay || charge <= 0.10f)
                return RecommendationSeverity.Critical;
            if (deficit >= 200f || sustained >= PowerTime.TicksPerHour || charge <= 0.25f)
                return RecommendationSeverity.Warning;
            return RecommendationSeverity.Info;
        }

        /// <summary>
        /// Adds one recommendation per (kind, network/building). Deduplicates identical
        /// unresolved conditions so a flickering fault cannot spam the list.
        /// </summary>
        private static void Add(PowerRecommendation recommendation, float magnitude, int? networkId,
            Building building)
        {
            if (Settings == null || Tracker == null) return;
            if (Tracker.IsDismissed(recommendation.Kind, networkId, building)) return;
            if (recommendation.EstimatedBenefitWatts < MinimumBenefitWatts &&
                recommendation.Kind != RecommendationKind.LowBatteryReserve &&
                recommendation.Kind != RecommendationKind.NearCapacity &&
                recommendation.Kind != RecommendationKind.UncontrollableBuilding &&
                recommendation.Kind != RecommendationKind.ImbalancedGeneration)
            {
                return;
            }
            string key = RecommendationKey(recommendation.Kind, networkId, building);
            if (!Emitted.Add(key)) return;
            if (Candidates.Count >= MaxRecommendations * 2) return;
            recommendation.Ongoing = true;
            recommendation.Dismissed = false;
            recommendation.NetworkId = networkId;
            recommendation.Building = building;
            Candidates.Add(recommendation);
        }

        private static string RecommendationKey(RecommendationKind kind, int? networkId,
            Building building)
        {
            return kind + ":" + (networkId.HasValue ? networkId.Value.ToString() : "n") +
                   ":" + (building == null ? "-" : building.def?.defName ?? "?");
        }
    }
}
