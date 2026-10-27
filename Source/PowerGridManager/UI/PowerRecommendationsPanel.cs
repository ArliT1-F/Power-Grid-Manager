using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Modernized Optimization tab: actionable recommendation cards produced by
    /// <see cref="PowerRecommendationEngine"/>, each styled as a software notification card
    /// with severity icon + label, explanation, measured evidence strip, labelled benefit,
    /// recommended actions, and explicit dismiss / inspect / locate actions.
    /// </summary>
    public sealed class PowerRecommendationsPanel
    {
        private const float HeaderHeight = 36f;
        private const float RowHeight = 140f;
        private const float RowGap = 6f;
        private const float PillWidth = 108f;
        private const float ButtonWidth = 88f;

        private readonly List<PowerRecommendation> visible = new List<PowerRecommendation>(32);
        private int ongoingCount;
        private int dismissedCount;

        public int VisibleCount => visible.Count;

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, PowerRecommendationTracker tracker,
            IReadOnlyList<PowerRecommendation> recommendations, Action<int> showBuildingsOnNetwork)
        {
            if (state == null || rect.width <= 8f || rect.height <= 8f) return;

            BuildVisible(recommendations, state.RecommendationView);

            float y = rect.y;
            float headerH = rect.width < 580f ? 62f : HeaderHeight;
            DrawHeader(new Rect(rect.x, y, rect.width, headerH), state, tracker);
            y += headerH + 6f;

            Rect body = new Rect(rect.x, y, rect.width, Mathf.Max(60f, rect.yMax - y));
            PowerUiDraw.Panel(body);

            if (visible.Count == 0)
            {
                bool any = recommendations != null && recommendations.Count > 0;
                if (!any)
                {
                    Rect nomCard = new Rect(body.x + 12f, body.y + 12f,
                        Mathf.Max(20f, body.width - 24f), Mathf.Min(78f, Mathf.Max(42f, body.height - 24f)));
                    PowerUiDraw.AccentCard(nomCard, PowerUiDraw.Good, PowerUiDraw.BgElevated);
                    Rect nomInner = nomCard.ContractedBy(10f);
                    Rect okBadge = new Rect(nomInner.x, nomInner.y, Mathf.Min(110f, nomInner.width), 20f);
                    PowerUiDraw.StatusBadge(okBadge, "PGM_Overview_NominalBadge".Translate(),
                        PowerUiDraw.Good, "OK");
                    PowerUiDraw.WrappedText(
                        new Rect(nomInner.x, nomInner.y + 24f, nomInner.width, Mathf.Max(18f, nomInner.height - 24f)),
                        EmptyMessage(recommendations, state.RecommendationView, settings),
                        PowerUiDraw.TextSecondary);
                }
                else
                {
                    PowerUiDraw.WrappedText(new Rect(body.x + 12f, body.y + 12f, body.width - 24f, 80f),
                        EmptyMessage(recommendations, state.RecommendationView, settings),
                        PowerUiDraw.TextSecondary);
                }
                return;
            }

            Rect outer = new Rect(body.x + 6f, body.y + 6f, body.width - 12f, body.height - 12f);
            float contentHeight = visible.Count * (RowHeight + RowGap);
            Rect view = new Rect(0f, 0f, Mathf.Max(140f, outer.width - 18f), Mathf.Max(contentHeight, outer.height));
            Widgets.BeginScrollView(outer, ref state.RecommendationScroll, view);
            try
            {
                float rowY = 0f;
                for (int i = 0; i < visible.Count; i++)
                {
                    Rect row = new Rect(0f, rowY, view.width, RowHeight);
                    DrawRow(row, visible[i], tracker, showBuildingsOnNetwork);
                    rowY += RowHeight + RowGap;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawHeader(Rect rect, PowerUiState state, PowerRecommendationTracker tracker)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(6f);
            bool twoRow = rect.height > 44f;

            float x = inner.x + 4f;
            Widgets.DrawBoxSolid(new Rect(x, inner.y + 5f, 3f, 13f), PowerUiDraw.AccentCyan);
            PowerUiDraw.TextEllipsized(new Rect(x + 8f, inner.y, 168f, 24f),
                "PGM_Rec_Title".Translate(), PowerUiDraw.TextPrimary);
            x += 180f;

            string counts = "PGM_Rec_Counts".Translate(ongoingCount, dismissedCount);
            if (!twoRow)
            {
                PowerUiDraw.TextEllipsized(new Rect(x, inner.y, Mathf.Min(210f, Mathf.Max(40f, inner.xMax - x - 266f)), 24f),
                    counts, PowerUiDraw.TextSecondary);
            }
            else
            {
                PowerUiDraw.TextEllipsized(new Rect(x, inner.y, Mathf.Max(40f, inner.xMax - x), 24f),
                    counts, PowerUiDraw.TextSecondary);
            }

            float ctrlX = twoRow ? inner.x + 4f : Mathf.Max(x + 120f, inner.xMax - 262f);
            float ctrlY = twoRow ? inner.y + 26f : inner.y;

            Rect filterRect = new Rect(ctrlX, ctrlY, 122f, 24f);
            if (PowerUiDraw.Button(filterRect, "PGM_Rec_FilterButton".Translate(FilterLabel(state.RecommendationView))))
            {
                OpenFilterMenu(state);
            }
            ctrlX += filterRect.width + 6f;

            if (tracker != null && tracker.DismissedCount > 0)
            {
                Rect restoreRect = new Rect(ctrlX, ctrlY, Mathf.Min(132f, Mathf.Max(60f, inner.xMax - ctrlX)), 24f);
                if (PowerUiDraw.Button(restoreRect, "PGM_Rec_RestoreAll".Translate(tracker.DismissedCount)))
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

        private static void DrawRow(Rect rect, PowerRecommendation recommendation,
            PowerRecommendationTracker tracker, Action<int> showBuildingsOnNetwork)
        {
            PowerDiagnosticSeverity sev = (PowerDiagnosticSeverity)recommendation.Severity;
            Color severityColor = PowerUiDraw.SeverityColor(sev);
            Color stripe = recommendation.Ongoing ? severityColor : PowerUiDraw.TextSecondary;
            Color cardFill = recommendation.Ongoing ? PowerUiDraw.BgElevated : PowerUiDraw.BgSecondary;
            PowerUiDraw.AccentCard(rect, stripe, cardFill);

            Rect pill = new Rect(rect.x + 10f, rect.y + 6f, PillWidth, 18f);
            PowerUiDraw.StatusBadge(pill,
                PowerGridUtilities.SeverityLabelKey(sev).Translate(),
                stripe, PowerUiDraw.SeveritySymbol(sev));

            float titleX = pill.xMax + 8f;
            float statusWidth = Mathf.Min(150f, rect.width * 0.28f);
            PowerUiDraw.TextEllipsized(
                new Rect(titleX, rect.y + 4f, Mathf.Max(40f, rect.xMax - titleX - statusWidth - 12f), 22f),
                FormatTitle(recommendation), PowerUiDraw.TextPrimary);
            PowerUiDraw.TextEllipsized(
                new Rect(rect.xMax - statusWidth - 8f, rect.y + 4f, statusWidth, 22f),
                Status(recommendation),
                recommendation.Ongoing ? severityColor : PowerUiDraw.TextSecondary,
                TextAnchor.MiddleRight);

            float textX = rect.x + 10f;
            float textWidth = Mathf.Max(60f, rect.width - 18f);
            PowerUiDraw.WrappedText(new Rect(textX, rect.y + 28f, textWidth, 32f),
                FormatKey(recommendation.ExplanationKey),
                PowerUiDraw.TextSecondary);

            Rect evBox = new Rect(textX, rect.y + 62f, textWidth, 20f);
            Widgets.DrawBoxSolid(evBox, PowerUiDraw.BgRecessed);
            PowerUiDraw.TextEllipsized(new Rect(evBox.x + 6f, evBox.y, evBox.width - 12f, 20f),
                Evidence(recommendation), PowerUiDraw.TextPrimary);

            string actionLine = Benefit(recommendation);
            string acts = FormatActions(recommendation);
            if (acts.Length > 0) actionLine = actionLine + " \u00B7 " + acts;
            PowerUiDraw.TextEllipsized(new Rect(textX, rect.y + 84f, textWidth, 18f),
                actionLine, PowerUiDraw.AccentCyan);

            float buttonY = rect.yMax - 30f;
            float buttonX = rect.xMax - 8f;

            buttonX -= ButtonWidth;
            Rect dismissRect = new Rect(buttonX, buttonY, ButtonWidth - 4f, 24f);
            string dismissLabel = recommendation.Dismissed
                ? "PGM_Rec_Restore".Translate()
                : "PGM_Rec_Dismiss".Translate();
            if (PowerUiDraw.Button(dismissRect, dismissLabel) && tracker != null)
            {
                if (recommendation.Dismissed) tracker.ClearDismissals();
                else tracker.Dismiss(recommendation, recommendation.LastSeenTick);
            }
            PowerUiDraw.Tip(dismissRect, recommendation.Dismissed
                ? "PGM_Rec_RestoreTip".Translate()
                : "PGM_Rec_DismissTip".Translate());

            Building target = recommendation.Building;
            if (target != null && !target.Destroyed && target.Spawned)
            {
                buttonX -= ButtonWidth;
                if (buttonX >= rect.x + 8f)
                {
                    Rect locateRect = new Rect(buttonX, buttonY, ButtonWidth - 4f, 24f);
                    if (PowerUiDraw.Button(locateRect, "PGM_Rec_Locate".Translate()))
                    {
                        CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(target));
                    }
                    PowerUiDraw.Tip(locateRect, "PGM_Rec_LocateTip".Translate());
                }

                buttonX -= ButtonWidth;
                if (buttonX >= rect.x + 8f)
                {
                    Rect inspectRect = new Rect(buttonX, buttonY, ButtonWidth - 4f, 24f);
                    if (PowerUiDraw.Button(inspectRect, "PGM_Rec_Inspect".Translate()))
                    {
                        Find.WindowStack.Add(new Dialog_InfoCard(target));
                    }
                    PowerUiDraw.Tip(inspectRect, "PGM_Rec_InspectTip".Translate());
                }
            }

            if (recommendation.NetworkId.HasValue && showBuildingsOnNetwork != null)
            {
                buttonX -= ButtonWidth + 14f;
                if (buttonX >= rect.x + 8f)
                {
                    Rect showRect = new Rect(buttonX, buttonY, ButtonWidth + 10f, 24f);
                    if (PowerUiDraw.Button(showRect, "PGM_Rec_ShowBuildings".Translate(), false, true))
                    {
                        showBuildingsOnNetwork(recommendation.NetworkId.Value);
                    }
                    PowerUiDraw.Tip(showRect, "PGM_Rec_ShowBuildingsTip".Translate());
                }
            }

            PowerUiDraw.Tip(rect, "PGM_Rec_RowTip".Translate());
        }

        private static string FormatTitle(PowerRecommendation recommendation)
        {
            if (string.IsNullOrEmpty(recommendation.TitleKey)) return string.Empty;
            if (recommendation.TitleKey.StartsWith("PGM_", StringComparison.Ordinal))
                return recommendation.TitleKey.Translate();
            return recommendation.TitleKey;
        }

        private static string FormatKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (key.StartsWith("PGM_", StringComparison.Ordinal))
                return key.Translate();
            return key;
        }

        private static string Evidence(PowerRecommendation recommendation)
        {
            if (string.IsNullOrEmpty(recommendation.EvidenceKey)) return string.Empty;
            if (recommendation.EvidenceArgs == null || recommendation.EvidenceArgs.Length == 0)
                return FormatKey(recommendation.EvidenceKey);
            return recommendation.EvidenceKey.Translate(recommendation.EvidenceArgs);
        }

        private static string FormatActions(PowerRecommendation recommendation)
        {
            if (recommendation.ActionKeys == null || recommendation.ActionKeys.Length == 0)
                return string.Empty;
            string first = FormatKey(recommendation.ActionKeys[0]);
            if (recommendation.ActionKeys.Length == 1) return first;
            return first + ", " + FormatKey(recommendation.ActionKeys[1]);
        }

        private static string Benefit(PowerRecommendation recommendation)
        {
            if (!recommendation.Ongoing) return "PGM_Rec_Resolved".Translate();
            if (recommendation.EstimatedBenefitWatts > 0.01f)
            {
                return recommendation.BenefitIsEstimate
                    ? (string)"PGM_Rec_BenefitEstimate".Translate(
                        PowerFormatting.FormatWatts(recommendation.EstimatedBenefitWatts))
                    : (string)"PGM_Rec_Benefit".Translate(
                        PowerFormatting.FormatWatts(recommendation.EstimatedBenefitWatts));
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
                new FloatMenuOption("PGM_Rec_Filter_All".Translate(),
                    () => state.RecommendationView = RecommendationFilter.All),
                new FloatMenuOption("PGM_Rec_Filter_Ongoing".Translate(),
                    () => state.RecommendationView = RecommendationFilter.OngoingOnly),
                new FloatMenuOption("PGM_Rec_Filter_Dismissed".Translate(),
                    () => state.RecommendationView = RecommendationFilter.DismissedOnly)
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
