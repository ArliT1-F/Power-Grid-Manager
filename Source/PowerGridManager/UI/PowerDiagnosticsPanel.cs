using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// The diagnostics tab: one card per advisory, each with severity, what was observed, the
    /// evidence behind it, what to do about it, and whether the condition is ongoing or resolved.
    /// <para>
    /// The list is produced by <see cref="PowerDiagnostics"/> on refresh; this panel only filters
    /// and formats it. Rows are a fixed height so the scroll view stays stable while scrolling.
    /// </para>
    /// </summary>
    public sealed class PowerDiagnosticsPanel
    {
        private const float HeaderHeight = 28f;
        private const float RowHeight = 116f;
        private const float RowGap = 4f;
        private const float PillWidth = 118f;
        private const float ButtonWidth = 84f;

        private readonly List<PowerDiagnostic> visible = new List<PowerDiagnostic>(32);
        private int criticalCount;
        private int warningCount;
        private int resolvedCount;

        public int VisibleCount => visible.Count;

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, PowerDiagnosticTracker tracker,
            IReadOnlyList<PowerDiagnostic> advisories, Action<int> showBuildingsOnNetwork)
        {
            float y = rect.y;
            DrawHeader(new Rect(rect.x, y, rect.width, HeaderHeight), state, settings, tracker);
            y += HeaderHeight + 4f;

            Rect body = new Rect(rect.x, y, rect.width, Mathf.Max(60f, rect.yMax - y));
            PowerUiDraw.Panel(body);

            BuildVisible(advisories, state.DiagnosticView);
            if (visible.Count == 0)
            {
                PowerUiDraw.WrappedText(new Rect(body.x + 10f, body.y + 10f, body.width - 20f, 80f),
                    EmptyMessage(advisories, state.DiagnosticView, settings), PowerUiDraw.Muted);
                return;
            }

            Rect outer = new Rect(body.x + 4f, body.y + 4f, body.width - 8f, body.height - 8f);
            float contentHeight = visible.Count * (RowHeight + RowGap);
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(contentHeight, outer.height));
            Widgets.BeginScrollView(outer, ref state.DiagnosticScroll, view);
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
            PowerDiagnosticTracker tracker)
        {
            float x = rect.x;
            PowerUiDraw.DrawText(new Rect(x, rect.y + 4f, 190f, 24f), "PGM_Diag_Title".Translate(), Color.white);
            x += 196f;

            string counts = "PGM_Diag_Counts".Translate(criticalCount, warningCount, resolvedCount);
            PowerUiDraw.DrawText(new Rect(x, rect.y + 4f, 210f, 24f), counts, PowerUiDraw.Muted);
            x += 216f;

            Rect diagnoseRect = new Rect(x, rect.y, 128f, 26f);
            if (Widgets.ButtonText(diagnoseRect, "PGM_Diag_Diagnose".Translate()))
            {
                Map map = Find.CurrentMap;
                PowerGridManagerComponent component = map == null ? null
                    : map.GetComponent<PowerGridManagerComponent>();
                if (component != null)
                    Find.WindowStack.Add(new PowerTroubleshootingDialog(component, state));
            }
            PowerUiDraw.Tip(diagnoseRect, "PGM_Diag_DiagnoseTip".Translate());
            x += diagnoseRect.width + 4f;

            Rect filterRect = new Rect(x, rect.y, 118f, 26f);
            if (Widgets.ButtonText(filterRect, "PGM_Diag_FilterButton".Translate(FilterLabel(state.DiagnosticView))))
            {
                OpenFilterMenu(state);
            }
            x += filterRect.width + 4f;

            if (tracker != null && tracker.DismissedCount > 0)
            {
                Rect restoreRect = new Rect(x, rect.y, 132f, 26f);
                if (Widgets.ButtonText(restoreRect, "PGM_Diag_Restore".Translate(tracker.DismissedCount)))
                {
                    tracker.ClearDismissals();
                }
                PowerUiDraw.Tip(restoreRect, "PGM_Diag_RestoreTip".Translate());
            }

            if (settings != null && settings.MutedKindCount > 0)
            {
                PowerUiDraw.DrawText(new Rect(rect.x, rect.yMax - 2f, rect.width, 16f),
                    "PGM_Diag_MutedCount".Translate(settings.MutedKindCount), PowerUiDraw.Warn);
            }
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

        private void DrawRow(Rect rect, PowerDiagnostic advisory, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, PowerDiagnosticTracker tracker, Action<int> showBuildingsOnNetwork)
        {
            Widgets.DrawBox(rect, 1);
            if (!advisory.Ongoing) Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.03f));

            Color severityColor = PowerUiDraw.SeverityColor(advisory.Severity);
            Rect pill = new Rect(rect.x + 6f, rect.y + 6f, PillWidth, 18f);
            PowerUiDraw.StatusPill(pill, PowerGridUtilities.SeverityLabelKey(advisory.Severity).Translate(),
                severityColor);

            float titleX = pill.xMax + 6f;
            float statusWidth = 132f;
            PowerUiDraw.TextEllipsized(
                new Rect(titleX, rect.y + 4f, Mathf.Max(40f, rect.xMax - titleX - statusWidth - 12f), 22f),
                Title(advisory), Color.white);
            PowerUiDraw.DrawText(new Rect(rect.xMax - statusWidth - 6f, rect.y + 4f, statusWidth, 22f),
                Status(advisory), advisory.Ongoing ? severityColor : PowerUiDraw.Muted, TextAnchor.MiddleRight);

            float textX = rect.x + 8f;
            float textWidth = Mathf.Max(60f, rect.width - 16f);
            PowerUiDraw.WrappedText(new Rect(textX, rect.y + 28f, textWidth, 34f), Description(advisory),
                new Color(0.86f, 0.86f, 0.86f));
            PowerUiDraw.TextEllipsized(new Rect(textX, rect.y + 62f, textWidth, 18f), Evidence(advisory),
                PowerUiDraw.Muted);
            PowerUiDraw.TextEllipsized(new Rect(textX, rect.y + 80f, textWidth, 18f), Action(advisory),
                PowerUiDraw.Info);

            float buttonY = rect.yMax - 28f;
            float buttonX = rect.xMax - 6f;

            buttonX -= ButtonWidth;
            string muteLabel = settings != null && settings.IsMuted(advisory.Kind)
                ? "PGM_Diag_Unmute".Translate()
                : "PGM_Diag_Mute".Translate();
            Rect muteRect = new Rect(buttonX, buttonY, ButtonWidth - 4f, 24f);
            if (Widgets.ButtonText(muteRect, muteLabel) && settings != null)
            {
                settings.SetMuted(advisory.Kind, !settings.IsMuted(advisory.Kind));
                PowerGridManagerMod.Save();
            }
            PowerUiDraw.Tip(muteRect, "PGM_Diag_MuteTip".Translate());

            buttonX -= ButtonWidth;
            Rect dismissRect = new Rect(buttonX, buttonY, ButtonWidth - 4f, 24f);
            if (Widgets.ButtonText(dismissRect, "PGM_Diag_Dismiss".Translate()) && tracker != null)
            {
                tracker.Dismiss(advisory, advisory.LastSeenTick);
            }
            PowerUiDraw.Tip(dismissRect, "PGM_Diag_DismissTip".Translate());

            Building target = LocateTarget(advisory, snapshot);
            if (target != null)
            {
                buttonX -= ButtonWidth;
                Rect locateRect = new Rect(buttonX, buttonY, ButtonWidth - 4f, 24f);
                if (Widgets.ButtonText(locateRect, "PGM_Diag_Locate".Translate()))
                {
                    CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(target));
                }
            }

            if (advisory.NetworkId.HasValue && showBuildingsOnNetwork != null)
            {
                buttonX -= ButtonWidth;
                Rect showRect = new Rect(buttonX, buttonY, ButtonWidth - 4f, 24f);
                if (Widgets.ButtonText(showRect, "PGM_Diag_ShowBuildings".Translate()))
                {
                    showBuildingsOnNetwork(advisory.NetworkId.Value);
                }
            }

            PowerUiDraw.Tip(rect, "PGM_Diag_RowTip".Translate());
        }

        /// <summary>The building a "locate" action should jump to, when one can be identified.</summary>
        private static Building LocateTarget(PowerDiagnostic advisory, PowerGridSnapshot snapshot)
        {
            if (advisory.Building != null && !advisory.Building.Destroyed && advisory.Building.Spawned)
            {
                return advisory.Building;
            }
            if (!advisory.NetworkId.HasValue || snapshot == null || snapshot.Buildings == null) return null;

            // For a network advisory, jump to the largest consumer that is confirmed to be on it.
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

        // --- Row text ---------------------------------------------------------

        private static string Title(PowerDiagnostic advisory)
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

        private static string Description(PowerDiagnostic advisory)
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

        private static string Evidence(PowerDiagnostic advisory)
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
                        : "PGM_Value_Unknown".Translate());
                case PowerDiagnosticKind.DrawAboveConfigured:
                    return "PGM_Diag_Ev_AboveConfig".Translate(PowerFormatting.FormatWatts(
                        advisory.MeasuredWatts - advisory.ConfiguredWatts));
                case PowerDiagnosticKind.SwitchedOffButDrawing:
                    return "PGM_Diag_Ev_OffDrawing".Translate(PowerFormatting.FormatWatts(advisory.ConfiguredWatts));
                default:
                    return "PGM_Diag_Ev_Unsupported".Translate();
            }
        }

        private static string Action(PowerDiagnostic advisory)
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

        private static string Status(PowerDiagnostic advisory)
        {
            string label = PowerGridUtilities.DiagnosticStatusKey(advisory.Ongoing).Translate();
            if (advisory.Ongoing)
            {
                return "PGM_Diag_Duration".Translate(label, PowerFormatting.FormatDuration(advisory.SustainedTicks));
            }
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
