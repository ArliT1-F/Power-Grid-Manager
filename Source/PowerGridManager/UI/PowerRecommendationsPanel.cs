using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// The Optimization tab: actionable recommendations produced by
    /// <see cref="PowerRecommendationEngine"/>, each with severity, evidence, an estimated
    /// benefit, and explicit dismiss / inspect / locate actions. The panel only formats
    /// what the engine already produced; it never changes the power simulation.
    /// </summary>
    public sealed class PowerRecommendationsPanel
    {
        private const float HeaderHeight = 28f;
        private const float RowHeight = 122f;
        private const float RowGap = 4f;
        private const float PillWidth = 118f;
        private const float ButtonWidth = 84f;

        private readonly List<PowerRecommendation> visible = new List<PowerRecommendation>(32);
        private int ongoingCount;
        private int dismissedCount;

        public int VisibleCount => visible.Count;

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, PowerRecommendationTracker tracker,
            IReadOnlyList<PowerRecommendation> recommendations, Action<int> showBuildingsOnNetwork)
        {
            float y = rect.y;
            DrawHeader(new Rect(rect.x, y, rect.width, HeaderHeight), state, settings, tracker);
            y += HeaderHeight + 4f;

            Rect body = new Rect(rect.x, y, rect.width, Mathf.Max(60f, rect.yMax - y));
            PowerUiDraw.Panel(body);

            BuildVisible(recommendations, state.RecommendationView);
            if (visible.Count == 0)
            {
                PowerUiDraw.WrappedText(new Rect(body.x + 10f, body.y + 10f, body.width - 20f, 80f),
                    EmptyMessage(recommendations, state.RecommendationView, settings), PowerUiDraw.Muted);
                return;
            }

            Rect outer = new Rect(body.x + 4f, body.y + 4f, body.width - 8f, body.height - 8f);
            float contentHeight = visible.Count * (RowHeight + RowGap);
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(contentHeight, outer.height));
            Widgets.BeginScrollView(outer, ref state.RecommendationScroll, view);
            try
            {
                float rowY = 0f;
                for (int i = 0; i < visible.Count; i++)
                {
                    Rect row = new Rect(0f, rowY, view.width, RowHeight);
                    DrawRow(row, visible[i], snapshot, state, settings, tracker, showBuildingsOnNetwork);
                    rowY += RowHeight + RowGap;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawHeader(Rect rect, PowerUiState state, PowerGridManagerSettings settings,
            PowerRecommendationTracker tracker)
        {
            float x = rect.x;
            PowerUiDraw.DrawText(new Rect(x, rect.y + 4f, 190f, 24f), "PGM_Rec_Title".Translate(), Color.white);
            x += 196f;
            string counts = "PGM_Rec_Counts".Translate(ongoingCount, dismissedCount);
            PowerUiDraw.DrawText(new Rect(x, rect.y + 4f, 210f, 24f), counts, PowerUiDraw.Muted);
            x += 216f;

            Rect filterRect = new Rect(x, rect.y, 118f, 26f);
            if (Widgets.ButtonText(filterRect, "PGM_Rec_FilterButton".Translate(FilterLabel(state.RecommendationView))))
            {
                OpenFilterMenu(state);
            }
            x += filterRect.width + 4f;

            if (tracker != null && tracker.DismissedCount > 0)
            {
                Rect restoreRect = new Rect(x, rect.y, 132f, 26f);
                if (Widgets.ButtonText(restoreRect, "PGM_Rec_RestoreAll".Translate(tracker.DismissedCount)))
                {
                    tracker.ClearDismissals();
                }
                PowerUiDraw.Tip(restoreRect, "PGM_Rec_RestoreAllTip".Translate());
            }
        }

        private void BuildVisible(IReadOnlyList<PowerRecommendation> recommendations, RecommendationFilter filter)
        {
            visible.Clear();
            ongoingCount = 0;
            dismissedCount = 0;
            if (recommendations == null) return;
            for (int i = 0; i < recommendations.Count; i++)
            {
                PowerRecommendation recommendation = recommendations[i];
                if (recommendation == null) continue;
                if (recommendation.Dismissed) dismissedCount++;
                else if (recommendation.Ongoing) ongoingCount++;

                switch (filter)
                {
                    case RecommendationFilter.OngoingOnly:
                        if (!recommendation.Ongoing) continue;
                        break;
                    case RecommendationFilter.DismissedOnly:
                        if (!recommendation.Dismissed) continue;
                        break;
                }
                visible.Add(recommendation);
            }
        }

        private void DrawRow(Rect rect, PowerRecommendation recommendation, PowerGridSnapshot snapshot,
            PowerUiState state, PowerGridManagerSettings settings, PowerRecommendationTracker tracker,
            Action<int> showBuildingsOnNetwork)
        {
            Widgets.DrawBox(rect, 1);
            if (!recommendation.Ongoing) Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.03f));

            Color severityColor = PowerUiDraw.SeverityColor((PowerDiagnosticSeverity)recommendation.Severity);
            Rect pill = new Rect(rect.x + 6f, rect.y + 6f, PillWidth, 18f);
            PowerUiDraw.StatusPill(pill, PowerGridUtilities.SeverityLabelKey((PowerDiagnosticSeverity)recommendation.Severity).Translate(),
                severityColor);

            float titleX = pill.xMax + 6f;
            float statusWidth = 132f;
            PowerUiDraw.TextEllipsized(new Rect(titleX, rect.y + 4f, Mathf.Max(40f, rect.xMax - titleX - statusWidth - 12f), 22f),
                recommendation.TitleKey, Color.white);
            PowerUiDraw.DrawText(new Rect(rect.xMax - statusWidth - 6f, rect.y + 4f, statusWidth, 22f),
                Status(recommendation), recommendation.Ongoing ? severityColor : PowerUiDraw.Muted, TextAnchor.MiddleRight);

            float textX = rect.x + 8f;
            float textWidth = Mathf.Max(60f, rect.width - 16f);
            PowerUiDraw.WrappedText(new Rect(textX, rect.y + 28f, textWidth, 34f), recommendation.ExplanationKey.Translate(),
                new Color(0.86f, 0.86f, 0.86f));
            PowerUiDraw.TextEllipsized(new Rect(textX, rect.y + 62f, textWidth, 18f), Evidence(recommendation), PowerUiDraw.Muted);
            PowerUiDraw.TextEllipsized(new Rect(textX, rect.y + 80f, textWidth, 18f), Benefit(recommendation), PowerUiDraw.Info);

            float buttonY = rect.yMax - 28f;
            float buttonX = rect.xMax - 6f;

            buttonX -= ButtonWidth;
            Rect dismissRect = new Rect(buttonX, buttonY, ButtonWidth - 4f, 24f);
            string dismissLabel = recommendation.Dismissed ? "PGM_Rec_Restore".Translate() : "PGM_Rec_Dismiss".Translate();
            if (Widgets.ButtonText(dismissRect, dismissLabel) && tracker != null)
            {
                if (recommendation.Dismissed) tracker.ClearDismissals();
                else tracker.Dismiss(recommendation, recommendation.LastSeenTick);
            }
            PowerUiDraw.Tip(dismissRect, recommendation.Dismissed ? "PGM_Rec_RestoreTip".Translate() : "PGM_Rec_DismissTip".Translate());

            Building target = recommendation.Building;
            if (target != null && !target.Destroyed && target.Spawned)
            {
                buttonX -= ButtonWidth;
                Rect locateRect = new Rect(buttonX, buttonY, ButtonWidth - 4f, 24f);
                if (Widgets.ButtonText(locateRect, "PGM_Rec_Locate".Translate()))
                {
                    CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(target));
                }
                PowerUiDraw.Tip(locateRect, "PGM_Rec_LocateTip".Translate());

                buttonX -= ButtonWidth;
                Rect inspectRect = new Rect(buttonX, buttonY, ButtonWidth - 4f, 24f);
                if (Widgets.ButtonText(inspectRect, "PGM_Rec_Inspect".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(target));
                }
                PowerUiDraw.Tip(inspectRect, "PGM_Rec_InspectTip".Translate());
            }

            if (recommendation.NetworkId.HasValue && showBuildingsOnNetwork != null)
            {
                buttonX -= ButtonWidth;
                Rect showRect = new Rect(buttonX, buttonY, ButtonWidth - 4f, 24f);
                if (Widgets.ButtonText(showRect, "PGM_Rec_ShowBuildings".Translate()))
                {
                    showBuildingsOnNetwork(recommendation.NetworkId.Value);
                }
                PowerUiDraw.Tip(showRect, "PGM_Rec_ShowBuildingsTip".Translate());
            }

            PowerUiDraw.Tip(rect, "PGM_Rec_RowTip".Translate());
        }

        private static string Evidence(PowerRecommendation recommendation)
        {
            if (recommendation.EvidenceArgs == null) return recommendation.EvidenceKey;
            return recommendation.EvidenceKey.Translate(recommendation.EvidenceArgs);
        }

        private static string Benefit(PowerRecommendation recommendation)
        {
            if (!recommendation.Ongoing) return "PGM_Rec_Resolved".Translate();
            if (recommendation.EstimatedBenefitWatts > 0.01f)
            {
                string label = recommendation.BenefitIsEstimate
                    ? "PGM_Rec_BenefitEstimate".Translate(PowerFormatting.FormatWatts(recommendation.EstimatedBenefitWatts))
                    : "PGM_Rec_Benefit".Translate(PowerFormatting.FormatWatts(recommendation.EstimatedBenefitWatts));
                return label;
            }
            return "PGM_Rec_BenefitNone".Translate();
        }

        private static string Status(PowerRecommendation recommendation)
        {
            string label = PowerGridUtilities.DiagnosticStatusKey(recommendation.Ongoing).Translate();
            if (recommendation.Ongoing)
                return "PGM_Rec_Duration".Translate(label,
                    PowerFormatting.FormatDuration(recommendation.LastSeenTick - recommendation.FirstSeenTick));
            return label;
        }

        private static string EmptyMessage(IReadOnlyList<PowerRecommendation> recommendations,
            RecommendationFilter filter, PowerGridManagerSettings settings)
        {
            bool any = recommendations != null && recommendations.Count > 0;
            if (any) return "PGM_Rec_EmptyFiltered".Translate();
            if (settings == null) return "PGM_Rec_Empty".Translate();
            return "PGM_Rec_Empty".Translate();
        }

        private static string FilterLabel(RecommendationFilter filter)
        {
            switch (filter)
            {
                case RecommendationFilter.OngoingOnly: return "PGM_Rec_Filter_Ongoing".Translate();
                case RecommendationFilter.DismissedOnly: return "PGM_Rec_Filter_Dismissed".Translate();
                default: return "PGM_Rec_Filter_All".Translate();
            }
        }

        private static void OpenFilterMenu(PowerUiState state)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("PGM_Rec_Filter_All".Translate(), () => state.RecommendationView = RecommendationFilter.All),
                new FloatMenuOption("PGM_Rec_Filter_Ongoing".Translate(), () => state.RecommendationView = RecommendationFilter.OngoingOnly),
                new FloatMenuOption("PGM_Rec_Filter_Dismissed".Translate(), () => state.RecommendationView = RecommendationFilter.DismissedOnly)
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    /// <summary>Which recommendations the Optimization tab shows.</summary>
    public enum RecommendationFilter
    {
        All = 0,
        OngoingOnly,
        DismissedOnly
    }
}
