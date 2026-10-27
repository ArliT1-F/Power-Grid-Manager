using System;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Modernized guided "Diagnose Power Problem" dialog. Produces a deterministic,
    /// rule-based report for the whole map, a selected network, or a selected building,
    /// styled in the dark electrical-grid monitoring palette with full-bleed custom chrome.
    /// </summary>
    public sealed class PowerTroubleshootingDialog : Window
    {
        public override Vector2 InitialSize => new Vector2(760f, 560f);

        protected override float Margin => 0f;

        private readonly PowerGridManagerComponent component;
        private readonly PowerUiState state;
        private readonly PowerGridManagerSettings settings;

        private DiagnosisScope scope = DiagnosisScope.WholeMap;
        private PowerDiagnosticReport report;
        private bool reportReady;
        private Vector2 reportScroll;

        public PowerTroubleshootingDialog(PowerGridManagerComponent component, PowerUiState state)
        {
            this.component = component;
            this.state = state;
            this.settings = PowerGridManagerMod.Settings;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            doCloseButton = false;
            doCloseX = false;
            doWindowBackground = false;
            closeOnAccept = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (inRect.width <= 8f || inRect.height <= 8f) return;

            PowerUiDraw.DrawWindowBackground(inRect);
            Rect content = inRect.ContractedBy(12f);

            float y = content.y;

            // Header bar with close button.
            Rect headerRect = new Rect(content.x, y, content.width, 36f);
            PowerUiDraw.Panel(headerRect);
            Widgets.DrawBoxSolid(new Rect(headerRect.x, headerRect.y, headerRect.width, 2f),
                PowerUiDraw.AccentPrimary);
            PowerUiDraw.TextEllipsized(
                new Rect(headerRect.x + 12f, headerRect.y + 6f, Mathf.Max(40f, headerRect.width - 52f), 24f),
                "PGM_Trouble_Title".Translate(), PowerUiDraw.TextPrimary,
                TextAnchor.MiddleLeft, GameFont.Small);
            Rect closeRect = new Rect(headerRect.xMax - 30f, headerRect.y + 6f, 24f, 24f);
            if (PowerUiDraw.Button(closeRect, "\u00D7", false, false, true, PowerUiDraw.NoticeColor))
            {
                Close();
                return;
            }
            y += 44f;

            // Scope + Diagnose control card.
            Rect ctrlCard = new Rect(content.x, y, content.width, 74f);
            PowerUiDraw.Panel(ctrlCard);
            Rect ctrlInner = ctrlCard.ContractedBy(8f);
            DrawScope(new Rect(ctrlInner.x, ctrlInner.y, ctrlInner.width, 26f));

            Rect buttonArea = new Rect(ctrlInner.x, ctrlInner.y + 32f, ctrlInner.width, 26f);
            if (PowerUiDraw.Button(buttonArea, "PGM_Trouble_Diagnose".Translate(), false, true))
            {
                RunDiagnosis();
            }
            PowerUiDraw.Tip(buttonArea, "PGM_Trouble_DiagnoseTip".Translate());
            y += 82f;

            // Report body.
            Rect body = new Rect(content.x, y, content.width, Mathf.Max(60f, content.yMax - y));
            PowerUiDraw.Panel(body);
            if (!reportReady)
            {
                PowerUiDraw.WrappedText(new Rect(body.x + 12f, body.y + 12f, body.width - 24f, 68f),
                    "PGM_Trouble_Prompt".Translate(), PowerUiDraw.TextSecondary);
                return;
            }
            DrawReport(body);
        }

        private void DrawScope(Rect rect)
        {
            float x = rect.x;
            float width = Mathf.Max(60f, (rect.width - 12f) / 3f);
            DiagnosisScope[] scopes =
            {
                DiagnosisScope.WholeMap,
                DiagnosisScope.SelectedNetwork,
                DiagnosisScope.SelectedBuilding
            };
            string[] labels =
            {
                "PGM_Trouble_Scope_Map".Translate(),
                "PGM_Trouble_Scope_Network".Translate(),
                "PGM_Trouble_Scope_Building".Translate()
            };
            for (int i = 0; i < scopes.Length; i++)
            {
                DiagnosisScope s = scopes[i];
                Rect b = new Rect(x, rect.y, width, 26f);
                if (PowerUiDraw.Button(b, labels[i], this.scope == s))
                    this.scope = s;
                x += width + 6f;
            }
        }

        private void RunDiagnosis()
        {
            PowerGridSnapshot snapshot = component == null ? null : component.Snapshot;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            Building selected = state == null ? null : state.SelectedBuilding;
            int? network = state == null ? (int?)null : state.SelectedNetworkId;
            report = PowerTroubleshootingAssistant.Diagnose(scope, snapshot, settings,
                component == null ? null : component.Advisories, now, selected, network);
            reportReady = true;
        }

        private void DrawReport(Rect rect)
        {
            if (report == null) return;

            Rect outer = rect.ContractedBy(8f);
            int actionCount = report.RecommendedActionKeys == null ? 0 : report.RecommendedActionKeys.Length;
            float estHeight = 200f + actionCount * 22f;
            Rect view = new Rect(0f, 0f, Mathf.Max(140f, outer.width - 18f), Mathf.Max(outer.height, estHeight));

            Widgets.BeginScrollView(outer, ref reportScroll, view);
            try
            {
                bool noProblem = report.ProblemKey == "PGM_Trouble_Problem_None";
                Color severityColor = noProblem
                    ? PowerUiDraw.Good
                    : PowerUiDraw.SeverityColor(report.Severity);
                string symbol = noProblem ? "OK" : PowerUiDraw.SeveritySymbol(report.Severity);

                Rect summaryCard = new Rect(0f, 0f, view.width, 64f);
                PowerUiDraw.AccentCard(summaryCard, severityColor, PowerUiDraw.BgElevated);
                Rect sumInner = summaryCard.ContractedBy(8f);

                Rect pill = new Rect(sumInner.x, sumInner.y, 118f, 20f);
                string sevText = noProblem
                    ? (string)"PGM_Overview_NominalBadge".Translate()
                    : (string)PowerGridUtilities.SeverityLabelKey(report.Severity).Translate();
                PowerUiDraw.StatusBadge(pill, sevText, severityColor, symbol);

                PowerUiDraw.TextEllipsized(
                    new Rect(sumInner.x, sumInner.y + 24f, sumInner.width, 24f),
                    FormatKey(report.ProblemKey, report.ProblemArgs),
                    PowerUiDraw.TextPrimary, TextAnchor.MiddleLeft, GameFont.Small);

                float y = summaryCard.yMax + 8f;

                if (!string.IsNullOrEmpty(report.EvidenceKey))
                {
                    Rect evBox = new Rect(0f, y, view.width, 38f);
                    Widgets.DrawBoxSolid(evBox, PowerUiDraw.BgRecessed);
                    PowerUiDraw.DrawBorder(evBox, PowerUiDraw.BorderPanel, 1);
                    PowerUiDraw.WrappedText(evBox.ContractedBy(6f),
                        FormatKey(report.EvidenceKey, report.EvidenceArgs),
                        PowerUiDraw.TextPrimary);
                    y += 46f;
                }

                if (!string.IsNullOrEmpty(report.LikelyCauseKey))
                {
                    PowerUiDraw.SectionHeader(new Rect(0f, y, view.width, 20f),
                        "PGM_Trouble_LikelyCause".Translate());
                    y += 22f;
                    PowerUiDraw.WrappedText(new Rect(0f, y, view.width, 36f),
                        FormatKey(report.LikelyCauseKey, report.CauseArgs),
                        PowerUiDraw.TextSecondary);
                    y += 40f;
                }

                if (report.RecommendedActionKeys != null && report.RecommendedActionKeys.Length > 0)
                {
                    PowerUiDraw.SectionHeader(new Rect(0f, y, view.width, 20f),
                        "PGM_Trouble_RecommendedActions".Translate());
                    y += 22f;
                    for (int i = 0; i < report.RecommendedActionKeys.Length; i++)
                    {
                        PowerUiDraw.TextEllipsized(new Rect(4f, y, view.width - 8f, 22f),
                            "\u2022 " + FormatKey(report.RecommendedActionKeys[i], null),
                            PowerUiDraw.AccentCyan);
                        y += 22f;
                    }
                    y += 6f;
                }

                if (report.HasNavigationBuilding)
                {
                    float btnW = Mathf.Min(140f, (view.width - 8f) * 0.5f);
                    Rect locateRect = new Rect(0f, y, btnW, 26f);
                    if (PowerUiDraw.Button(locateRect, "PGM_Trouble_Locate".Translate(), false, true))
                    {
                        CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(report.NavigationBuilding));
                    }
                    PowerUiDraw.Tip(locateRect, "PGM_Trouble_LocateTip".Translate());

                    Rect inspectRect = new Rect(locateRect.xMax + 8f, y, btnW, 26f);
                    if (PowerUiDraw.Button(inspectRect, "PGM_Trouble_Inspect".Translate()))
                    {
                        Find.WindowStack.Add(new Dialog_InfoCard(report.NavigationBuilding));
                    }
                    PowerUiDraw.Tip(inspectRect, "PGM_Trouble_InspectTip".Translate());
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private static string FormatKey(string key, object[] args)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (args == null || args.Length == 0) return key.Translate();
            return key.Translate(args);
        }
    }
}
