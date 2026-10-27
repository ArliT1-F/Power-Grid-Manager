using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Optional management tab. It is deliberately inert until the player enables management:
    /// the master switches are off by default, no building is shed unless explicitly marked,
    /// and the "affected buildings" list is a read-only preview of the same plan the engine
    /// would execute. All switch changes go through <see cref="PowerManagementController"/>.
    /// </summary>
    public sealed class PowerManagementPanel
    {
        private const float Gap = 6f;
        private const float RowHeight = 22f;
        private const float GroupRowHeight = 24f;
        private const float EventRowHeight = 20f;
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

        /// <summary>Plan used by the last draw pass; exposed for headless UI checks.</summary>
        public LoadSheddingPlan LastPlan => drawnPlan;

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, PowerManagementController management, int tick)
        {
            drawnPlan = management == null ? null
                : management.Preview(snapshot, settings, tick);
            LoadSheddingPlan plan = drawnPlan;
            PowerManagementRegistry registry = management == null ? null : management.Registry;
            PowerManagementEventLog log = management == null ? null : management.Events;

            bool compact = rect.width < 700f;
            float width = Mathf.Max(200f, rect.width - 18f);
            BuildRows(snapshot, state, registry, management == null ? -1 : management.Revision);

            int previewRows = plan == null ? 0 : Math.Min(plan.ToShed.Count, PreviewRowCap);
            int groupRows = registry == null ? 0 : Math.Min(registry.GroupCount, GroupRowCap);
            int pageCount = Math.Max(1, (rows.Count + BuildingRowCap - 1) / BuildingRowCap);
            if (state.ManagementPage >= pageCount) state.ManagementPage = 0;
            int pageStart = state.ManagementPage * BuildingRowCap;
            int pageRows = Math.Min(BuildingRowCap, Math.Max(0, rows.Count - pageStart));
            int eventRows = log == null ? 0 : Math.Min(log.Count, EventRowCap);

            float headerH = compact ? 76f : 56f;
            float actionH = compact ? 84f : 80f;
            float contentHeight = headerH + 48f + 64f + 64f + 112f +
                                 HeaderHeight + Math.Max(28f, previewRows * RowHeight) + 30f +
                                 HeaderHeight + 28f + Math.Max(26f, groupRows * GroupRowHeight) +
                                 HeaderHeight + Math.Max(28f, pageRows * RowHeight) + actionH + 26f +
                                 HeaderHeight + Math.Max(24f, eventRows * EventRowHeight) + Gap * 10f + 40f;

            Rect view = new Rect(0f, 0f, width, contentHeight);
            Widgets.BeginScrollView(rect, ref state.ManagementScroll, view);
            try
            {
                float y = 0f;
                DrawHeader(new Rect(0f, y, width, headerH), compact, state, settings, management,
                    snapshot, tick);
                y += headerH + Gap;

                PowerUiDraw.WrappedText(new Rect(0f, y, width, 44f), "PGM_Mgmt_Note".Translate(),
                    PowerUiDraw.Muted);
                y += 48f + Gap;

                DrawTiles(new Rect(0f, y, width, 58f), snapshot, settings, plan, management);
                y += 64f + Gap;

                y = DrawPresets(new Rect(0f, y, width, 0f), settings, management, tick);
                y += 10f + Gap;

                DrawPolicy(new Rect(0f, y, width, 106f), state, settings, management, plan, tick);
                y += 112f + Gap;

                y = DrawPreview(new Rect(0f, y, width, 0f), plan, registry, previewRows);
                y = DrawGroups(new Rect(0f, y, width, 0f), state, registry, management,
                    groupRows, tick, compact);
                y = DrawBuildings(new Rect(0f, y, width, 0f), snapshot, state, settings, management,
                    registry, pageStart, pageRows, pageCount, tick, compact, actionH);
                DrawEvents(new Rect(0f, y, width, 0f), log, eventRows, tick);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        // --- Header ----------------------------------------------------------

        private void DrawHeader(Rect rect, bool compact, PowerUiState state,
            PowerGridManagerSettings settings, PowerManagementController management,
            PowerGridSnapshot snapshot, int tick)
        {
            bool enabled = settings != null && settings.ManagementEnabled;
            bool automation = enabled && settings.LoadSheddingEnabled;
            LoadSheddingPhase phase = management == null || management.LastPlan == null
                ? (enabled ? LoadSheddingPhase.Idle : LoadSheddingPhase.Disabled)
                : management.LastPlan.Phase;
            if (!automation) phase = LoadSheddingPhase.Disabled;

            float x = rect.x;
            PowerUiDraw.DrawText(new Rect(x, rect.y, Mathf.Max(120f, rect.width * 0.32f), 24f),
                "PGM_Mgmt_Title".Translate(), Color.white);
            Rect pill = new Rect(rect.xMax - 158f, rect.y, 158f, 22f);
            PowerUiDraw.StatusPill(pill, PhaseLabel(phase), PhaseColor(phase));
            PowerUiDraw.Tip(pill, "PGM_Mgmt_StatusTip".Translate());

            PowerManagementController.AutomationStatus status = management == null
                ? PowerManagementController.AutomationStatus.Unavailable : management.Status(settings);
            Rect statusPill = new Rect(rect.xMax - 158f, rect.y + 25f, 158f, 18f);
            PowerUiDraw.StatusPill(statusPill, AutomationStatusLabel(status),
                AutomationStatusColor(status));
            PowerUiDraw.Tip(statusPill, "PGM_Mgmt_AutomationStatusTip".Translate());

            float buttonY = rect.y + 48f;
            bool showEmergency = automation;
            bool showRestore = management != null && management.AutoShedCount > 0;
            int count = (showRestore ? 3 : 2) + (showEmergency ? 1 : 0);
            float buttonWidth = Mathf.Max(76f, (rect.width - (count - 1) * 4f) / count);
            float bx = rect.x;

            Rect master = new Rect(bx, buttonY, buttonWidth, 24f);
            if (Widgets.ButtonText(master, enabled
                    ? "PGM_Mgmt_Disable".Translate() : "PGM_Mgmt_Enable".Translate()) &&
                settings != null && management != null)
            {
                management.SetPolicy(settings, !enabled, automation, snapshot, tick);
            }
            PowerUiDraw.Tip(master, "PGM_Mgmt_MasterTip".Translate());
            bx += buttonWidth + 4f;

            Rect auto = new Rect(bx, buttonY, buttonWidth, 24f);
            if (Widgets.ButtonText(auto, automation
                    ? "PGM_Mgmt_AutoOff".Translate() : "PGM_Mgmt_AutoOn".Translate()) &&
                settings != null && management != null)
            {
                // Automation requires the master switch; an explicit click on this button
                // enables both rather than silently doing nothing.
                management.SetPolicy(settings, true, !automation, snapshot, tick);
            }
            PowerUiDraw.Tip(auto, "PGM_Mgmt_AutomationTip".Translate());
            bx += buttonWidth + 4f;

            Rect emergency = new Rect(bx, buttonY, buttonWidth, 24f);
            bool emergencyActive = management != null && management.EmergencyOverrideActive;
            if (Widgets.ButtonText(emergency, emergencyActive
                    ? "PGM_Mgmt_EmergencyOff".Translate() : "PGM_Mgmt_EmergencyOn".Translate()) &&
                settings != null && management != null)
            {
                if (emergencyActive) management.ClearEmergencyOverride(tick);
                else management.ActivateEmergencyOverride(tick);
            }
            PowerUiDraw.Tip(emergency, emergencyActive
                ? "PGM_Mgmt_EmergencyOffTip".Translate()
                : "PGM_Mgmt_EmergencyOnTip".Translate());
            bx += buttonWidth + 4f;

            if (showRestore)
            {
                float restoreX = compact ? rect.x : bx;
                float restoreWidth = compact
                    ? Mathf.Min(buttonWidth * 2f + 4f, rect.width)
                    : Mathf.Max(60f, Mathf.Min(buttonWidth, rect.xMax - bx));
                Rect restore = new Rect(restoreX, compact ? buttonY + 26f : buttonY,
                    restoreWidth, 24f);
                if (Widgets.ButtonText(restore, "PGM_Mgmt_RestoreNow".Translate(
                        management.AutoShedCount)) && management != null)
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
                case LoadSheddingPhase.Idle: return PowerUiDraw.Info;
                default: return PowerUiDraw.Muted;
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
                default: return PowerUiDraw.Muted;
            }
        }

        // --- Metrics and policy ---------------------------------------------

        private static void DrawTiles(Rect rect, PowerGridSnapshot snapshot,
            PowerGridManagerSettings settings, LoadSheddingPlan plan, PowerManagementController management)
        {
            float tileWidth = (rect.width - 18f) / 4f;
            float x = rect.x;
            float charge = plan == null ? -1f : plan.ChargeFraction;
            PowerUiDraw.MetricTile(new Rect(x, rect.y, tileWidth, 58f), "PGM_Mgmt_Tile_Charge".Translate(),
                charge < 0f ? "PGM_Value_Unknown".Translate().ToString()
                            : PowerFormatting.FormatCharge(charge),
                charge >= 0f && settings != null && charge <= settings.LoadShedLowThreshold
                    ? PowerUiDraw.Warn : Color.white,
                "PGM_Mgmt_Tile_ChargeTip".Translate());
            x += tileWidth + 6f;
            PowerUiDraw.MetricTile(new Rect(x, rect.y, tileWidth, 58f), "PGM_Mgmt_Tile_Net".Translate(),
                snapshot == null ? "—" : PowerFormatting.FormatWattsSigned(snapshot.NetWatts),
                snapshot != null && snapshot.NetWatts < -0.01f ? PowerUiDraw.NoticeColor : Color.white,
                "PGM_Mgmt_Tile_NetTip".Translate());
            x += tileWidth + 6f;
            PowerUiDraw.MetricTile(new Rect(x, rect.y, tileWidth, 58f), "PGM_Mgmt_Tile_Sheddable".Translate(),
                (plan == null ? 0 : plan.CandidateCount).ToString(), Color.white,
                "PGM_Mgmt_Tile_SheddableTip".Translate());
            x += tileWidth + 6f;
            PowerUiDraw.MetricTile(new Rect(x, rect.y, tileWidth, 58f), "PGM_Mgmt_Tile_AutoShed".Translate(),
                (management == null ? 0 : management.AutoShedCount).ToString(),
                management != null && management.AutoShedCount > 0 ? PowerUiDraw.Warn : Color.white,
                "PGM_Mgmt_Tile_AutoShedTip".Translate());
        }

        private float DrawPresets(Rect rect, PowerGridManagerSettings settings,
            PowerManagementController management, int tick)
        {
            float y = rect.y;
            List<PowerPreset> presets = PowerPresetManager.LoadPresets(settings);
            PowerUiDraw.SectionHeader(new Rect(rect.x, y, rect.width, HeaderHeight),
                "PGM_Preset_Header".Translate(presets.Count));
            y += HeaderHeight + 2f;

            if (presets.Count == 0)
            {
                PowerUiDraw.WrappedText(new Rect(rect.x + 4f, y, rect.width - 8f, 26f),
                    "PGM_Preset_Empty".Translate(), PowerUiDraw.Muted);
                return y + 28f + Gap;
            }

            Rect newButton = new Rect(rect.xMax - 100f, y, 96f, 24f);
            if (Widgets.ButtonText(newButton, "PGM_Preset_Create".Translate()))
            {
                if (PowerPresetManager.Create(presets, "New preset"))
                    PowerPresetManager.SavePresets(settings, presets);
            }
            PowerUiDraw.Tip(newButton, "PGM_Preset_CreateTip".Translate());
            y += 28f;

            for (int i = 0; i < presets.Count; i++)
            {
                PowerPreset preset = presets[i];
                Rect row = new Rect(rect.x, y, rect.width, RowHeight);
                if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.04f));
                if (preset.IsActive) Widgets.DrawBoxSolid(row, new Color(0.2f, 0.4f, 0.2f, 0.15f));
                PowerUiDraw.DrawText(new Rect(row.x + 4f, row.y, row.width * 0.34f, RowHeight),
                    preset.Name, preset.IsActive ? PowerUiDraw.Good : Color.white);
                PowerUiDraw.DrawText(new Rect(row.x + row.width * 0.34f, row.y,
                        row.width * 0.22f, RowHeight),
                    "PGM_Preset_Priority".Translate(preset.DefaultPriority), PowerUiDraw.Muted,
                    TextAnchor.MiddleRight);
                PowerUiDraw.DrawText(new Rect(row.x + row.width * 0.56f, row.y,
                        row.width * 0.22f, RowHeight),
                    "PGM_Preset_ShedAt".Translate(PowerFormatting.FormatCharge(
                        preset.LoadShedLowThreshold)), PowerUiDraw.Muted, TextAnchor.MiddleRight);
                float bx = row.xMax;
                bx -= 46f;
                if (Widgets.ButtonText(new Rect(bx, row.y + 1f, 44f, 22f),
                        preset.IsActive ? "PGM_Preset_Active" : "PGM_Preset_Activate"))
                {
                    if (!preset.IsActive)
                        PowerPresetManager.Activate(settings, preset, tick);
                }
                bx -= 46f;
                if (Widgets.ButtonText(new Rect(bx, row.y + 1f, 44f, 22f),
                        "PGM_Preset_Delete".Translate()))
                {
                    PowerPresetManager.Delete(presets, preset);
                    PowerPresetManager.SavePresets(settings, presets);
                }
                bx -= 56f;
                if (Widgets.ButtonText(new Rect(bx, row.y + 1f, 54f, 22f),
                        "PGM_Preset_Duplicate".Translate()))
                {
                    PowerPresetManager.Duplicate(presets, preset);
                    PowerPresetManager.SavePresets(settings, presets);
                }
                y += RowHeight;
            }
            return y + Gap;
        }

        private void DrawPolicy(Rect rect, PowerUiState state, PowerGridManagerSettings settings,
            PowerManagementController management, LoadSheddingPlan plan, int tick)
        {
            PowerUiDraw.Panel(rect);
            PowerUiDraw.SectionHeader(new Rect(rect.x + 8f, rect.y + 2f, rect.width - 16f, 20f),
                "PGM_Mgmt_PolicyTitle".Translate());
            float y = rect.y + 24f;
            float buttonWidth = Mathf.Max(90f, (rect.width - 24f) / 3f);
            float x = rect.x + 8f;

            Rect low = new Rect(x, y, buttonWidth, 24f);
            if (Widgets.ButtonText(low, "PGM_Mgmt_Low_Label".Translate(
                    PowerFormatting.FormatCharge(settings == null ? 0f : settings.LoadShedLowThreshold))))
                OpenThresholdMenu(settings, true);
            PowerUiDraw.Tip(low, "PGM_Mgmt_Low_Tip".Translate());
            x += buttonWidth + 4f;

            Rect restore = new Rect(x, y, buttonWidth, 24f);
            if (Widgets.ButtonText(restore, "PGM_Mgmt_Restore_Label".Translate(
                    PowerFormatting.FormatCharge(settings == null ? 0f : settings.LoadShedRestoreThreshold))))
                OpenThresholdMenu(settings, false);
            PowerUiDraw.Tip(restore, "PGM_Mgmt_Restore_Tip".Translate());
            x += buttonWidth + 4f;

            Rect recovery = new Rect(x, y, Mathf.Max(0f, rect.xMax - x - 8f), 24f);
            if (Widgets.ButtonText(recovery, "PGM_Mgmt_Recovery_Label".Translate(
                    RecoveryLabel(settings == null ? LoadSheddingRecoveryMode.Staged : settings.LoadShedRecovery))))
                OpenRecoveryMenu(settings);
            PowerUiDraw.Tip(recovery, "PGM_Mgmt_Recovery_Tip".Translate());

            y += 28f;
            string reason = plan == null ? "PGM_Mgmt_Reason_Off".Translate() : plan.ReasonKey.Translate();
            PowerUiDraw.TextEllipsized(new Rect(rect.x + 8f, y, rect.width - 16f, 22f),
                "PGM_Mgmt_Status_Reason".Translate(reason), PowerUiDraw.Info);
            y += 22f;
            PowerUiDraw.WrappedText(new Rect(rect.x + 8f, y, rect.width - 16f, 30f),
                "PGM_Mgmt_PolicyNote".Translate(
                    settings == null ? 0 : settings.LoadShedMaxActionsPerPass,
                    PowerFormatting.FormatDuration(settings == null ? 0 : settings.LoadShedStableTicks),
                    PowerFormatting.FormatDuration(settings == null ? 0 : settings.LoadShedCooldownTicks)),
                PowerUiDraw.Muted);
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

        // --- Preview ---------------------------------------------------------

        private float DrawPreview(Rect rect, LoadSheddingPlan plan, PowerManagementRegistry registry,
            int previewRows)
        {
            float y = rect.y;
            PowerUiDraw.SectionHeader(new Rect(rect.x, y, rect.width, HeaderHeight),
                "PGM_Mgmt_PreviewTitle".Translate(plan == null ? 0 : plan.ToShed.Count));
            y += HeaderHeight + 2f;

            if (plan == null || plan.ToShed.Count == 0)
            {
                PowerUiDraw.WrappedText(new Rect(rect.x + 4f, y, rect.width - 8f, 26f),
                    "PGM_Mgmt_PreviewEmpty".Translate(), PowerUiDraw.Muted);
                y += 28f;
            }
            else
            {
                for (int i = 0; i < previewRows; i++)
                {
                    BuildingPowerSnapshot row = plan.ToShed[i];
                    Rect line = new Rect(rect.x, y, rect.width, RowHeight);
                    if (i % 2 == 0) Widgets.DrawBoxSolid(line, new Color(1f, 1f, 1f, 0.04f));
                    PowerUiDraw.TextEllipsized(new Rect(line.x + 4f, line.y, line.width * 0.46f, RowHeight),
                        PowerGridUtilities.BuildingLabel(row.Building), Color.white);
                    int priority = registry == null
                        ? PowerManagementRegistry.DefaultPriority : registry.PriorityOf(row.Building);
                    PowerUiDraw.DrawText(new Rect(line.x + line.width * 0.46f, line.y, line.width * 0.20f, RowHeight),
                        "PGM_Mgmt_Priority".Translate(priority), PowerUiDraw.Muted,
                        TextAnchor.MiddleRight);
                    PowerUiDraw.DrawText(new Rect(line.x + line.width * 0.66f, line.y, line.width * 0.34f - 4f,
                            RowHeight), PowerFormatting.FormatWatts(row.ConsumptionWatts), PowerUiDraw.Warn,
                        TextAnchor.MiddleRight);
                    y += RowHeight;
                }
                if (plan.ToShed.Count > previewRows)
                {
                    PowerUiDraw.DrawText(new Rect(rect.x + 4f, y, rect.width - 8f, RowHeight),
                        "PGM_Mgmt_MoreRows".Translate(plan.ToShed.Count - previewRows), PowerUiDraw.Muted);
                    y += RowHeight;
                }
            }

            Rect summary = new Rect(rect.x, y, rect.width, 24f);
            PowerUiDraw.LabelValue(summary, "PGM_Mgmt_PreviewSummary".Translate(),
                plan == null ? "—" : PowerFormatting.FormatWatts(plan.ProjectedSavingsWatts),
                rect.width * 0.62f, Color.white);
            PowerUiDraw.Tip(summary, "PGM_Mgmt_PreviewTip".Translate());
            return y + 30f + Gap;
        }

        // --- Groups ----------------------------------------------------------

        private float DrawGroups(Rect rect, PowerUiState state, PowerManagementRegistry registry,
            PowerManagementController management, int groupRows, int tick, bool compact)
        {
            float y = rect.y;
            PowerUiDraw.SectionHeader(new Rect(rect.x, y, rect.width, HeaderHeight),
                "PGM_Mgmt_GroupsTitle".Translate(registry == null ? 0 : registry.GroupCount));
            y += HeaderHeight + 2f;

            float fieldWidth = Mathf.Max(90f, rect.width * 0.44f);
            Rect field = new Rect(rect.x, y, fieldWidth, 24f);
            state.ManagementNewGroupName = Widgets.TextField(field, state.ManagementNewGroupName ?? "");
            Rect create = new Rect(field.xMax + 4f, y, 76f, 24f);
            if (Widgets.ButtonText(create, "PGM_Mgmt_Create".Translate()) && registry != null)
            {
                bool created = management != null
                    ? management.CreateGroup(state.ManagementNewGroupName, tick)
                    : registry.AddGroup(state.ManagementNewGroupName);
                if (created)
                {
                    state.ManagementGroupFilter = state.ManagementNewGroupName;
                    state.ManagementNewGroupName = "";
                    state.ManagementPage = 0;
                }
            }
            Rect all = new Rect(create.xMax + 4f, y, Mathf.Max(0f, rect.xMax - create.xMax - 4f), 24f);
            if (Widgets.ButtonText(all, "PGM_Mgmt_Group_All".Translate()))
            {
                state.ManagementGroupFilter = "";
                state.ManagementPage = 0;
            }
            y += 28f;

            if (groupRows == 0)
            {
                PowerUiDraw.DrawText(new Rect(rect.x + 4f, y, rect.width - 8f, 24f),
                    "PGM_Mgmt_EmptyGroups".Translate(), PowerUiDraw.Muted);
                return y + 26f + Gap;
            }

            for (int i = 0; i < groupRows; i++)
            {
                string name = registry.Groups[i];
                Rect row = new Rect(rect.x, y, rect.width, GroupRowHeight);
                if (name == state.ManagementGroupFilter) Widgets.DrawHighlightSelected(row);
                else Widgets.DrawHighlightIfMouseover(row);
                bool narrow = row.width < 520f;
                float buttonsWidth = narrow ? 138f : 228f;
                float nameWidth = Mathf.Max(60f, row.width - buttonsWidth - 6f);
                PowerUiDraw.TextEllipsized(new Rect(row.x + 4f, row.y, nameWidth, GroupRowHeight), name,
                    Color.white);
                if (!narrow)
                {
                    PowerUiDraw.TextEllipsized(new Rect(row.x + nameWidth - 90f, row.y, 88f, GroupRowHeight),
                        "PGM_Mgmt_Group_Members".Translate(registry.GroupMemberCount(name)) + " · " +
                        "PGM_Mgmt_Priority".Translate(registry.GroupPriority(name)),
                        PowerUiDraw.Muted, TextAnchor.MiddleRight);
                }
                if (Widgets.ButtonInvisible(new Rect(row.x, row.y, nameWidth, GroupRowHeight)))
                {
                    state.ManagementGroupFilter = name;
                    state.ManagementPage = 0;
                }

                float bx = row.xMax;
                bx -= 46f;
                if (Widgets.ButtonText(new Rect(bx, row.y + 1f, 44f, 22f),
                    "PGM_Mgmt_Group_Delete".Translate()))
                {
                    bool removed = management != null
                        ? management.DeleteGroup(name, tick) : registry.RemoveGroup(name);
                    if (removed && state.ManagementGroupFilter == name)
                        state.ManagementGroupFilter = "";
                }
                if (narrow)
                {
                    bx -= 46f;
                    if (Widgets.ButtonText(new Rect(bx, row.y + 1f, 44f, 22f),
                        "PGM_Mgmt_Group_Mark".Translate()))
                        SetGroupSheddable(management, registry, name, true, tick);
                    bx -= 46f;
                    if (Widgets.ButtonText(new Rect(bx, row.y + 1f, 44f, 22f),
                        "PGM_Mgmt_Group_Clear".Translate()))
                        SetGroupSheddable(management, registry, name, false, tick);
                }
                else
                {
                    bx -= 46f;
                    int currentPriority = registry.GroupPriority(name);
                    if (Widgets.ButtonText(new Rect(bx, row.y + 1f, 44f, 22f),
                        currentPriority >= PowerManagementRegistry.MaxPriority ? "1" : "+"))
                    {
                        int next = currentPriority >= PowerManagementRegistry.MaxPriority
                            ? PowerManagementRegistry.MinPriority : currentPriority + 1;
                        if (management != null) management.SetGroupPriority(name, next, tick);
                        else { registry.SetGroupPriority(name, next); registry.ApplyGroupPriority(name, next); }
                    }
                    bx -= 56f;
                    if (Widgets.ButtonText(new Rect(bx, row.y + 1f, 54f, 22f),
                        "PGM_Mgmt_Group_Mark".Translate()))
                        SetGroupSheddable(management, registry, name, true, tick);
                    bx -= 56f;
                    if (Widgets.ButtonText(new Rect(bx, row.y + 1f, 54f, 22f),
                        "PGM_Mgmt_Group_Clear".Translate()))
                        SetGroupSheddable(management, registry, name, false, tick);
                }
                PowerUiDraw.Tip(new Rect(row.xMax - buttonsWidth, row.y, buttonsWidth, GroupRowHeight),
                    "PGM_Mgmt_Group_Tip".Translate());
                y += GroupRowHeight;
            }
            return y + Gap;
        }

        private static void SetGroupSheddable(PowerManagementController management,
            PowerManagementRegistry registry, string name, bool value, int tick)
        {
            if (management != null) management.SetGroupSheddable(name, value, tick);
            else if (registry != null) registry.SetGroupSheddable(name, value);
        }

        // --- Building table and action bar -----------------------------------

        private void BuildRows(PowerGridSnapshot snapshot, PowerUiState state,
            PowerManagementRegistry registry, int revision)
        {
            string filter = state.ManagementGroupFilter ?? "";
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
            float y = rect.y;
            PowerUiDraw.SectionHeader(new Rect(rect.x, y, rect.width, HeaderHeight),
                "PGM_Mgmt_BuildingsTitle".Translate(rows.Count));
            y += HeaderHeight + 2f;

            if (pageRows == 0)
            {
                PowerUiDraw.DrawText(new Rect(rect.x + 4f, y, rect.width - 8f, 26f),
                    "PGM_Mgmt_NoBuildings".Translate(), PowerUiDraw.Muted);
                y += 28f;
            }
            else
            {
                for (int i = 0; i < pageRows; i++)
                {
                    BuildingPowerSnapshot row = rows[pageStart + i];
                    PowerManagementRecord record = registry == null ? null : registry.Find(row.Building);
                    string id = PowerManagementRegistry.StableId(row.Building);
                    Rect line = new Rect(rect.x, y, rect.width, RowHeight);
                    if (id != null && id == state.ManagementSelectedId) Widgets.DrawHighlightSelected(line);
                    else Widgets.DrawHighlightIfMouseover(line);

                    float nameWidth = Mathf.Max(80f, line.width * 0.36f);
                    PowerUiDraw.TextEllipsized(new Rect(line.x + 4f, line.y, nameWidth, RowHeight),
                        PowerGridUtilities.BuildingLabel(row.Building), Color.white);
                    float cx = line.x + nameWidth;
                    PowerUiDraw.TextEllipsized(new Rect(cx, line.y, 72f, RowHeight),
                        record == null || record.Group.Length == 0
                            ? "PGM_Mgmt_Group_None".Translate().ToString() : record.Group, PowerUiDraw.Muted);
                    cx += 74f;
                    PowerUiDraw.DrawText(new Rect(cx, line.y, 46f, RowHeight),
                        "PGM_Mgmt_Priority".Translate(record == null
                            ? PowerManagementRegistry.DefaultPriority : record.ShedPriority),
                        PowerUiDraw.Info, TextAnchor.MiddleCenter);
                    cx += 48f;
                    PowerUiDraw.DrawText(new Rect(cx, line.y, Mathf.Max(0f, line.xMax - cx - 96f), RowHeight),
                        PowerFormatting.FormatWatts(row.ConsumptionWatts), Color.white,
                        TextAnchor.MiddleRight);
                    PowerUiDraw.DrawText(new Rect(line.xMax - 92f, line.y, 92f, RowHeight),
                        row.SwitchIsOn == true ? "PGM_Mgmt_Switch_On".Translate()
                                               : "PGM_Mgmt_Switch_Off".Translate(),
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

            y = DrawActionBar(new Rect(rect.x, y, rect.width, actionH), snapshot, state, settings,
                management, registry, tick, compact);
            Rect pager = new Rect(rect.x, y, rect.width, 24f);
            if (Widgets.ButtonText(new Rect(pager.x, pager.y, 60f, 22f), "PGM_Mgmt_Prev".Translate()))
            {
                state.ManagementPage = state.ManagementPage > 0 ? state.ManagementPage - 1 : pageCount - 1;
            }
            if (Widgets.ButtonText(new Rect(pager.x + 64f, pager.y, 60f, 22f), "PGM_Mgmt_Next".Translate()))
            {
                state.ManagementPage = (state.ManagementPage + 1) % Math.Max(1, pageCount);
            }
            PowerUiDraw.DrawText(new Rect(pager.x + 128f, pager.y, Mathf.Max(0f, pager.width - 128f), 22f),
                "PGM_Mgmt_Page".Translate(state.ManagementPage + 1, pageCount), PowerUiDraw.Muted);
            return y + 26f + Gap;
        }

        private float DrawActionBar(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, PowerManagementController management,
            PowerManagementRegistry registry, int tick, bool compact)
        {
            PowerUiDraw.Panel(rect);
            BuildingPowerSnapshot selected = selectedRow;
            if (selected == null || selected.Building.Destroyed || !selected.Building.Spawned ||
                management == null || registry == null)
            {
                PowerUiDraw.WrappedText(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 54f),
                    "PGM_Mgmt_SelectPrompt".Translate(), PowerUiDraw.Muted);
                return rect.yMax;
            }

            Building building = selected.Building;
            PowerManagementRecord record = registry.Find(building);
            PowerControlSupport support = CompatibilityRegistry.EvaluateControl(selected, registry);
            Rect title = new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, 20f);
            PowerUiDraw.TextEllipsized(title,
                "PGM_Mgmt_Selected".Translate(PowerGridUtilities.BuildingLabel(building),
                    CompatibilityRegistry.ControlLabelKey(support).Translate()),
                support == PowerControlSupport.Unsupported ? PowerUiDraw.Warn : Color.white);
            PowerUiDraw.Tip(title, CompatibilityRegistry.ControlTooltipKey(support).Translate());

            bool canUse = settings != null && settings.ManagementEnabled &&
                          support != PowerControlSupport.Unsupported &&
                          support != PowerControlSupport.Pending;
            string[] labels =
            {
                "PGM_Mgmt_Action_On".Translate(), "PGM_Mgmt_Action_Off".Translate(),
                "PGM_Mgmt_Action_Group".Translate(), "–", "+",
                "PGM_Mgmt_Action_Sheddable".Translate(), "PGM_Mgmt_Action_Exclude".Translate(),
                "PGM_Mgmt_Action_Diagnostics".Translate(), "PGM_Mgmt_Action_Locate".Translate()
            };
            Action[] actions =
            {
                delegate { string detail; management.ManualSwitch(snapshot, building, true, settings, tick, out detail); },
                delegate { string detail; management.ManualSwitch(snapshot, building, false, settings, tick, out detail); },
                delegate { OpenGroupMenu(management, building, state, tick); },
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
            float buttonWidth = Mathf.Max(46f, (rect.width - 12f - (perRow - 1) * 4f) / perRow);
            float y = rect.y + 26f;
            for (int i = 0; i < labels.Length; i++)
            {
                int rowIndex = i / perRow;
                int column = i % perRow;
                float x = rect.x + 6f + column * (buttonWidth + 4f);
                float buttonY = y + rowIndex * 25f;
                if (buttonY + 23f > rect.yMax) break;
                Rect button = new Rect(x, buttonY, buttonWidth, 23f);
                if (Widgets.ButtonText(button, labels[i]) && usable[i]) actions[i]();
            }

            if (!compact && buttonRows == 1)
            {
                PowerUiDraw.TextEllipsized(new Rect(rect.x + 8f, y + 27f, rect.width - 16f, 18f),
                    Flags(record), PowerUiDraw.Muted);
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

        private static void OpenGroupMenu(PowerManagementController management, Building building,
            PowerUiState state, int tick)
        {
            PowerManagementRegistry registry = management.Registry;
            var options = new List<FloatMenuOption>(registry.GroupCount + 1)
            {
                new FloatMenuOption("PGM_Mgmt_Group_None".Translate(), delegate
                {
                    management.SetGroup(building, "", tick);
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

        // --- Event history ---------------------------------------------------

        private float DrawEvents(Rect rect, PowerManagementEventLog log, int eventRows, int tick)
        {
            float y = rect.y;
            PowerUiDraw.SectionHeader(new Rect(rect.x, y, rect.width, HeaderHeight),
                "PGM_Mgmt_EventsTitle".Translate(log == null ? 0 : log.Count));
            y += HeaderHeight + 2f;
            if (eventRows == 0)
            {
                PowerUiDraw.DrawText(new Rect(rect.x + 4f, y, rect.width - 8f, 22f),
                    "PGM_Mgmt_EmptyEvents".Translate(), PowerUiDraw.Muted);
                return y + 24f;
            }

            int start = log.Count - eventRows;
            for (int i = 0; i < eventRows; i++)
            {
                PowerManagementEvent entry = log[start + i];
                Rect row = new Rect(rect.x, y, rect.width, EventRowHeight);
                if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.04f));
                PowerUiDraw.TextEllipsized(new Rect(row.x + 4f, row.y, row.width - 8f, EventRowHeight),
                    "PGM_Mgmt_EventRow".Translate(PowerFormatting.FormatDuration(
                            Math.Max(0, tick - entry.Tick)),
                        EventKindLabel(entry.Kind), entry.Label,
                        entry.Success ? "PGM_Mgmt_Event_Ok".Translate()
                                      : "PGM_Mgmt_Event_Failed".Translate()),
                    entry.Success ? PowerUiDraw.Muted : PowerUiDraw.Warn);
                PowerUiDraw.Tip(row, entry.Detail ?? "");
                y += EventRowHeight;
            }
            return y;
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
