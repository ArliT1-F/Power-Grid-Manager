using RimWorld;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// One guided troubleshooting report produced by <see cref="PowerTroubleshootingAssistant"/>.
    /// It is deterministic and rule-based: no external service, no guessing, and it never
    /// applies a change without the player's explicit action.
    /// </summary>
    public sealed class PowerDiagnosticReport
    {
        public PowerDiagnosticSeverity Severity = PowerDiagnosticSeverity.Info;

        /// <summary>Localization key for the problem statement.</summary>
        public string ProblemKey;
        public object[] ProblemArgs;

        /// <summary>Localization key for the supporting measurements.</summary>
        public string EvidenceKey;
        public object[] EvidenceArgs;

        /// <summary>Localization key for the likely cause.</summary>
        public string LikelyCauseKey;
        public object[] CauseArgs;

        /// <summary>Prioritized recommended actions, as localization keys.</summary>
        public string[] RecommendedActionKeys;

        /// <summary>Building to jump to / inspect, when one can be identified.</summary>
        public Building NavigationBuilding;

        /// <summary>Network whose buildings to show, when relevant.</summary>
        public int? NavigationNetworkId;

        /// <summary>Optional safe action the player may preview, when one exists.</summary>
        public string OptionalActionKey;
        public string OptionalActionTip;

        public bool HasProblem => !string.IsNullOrEmpty(ProblemKey);
        public bool HasNavigationBuilding =>
            NavigationBuilding != null && !NavigationBuilding.Destroyed && NavigationBuilding.Spawned;
        public bool HasNavigationNetwork => NavigationNetworkId.HasValue;
    }

    /// <summary>Scope of a troubleshooting diagnosis.</summary>
    public enum DiagnosisScope
    {
        WholeMap = 0,
        SelectedNetwork,
        SelectedBuilding
    }
}
