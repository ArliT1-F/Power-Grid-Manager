using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Modernized Automation &amp; Power Management tab.
    ///
    /// Preserves opt-in safety invariants (off by default, only explicitly sheddable buildings
    /// are ever switched automatically, emergency override suspends automation immediately)
    /// while presenting controls, KPI cards, hysteresis policy band, groups, presets, managed
    /// buildings, and audit log in the dark electrical-grid monitoring design system.
    /// </summary>
    public sealed class PowerManagementPanel
    {
        private const float Gap = 8f;
        private const float RowHeight = 24f;
        private const float GroupRowHeight = 26f;
        private const float EventRowHeight = 22f;
        private const float HeaderHeight = 22f;
        private const int PreviewRowCap = 8;
        private const int GroupRowCap = 6;
        private const int BuildingRowCap = 14;
        private const int EventRowCap = 6;

        private readonly List<BuildingPowerSnapshot> rows = new List<BuildingPowerSnapshot>(BuildingRowCap);
        private PowerGridSnapshot cachedRowsSnapshot;
        private string cachedRowsFilter;
        private int cachedRowsRevision = -1;
        private BuildingPowerSnapshot selectedRow;
        private LoadSheddingPlan drawnPlan;
        private float measuredContentHeight = 960f;

        /// <summary>Plan used by the last draw pass; exposed for headless UI checks.</summary>
        public LoadSheddingPlan LastPlan => drawnPlan;

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, PowerManagementController management, int tick)
        {
            if (state == null || rect.width <= 8f || rect.height <= 8f) return;

            drawnPlan = management == null ? null : management.Preview(snapshot, settings, tick);
            LoadSheddingPlan plan = drawnPlan;
            PowerManagementRegistry registry = management == null ? null : management.Registry;
            PowerManagementEventLog log = management == null ? null : management.Events;

            bool compact = rect.width < 700f;
            float width = Mathf.Max(180f, rect.width - 18f);
            BuildRows(snapshot, state, registry, management == null ? -1 : management.Revision);

            int previewRows = plan == null ? 0 : Math.Min(plan.ToShed.Count, PreviewRowCap);
            int groupRows = registry == null ? 0 : Math.Min(registry.GroupCount, GroupRowCap);
            int pageCount = Math.Max(1, (rows.Count + BuildingRowCap - 1) / BuildingRowCap);
            if (state.ManagementPage >= pageCount) state.ManagementPage = 0;
            int pageStart = state.ManagementPage * BuildingRowCap;
            int pageRows = Math.Min(BuildingRowCap, Math.Max(0, rows.Count - pageStart));
            int eventRows = log == null ? 0 : Math.Min(log.Count, EventRowCap);

            float headerH = compact ? 104f : 86f;
            float actionH = compact ? 92f : 84f;

            Rect view = new Rect(0f, 0f, width, Mathf.Max(rect.height, measuredContentHeight));
            Widgets.BeginScrollView(rect, ref state.ManagementScroll, view);
            try
            {
                float y = 0f;
                DrawHeader(new Rect(0f, y, width, headerH), compact, settings, management, snapshot, tick);
                y += headerH + Gap;

                DrawTiles(new Rect(0f, y, width, 68f), snapshot, settings, plan, management);
                y += 68f + Gap;

                DrawPolicy(new Rect(0f, y, width, 128f), settings, plan);
                y += 128f + Gap;

                y = DrawPreview(new Rect(0f, y, width, 0f), plan, registry, previewRows);
                y = DrawGroups(new Rect(0f, y, width, 0f), state, registry, management, groupRows, tick);
                y = DrawPresets(new Rect(0f, y, width, 0f), settings, tick);
                y = DrawBuildings(new Rect(0f, y, width, 0f), snapshot, state, settings, management,
                    registry, pageStart, pageRows, pageCount, tick, compact, actionH);
                y = DrawEvents(new Rect(0f, y, width, 0f), log, eventRows, tick);

                measuredContentHeight = y + 16f;
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private static void DrawHeader(Rect rect, bool compact,
            PowerGridManagerSettings settings, PowerManagementController management,
            PowerGridSnapshot snapshot, int tick)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(10f);

            bool enabled = settings != null && settings.ManagementEnabled;
            bool automation = enabled && settings.LoadSheddingEnabled;
            LoadSheddingPhase phase = management == null || management.LastPlan == null
                ? (enabled ? LoadSheddingPhase.Idle : LoadSheddingPhase.Disabled)
                : management.LastPlan.Phase;
            if (!automation) phase = LoadSheddingPhase.Disabled;

            PowerManagementController.AutomationStatus status = management == null
                ? PowerManagementController.AutomationStatus.Unavailable
                : management.Status(settings);

            float badgeW = Mathf.Min(150f, inner.width * 0.34f);
            Rect statusPill = new Rect(inner.xMax - badgeW, inner.y, badgeW, 20f);
            PowerUiDraw.StatusBadge(statusPill, AutomationStatusLabel(status),
                AutomationStatusColor(status), AutomationSymbol(status));
            PowerUiDraw.Tip(statusPill, "PGM_Mgmt_AutomationStatusTip".Translate());

            Rect phasePill = new Rect(statusPill.x - badgeW - 6f, inner.y, badgeW, 20f);
            if (inner.width >= 440f)
            {
                PowerUiDraw.StatusBadge(phasePill, PhaseLabel(phase), PhaseColor(phase));
                PowerUiDraw.Tip(phasePill, "PGM_Mgmt_StatusTip".Translate());
            }

            float titleMax = inner.width >= 440f ? phasePill.x - inner.x - 8f : statusPill.x - inner.x - 8f;
            Widgets.DrawBoxSolid(new Rect(inner.x, inner.y + 4f, 3f, 13f), PowerUiDraw.AccentCyan);
            PowerUiDraw.TextEllipsized(new Rect(inner.x + 8f, inner.y, Mathf.Max(40f, titleMax - 8f), 20f),
                "PGM_Mgmt_Title".Translate(), PowerUiDraw.TextPrimary);

            PowerUiDraw.TextEllipsized(new Rect(inner.x, inner.y + 22f, inner.width, 18f),
                "PGM_Mgmt_Note".Translate(), PowerUiDraw.TextSecondary);
            PowerUiDraw.Tip(new Rect(inner.x, inner.y + 22f, inner.width, 18f), "PGM_Mgmt_Note".Translate());

            float buttonY = inner.y + 42f;
            bool showEmergency = automation;
            bool showRestore = management != null && management.AutoShedCount > 0;
            int count = 2 + (showEmergency ? 1 : 0) + (!compact && showRestore ? 1 : 0);
            float buttonWidth = Mathf.Max(76f, (inner.width - (count - 1) * 6f) / count);
            float bx = inner.x;

            // Button 0: Master toggle.
            Rect master = new Rect(bx, buttonY, buttonWidth, 26f);
            if (PowerUiDraw.Button(master,
                enabled ? "PGM_Mgmt_Disable".Translate() : "PGM_Mgmt_Enable".Translate(),
                enabled) && settings != null && management != null)
            {
                management.SetPolicy(settings, !enabled, automation, snapshot, tick);
            }
            PowerUiDraw.Tip(master, "PGM_Mgmt_MasterTip".Translate());
            bx += buttonWidth + 6f;

            // Button 1: Automation toggle.
            Rect auto = new Rect(bx, buttonY, buttonWidth, 26f);
            if (PowerUiDraw.Button(auto,
                automation ? "PGM_Mgmt_AutoOff".Translate() : "PGM_Mgmt_AutoOn".Translate(),
                automation) && settings != null && management != null)
            {
                management.SetPolicy(settings, true, !automation, snapshot, tick);
            }
            PowerUiDraw.Tip(auto, "PGM_Mgmt_AutomationTip".Translate());
            bx += buttonWidth + 6f;

            // Emergency override (drawn only when automation is active).
            if (showEmergency)
            {
                Rect emergency = new Rect(bx, buttonY, buttonWidth, 26f);
                bool emergencyActive = management != null && management.EmergencyOverrideActive;
                if (PowerUiDraw.Button(emergency,
                    emergencyActive ? "PGM_Mgmt_EmergencyOff".Translate() : "PGM_Mgmt_EmergencyOn".Translate(),
                    emergencyActive, true, true, PowerUiDraw.NoticeColor) &&
                    settings != null && management != null)
                {
                    if (emergencyActive) management.ClearEmergencyOverride(tick);
                    else management.ActivateEmergencyOverride(tick);
                }
                PowerUiDraw.Tip(emergency, emergencyActive
                    ? "PGM_Mgmt_EmergencyOffTip".Translate()
                    : "PGM_Mgmt_EmergencyOnTip".Translate());
                bx += buttonWidth + 6f;
            }

            if (showRestore)
            {
                float restoreX = compact ? inner.x : bx;
                float restoreY = compact ? buttonY + 28f : buttonY;
                float restoreW = compact
                    ? Mathf.Min(buttonWidth * 2f + 6f, inner.width)
                    : Mathf.Max(60f, Mathf.Min(buttonWidth, inner.xMax - bx));
                Rect restore = new Rect(restoreX, restoreY, restoreW, 26f);
                if (PowerUiDraw.Button(restore,
                    "PGM_Mgmt_RestoreNow".Translate(management.AutoShedCount), false, true, true, PowerUiDraw.Good) &&
                    management != null)
                {
                    management.RestoreNow(snapshot, tick);
                }
                PowerUiDraw.Tip(restore, "PGM_Mgmt_RestoreTip".Translate());
            }
        }

        private static string PhaseLabel(LoadSheddingPhase phase)
        {
            switch (phase)
            {
                case LoadSheddingPhase.Shedding: return "PGM_Mgmt_Status_Shedding".Translate();
                case LoadSheddingPhase.Restoring: return "PGM_Mgmt_Status_Restoring".Translate();
                case LoadSheddingPhase.Watching: return "PGM_Mgmt_Status_Watching".Translate();
                case LoadSheddingPhase.Idle: return "PGM_Mgmt_Status_Armed".Translate();
                default: return "PGM_Mgmt_Status_Off".Translate();
            }
        }

        private static Color PhaseColor(LoadSheddingPhase phase)
        {
            switch (phase)
            {
                case LoadSheddingPhase.Shedding: return PowerUiDraw.NoticeColor;
                case LoadSheddingPhase.Restoring: return PowerUiDraw.Good;
                case LoadSheddingPhase.Watching: return PowerUiDraw.Warn;
                case LoadSheddingPhase.Idle: return PowerUiDraw.AccentCyan;
                default: return PowerUiDraw.TextSecondary;
            }
        }

        private static string AutomationStatusLabel(PowerManagementController.AutomationStatus status)
        {
            switch (status)
            {
                case PowerManagementController.AutomationStatus.Enabled: return "PGM_Mgmt_Status_Enabled".Translate();
                case PowerManagementController.AutomationStatus.Suspended: return "PGM_Mgmt_Status_Suspended".Translate();
                case PowerManagementController.AutomationStatus.Off: return "PGM_Mgmt_Status_Off".Translate();
                default: return "PGM_Mgmt_Status_Unavailable".Translate();
            }
        }

        private static Color AutomationStatusColor(PowerManagementController.AutomationStatus status)
        {
            switch (status)
            {
                case PowerManagementController.AutomationStatus.Enabled: return PowerUiDraw.Good;
                case PowerManagementController.AutomationStatus.Suspended: return PowerUiDraw.NoticeColor;
                case PowerManagementController.AutomationStatus.Off: return PowerUiDraw.Warn;
                default: return PowerUiDraw.TextSecondary;
            }
        }

        private static string AutomationSymbol(PowerManagementController.AutomationStatus status)
        {
            switch (status)
            {
                case PowerManagementController.AutomationStatus.Enabled: return "\u25CF";
                case PowerManagementController.AutomationStatus.Suspended: return "!!";
                case PowerManagementController.AutomationStatus.Off: return "\u2013";
                default: return "?";
            }
        }

        private static void DrawTiles(Rect rect, PowerGridSnapshot snapshot,
            PowerGridManagerSettings settings, LoadSheddingPlan plan, PowerManagementController management)
        {
            float tileWidth = (rect.width - 18f) / 4f;
            float x = rect.x;
            float charge = plan == null ? -1f : plan.ChargeFraction;
            bool lowCharge = charge >= 0f && settings != null && charge <= settings.LoadShedLowThreshold;
            Color chgCol = charge < 0f
                ? PowerUiDraw.TextSecondary
                : (lowCharge ? PowerUiDraw.Warn : PowerUiDraw.Good);

            PowerUiDraw.StatCard(new Rect(x, rect.y, tileWidth, rect.height),
                "PGM_Mgmt_Tile_Charge".Translate(),
                charge < 0f ? (string)"PGM_Value_Unknown".Translate() : PowerFormatting.FormatCharge(charge),
                chgCol, chgCol, null, "PGM_Mgmt_Tile_ChargeTip".Translate(),
                charge >= 0f ? (float?)Mathf.Clamp01(charge) : null, chgCol);
            x += tileWidth + 6f;

            Color netCol = snapshot == null ? PowerUiDraw.TextSecondary : PowerUiDraw.BalanceColor(snapshot, settings);
            PowerUiDraw.StatCard(new Rect(x, rect.y, tileWidth, rect.height),
                "PGM_Mgmt_Tile_Net".Translate(),
                snapshot == null ? "\u2014" : PowerFormatting.FormatWattsSigned(snapshot.NetWatts),
                netCol, netCol, null, "PGM_Mgmt_Tile_NetTip".Translate());
            x += tileWidth + 6f;

            PowerUiDraw.StatCard(new Rect(x, rect.y, tileWidth, rect.height),
                "PGM_Mgmt_Tile_Sheddable".Translate(),
                (plan == null ? 0 : plan.CandidateCount).ToString(),
                PowerUiDraw.AccentCyan, PowerUiDraw.AccentCyan,
                null, "PGM_Mgmt_Tile_SheddableTip".Translate());
            x += tileWidth + 6f;

            int autoCount = management == null ? 0 : management.AutoShedCount;
            Color shedCol = autoCount > 0 ? PowerUiDraw.Warn : PowerUiDraw.TextPrimary;
            PowerUiDraw.StatCard(new Rect(x, rect.y, tileWidth, rect.height),
                "PGM_Mgmt_Tile_AutoShed".Translate(),
                autoCount.ToString(),
                shedCol, autoCount > 0 ? PowerUiDraw.Warn : PowerUiDraw.AccentPrimary,
                null, "PGM_Mgmt_Tile_AutoShedTip".Translate());
        }

        private static void DrawPolicy(Rect rect, PowerGridManagerSettings settings, LoadSheddingPlan plan)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(10f);
            PowerUiDraw.SectionHeader(new Rect(inner.x, inner.y, inner.width, 20f),
                "PGM_Mgmt_PolicyTitle".Translate());
            float y = inner.y + 24f;
            float buttonWidth = Mathf.Max(80f, (inner.width - 12f) / 3f);
            float x = inner.x;

            float lowFrac = settings == null ? 0.2f : settings.LoadShedLowThreshold;
            float restoreFrac = settings == null ? 0.5f : settings.LoadShedRestoreThreshold;

            Rect low = new Rect(x, y, buttonWidth, 25f);
            if (PowerUiDraw.Button(low, "PGM_Mgmt_Low_Label".Translate(PowerFormatting.FormatCharge(lowFrac))))
                OpenThresholdMenu(settings, true);
            PowerUiDraw.Tip(low, "PGM_Mgmt_Low_Tip".Translate());
            x += buttonWidth + 6f;

            Rect restore = new Rect(x, y, buttonWidth, 25f);
            if (PowerUiDraw.Button(restore, "PGM_Mgmt_Restore_Label".Translate(PowerFormatting.FormatCharge(restoreFrac))))
                OpenThresholdMenu(settings, false);
            PowerUiDraw.Tip(restore, "PGM_Mgmt_Restore_Tip".Translate());
            x += buttonWidth + 6f;

            Rect recovery = new Rect(x, y, Mathf.Max(40f, inner.xMax - x), 25f);
            if (PowerUiDraw.Button(recovery, "PGM_Mgmt_Recovery_Label".Translate(
                    RecoveryLabel(settings == null ? LoadSheddingRecoveryMode.Staged : settings.LoadShedRecovery))))
                OpenRecoveryMenu(settings);
            PowerUiDraw.Tip(recovery, "PGM_Mgmt_Recovery_Tip".Translate());

            y += 29f;

            // Visual hysteresis band bar: red below lowFrac, cyan/blue deadband, green above restoreFrac.
            Rect band = new Rect(inner.x, y, inner.width, 8f);
            DrawHysteresisBar(band, lowFrac, restoreFrac, plan != null ? plan.ChargeFraction : -1f);
            y += 12f;

            string reason = plan == null ? "PGM_Mgmt_Reason_Off".Translate() : plan.ReasonKey.Translate();
            PowerUiDraw.TextEllipsized(new Rect(inner.x, y, inner.width, 20f),
                "PGM_Mgmt_Status_Reason".Translate(reason), PowerUiDraw.AccentCyan);
            y += 20f;
            PowerUiDraw.WrappedText(new Rect(inner.x, y, inner.width, Mathf.Max(18f, inner.yMax - y)),
                "PGM_Mgmt_PolicyNote".Translate(
                    settings == null ? 0 : settings.LoadShedMaxActionsPerPass,
                    PowerFormatting.FormatDuration(settings == null ? 0 : settings.LoadShedStableTicks),
                    PowerFormatting.FormatDuration(settings == null ? 0 : settings.LoadShedCooldownTicks)),
                PowerUiDraw.TextSecondary);
        }

        private static void DrawHysteresisBar(Rect rect, float low, float restore, float currentCharge)
        {
            if (rect.width <= 8f || rect.height <= 2f) return;
            Widgets.DrawBoxSolid(rect, PowerUiDraw.BgRecessed);
            float lw = Mathf.Clamp01(low) * rect.width;
            float rw = Mathf.Clamp01(restore) * rect.width;
            if (lw > 1f)
            {
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, lw, rect.height),
                    new Color(PowerUiDraw.NoticeColor.r, PowerUiDraw.NoticeColor.g, PowerUiDraw.NoticeColor.b, 0.35f));
            }
            if (rect.width - rw > 1f)
            {
                Widgets.DrawBoxSolid(new Rect(rect.x + rw, rect.y, rect.width - rw, rect.height),
                    new Color(PowerUiDraw.Good.r, PowerUiDraw.Good.g, PowerUiDraw.Good.b, 0.30f));
            }
            Widgets.DrawBoxSolid(new Rect(rect.x + lw, rect.y, 2f, rect.height), PowerUiDraw.Warn);
            Widgets.DrawBoxSolid(new Rect(rect.x + rw, rect.y, 2f, rect.height), PowerUiDraw.Good);
            if (currentCharge >= 0f)
            {
                float cx = rect.x + Mathf.Clamp01(currentCharge) * rect.width;
                Widgets.DrawBoxSolid(new Rect(cx - 1f, rect.y - 1f, 3f, rect.height + 2f), PowerUiDraw.AccentCyan);
            }
            PowerUiDraw.DrawBorder(rect, PowerUiDraw.BorderPanel, 1);
        }

        private static void OpenThresholdMenu(PowerGridManagerSettings settings, bool low)
        {
            if (settings == null) return;
            float[] values = low
                ? new[] { 0.10f, 0.15f, 0.20f, 0.25f, 0.30f, 0.40f }
                : new[] { 0.40f, 0.50f, 0.60f, 0.70f, 0.80f, 0.90f };
            var options = new List<FloatMenuOption>(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                float value = values[i];
                options.Add(new FloatMenuOption(PowerFormatting.FormatCharge(value), delegate
                {
                    if (low) settings.LoadShedLowThreshold = value;
                    else settings.LoadShedRestoreThreshold = value;
                    settings.NormalizeManagement();
                    PowerGridManagerMod.Save();
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void OpenRecoveryMenu(PowerGridManagerSettings settings)
        {
            if (settings == null) return;
            var options = new List<FloatMenuOption>(3);
            LoadSheddingRecoveryMode[] modes =
            {
                LoadSheddingRecoveryMode.Staged, LoadSheddingRecoveryMode.All,
                LoadSheddingRecoveryMode.Manual
            };
            for (int i = 0; i < modes.Length; i++)
            {
                LoadSheddingRecoveryMode mode = modes[i];
                options.Add(new FloatMenuOption(RecoveryLabel(mode), delegate
                {
                    settings.LoadShedRecovery = mode;
                    settings.NormalizeManagement();
                    PowerGridManagerMod.Save();
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string RecoveryLabel(LoadSheddingRecoveryMode mode)
        {
            switch (mode)
            {
                case LoadSheddingRecoveryMode.All: return "PGM_Mgmt_Recovery_All".Translate();
                case LoadSheddingRecoveryMode.Manual: return "PGM_Mgmt_Recovery_Manual".Translate();
                default: return "PGM_Mgmt_Recovery_Staged".Translate();
            }
        }

        private static float DrawPreview(Rect rect, LoadSheddingPlan plan, PowerManagementRegistry registry,
            int previewRows)
        {
            float contentH = HeaderHeight + 28f + (plan != null && plan.ToShed.Count > 0
                ? previewRows * RowHeight + (plan.ToShed.Count > previewRows ? RowHeight : 0f)
                : 28f) + 18f;
            Rect panel = new Rect(rect.x, rect.y, rect.width, contentH);
            PowerUiDraw.Panel(panel);
            Rect inner = panel.ContractedBy(10f);

            float y = inner.y;
            PowerUiDraw.SectionHeader(new Rect(inner.x, y, inner.width, HeaderHeight),
                "PGM_Mgmt_PreviewTitle".Translate(plan == null ? 0 : plan.ToShed.Count));
            y += HeaderHeight + 4f;

            if (plan == null || plan.ToShed.Count == 0)
            {
                PowerUiDraw.WrappedText(new Rect(inner.x, y, inner.width, 24f),
                    "PGM_Mgmt_PreviewEmpty".Translate(), PowerUiDraw.TextSecondary);
                y += 26f;
            }
            else
            {
                for (int i = 0; i < previewRows; i++)
                {
                    BuildingPowerSnapshot row = plan.ToShed[i];
                    Rect line = new Rect(inner.x, y, inner.width, RowHeight);
                    PowerUiDraw.RowBackground(line, i, false);
                    PowerUiDraw.TextEllipsized(new Rect(line.x + 6f, line.y, line.width * 0.46f, RowHeight),
                        PowerGridUtilities.BuildingLabel(row.Building), PowerUiDraw.TextPrimary);
                    int priority = registry == null
                        ? PowerManagementRegistry.DefaultPriority : registry.PriorityOf(row.Building);
                    PowerUiDraw.DrawText(new Rect(line.x + line.width * 0.46f, line.y, line.width * 0.20f, RowHeight),
                        "PGM_Mgmt_Priority".Translate(priority), PowerUiDraw.AccentCyan,
                        TextAnchor.MiddleRight);
                    PowerUiDraw.DrawText(new Rect(line.x + line.width * 0.66f, line.y, line.width * 0.34f - 6f, RowHeight),
                        PowerFormatting.FormatWatts(row.ConsumptionWatts), PowerUiDraw.Warn,
                        TextAnchor.MiddleRight);
                    y += RowHeight;
                }
                if (plan.ToShed.Count > previewRows)
                {
                    PowerUiDraw.DrawText(new Rect(inner.x + 4f, y, inner.width - 8f, RowHeight),
                        "PGM_Mgmt_MoreRows".Translate(plan.ToShed.Count - previewRows), PowerUiDraw.TextSecondary);
                    y += RowHeight;
                }
            }

            Rect summary = new Rect(inner.x, y, inner.width, 22f);
            PowerUiDraw.LabelValue(summary, "PGM_Mgmt_PreviewSummary".Translate(),
                plan == null ? "\u2014" : PowerFormatting.FormatWatts(plan.ProjectedSavingsWatts),
                inner.width * 0.62f, PowerUiDraw.TextPrimary);
            PowerUiDraw.Tip(summary, "PGM_Mgmt_PreviewTip".Translate());
            return panel.yMax + Gap;
        }

        private static float DrawGroups(Rect rect, PowerUiState state, PowerManagementRegistry registry,
            PowerManagementController management, int groupRows, int tick)
        {
            float contentH = HeaderHeight + 34f + Math.Max(26f, groupRows * GroupRowHeight) + 22f;
            Rect panel = new Rect(rect.x, rect.y, rect.width, contentH);
            PowerUiDraw.Panel(panel);
            Rect inner = panel.ContractedBy(10f);

            float y = inner.y;
            PowerUiDraw.SectionHeader(new Rect(inner.x, y, inner.width, HeaderHeight),
                "PGM_Mgmt_GroupsTitle".Translate(registry == null ? 0 : registry.GroupCount));
            y += HeaderHeight + 4f;

            float fieldWidth = Mathf.Max(80f, inner.width * 0.44f);
            Rect field = new Rect(inner.x, y, fieldWidth, 25f);
            Widgets.DrawBoxSolid(field, PowerUiDraw.BgRecessed);
            state.ManagementNewGroupName = Widgets.TextField(field, state.ManagementNewGroupName ?? string.Empty);
            PowerUiDraw.DrawBorder(field, PowerUiDraw.BorderPanel, 1);

            Rect create = new Rect(field.xMax + 6f, y, 82f, 25f);
            if (PowerUiDraw.Button(create, "PGM_Mgmt_Create".Translate(), false, true) && registry != null)
            {
                bool created = management != null
                    ? management.CreateGroup(state.ManagementNewGroupName, tick)
                    : registry.AddGroup(state.ManagementNewGroupName);
                if (created)
                {
                    state.ManagementGroupFilter = state.ManagementNewGroupName;
                    state.ManagementNewGroupName = string.Empty;
                    state.ManagementPage = 0;
                }
            }

            Rect all = new Rect(create.xMax + 6f, y, Mathf.Max(40f, inner.xMax - create.xMax - 6f), 25f);
            if (PowerUiDraw.Button(all, "PGM_Mgmt_Group_All".Translate(), string.IsNullOrEmpty(state.ManagementGroupFilter)))
            {
                state.ManagementGroupFilter = string.Empty;
                state.ManagementPage = 0;
            }
            y += 30f;

            if (groupRows == 0)
            {
                PowerUiDraw.DrawText(new Rect(inner.x + 2f, y, inner.width - 4f, 24f),
                    "PGM_Mgmt_EmptyGroups".Translate(), PowerUiDraw.TextSecondary);
                return panel.yMax + Gap;
            }

            for (int i = 0; i < groupRows; i++)
            {
                string name = registry.Groups[i];
                Rect row = new Rect(inner.x, y, inner.width, GroupRowHeight);
                PowerUiDraw.RowBackground(row, i, name == state.ManagementGroupFilter);

                bool narrow = row.width < 520f;
                float buttonsWidth = narrow ? 142f : 236f;
                float nameWidth = Mathf.Max(60f, row.width - buttonsWidth - 8f);
                PowerUiDraw.TextEllipsized(new Rect(row.x + 6f, row.y, nameWidth, GroupRowHeight),
                    name, PowerUiDraw.TextPrimary);
                if (!narrow)
                {
                    PowerUiDraw.TextEllipsized(new Rect(row.x + nameWidth - 98f, row.y, 96f, GroupRowHeight),
                        "PGM_Mgmt_Group_Members".Translate(registry.GroupMemberCount(name)) + " \u00B7 " +
                        "PGM_Mgmt_Priority".Translate(registry.GroupPriority(name)),
                        PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);
                }
                if (Widgets.ButtonInvisible(new Rect(row.x, row.y, nameWidth, GroupRowHeight)))
                {
                    state.ManagementGroupFilter = name;
                    state.ManagementPage = 0;
                }

                float bx = row.xMax;
                bx -= 48f;
                if (PowerUiDraw.Button(new Rect(bx, row.y + 2f, 46f, 22f),
                    "PGM_Mgmt_Group_Delete".Translate(), false, false, true, PowerUiDraw.NoticeColor))
                {
                    bool removed = management != null
                        ? management.DeleteGroup(name, tick) : registry.RemoveGroup(name);
                    if (removed && state.ManagementGroupFilter == name)
                        state.ManagementGroupFilter = string.Empty;
                }
                if (narrow)
                {
                    bx -= 48f;
                    if (PowerUiDraw.Button(new Rect(bx, row.y + 2f, 46f, 22f),
                        "PGM_Mgmt_Group_Mark".Translate()))
                        SetGroupSheddable(management, registry, name, true, tick);
                    bx -= 48f;
                    if (PowerUiDraw.Button(new Rect(bx, row.y + 2f, 46f, 22f),
                        "PGM_Mgmt_Group_Clear".Translate()))
                        SetGroupSheddable(management, registry, name, false, tick);
                }
                else
                {
                    bx -= 48f;
                    int currentPriority = registry.GroupPriority(name);
                    if (PowerUiDraw.Button(new Rect(bx, row.y + 2f, 46f, 22f),
                        currentPriority >= PowerManagementRegistry.MaxPriority ? "1" : "+"))
                    {
                        int next = currentPriority >= PowerManagementRegistry.MaxPriority
                            ? PowerManagementRegistry.MinPriority : currentPriority + 1;
                        if (management != null) management.SetGroupPriority(name, next, tick);
                        else { registry.SetGroupPriority(name, next); registry.ApplyGroupPriority(name, next); }
                    }
                    bx -= 58f;
                    if (PowerUiDraw.Button(new Rect(bx, row.y + 2f, 56f, 22f),
                        "PGM_Mgmt_Group_Mark".Translate()))
                        SetGroupSheddable(management, registry, name, true, tick);
                    bx -= 58f;
                    if (PowerUiDraw.Button(new Rect(bx, row.y + 2f, 56f, 22f),
                        "PGM_Mgmt_Group_Clear".Translate()))
                        SetGroupSheddable(management, registry, name, false, tick);
                }
                PowerUiDraw.Tip(new Rect(row.xMax - buttonsWidth, row.y, buttonsWidth, GroupRowHeight),
                    "PGM_Mgmt_Group_Tip".Translate());
                y += GroupRowHeight;
            }
            return panel.yMax + Gap;
        }

        private static float DrawPresets(Rect rect, PowerGridManagerSettings settings, int tick)
        {
            List<PowerPreset> presets = PowerPresetManager.LoadPresets(settings);
            float contentH = HeaderHeight + 34f + Math.Max(26f, presets.Count * RowHeight) + 22f;
            Rect panel = new Rect(rect.x, rect.y, rect.width, contentH);
            PowerUiDraw.Panel(panel);
            Rect inner = panel.ContractedBy(10f);

            float y = inner.y;
            PowerUiDraw.SectionHeader(new Rect(inner.x, y, inner.width, HeaderHeight),
                "PGM_Preset_Header".Translate(presets.Count));
            y += HeaderHeight + 4f;

            Rect newButton = new Rect(inner.xMax - 110f, y, 110f, 24f);
            if (PowerUiDraw.Button(newButton, "PGM_Preset_Create".Translate(), false, true))
            {
                if (PowerPresetManager.Create(presets, "Preset " + (presets.Count + 1)))
                    PowerPresetManager.SavePresets(settings, presets);
            }
            PowerUiDraw.Tip(newButton, "PGM_Preset_CreateTip".Translate());

            if (presets.Count == 0)
            {
                PowerUiDraw.TextEllipsized(new Rect(inner.x, y, Mathf.Max(20f, newButton.x - inner.x - 8f), 24f),
                    "PGM_Preset_Empty".Translate(), PowerUiDraw.TextSecondary);
                return panel.yMax + Gap;
            }

            y += 28f;
            for (int i = 0; i < presets.Count; i++)
            {
                PowerPreset preset = presets[i];
                Rect row = new Rect(inner.x, y, inner.width, RowHeight);
                PowerUiDraw.RowBackground(row, i, preset.IsActive);

                PowerUiDraw.TextEllipsized(new Rect(row.x + 6f, row.y, row.width * 0.34f, RowHeight),
                    preset.Name, preset.IsActive ? PowerUiDraw.Good : PowerUiDraw.TextPrimary);
                PowerUiDraw.DrawText(new Rect(row.x + row.width * 0.34f, row.y, row.width * 0.20f, RowHeight),
                    "PGM_Preset_Priority".Translate(preset.DefaultPriority), PowerUiDraw.TextSecondary,
                    TextAnchor.MiddleRight);
                PowerUiDraw.DrawText(new Rect(row.x + row.width * 0.54f, row.y, row.width * 0.20f, RowHeight),
                    "PGM_Preset_ShedAt".Translate(PowerFormatting.FormatCharge(preset.LoadShedLowThreshold)),
                    PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);

                float bx = row.xMax;
                bx -= 66f;
                if (PowerUiDraw.Button(new Rect(bx, row.y + 1f, 64f, 22f),
                        preset.IsActive
                            ? (string)"PGM_Preset_Active".Translate()
                            : (string)"PGM_Preset_Activate".Translate(),
                        preset.IsActive))
                {
                    if (!preset.IsActive)
                        PowerPresetManager.Activate(settings, preset, tick);
                }
                bx -= 50f;
                if (PowerUiDraw.Button(new Rect(bx, row.y + 1f, 48f, 22f),
                        "PGM_Preset_Delete".Translate(), false, false, true, PowerUiDraw.NoticeColor))
                {
                    PowerPresetManager.Delete(presets, preset);
                    PowerPresetManager.SavePresets(settings, presets);
                }
                bx -= 62f;
                if (PowerUiDraw.Button(new Rect(bx, row.y + 1f, 60f, 22f),
                        "PGM_Preset_Duplicate".Translate()))
                {
                    PowerPresetManager.Duplicate(presets, preset);
                    PowerPresetManager.SavePresets(settings, presets);
                }
                y += RowHeight;
            }
            return panel.yMax + Gap;
        }

        private static void SetGroupSheddable(PowerManagementController management,
            PowerManagementRegistry registry, string name, bool value, int tick)
        {
            if (management != null) management.SetGroupSheddable(name, value, tick);
            else if (registry != null) registry.SetGroupSheddable(name, value);
        }

        private void BuildRows(PowerGridSnapshot snapshot, PowerUiState state,
            PowerManagementRegistry registry, int revision)
        {
            string filter = state.ManagementGroupFilter ?? string.Empty;
            if (snapshot == null)
            {
                rows.Clear();
                selectedRow = null;
                cachedRowsSnapshot = null;
                cachedRowsFilter = filter;
                cachedRowsRevision = revision;
                return;
            }
            if (cachedRowsSnapshot == snapshot && cachedRowsFilter == filter &&
                cachedRowsRevision == revision) return;
            cachedRowsSnapshot = snapshot;
            cachedRowsFilter = filter;
            cachedRowsRevision = revision;
            rows.Clear();
            selectedRow = null;
            if (snapshot.Buildings == null) return;
            IReadOnlyList<BuildingPowerSnapshot> buildings = snapshot.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingPowerSnapshot row = buildings[i];
                if (row == null || row.Building == null) continue;
                if (filter.Length > 0)
                {
                    PowerManagementRecord record = registry == null ? null : registry.Find(row.Building);
                    if (record == null || record.Group != filter) continue;
                }
                rows.Add(row);
                if (state.ManagementSelectedId != null &&
                    PowerManagementRegistry.StableId(row.Building) == state.ManagementSelectedId)
                    selectedRow = row;
            }
        }

        private float DrawBuildings(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, PowerManagementController management,
            PowerManagementRegistry registry, int pageStart, int pageRows, int pageCount, int tick,
            bool compact, float actionH)
        {
            float rowsH = pageRows == 0 ? 28f : pageRows * RowHeight;
            float contentH = HeaderHeight + 8f + rowsH + 8f + actionH + 30f + 20f;
            Rect panel = new Rect(rect.x, rect.y, rect.width, contentH);
            PowerUiDraw.Panel(panel);
            Rect inner = panel.ContractedBy(10f);

            float y = inner.y;
            PowerUiDraw.SectionHeader(new Rect(inner.x, y, inner.width, HeaderHeight),
                "PGM_Mgmt_BuildingsTitle".Translate(rows.Count));
            y += HeaderHeight + 4f;

            if (pageRows == 0)
            {
                PowerUiDraw.DrawText(new Rect(inner.x + 2f, y, inner.width - 4f, 26f),
                    "PGM_Mgmt_NoBuildings".Translate(), PowerUiDraw.TextSecondary);
                y += 28f;
            }
            else
            {
                for (int i = 0; i < pageRows; i++)
                {
                    BuildingPowerSnapshot row = rows[pageStart + i];
                    PowerManagementRecord record = registry == null ? null : registry.Find(row.Building);
                    string id = PowerManagementRegistry.StableId(row.Building);
                    Rect line = new Rect(inner.x, y, inner.width, RowHeight);
                    PowerUiDraw.RowBackground(line, i, id != null && id == state.ManagementSelectedId);

                    float nameWidth = Mathf.Max(80f, line.width * 0.36f);
                    PowerUiDraw.TextEllipsized(new Rect(line.x + 6f, line.y, nameWidth, RowHeight),
                        PowerGridUtilities.BuildingLabel(row.Building), PowerUiDraw.TextPrimary);
                    float cx = line.x + nameWidth + 4f;
                    PowerUiDraw.TextEllipsized(new Rect(cx, line.y, 76f, RowHeight),
                        record == null || record.Group.Length == 0
                            ? "PGM_Mgmt_Group_None".Translate().ToString() : record.Group,
                        PowerUiDraw.TextSecondary);
                    cx += 78f;
                    PowerUiDraw.DrawText(new Rect(cx, line.y, 48f, RowHeight),
                        "PGM_Mgmt_Priority".Translate(record == null
                            ? PowerManagementRegistry.DefaultPriority : record.ShedPriority),
                        PowerUiDraw.AccentCyan, TextAnchor.MiddleCenter);
                    cx += 50f;
                    PowerUiDraw.DrawText(new Rect(cx, line.y, Mathf.Max(0f, line.xMax - cx - 98f), RowHeight),
                        PowerFormatting.FormatWatts(row.ConsumptionWatts), PowerUiDraw.TextPrimary,
                        TextAnchor.MiddleRight);
                    PowerUiDraw.DrawText(new Rect(line.xMax - 92f, line.y, 88f, RowHeight),
                        row.SwitchIsOn == true
                            ? "\u25CF " + "PGM_Mgmt_Switch_On".Translate()
                            : "\u2013 " + "PGM_Mgmt_Switch_Off".Translate(),
                        row.SwitchIsOn == true ? PowerUiDraw.Good : PowerUiDraw.Warn,
                        TextAnchor.MiddleRight);
                    if (Widgets.ButtonInvisible(line))
                    {
                        state.ManagementSelectedId = id;
                        selectedRow = row;
                    }
                    y += RowHeight;
                }
            }

            y += 4f;
            y = DrawActionBar(new Rect(inner.x, y, inner.width, actionH), snapshot, state, settings,
                management, registry, tick, compact);
            y += 4f;

            Rect pager = new Rect(inner.x, y, inner.width, 24f);
            if (PowerUiDraw.Button(new Rect(pager.x, pager.y, 64f, 24f), "PGM_Mgmt_Prev".Translate()))
            {
                state.ManagementPage = state.ManagementPage > 0 ? state.ManagementPage - 1 : pageCount - 1;
            }
            if (PowerUiDraw.Button(new Rect(pager.x + 70f, pager.y, 64f, 24f), "PGM_Mgmt_Next".Translate()))
            {
                state.ManagementPage = (state.ManagementPage + 1) % Math.Max(1, pageCount);
            }
            PowerUiDraw.DrawText(new Rect(pager.x + 142f, pager.y, Mathf.Max(0f, pager.width - 142f), 24f),
                "PGM_Mgmt_Page".Translate(state.ManagementPage + 1, pageCount), PowerUiDraw.TextSecondary);
            return panel.yMax + Gap;
        }

        private float DrawActionBar(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, PowerManagementController management,
            PowerManagementRegistry registry, int tick, bool compact)
        {
            PowerUiDraw.ElevatedPanel(rect);
            BuildingPowerSnapshot selected = selectedRow;
            if (selected == null || selected.Building.Destroyed || !selected.Building.Spawned ||
                management == null || registry == null)
            {
                PowerUiDraw.WrappedText(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f),
                    "PGM_Mgmt_SelectPrompt".Translate(), PowerUiDraw.TextSecondary);
                return rect.yMax;
            }

            Building building = selected.Building;
            PowerManagementRecord record = registry.Find(building);
            PowerControlSupport support = CompatibilityRegistry.EvaluateControl(selected, registry);
            Rect title = new Rect(rect.x + 10f, rect.y + 4f, rect.width - 20f, 20f);
            PowerUiDraw.TextEllipsized(title,
                "PGM_Mgmt_Selected".Translate(PowerGridUtilities.BuildingLabel(building),
                    CompatibilityRegistry.ControlLabelKey(support).Translate()),
                support == PowerControlSupport.Unsupported ? PowerUiDraw.Warn : PowerUiDraw.TextPrimary);
            PowerUiDraw.Tip(title, CompatibilityRegistry.ControlTooltipKey(support).Translate());

            bool canUse = settings != null && settings.ManagementEnabled &&
                          support != PowerControlSupport.Unsupported &&
                          support != PowerControlSupport.Pending;
            string[] labels =
            {
                "PGM_Mgmt_Action_On".Translate(), "PGM_Mgmt_Action_Off".Translate(),
                "PGM_Mgmt_Action_Group".Translate(), "\u2013", "+",
                "PGM_Mgmt_Action_Sheddable".Translate(), "PGM_Mgmt_Action_Exclude".Translate(),
                "PGM_Mgmt_Action_Diagnostics".Translate(), "PGM_Mgmt_Action_Locate".Translate()
            };
            Action[] actions =
            {
                delegate { string detail; management.ManualSwitch(snapshot, building, true, settings, tick, out detail); },
                delegate { string detail; management.ManualSwitch(snapshot, building, false, settings, tick, out detail); },
                delegate { OpenGroupMenu(management, building, tick); },
                delegate { management.SetPriority(building, Priority(record) - 1, tick); },
                delegate { management.SetPriority(building, Priority(record) + 1, tick); },
                delegate { management.SetSheddable(building, record == null || !record.Sheddable, tick); },
                delegate { management.SetExcludedFromManagement(building,
                              record == null || !record.ExcludeFromManagement, tick); },
                delegate { management.SetExcludedFromDiagnostics(building,
                              record == null || !record.ExcludeFromDiagnostics, tick); },
                delegate { CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(building)); }
            };
            bool[] usable =
            {
                canUse, canUse, true, true, true, true, true, true, true
            };

            int perRow = compact ? 5 : 9;
            int buttonRows = (labels.Length + perRow - 1) / perRow;
            float buttonWidth = Mathf.Max(44f, (rect.width - 16f - (perRow - 1) * 4f) / perRow);
            float y = rect.y + 28f;
            for (int i = 0; i < labels.Length; i++)
            {
                int rowIndex = i / perRow;
                int column = i % perRow;
                float x = rect.x + 8f + column * (buttonWidth + 4f);
                float buttonY = y + rowIndex * 26f;
                if (buttonY + 24f > rect.yMax) break;
                Rect button = new Rect(x, buttonY, buttonWidth, 24f);
                if (PowerUiDraw.Button(button, labels[i], false, false, usable[i]))
                    actions[i]();
            }

            if (!compact && buttonRows == 1)
            {
                PowerUiDraw.TextEllipsized(new Rect(rect.x + 10f, y + 28f, rect.width - 20f, 18f),
                    Flags(record), PowerUiDraw.TextSecondary);
            }
            return rect.yMax;
        }

        private static int Priority(PowerManagementRecord record)
        {
            return record == null ? PowerManagementRegistry.DefaultPriority : record.ShedPriority;
        }

        private static string Flags(PowerManagementRecord record)
        {
            return "PGM_Mgmt_SelectedFlags".Translate(
                record != null && record.Sheddable
                    ? "PGM_Mgmt_Flag_Sheddable".Translate() : "PGM_Mgmt_Flag_Critical".Translate(),
                record != null && record.ExcludeFromManagement
                    ? "PGM_Mgmt_Flag_ExcludedControl".Translate() : "PGM_Mgmt_Flag_Allowed".Translate(),
                record != null && record.ExcludeFromDiagnostics
                    ? "PGM_Mgmt_Flag_ExcludedDiagnostics".Translate() : "PGM_Mgmt_Flag_Allowed".Translate());
        }

        private static void OpenGroupMenu(PowerManagementController management, Building building, int tick)
        {
            PowerManagementRegistry registry = management.Registry;
            var options = new List<FloatMenuOption>(registry.GroupCount + 1)
            {
                new FloatMenuOption("PGM_Mgmt_Group_None".Translate(), delegate
                {
                    management.SetGroup(building, string.Empty, tick);
                })
            };
            for (int i = 0; i < registry.GroupCount; i++)
            {
                string name = registry.Groups[i];
                options.Add(new FloatMenuOption(name, delegate
                {
                    management.SetGroup(building, name, tick);
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static float DrawEvents(Rect rect, PowerManagementEventLog log, int eventRows, int tick)
        {
            float contentH = HeaderHeight + 8f + Math.Max(26f, eventRows * EventRowHeight) + 18f;
            Rect panel = new Rect(rect.x, rect.y, rect.width, contentH);
            PowerUiDraw.Panel(panel);
            Rect inner = panel.ContractedBy(10f);

            float y = inner.y;
            PowerUiDraw.SectionHeader(new Rect(inner.x, y, inner.width, HeaderHeight),
                "PGM_Mgmt_EventsTitle".Translate(log == null ? 0 : log.Count));
            y += HeaderHeight + 4f;
            if (eventRows == 0)
            {
                PowerUiDraw.DrawText(new Rect(inner.x + 2f, y, inner.width - 4f, 22f),
                    "PGM_Mgmt_EmptyEvents".Translate(), PowerUiDraw.TextSecondary);
                return panel.yMax;
            }

            int start = log.Count - eventRows;
            for (int i = 0; i < eventRows; i++)
            {
                PowerManagementEvent entry = log[start + i];
                Rect row = new Rect(inner.x, y, inner.width, EventRowHeight);
                PowerUiDraw.RowBackground(row, i, false);
                PowerUiDraw.TextEllipsized(new Rect(row.x + 6f, row.y, row.width - 12f, EventRowHeight),
                    "PGM_Mgmt_EventRow".Translate(
                        PowerFormatting.FormatDuration(Math.Max(0, tick - entry.Tick)),
                        EventKindLabel(entry.Kind),
                        entry.Label,
                        entry.Success
                            ? "PGM_Mgmt_Event_Ok".Translate()
                            : "PGM_Mgmt_Event_Failed".Translate()),
                    entry.Success ? PowerUiDraw.TextSecondary : PowerUiDraw.Warn);
                PowerUiDraw.Tip(row, entry.Detail ?? string.Empty);
                y += EventRowHeight;
            }
            return panel.yMax;
        }

        private static string EventKindLabel(PowerManagementEventKind kind)
        {
            switch (kind)
            {
                case PowerManagementEventKind.ManualOff: return "PGM_Mgmt_EventKind_ManualOff".Translate();
                case PowerManagementEventKind.ManualOn: return "PGM_Mgmt_EventKind_ManualOn".Translate();
                case PowerManagementEventKind.AutomaticOff: return "PGM_Mgmt_EventKind_AutomaticOff".Translate();
                case PowerManagementEventKind.AutomaticOn: return "PGM_Mgmt_EventKind_AutomaticOn".Translate();
                case PowerManagementEventKind.RestoreAll: return "PGM_Mgmt_EventKind_RestoreAll".Translate();
                case PowerManagementEventKind.GroupChanged: return "PGM_Mgmt_EventKind_Group".Translate();
                case PowerManagementEventKind.PriorityChanged: return "PGM_Mgmt_EventKind_Priority".Translate();
                case PowerManagementEventKind.PolicyChanged: return "PGM_Mgmt_EventKind_Policy".Translate();
                case PowerManagementEventKind.TrackedCleared: return "PGM_Mgmt_EventKind_Tracking".Translate();
                default: return "PGM_Mgmt_EventKind_Refused".Translate();
            }
        }
    }
}
