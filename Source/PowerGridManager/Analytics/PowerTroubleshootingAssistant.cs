using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Guided, deterministic troubleshooting. Given a scope it produces a single
    /// <see cref="PowerDiagnosticReport"/>: what is wrong, what the measurements say, the
    /// likely cause, prioritized actions, and where to navigate. It never applies a change;
    /// applying anything always requires an explicit player action or an explicitly enabled
    /// automation policy.
    /// </summary>
    public static class PowerTroubleshootingAssistant
    {
        public const float MinimumDeficitWatts = 1f;

        public static PowerDiagnosticReport Diagnose(DiagnosisScope scope,
            PowerGridSnapshot snapshot, PowerGridManagerSettings settings,
            IReadOnlyList<PowerDiagnostic> advisories, int now,
            Building selectedBuilding, int? selectedNetworkId)
        {
            var report = new PowerDiagnosticReport();
            if (snapshot == null || settings == null) return report;

            float minimumDeficit = settings.DeficitAlertWatts;
            if (minimumDeficit < MinimumDeficitWatts) minimumDeficit = MinimumDeficitWatts;

            List<PowerDiagnostic> relevant = Filter(advisories, scope, selectedBuilding,
                selectedNetworkId);
            PowerDiagnostic top = relevant.Count > 0 ? relevant[0] : null;

            if (top != null)
            {
                return ReportForAdvisory(top, snapshot, settings, now);
            }

            // No advisory fired: synthesize from the observed conditions so the player is
            // never told "nothing" when a measurable problem exists.
            if (scope == DiagnosisScope.WholeMap)
            {
                return DiagnoseMap(snapshot, settings, now, minimumDeficit);
            }
            if (scope == DiagnosisScope.SelectedNetwork && selectedNetworkId.HasValue)
            {
                for (int i = 0; i < snapshot.Networks.Count; i++)
                {
                    if (snapshot.Networks[i].Id == selectedNetworkId.Value)
                    {
                        return DiagnoseNetwork(snapshot.Networks[i], settings, now,
                            minimumDeficit);
                    }
                }
                return NoProblem();
            }
            if (scope == DiagnosisScope.SelectedBuilding && selectedBuilding != null)
            {
                return DiagnoseBuilding(snapshot, selectedBuilding, settings, now);
            }
            return NoProblem();
        }

        // --- Scope filtering -------------------------------------------------------

        private static List<PowerDiagnostic> Filter(IReadOnlyList<PowerDiagnostic> advisories,
            DiagnosisScope scope, Building selectedBuilding, int? selectedNetworkId)
        {
            var result = new List<PowerDiagnostic>();
            if (advisories == null) return result;
            for (int i = 0; i < advisories.Count; i++)
            {
                PowerDiagnostic advisory = advisories[i];
                if (advisory == null || !advisory.Ongoing) continue;
                bool matches = false;
                switch (scope)
                {
                    case DiagnosisScope.WholeMap:
                        matches = true;
                        break;
                    case DiagnosisScope.SelectedNetwork:
                        matches = advisory.NetworkId.HasValue &&
                            advisory.NetworkId.Value == selectedNetworkId.Value;
                        break;
                    case DiagnosisScope.SelectedBuilding:
                        matches = ReferenceEquals(advisory.Building, selectedBuilding);
                        break;
                }
                if (matches) result.Add(advisory);
            }
            return result;
        }

        // --- From an existing advisory ---------------------------------------------

        private static PowerDiagnosticReport ReportForAdvisory(PowerDiagnostic advisory,
            PowerGridSnapshot snapshot, PowerGridManagerSettings settings, int now)
        {
            var report = new PowerDiagnosticReport
            {
                Severity = advisory.Severity,
                NavigationBuilding = advisory.Building,
                NavigationNetworkId = advisory.NetworkId
            };

            switch (advisory.Kind)
            {
                case PowerDiagnosticKind.PersistentDeficit:
                    report.ProblemKey = "PGM_Trouble_Problem_Deficit";
                    report.ProblemArgs = new object[] { advisory.NetworkId ?? 0 };
                    report.EvidenceKey = "PGM_Trouble_Ev_Deficit";
                    report.EvidenceArgs = new object[]
                    {
                        PowerFormatting.FormatWatts(advisory.GenerationWatts),
                        PowerFormatting.FormatWatts(advisory.ConsumptionWatts),
                        PowerFormatting.FormatWatts(advisory.DeficitWatts),
                        PowerFormatting.FormatDuration(advisory.SustainedTicks)
                    };
                    report.LikelyCauseKey = "PGM_Trouble_Cause_Deficit";
                    report.RecommendedActionKeys = new[]
                    {
                        "PGM_Trouble_Act_InspectConsumers",
                        "PGM_Trouble_Act_AddGeneration",
                        "PGM_Trouble_Act_PreviewLoadShedding"
                    };
                    report.OptionalActionKey = "PGM_Trouble_Opt_PreviewLoadShedding";
                    report.OptionalActionTip = "PGM_Trouble_Opt_PreviewLoadSheddingTip";
                    break;
                case PowerDiagnosticKind.BatteryDischarge:
                    report.ProblemKey = "PGM_Trouble_Problem_Discharge";
                    report.ProblemArgs = new object[] { advisory.NetworkId ?? 0 };
                    report.EvidenceKey = "PGM_Trouble_Ev_Discharge";
                    report.EvidenceArgs = new object[]
                    {
                        PowerFormatting.FormatWatts(advisory.DeficitWatts),
                        PowerFormatting.FormatCharge(advisory.ChargeFraction),
                        PowerFormatting.FormatDuration(advisory.SustainedTicks)
                    };
                    report.LikelyCauseKey = "PGM_Trouble_Cause_Discharge";
                    report.RecommendedActionKeys = new[]
                    {
                        "PGM_Trouble_Act_InspectConsumers",
                        "PGM_Trouble_Act_ReduceDemand",
                        "PGM_Trouble_Act_AddGeneration"
                    };
                    break;
                case PowerDiagnosticKind.NoGeneration:
                    report.ProblemKey = "PGM_Trouble_Problem_NoGeneration";
                    report.ProblemArgs = new object[] { advisory.NetworkId ?? 0 };
                    report.EvidenceKey = "PGM_Trouble_Ev_NoGeneration";
                    report.EvidenceArgs = new object[]
                    {
                        PowerFormatting.FormatWatts(advisory.ConsumptionWatts),
                        PowerFormatting.FormatCharge(advisory.ChargeFraction)
                    };
                    report.LikelyCauseKey = "PGM_Trouble_Cause_NoGeneration";
                    report.RecommendedActionKeys = new[]
                    {
                        "PGM_Trouble_Act_AddGeneration",
                        "PGM_Trouble_Act_RepairGenerator"
                    };
                    break;
                case PowerDiagnosticKind.HighLoad:
                    report.ProblemKey = "PGM_Trouble_Problem_HighLoad";
                    report.ProblemArgs = new object[] { advisory.NetworkId ?? 0 };
                    report.EvidenceKey = "PGM_Trouble_Ev_HighLoad";
                    report.EvidenceArgs = new object[]
                    {
                        PowerFormatting.FormatCharge(advisory.DeficitWatts),
                        PowerFormatting.FormatWatts(advisory.ConsumptionWatts)
                    };
                    report.LikelyCauseKey = "PGM_Trouble_Cause_HighLoad";
                    report.RecommendedActionKeys = new[]
                    {
                        "PGM_Trouble_Act_InspectConsumers",
                        "PGM_Trouble_Act_AddGeneration"
                    };
                    break;
                case PowerDiagnosticKind.IsolatedConsumer:
                    report.ProblemKey = "PGM_Trouble_Problem_Isolated";
                    report.EvidenceKey = "PGM_Trouble_Ev_Isolated";
                    report.EvidenceArgs = new object[]
                    {
                        PowerGridUtilities.BuildingLabel(advisory.Building),
                        PowerFormatting.FormatWatts(advisory.ConfiguredWatts)
                    };
                    report.LikelyCauseKey = "PGM_Trouble_Cause_Isolated";
                    report.RecommendedActionKeys = new[]
                    {
                        "PGM_Trouble_Act_ConnectBuilding",
                        "PGM_Trouble_Act_InspectBuilding"
                    };
                    break;
                case PowerDiagnosticKind.GeneratorUnderOutput:
                    report.ProblemKey = "PGM_Trouble_Problem_Generator";
                    report.EvidenceKey = "PGM_Trouble_Ev_Generator";
                    report.EvidenceArgs = new object[]
                    {
                        PowerGridUtilities.BuildingLabel(advisory.Building),
                        PowerFormatting.FormatWatts(advisory.MeasuredWatts),
                        PowerFormatting.FormatWatts(advisory.ConfiguredWatts),
                        PowerFormatting.FormatDuration(advisory.SustainedTicks)
                    };
                    report.LikelyCauseKey = "PGM_Trouble_Cause_Generator";
                    report.RecommendedActionKeys = new[]
                    {
                        "PGM_Trouble_Act_InspectBuilding",
                        "PGM_Trouble_Act_RepairGenerator"
                    };
                    break;
                case PowerDiagnosticKind.SwitchedOffButDrawing:
                    report.ProblemKey = "PGM_Trouble_Problem_OffDrawing";
                    report.EvidenceKey = "PGM_Trouble_Ev_OffDrawing";
                    report.EvidenceArgs = new object[]
                    {
                        PowerGridUtilities.BuildingLabel(advisory.Building),
                        PowerFormatting.FormatWatts(advisory.MeasuredWatts)
                    };
                    report.LikelyCauseKey = "PGM_Trouble_Cause_OffDrawing";
                    report.RecommendedActionKeys = new[]
                    {
                        "PGM_Trouble_Act_SwitchOff",
                        "PGM_Trouble_Act_InspectBuilding"
                    };
                    report.OptionalActionKey = "PGM_Trouble_Opt_SwitchOff";
                    report.OptionalActionTip = "PGM_Trouble_Opt_SwitchOffTip";
                    break;
                case PowerDiagnosticKind.DrawAboveConfigured:
                    report.ProblemKey = "PGM_Trouble_Problem_AboveConfig";
                    report.EvidenceKey = "PGM_Trouble_Ev_AboveConfig";
                    report.EvidenceArgs = new object[]
                    {
                        PowerGridUtilities.BuildingLabel(advisory.Building),
                        PowerFormatting.FormatWatts(advisory.MeasuredWatts),
                        PowerFormatting.FormatWatts(advisory.ConfiguredWatts)
                    };
                    report.LikelyCauseKey = "PGM_Trouble_Cause_AboveConfig";
                    report.RecommendedActionKeys = new[] { "PGM_Trouble_Act_InspectBuilding" };
                    break;
                case PowerDiagnosticKind.UnsupportedComponent:
                    report.ProblemKey = "PGM_Trouble_Problem_Unsupported";
                    report.EvidenceKey = "PGM_Trouble_Ev_Unsupported";
                    report.EvidenceArgs = new object[] { PowerGridUtilities.BuildingLabel(advisory.Building) };
                    report.LikelyCauseKey = "PGM_Trouble_Cause_Unsupported";
                    report.RecommendedActionKeys = new[] { "PGM_Trouble_Act_InspectBuilding" };
                    break;
                case PowerDiagnosticKind.PendingMembership:
                    report.ProblemKey = "PGM_Trouble_Problem_Pending";
                    report.EvidenceKey = "PGM_Trouble_Ev_Pending";
                    report.LikelyCauseKey = "PGM_Trouble_Cause_Pending";
                    report.RecommendedActionKeys = new[] { "PGM_Trouble_Act_Wait" };
                    break;
                default:
                    return NoProblem();
            }
            return report;
        }

        // --- Synthesis when no advisory fired --------------------------------------

        private static PowerDiagnosticReport DiagnoseMap(PowerGridSnapshot snapshot,
            PowerGridManagerSettings settings, int now, float minimumDeficit)
        {
            float deficit = snapshot.ConsumptionWatts - snapshot.GenerationWatts;
            if (snapshot.ConsumerCount > 0 && deficit >= minimumDeficit)
            {
                var report = new PowerDiagnosticReport
                {
                    Severity = snapshot.BatteryCount > 0 && snapshot.StoredWattDays > 0.001f
                        ? PowerDiagnosticSeverity.Warning : PowerDiagnosticSeverity.Critical,
                    ProblemKey = "PGM_Trouble_Problem_MapDeficit",
                    EvidenceKey = "PGM_Trouble_Ev_MapDeficit",
                    EvidenceArgs = new object[]
                    {
                        PowerFormatting.FormatWatts(snapshot.GenerationWatts),
                        PowerFormatting.FormatWatts(snapshot.ConsumptionWatts),
                        PowerFormatting.FormatWatts(deficit)
                    },
                    LikelyCauseKey = "PGM_Trouble_Cause_MapDeficit",
                    RecommendedActionKeys = new[]
                    {
                        "PGM_Trouble_Act_InspectConsumers",
                        "PGM_Trouble_Act_AddGeneration",
                        "PGM_Trouble_Act_PreviewLoadShedding"
                    }
                };
                return report;
            }
            if (snapshot.BatteryCount > 0 && snapshot.CapacityWattDays > 0f)
            {
                float charge = snapshot.ChargeFraction ?? 0f;
                if (charge <= settings.LowBatteryWarningFraction && snapshot.NetWatts < -0.01f)
                {
                    return new PowerDiagnosticReport
                    {
                        Severity = PowerDiagnosticSeverity.Warning,
                        ProblemKey = "PGM_Trouble_Problem_LowBattery",
                        EvidenceKey = "PGM_Trouble_Ev_LowBattery",
                        EvidenceArgs = new object[]
                        {
                            PowerFormatting.FormatCharge(charge),
                            PowerFormatting.FormatWatts(deficit)
                        },
                        LikelyCauseKey = "PGM_Trouble_Cause_LowBattery",
                        RecommendedActionKeys = new[]
                        {
                            "PGM_Trouble_Act_ReduceDemand",
                            "PGM_Trouble_Act_AddGeneration"
                        }
                    };
                }
            }
            return NoProblem();
        }

        private static PowerDiagnosticReport DiagnoseNetwork(PowerNetworkSnapshot network,
            PowerGridManagerSettings settings, int now, float minimumDeficit)
        {
            float deficit = network.ConsumptionWatts - network.GenerationWatts;
            if (network.ConsumerCount > 0 && deficit >= minimumDeficit)
            {
                return new PowerDiagnosticReport
                {
                    Severity = PowerDiagnosticSeverity.Warning,
                    ProblemKey = "PGM_Trouble_Problem_NetworkDeficit",
                    ProblemArgs = new object[] { network.Id },
                    EvidenceKey = "PGM_Trouble_Ev_NetworkDeficit",
                    EvidenceArgs = new object[]
                    {
                        network.Id,
                        PowerFormatting.FormatWatts(network.GenerationWatts),
                        PowerFormatting.FormatWatts(network.ConsumptionWatts),
                        PowerFormatting.FormatWatts(deficit)
                    },
                    LikelyCauseKey = "PGM_Trouble_Cause_NetworkDeficit",
                    RecommendedActionKeys = new[]
                    {
                        "PGM_Trouble_Act_InspectConsumers",
                        "PGM_Trouble_Act_AddGeneration"
                    }
                };
            }
            return NoProblem();
        }

        private static PowerDiagnosticReport DiagnoseBuilding(PowerGridSnapshot snapshot,
            Building building, PowerGridManagerSettings settings, int now)
        {
            for (int i = 0; i < snapshot.Buildings.Count; i++)
            {
                if (ReferenceEquals(snapshot.Buildings[i].Building, building))
                {
                    var row = snapshot.Buildings[i];
                    if (row.Connection == PowerConnectionState.NoNetwork && row.IsConsumer)
                    {
                        return new PowerDiagnosticReport
                        {
                            Severity = PowerDiagnosticSeverity.Warning,
                            ProblemKey = "PGM_Trouble_Problem_BuildingIsolated",
                            NavigationBuilding = building,
                            EvidenceKey = "PGM_Trouble_Ev_BuildingIsolated",
                            EvidenceArgs = new object[]
                            {
                                PowerGridUtilities.BuildingLabel(building),
                                PowerFormatting.FormatWatts(row.ConsumptionWatts)
                            },
                            LikelyCauseKey = "PGM_Trouble_Cause_BuildingIsolated",
                            RecommendedActionKeys = new[]
                            {
                                "PGM_Trouble_Act_ConnectBuilding",
                                "PGM_Trouble_Act_InspectBuilding"
                            }
                        };
                    }
                    if (row.IsFlickable && row.SwitchIsOn == false && row.ConsumptionWatts > 0.01f)
                    {
                        return new PowerDiagnosticReport
                        {
                            Severity = PowerDiagnosticSeverity.Warning,
                            ProblemKey = "PGM_Trouble_Problem_BuildingOffDrawing",
                            NavigationBuilding = building,
                            EvidenceKey = "PGM_Trouble_Ev_BuildingOffDrawing",
                            EvidenceArgs = new object[]
                            {
                                PowerGridUtilities.BuildingLabel(building),
                                PowerFormatting.FormatWatts(row.ConsumptionWatts)
                            },
                            LikelyCauseKey = "PGM_Trouble_Cause_BuildingOffDrawing",
                            RecommendedActionKeys = new[]
                            {
                                "PGM_Trouble_Act_SwitchOff",
                                "PGM_Trouble_Act_InspectBuilding"
                            },
                            OptionalActionKey = "PGM_Trouble_Opt_SwitchOff",
                            OptionalActionTip = "PGM_Trouble_Opt_SwitchOffTip"
                        };
                    }
                    return NoProblem();
                }
            }
            return NoProblem();
        }

        private static PowerDiagnosticReport NoProblem()
        {
            return new PowerDiagnosticReport
            {
                Severity = PowerDiagnosticSeverity.Info,
                ProblemKey = "PGM_Trouble_Problem_None",
                LikelyCauseKey = "PGM_Trouble_Cause_None",
                RecommendedActionKeys = new string[0]
            };
        }
    }
}
