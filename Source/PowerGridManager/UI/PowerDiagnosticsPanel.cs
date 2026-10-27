using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Modernized diagnostics panel: renders observed power advisories as structured software
    /// notification cards with severity stripe + symbol + text badge, concise explanation,
    /// recessed measurement evidence strip, cyan recommended action, and quick actions.
    /// </summary>
    public sealed class PowerDiagnosticsPanel
    {
        private const float HeaderHeight = 38f;
        private const float RowHeight = 128f;
        private const float RowGap = 8f;
        private const float PillWidth = 122f;
        private const float ButtonWidth = 88f;

        private readonly List<PowerDiagnostic> visible = new List<PowerDiagnostic>(32);
        private int criticalCount;
        private int warningCount;
        private int resolvedCount;

        public int VisibleCount => visible.Count;

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, PowerDiagnosticTracker tracker,
            IReadOnlyList<PowerDiagnostic> advisories, Action<int> showBuildingsOnNetwork)
        {
            if (state == null || rect.width <= 8f || rect.height <= 8f) return;

            BuildVisible(advisories, state.DiagnosticView);

            float headerH = rect.height < 140f ? 28f : HeaderHeight;
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, headerH);
            Rect deferredDiagnoseRect;
            DrawHeader(headerRect, state, settings, tracker, out deferredDiagnoseRect);

            float y = headerRect.yMax + PowerUiDraw.SpaceRelated;
            Rect body = new Rect(rect.x, y, rect.width, Mathf.Max(36f, rect.yMax - y));
            PowerUiDraw.Panel(body);

            if (visible.Count == 0)
            {
                Rect emptyInner = body.ContractedBy(PowerUiDraw.PadPanel);
                bool allHealthy = (advisories == null || advisories.Count == 0) &&
                                  (settings == null || settings.DiagnosticsEnabled);
                if (allHealthy && emptyInner.height >= 56f)
                {
                    PowerUiDraw.PositiveStatusCard(
                        new Rect(emptyInner.x, emptyInner.y, emptyInner.width, Mathf.Min(92f, emptyInner.height)),
                        "PGM_Overview_NominalBadge".Translate(),
                        "PGM_Overview_NominalTitle".Translate(),
                        EmptyMessage(advisories, state.DiagnosticView, settings));
                }
                else
                {
                    PowerUiDraw.WrappedText(emptyInner,
                        EmptyMessage(advisories, state.DiagnosticView, settings),
                        PowerUiDraw.TextSecondary);
                }

                DrawDeferredDiagnoseButton(deferredDiagnoseRect, state);
                return;
            }

            Rect outer = body.ContractedBy(8f);
            float contentHeight = visible.Count * (RowHeight + RowGap);
            Rect view = new Rect(0f, 0f, Mathf.Max(40f, outer.width - 18f), Mathf.Max(contentHeight, outer.height));
            Widgets.BeginScrollView(outer, ref state.DiagnosticScroll, view);
            try
            {
                float rowY = 0f;
                for (int i = 0; i < visible.Count; i++)
                {
                    Rect row = new Rect(0f, rowY, view.width, RowHeight);
                    DrawRow(row, visible[i], snapshot, settings, tracker, showBuildingsOnNetwork);
                    rowY += RowHeight + RowGap;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }

            DrawDeferredDiagnoseButton(deferredDiagnoseRect, state);
        }

        private void DrawHeader(Rect rect, PowerUiState state, PowerGridManagerSettings settings,
            PowerDiagnosticTracker tracker, out Rect diagnoseRect)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = new Rect(rect.x + 10f, rect.y + (rect.height - 26f) * 0.5f,
                Mathf.Max(20f, rect.width - 20f), Mathf.Min(26f, rect.height - 4f));

            // Reserve right-side button slots so Filter stays ButtonText index 0 (and Restore index 1).
            bool hasRestore = tracker != null && tracker.DismissedCount > 0;
            bool showDiagnose = inner.width >= 480f;
            float diagW = showDiagnose ? 156f : 0f;
            float restoreW = hasRestore && inner.width >= 360f ? 136f : 0f;
            float filterW = Mathf.Min(154f, Mathf.Max(78f, inner.width * 0.28f));

            float rightStart = inner.xMax - diagW - (diagW > 0f ? 6f : 0f)
                - restoreW - (restoreW > 0f ? 6f : 0f) - filterW;
            rightStart = Mathf.Max(inner.x, rightStart);

            Rect filterRect = new Rect(rightStart, inner.y, filterW, inner.height);
            if (PowerUiDraw.Button(filterRect,
                "PGM_Diag_FilterButton".Translate(FilterLabel(state.DiagnosticView)),
                state.DiagnosticView != DiagnosticFilter.All))
            {
                OpenFilterMenu(state);
            }

            float nextX = filterRect.xMax + 6f;
            if (restoreW > 0f)
            {
                Rect restoreRect = new Rect(nextX, inner.y, restoreW, inner.height);
                if (PowerUiDraw.Button(restoreRect, "PGM_Diag_Restore".Translate(tracker.DismissedCount)))
                {
                    tracker.ClearDismissals();
                }
                PowerUiDraw.Tip(restoreRect, "PGM_Diag_RestoreTip".Translate());
                nextX = restoreRect.xMax + 6f;
            }

            diagnoseRect = showDiagnose
                ? new Rect(nextX, inner.y, diagW, inner.height)
                : Rect.zero;

            float leftW = Mathf.Max(0f, filterRect.x - inner.x - 10f);
            if (leftW >= 80f)
            {
                string title = "PGM_Diag_Title".Translate();
                float titleW = Mathf.Min(leftW * 0.45f, Text.CalcSize(title).x + 16f);
                Widgets.DrawBoxSolid(new Rect(inner.x, inner.y + 5f, 3f, Mathf.Max(6f, inner.height - 10f)),
                    PowerUiDraw.AccentPrimary);
                PowerUiDraw.TextEllipsized(new Rect(inner.x + 8f, inner.y, titleW, inner.height),
                    title, PowerUiDraw.TextPrimary);

                float countsX = inner.x + 8f + titleW + 6f;
                float countsW = Mathf.Max(0f, filterRect.x - countsX - 8f);
                if (countsW >= 60f)
                {
                    string counts = "PGM_Diag_Counts".Translate(criticalCount, warningCount, resolvedCount);
                    if (settings != null && settings.MutedKindCount > 0)
                        counts = counts + "  \u00B7  " + "PGM_Diag_MutedCount".Translate(settings.MutedKindCount);
                    Color cntCol = criticalCount > 0
                        ? PowerUiDraw.NoticeColor
                        : (warningCount > 0 ? PowerUiDraw.Warn : PowerUiDraw.TextSecondary);
                    PowerUiDraw.TextEllipsized(new Rect(countsX, inner.y, countsW, inner.height),
                        counts, cntCol);
                }
            }
        }

        private static void DrawDeferredDiagnoseButton(Rect diagnoseRect, PowerUiState state)
        {
            if (diagnoseRect.width <= 0f || diagnoseRect.height <= 0f) return;
            if (PowerUiDraw.Button(diagnoseRect, "PGM_Diag_Diagnose".Translate(), false, true))
            {
                Map map = Find.CurrentMap;
                PowerGridManagerComponent component = map == null ? null
                    : map.GetComponent<PowerGridManagerComponent>();
                if (component != null)
                    Find.WindowStack.Add(new PowerTroubleshootingDialog(component, state));
            }
            PowerUiDraw.Tip(diagnoseRect, "PGM_Diag_DiagnoseTip".Translate());
        }

        private void BuildVisible(IReadOnlyList<PowerDiagnostic> advisories, DiagnosticFilter filter)
        {
            visible.Clear();
            criticalCount = 0;
            warningCount = 0;
            resolvedCount = 0;
            if (advisories == null) return;

            for (int i = 0; i < advisories.Count; i++)
            {
                PowerDiagnostic advisory = advisories[i];
                if (!advisory.Ongoing) resolvedCount++;
                else if (advisory.Severity == PowerDiagnosticSeverity.Critical) criticalCount++;
                else if (advisory.Severity == PowerDiagnosticSeverity.Warning) warningCount++;

                switch (filter)
                {
                    case DiagnosticFilter.CriticalOnly:
                        if (advisory.Severity != PowerDiagnosticSeverity.Critical) continue;
                        break;
                    case DiagnosticFilter.WarningAndAbove:
                        if (advisory.Severity == PowerDiagnosticSeverity.Info) continue;
                        break;
                    case DiagnosticFilter.ResolvedOnly:
                        if (advisory.Ongoing) continue;
                        break;
                }
                visible.Add(advisory);
            }
        }

        private static void DrawRow(Rect rect, PowerDiagnostic advisory, PowerGridSnapshot snapshot,
            PowerGridManagerSettings settings, PowerDiagnosticTracker tracker,
            Action<int> showBuildingsOnNetwork)
        {
            Color severityColor = PowerUiDraw.SeverityColor(advisory.Severity);
            PowerUiDraw.NotificationCardShell(rect, severityColor, advisory.Ongoing);

            float padX = rect.x + 12f;
            float innerW = Mathf.Max(40f, rect.width - 20f);

            // Row 1: Severity badge (symbol + text) + Title + Duration/Status on right.
            Rect pill = new Rect(padX, rect.y + 6f, Mathf.Min(PillWidth, innerW * 0.35f), 20f);
            PowerUiDraw.StatusBadge(pill,
                PowerGridUtilities.SeverityLabelKey(advisory.Severity).Translate(),
                severityColor,
                PowerUiDraw.SeveritySymbol(advisory.Severity));

            float statusWidth = Mathf.Min(150f, innerW * 0.32f);
            float titleX = pill.xMax + 8f;
            PowerUiDraw.TextEllipsized(
                new Rect(titleX, rect.y + 5f, Mathf.Max(30f, rect.xMax - titleX - statusWidth - 14f), 22f),
                FormatTitle(advisory), PowerUiDraw.TextPrimary);
            PowerUiDraw.TextEllipsized(
                new Rect(rect.xMax - statusWidth - 8f, rect.y + 5f, statusWidth, 22f),
                FormatStatus(advisory),
                advisory.Ongoing ? severityColor : PowerUiDraw.TextSecondary,
                TextAnchor.MiddleRight);

            // Row 2: Concise explanation.
            PowerUiDraw.WrappedText(new Rect(padX, rect.y + 30f, innerW, 34f),
                FormatDescription(advisory), PowerUiDraw.TextSecondary);

            // Row 3: Recessed measurement evidence strip.
            Rect evBox = new Rect(padX, rect.y + 66f, innerW, 22f);
            PowerUiDraw.RecessedBox(evBox);
            PowerUiDraw.TextEllipsized(new Rect(evBox.x + 6f, evBox.y, Mathf.Max(0f, evBox.width - 12f), evBox.height),
                FormatEvidence(advisory), PowerUiDraw.TextPrimary);

            // Row 4: Recommended action on left + action buttons on right.
            float buttonY = rect.yMax - 30f;
            float buttonX = rect.xMax - 8f;

            // Button order strictly preserved: Mute -> Dismiss -> Locate -> ShowBuildings.
            buttonX -= ButtonWidth;
            bool isMuted = settings != null && settings.IsMuted(advisory.Kind);
            string muteLabel = isMuted ? "PGM_Diag_Unmute".Translate() : "PGM_Diag_Mute".Translate();
            Rect muteRect = new Rect(buttonX, buttonY, ButtonWidth - 4f, 24f);
            if (PowerUiDraw.Button(muteRect, muteLabel, isMuted) && settings != null)
            {
                settings.SetMuted(advisory.Kind, !isMuted);
                PowerGridManagerMod.Save();
            }
            PowerUiDraw.Tip(muteRect, "PGM_Diag_MuteTip".Translate());

            buttonX -= ButtonWidth;
            Rect dismissRect = new Rect(buttonX, buttonY, ButtonWidth - 4f, 24f);
            if (PowerUiDraw.Button(dismissRect, "PGM_Diag_Dismiss".Translate()) && tracker != null)
            {
                tracker.Dismiss(advisory, advisory.LastSeenTick);
            }
            PowerUiDraw.Tip(dismissRect, "PGM_Diag_DismissTip".Translate());

            Building target = LocateTarget(advisory, snapshot);
            if (target != null)
            {
                buttonX -= ButtonWidth;
                Rect locateRect = new Rect(buttonX, buttonY, ButtonWidth - 4f, 24f);
                if (PowerUiDraw.Button(locateRect, "PGM_Diag_Locate".Translate()))
                {
                    CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(target));
                }
            }

            if (advisory.NetworkId.HasValue && showBuildingsOnNetwork != null)
            {
                buttonX -= ButtonWidth;
                Rect showRect = new Rect(buttonX, buttonY, ButtonWidth - 4f, 24f);
                if (PowerUiDraw.Button(showRect, "PGM_Diag_ShowBuildings".Translate(), false, true))
                {
                    showBuildingsOnNetwork(advisory.NetworkId.Value);
                }
            }

            float actionW = Mathf.Max(0f, buttonX - padX - 8f);
            if (actionW >= 60f)
            {
                PowerUiDraw.TextEllipsized(new Rect(padX, buttonY, actionW, 24f),
                    FormatAction(advisory), PowerUiDraw.AccentCyan);
            }

            PowerUiDraw.Tip(rect, "PGM_Diag_RowTip".Translate());
        }

        private static Building LocateTarget(PowerDiagnostic advisory, PowerGridSnapshot snapshot)
        {
            if (advisory.Building != null && !advisory.Building.Destroyed && advisory.Building.Spawned)
                return advisory.Building;
            if (!advisory.NetworkId.HasValue || snapshot == null || snapshot.Buildings == null)
                return null;

            Building best = null;
            float bestDraw = 0f;
            IReadOnlyList<BuildingPowerSnapshot> buildings = snapshot.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingPowerSnapshot building = buildings[i];
                if (building.NetworkId != advisory.NetworkId.Value) continue;
                if (building.Building == null || building.Building.Destroyed || !building.Building.Spawned) continue;
                if (building.ConsumptionWatts <= bestDraw) continue;
                bestDraw = building.ConsumptionWatts;
                best = building.Building;
            }
            return bestDraw > 0.01f ? best : null;
        }

        internal static string FormatTitle(PowerDiagnostic advisory)
        {
            switch (advisory.Kind)
            {
                case PowerDiagnosticKind.PersistentDeficit:
                    return "PGM_Diag_Title_Deficit".Translate(advisory.NetworkId ?? 0);
                case PowerDiagnosticKind.HighLoad:
                    return "PGM_Diag_Title_HighLoad".Translate(advisory.NetworkId ?? 0);
                case PowerDiagnosticKind.BatteryDischarge:
                    return "PGM_Diag_Title_Discharge".Translate(advisory.NetworkId ?? 0);
                case PowerDiagnosticKind.NoGeneration:
                    return "PGM_Diag_Title_NoGeneration".Translate(advisory.NetworkId ?? 0);
                case PowerDiagnosticKind.IsolatedConsumer:
                    return "PGM_Diag_Title_Isolated".Translate(PowerGridUtilities.BuildingLabel(advisory.Building));
                case PowerDiagnosticKind.PendingMembership:
                    return "PGM_Diag_Title_Pending".Translate(PowerGridUtilities.BuildingLabel(advisory.Building));
                case PowerDiagnosticKind.GeneratorUnderOutput:
                    return "PGM_Diag_Title_Generator".Translate(PowerGridUtilities.BuildingLabel(advisory.Building));
                case PowerDiagnosticKind.DrawAboveConfigured:
                    return "PGM_Diag_Title_AboveConfig".Translate(PowerGridUtilities.BuildingLabel(advisory.Building));
                case PowerDiagnosticKind.SwitchedOffButDrawing:
                    return "PGM_Diag_Title_OffDrawing".Translate(PowerGridUtilities.BuildingLabel(advisory.Building));
                default:
                    return "PGM_Diag_Title_Unsupported".Translate(PowerGridUtilities.BuildingLabel(advisory.Building));
            }
        }

        internal static string FormatDescription(PowerDiagnostic advisory)
        {
            switch (advisory.Kind)
            {
                case PowerDiagnosticKind.PersistentDeficit:
                    return "PGM_Diag_Desc_Deficit".Translate(PowerFormatting.FormatWatts(advisory.DeficitWatts),
                        PowerFormatting.FormatDuration(advisory.SustainedTicks));
                case PowerDiagnosticKind.HighLoad:
                    return "PGM_Diag_Desc_HighLoad".Translate(PowerFormatting.FormatCharge(advisory.DeficitWatts));
                case PowerDiagnosticKind.BatteryDischarge:
                    return "PGM_Diag_Desc_Discharge".Translate(PowerFormatting.FormatWatts(advisory.DeficitWatts),
                        PowerFormatting.FormatDuration(advisory.SustainedTicks));
                case PowerDiagnosticKind.NoGeneration:
                    return "PGM_Diag_Desc_NoGeneration".Translate(advisory.AffectedCount);
                case PowerDiagnosticKind.IsolatedConsumer:
                    return "PGM_Diag_Desc_Isolated".Translate();
                case PowerDiagnosticKind.PendingMembership:
                    return "PGM_Diag_Desc_Pending".Translate();
                case PowerDiagnosticKind.GeneratorUnderOutput:
                    return "PGM_Diag_Desc_Generator".Translate(PowerFormatting.FormatWatts(advisory.MeasuredWatts),
                        PowerFormatting.FormatWatts(advisory.ConfiguredWatts),
                        PowerFormatting.FormatDuration(advisory.SustainedTicks));
                case PowerDiagnosticKind.DrawAboveConfigured:
                    return "PGM_Diag_Desc_AboveConfig".Translate(PowerFormatting.FormatWatts(advisory.MeasuredWatts),
                        PowerFormatting.FormatWatts(advisory.ConfiguredWatts));
                case PowerDiagnosticKind.SwitchedOffButDrawing:
                    return "PGM_Diag_Desc_OffDrawing".Translate(PowerFormatting.FormatWatts(advisory.MeasuredWatts));
                default:
                    return "PGM_Diag_Desc_Unsupported".Translate();
            }
        }

        internal static string FormatEvidence(PowerDiagnostic advisory)
        {
            switch (advisory.Kind)
            {
                case PowerDiagnosticKind.PersistentDeficit:
                    return "PGM_Diag_Ev_Deficit".Translate(PowerFormatting.FormatWatts(advisory.GenerationWatts),
                        PowerFormatting.FormatWatts(advisory.ConsumptionWatts), advisory.AffectedCount);
                case PowerDiagnosticKind.HighLoad:
                    return "PGM_Diag_Ev_HighLoad".Translate(PowerFormatting.FormatWatts(advisory.ConsumptionWatts),
                        PowerFormatting.FormatWatts(advisory.GenerationWatts));
                case PowerDiagnosticKind.BatteryDischarge:
                    return "PGM_Diag_Ev_Discharge".Translate(PowerFormatting.FormatCharge(advisory.ChargeFraction),
                        PowerFormatting.FormatEnergy(advisory.StoredWattDays),
                        PowerFormatting.FormatEnergy(advisory.CapacityWattDays),
                        DepletionText(advisory), advisory.AffectedCount);
                case PowerDiagnosticKind.NoGeneration:
                    return "PGM_Diag_Ev_NoGeneration".Translate(PowerFormatting.FormatWatts(advisory.ConsumptionWatts),
                        PowerFormatting.FormatCharge(advisory.ChargeFraction));
                case PowerDiagnosticKind.IsolatedConsumer:
                    return "PGM_Diag_Ev_Isolated".Translate(PowerFormatting.FormatWatts(advisory.ConfiguredWatts));
                case PowerDiagnosticKind.PendingMembership:
                    return "PGM_Diag_Ev_Pending".Translate();
                case PowerDiagnosticKind.GeneratorUnderOutput:
                    return "PGM_Diag_Ev_Generator".Translate(advisory.NetworkId.HasValue
                        ? advisory.NetworkId.Value.ToString()
                        : (string)"PGM_Value_Unknown".Translate());
                case PowerDiagnosticKind.DrawAboveConfigured:
                    return "PGM_Diag_Ev_AboveConfig".Translate(PowerFormatting.FormatWatts(
                        advisory.MeasuredWatts - advisory.ConfiguredWatts));
                case PowerDiagnosticKind.SwitchedOffButDrawing:
                    return "PGM_Diag_Ev_OffDrawing".Translate(PowerFormatting.FormatWatts(advisory.ConfiguredWatts));
                default:
                    return "PGM_Diag_Ev_Unsupported".Translate();
            }
        }

        internal static string FormatAction(PowerDiagnostic advisory)
        {
            switch (advisory.Kind)
            {
                case PowerDiagnosticKind.PersistentDeficit:
                    return "PGM_Diag_Act_Deficit".Translate(PowerFormatting.FormatWatts(advisory.DeficitWatts));
                case PowerDiagnosticKind.HighLoad:
                    return "PGM_Diag_Act_HighLoad".Translate();
                case PowerDiagnosticKind.BatteryDischarge:
                    return "PGM_Diag_Act_Discharge".Translate(PowerFormatting.FormatWatts(advisory.DeficitWatts));
                case PowerDiagnosticKind.NoGeneration:
                    return "PGM_Diag_Act_NoGeneration".Translate();
                case PowerDiagnosticKind.IsolatedConsumer:
                    return "PGM_Diag_Act_Isolated".Translate();
                case PowerDiagnosticKind.PendingMembership:
                    return "PGM_Diag_Act_Pending".Translate();
                case PowerDiagnosticKind.GeneratorUnderOutput:
                    return "PGM_Diag_Act_Generator".Translate();
                case PowerDiagnosticKind.DrawAboveConfigured:
                    return "PGM_Diag_Act_AboveConfig".Translate();
                case PowerDiagnosticKind.SwitchedOffButDrawing:
                    return "PGM_Diag_Act_OffDrawing".Translate();
                default:
                    return "PGM_Diag_Act_Unsupported".Translate();
            }
        }

        internal static string FormatStatus(PowerDiagnostic advisory)
        {
            string label = PowerGridUtilities.DiagnosticStatusKey(advisory.Ongoing).Translate();
            if (advisory.Ongoing)
                return "PGM_Diag_Duration".Translate(label, PowerFormatting.FormatDuration(advisory.SustainedTicks));
            return label;
        }

        private static string DepletionText(PowerDiagnostic advisory)
        {
            if (!advisory.DepletionTicks.HasValue) return "PGM_Value_Unknown".Translate();
            return PowerFormatting.FormatDuration(advisory.DepletionTicks.Value);
        }

        private static string EmptyMessage(IReadOnlyList<PowerDiagnostic> advisories, DiagnosticFilter filter,
            PowerGridManagerSettings settings)
        {
            bool any = advisories != null && advisories.Count > 0;
            if (any) return "PGM_Diag_EmptyFiltered".Translate();
            if (settings != null && !settings.DiagnosticsEnabled) return "PGM_Diag_Disabled".Translate();
            return "PGM_Diag_Empty".Translate();
        }

        private static string FilterLabel(DiagnosticFilter filter)
        {
            switch (filter)
            {
                case DiagnosticFilter.CriticalOnly: return "PGM_Diag_Filter_Critical".Translate();
                case DiagnosticFilter.WarningAndAbove: return "PGM_Diag_Filter_Warning".Translate();
                case DiagnosticFilter.ResolvedOnly: return "PGM_Diag_Filter_Resolved".Translate();
                default: return "PGM_Diag_Filter_All".Translate();
            }
        }

        private static void OpenFilterMenu(PowerUiState state)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("PGM_Diag_Filter_All".Translate(),
                    () => state.DiagnosticView = DiagnosticFilter.All),
                new FloatMenuOption("PGM_Diag_Filter_Critical".Translate(),
                    () => state.DiagnosticView = DiagnosticFilter.CriticalOnly),
                new FloatMenuOption("PGM_Diag_Filter_Warning".Translate(),
                    () => state.DiagnosticView = DiagnosticFilter.WarningAndAbove),
                new FloatMenuOption("PGM_Diag_Filter_Resolved".Translate(),
                    () => state.DiagnosticView = DiagnosticFilter.ResolvedOnly)
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
