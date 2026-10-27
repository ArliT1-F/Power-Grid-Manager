using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Guided "Diagnose Power Problem" dialog. It produces a deterministic, rule-based
    /// report for the whole map, a selected network, or a selected building, and lets the
    /// player navigate to the affected elements. Nothing here changes the power simulation.
    /// </summary>
    public sealed class PowerTroubleshootingDialog : Window
    {
        public override Vector2 InitialSize => new Vector2(740f, 560f);

        private readonly PowerGridManagerComponent component;
        private readonly PowerUiState state;
        private readonly PowerGridManagerSettings settings;

        private DiagnosisScope scope = DiagnosisScope.WholeMap;
        private PowerDiagnosticReport report;
        private bool reportReady;

        public PowerTroubleshootingDialog(PowerGridManagerComponent component, PowerUiState state)
        {
            this.component = component;
            this.state = state;
            this.settings = PowerGridManagerMod.Settings;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            doCloseButton = true;
            closeOnAccept = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;
            Rect title = new Rect(inRect.x, y, inRect.width, 30f);
            PowerUiDraw.DrawText(title, "PGM_Trouble_Title".Translate(), Color.white);
            y += 34f;

            Rect scopeArea = new Rect(inRect.x, y, inRect.width, 34f);
            DrawScope(scopeArea);
            y += 40f;

            Rect buttonArea = new Rect(inRect.x, y, inRect.width, 30f);
            if (Widgets.ButtonText(buttonArea, "PGM_Trouble_Diagnose".Translate()))
            {
                RunDiagnosis();
            }
            PowerUiDraw.Tip(buttonArea, "PGM_Trouble_DiagnoseTip".Translate());
            y += 36f;

            Rect body = new Rect(inRect.x, y, inRect.width, Mathf.Max(100f, inRect.yMax - y));
            PowerUiDraw.Panel(body);
            if (!reportReady)
            {
                PowerUiDraw.WrappedText(new Rect(body.x + 10f, body.y + 10f, body.width - 20f, 60f),
                    "PGM_Trouble_Prompt".Translate(), PowerUiDraw.Muted);
                return;
            }
            DrawReport(body);
        }

        private void DrawScope(Rect rect)
        {
            float x = rect.x;
            float width = Mathf.Min(160f, rect.width / 3f - 8f);
            Action<DiagnosisScope, string> button = (scopeValue, label) =>
            {
                Rect b = new Rect(x, rect.y, width, 26f);
                bool selected = this.scope == scopeValue;
                if (selected) Widgets.DrawBoxSolid(b, new Color(1f, 1f, 1f, 0.14f));
                if (Widgets.ButtonText(b, label)) this.scope = scopeValue;
                x += width + 8f;
            };
            button(DiagnosisScope.WholeMap, "PGM_Trouble_Scope_Map".Translate());
            button(DiagnosisScope.SelectedNetwork, "PGM_Trouble_Scope_Network".Translate());
            button(DiagnosisScope.SelectedBuilding, "PGM_Trouble_Scope_Building".Translate());
        }

        private void RunDiagnosis()
        {
            PowerGridSnapshot snapshot = component == null ? null : component.Snapshot;
            int now = Find.TickManager.TicksGame;
            Building selected = state == null ? null : state.SelectedBuilding;
            int? network = state == null ? (int?)null : state.SelectedNetworkId;
            report = PowerTroubleshootingAssistant.Diagnose(scope, snapshot, settings,
                component == null ? null : component.Advisories, now, selected, network);
            reportReady = true;
        }

        private void DrawReport(Rect rect)
        {
            if (report == null) return;
            Color severityColor = PowerUiDraw.SeverityColor(report.Severity);
            float y = rect.y + 8f;

            Rect problem = new Rect(rect.x + 8f, y, rect.width - 16f, 26f);
            PowerUiDraw.DrawText(problem, report.ProblemKey.Translate(report.ProblemArgs), Color.white);
            y += 30f;

            Rect pill = new Rect(rect.x + 8f, y, 118f, 18f);
            PowerUiDraw.StatusPill(pill,
                PowerGridUtilities.SeverityLabelKey(report.Severity).Translate(), severityColor);
            y += 26f;

            if (!string.IsNullOrEmpty(report.EvidenceKey))
            {
                Rect evidence = new Rect(rect.x + 8f, y, rect.width - 16f, 34f);
                PowerUiDraw.WrappedText(evidence, report.EvidenceKey.Translate(report.EvidenceArgs),
                    new Color(0.86f, 0.86f, 0.86f));
                y += 40f;
            }

            if (!string.IsNullOrEmpty(report.LikelyCauseKey))
            {
                Rect causeLabel = new Rect(rect.x + 8f, y, rect.width - 16f, 18f);
                PowerUiDraw.DrawText(causeLabel, "PGM_Trouble_LikelyCause".Translate(), PowerUiDraw.Muted);
                y += 20f;
                Rect cause = new Rect(rect.x + 8f, y, rect.width - 16f, 34f);
                PowerUiDraw.WrappedText(cause, report.LikelyCauseKey.Translate(report.CauseArgs),
                    Color.white);
                y += 40f;
            }

            if (report.RecommendedActionKeys != null && report.RecommendedActionKeys.Length > 0)
            {
                Rect actionsLabel = new Rect(rect.x + 8f, y, rect.width - 16f, 18f);
                PowerUiDraw.DrawText(actionsLabel, "PGM_Trouble_RecommendedActions".Translate(),
                    PowerUiDraw.Muted);
                y += 20f;
                for (int i = 0; i < report.RecommendedActionKeys.Length; i++)
                {
                    Rect action = new Rect(rect.x + 8f, y, rect.width - 16f, 22f);
                    PowerUiDraw.DrawText(action, "• " + report.RecommendedActionKeys[i].Translate(),
                        Color.white);
                    y += 22f;
                }
                y += 6f;
            }

            Rect nav = new Rect(rect.x + 8f, y, rect.width - 16f, 26f);
            if (report.HasNavigationBuilding)
            {
                if (Widgets.ButtonText(nav, "PGM_Trouble_Locate".Translate()))
                {
                    CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(report.NavigationBuilding));
                }
                PowerUiDraw.Tip(nav, "PGM_Trouble_LocateTip".Translate());
                nav = new Rect(nav.x + 100f, nav.y, Mathf.Max(0f, nav.width - 100f), nav.height);
            }
            if (report.HasNavigationBuilding)
            {
                if (Widgets.ButtonText(nav, "PGM_Trouble_Inspect".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(report.NavigationBuilding));
                }
                PowerUiDraw.Tip(nav, "PGM_Trouble_InspectTip".Translate());
            }
        }
    }
}
