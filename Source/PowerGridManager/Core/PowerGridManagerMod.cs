using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    public sealed class PowerGridManagerMod : Mod
    {
        private readonly PowerGridManagerSettings settings;
        private Vector2 settingsScroll;
        private float settingsContentHeight = 1480f;
        private static float embeddedContentHeight = 1480f;
        private static PowerGridManagerMod instance;

        /// <summary>
        /// The active settings. The setter is internal rather than private so the offline
        /// harness can install its own instance; the game always sets this via the constructor.
        /// </summary>
        public static PowerGridManagerSettings Settings { get; internal set; }

        public PowerGridManagerMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<PowerGridManagerSettings>();
            Settings = settings;
            instance = this;
        }

        /// <summary>
        /// Flushes the settings to disk. Muting an advisory kind or changing dashboard settings
        /// from the in-window Settings tab persists immediately.
        /// </summary>
        public static void Save()
        {
            if (instance != null) instance.WriteSettings();
        }

        public override string SettingsCategory()
        {
            return "PGM_ModName".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            PowerUiDraw.WindowChrome(inRect);
            Rect padded = inRect.ContractedBy(10f);
            DrawSettingsView(padded, settings, ref settingsScroll, ref settingsContentHeight);
            base.DoSettingsWindowContents(inRect);
        }

        public static void DrawEmbeddedSettings(Rect inRect, PowerGridManagerSettings activeSettings, ref Vector2 scroll)
        {
            if (activeSettings == null) return;
            DrawSettingsView(inRect, activeSettings, ref scroll, ref embeddedContentHeight);
        }

        private static void DrawSettingsView(Rect inRect, PowerGridManagerSettings s,
            ref Vector2 scroll, ref float cachedHeight)
        {
            if (inRect.width <= 8f || inRect.height <= 8f || s == null) return;

            float viewW = Mathf.Max(80f, inRect.width - 18f);
            Rect viewRect = new Rect(0f, 0f, viewW, Mathf.Max(inRect.height, cachedHeight));
            Widgets.BeginScrollView(inRect, ref scroll, viewRect);
            try
            {
                float y = 0f;

                // Section 1: Monitoring & Performance.
                float monStart = y;
                float monHeight = 368f;
                PowerUiDraw.Panel(new Rect(0f, monStart, viewW, monHeight));
                Rect monInner = new Rect(12f, monStart + 10f, Mathf.Max(40f, viewW - 24f), monHeight - 20f);
                PowerUiDraw.SectionHeader(new Rect(monInner.x, monInner.y, monInner.width, 22f),
                    "PGM_Settings_Monitoring".Translate(),
                    "PGM_Settings_MonitoringSub".Translate());
                float cy = monInner.y + 28f;

                PowerUiDraw.Label(new Rect(monInner.x, cy, monInner.width, 18f),
                    "PGM_PerformanceMode_Label".Translate(), GameFont.Small, PowerUiDraw.TextSecondary);
                cy += 20f;
                Rect modeRect = new Rect(monInner.x, cy, monInner.width, 28f);
                float modeBtnW = Mathf.Max(20f, (modeRect.width - 12f) / 3f);
                for (int i = 0; i < 3; i++)
                {
                    PerformanceMode mode = (PerformanceMode)i;
                    Rect b = new Rect(modeRect.x + i * (modeBtnW + 6f), modeRect.y, modeBtnW, 28f);
                    bool selected = s.PerformanceMode == mode;
                    if (PowerUiDraw.Button(b, PerformanceModeLabel(mode).Translate(), selected))
                    {
                        s.PerformanceMode = mode;
                        s.ApplyPerformanceMode();
                        Save();
                    }
                    PowerUiDraw.Tip(b, PerformanceModeTip(mode).Translate());
                }
                cy += 36f;

                s.RefreshIntervalTicks = Mathf.RoundToInt(DrawSliderRow(
                    ref cy, monInner.x, monInner.width,
                    "PGM_RefreshInterval_Label".Translate(s.RefreshIntervalTicks),
                    s.RefreshIntervalTicks, 60f, 600f,
                    "PGM_RefreshInterval_Tip", "60", "600"));

                DrawCheckboxRow(ref cy, monInner.x, monInner.width,
                    "PGM_HistoryEnabled_Label".Translate(), ref s.HistoryEnabled,
                    "PGM_HistoryEnabled_Tip".Translate());

                s.SampleIntervalTicks = Mathf.RoundToInt(DrawSliderRow(
                    ref cy, monInner.x, monInner.width,
                    "PGM_SampleInterval_Label".Translate(s.SampleIntervalTicks),
                    s.SampleIntervalTicks, 250f, 15000f,
                    "PGM_SampleInterval_Tip", "250", "15k"));

                s.RetentionDays = Mathf.RoundToInt(DrawSliderRow(
                    ref cy, monInner.x, monInner.width,
                    "PGM_Retention_Label".Translate(s.RetentionDays),
                    s.RetentionDays, 1f, 30f,
                    "PGM_Retention_Tip", "1d", "30d"));

                s.HighLoadThreshold = DrawSliderRow(
                    ref cy, monInner.x, monInner.width,
                    "PGM_HighLoad_Label".Translate((s.HighLoadThreshold * 100f).ToString("F0")),
                    s.HighLoadThreshold, 0.50f, 1.50f,
                    "PGM_HighLoad_Tip", "50%", "150%");

                s.HighConsumptionWatts = DrawSliderRow(
                    ref cy, monInner.x, monInner.width,
                    "PGM_HighConsumption_Label".Translate(s.HighConsumptionWatts.ToString("F0")),
                    s.HighConsumptionWatts, 0f, 5000f,
                    "PGM_HighConsumption_Tip", "0 W", "5 kW");

                y = monStart + monHeight + PowerUiDraw.SectionGap;

                // Section 2: Diagnostics & Alerts.
                float diagStart = y;
                float diagHeight = 352f;
                PowerUiDraw.Panel(new Rect(0f, diagStart, viewW, diagHeight));
                Rect diagInner = new Rect(12f, diagStart + 10f, Mathf.Max(40f, viewW - 24f), diagHeight - 20f);
                PowerUiDraw.SectionHeader(new Rect(diagInner.x, diagInner.y, diagInner.width, 22f),
                    "PGM_Settings_Diagnostics".Translate(),
                    "PGM_Settings_DiagnosticsSub".Translate());
                cy = diagInner.y + 28f;

                DrawCheckboxRow(ref cy, diagInner.x, diagInner.width,
                    "PGM_DiagnosticsEnabled_Label".Translate(), ref s.DiagnosticsEnabled,
                    "PGM_DiagnosticsEnabled_Tip".Translate());
                DrawCheckboxRow(ref cy, diagInner.x, diagInner.width,
                    "PGM_ShowInfoDiagnostics_Label".Translate(), ref s.ShowInfoDiagnostics,
                    "PGM_ShowInfoDiagnostics_Tip".Translate());
                DrawCheckboxRow(ref cy, diagInner.x, diagInner.width,
                    "PGM_ShowPowerMessages_Label".Translate(), ref s.ShowPowerMessages,
                    "PGM_ShowPowerMessages_Tip".Translate());
                cy += 4f;

                s.DeficitAlertWatts = DrawSliderRow(
                    ref cy, diagInner.x, diagInner.width,
                    "PGM_DeficitAlert_Label".Translate(s.DeficitAlertWatts.ToString("F0")),
                    s.DeficitAlertWatts, 1f, 2000f,
                    "PGM_DeficitAlert_Tip", "1 W", "2 kW");

                s.LowBatteryWarningFraction = DrawSliderRow(
                    ref cy, diagInner.x, diagInner.width,
                    "PGM_LowBattery_Label".Translate((s.LowBatteryWarningFraction * 100f).ToString("F0")),
                    s.LowBatteryWarningFraction, 0.01f, 0.90f,
                    "PGM_LowBattery_Tip", "1%", "90%");

                s.PersistentDeficitTicks = Mathf.RoundToInt(DrawSliderRow(
                    ref cy, diagInner.x, diagInner.width,
                    "PGM_PersistentDeficit_Label".Translate(PowerFormatting.FormatDuration(s.PersistentDeficitTicks)),
                    s.PersistentDeficitTicks, 60f, PowerTime.TicksPerDay * 7f,
                    "PGM_PersistentDeficit_Tip", null, null));

                s.GeneratorUnderOutputTicks = Mathf.RoundToInt(DrawSliderRow(
                    ref cy, diagInner.x, diagInner.width,
                    "PGM_GeneratorIdle_Label".Translate(PowerFormatting.FormatDuration(s.GeneratorUnderOutputTicks)),
                    s.GeneratorUnderOutputTicks, PowerTime.TicksPerHour, PowerTime.TicksPerDay * 7f,
                    "PGM_GeneratorIdle_Tip", null, null));

                s.AlertCooldownTicks = Mathf.RoundToInt(DrawSliderRow(
                    ref cy, diagInner.x, diagInner.width,
                    "PGM_AlertCooldown_Label".Translate(PowerFormatting.FormatDuration(s.AlertCooldownTicks)),
                    s.AlertCooldownTicks, 0f, PowerTime.TicksPerDay * 7f,
                    "PGM_AlertCooldown_Tip", null, null));

                y = diagStart + diagHeight + PowerUiDraw.SectionGap;

                // Section 3: Endurance Forecasting.
                float fcStart = y;
                float fcHeight = 112f;
                PowerUiDraw.Panel(new Rect(0f, fcStart, viewW, fcHeight));
                Rect fcInner = new Rect(12f, fcStart + 10f, Mathf.Max(40f, viewW - 24f), fcHeight - 20f);
                PowerUiDraw.SectionHeader(new Rect(fcInner.x, fcInner.y, fcInner.width, 22f),
                    "PGM_Settings_Forecast".Translate());
                cy = fcInner.y + 28f;

                DrawCheckboxRow(ref cy, fcInner.x, fcInner.width,
                    "PGM_SelfDischarge_Label".Translate(), ref s.ForecastIncludeSelfDischarge,
                    "PGM_SelfDischarge_Tip".Translate());
                PowerUiDraw.Label(new Rect(fcInner.x, cy, fcInner.width, 36f),
                    "PGM_Settings_ForecastNote".Translate(),
                    GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.UpperLeft, true);

                y = fcStart + fcHeight + PowerUiDraw.SectionGap;

                // Section 4: Optional Automation & Load Shedding.
                float mgmtStart = y;
                float mgmtHeight = 420f;
                PowerUiDraw.Panel(new Rect(0f, mgmtStart, viewW, mgmtHeight));
                Rect mgmtInner = new Rect(12f, mgmtStart + 10f, Mathf.Max(40f, viewW - 24f), mgmtHeight - 20f);
                PowerUiDraw.SectionHeader(new Rect(mgmtInner.x, mgmtInner.y, mgmtInner.width, 22f),
                    "PGM_Settings_Management".Translate());
                cy = mgmtInner.y + 28f;

                DrawCheckboxRow(ref cy, mgmtInner.x, mgmtInner.width,
                    "PGM_Mgmt_Master_Label".Translate(), ref s.ManagementEnabled,
                    "PGM_Mgmt_Master_Tip".Translate());
                DrawCheckboxRow(ref cy, mgmtInner.x, mgmtInner.width,
                    "PGM_Mgmt_Automation_Label".Translate(), ref s.LoadSheddingEnabled,
                    "PGM_Mgmt_Automation_Tip".Translate());
                DrawCheckboxRow(ref cy, mgmtInner.x, mgmtInner.width,
                    "PGM_Mgmt_RequireDeficit_Label".Translate(), ref s.LoadShedRequireDeficit,
                    "PGM_Mgmt_RequireDeficit_Tip".Translate());
                cy += 4f;

                s.LoadShedLowThreshold = DrawSliderRow(
                    ref cy, mgmtInner.x, mgmtInner.width,
                    "PGM_Mgmt_LowSetting_Label".Translate(PowerFormatting.FormatCharge(s.LoadShedLowThreshold)),
                    s.LoadShedLowThreshold, 0.05f, 0.60f,
                    "PGM_Mgmt_LowSetting_Tip", "5%", "60%");

                s.LoadShedRestoreThreshold = DrawSliderRow(
                    ref cy, mgmtInner.x, mgmtInner.width,
                    "PGM_Mgmt_RestoreSetting_Label".Translate(PowerFormatting.FormatCharge(s.LoadShedRestoreThreshold)),
                    s.LoadShedRestoreThreshold, 0.20f, 1f,
                    "PGM_Mgmt_RestoreSetting_Tip", "20%", "100%");

                s.LoadShedStableTicks = Mathf.RoundToInt(DrawSliderRow(
                    ref cy, mgmtInner.x, mgmtInner.width,
                    "PGM_Mgmt_Stable_Label".Translate(PowerFormatting.FormatDuration(s.LoadShedStableTicks)),
                    s.LoadShedStableTicks, 0f, PowerTime.TicksPerDay,
                    "PGM_Mgmt_Stable_Tip", null, null));

                s.LoadShedCooldownTicks = Mathf.RoundToInt(DrawSliderRow(
                    ref cy, mgmtInner.x, mgmtInner.width,
                    "PGM_Mgmt_Cooldown_Label".Translate(PowerFormatting.FormatDuration(s.LoadShedCooldownTicks)),
                    s.LoadShedCooldownTicks, 0f, PowerTime.TicksPerDay,
                    "PGM_Mgmt_Cooldown_Tip", null, null));

                s.LoadShedMaxActionsPerPass = Mathf.RoundToInt(DrawSliderRow(
                    ref cy, mgmtInner.x, mgmtInner.width,
                    "PGM_Mgmt_MaxActions_Label".Translate(s.LoadShedMaxActionsPerPass),
                    s.LoadShedMaxActionsPerPass, 1f, 16f,
                    "PGM_Mgmt_MaxActions_Tip", null, null));

                Rect recoveryRect = new Rect(mgmtInner.x, cy + 2f, Mathf.Min(320f, mgmtInner.width), 28f);
                if (PowerUiDraw.Button(recoveryRect,
                    "PGM_Mgmt_RecoverySetting_Label".Translate(RecoveryModeLabel(s.LoadShedRecovery))))
                {
                    OpenRecoveryModeMenu(s);
                }
                PowerUiDraw.Tip(recoveryRect, "PGM_Mgmt_RecoverySetting_Tip".Translate());
                cy += 34f;

                PowerUiDraw.Label(new Rect(mgmtInner.x, cy, mgmtInner.width, 40f),
                    "PGM_Mgmt_SettingsNote".Translate(),
                    GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.UpperLeft, true);
                s.NormalizeManagement();

                y = mgmtStart + mgmtHeight + PowerUiDraw.SectionGap;

                // Section 5: Muted Advisory Kinds (2-column grid on wide screens, 1-column on narrow).
                int kindCount = System.Enum.GetValues(typeof(PowerDiagnosticKind)).Length;
                int cols = viewW >= 540f ? 2 : 1;
                int rows = (kindCount + cols - 1) / cols;
                float muteHeight = 46f + rows * 28f;
                PowerUiDraw.Panel(new Rect(0f, y, viewW, muteHeight));
                Rect muteInner = new Rect(12f, y + 10f, Mathf.Max(40f, viewW - 24f), muteHeight - 20f);
                PowerUiDraw.SectionHeader(new Rect(muteInner.x, muteInner.y, muteInner.width, 22f),
                    "PGM_Settings_Muted".Translate(),
                    "PGM_Settings_MutedSub".Translate());

                float colW = cols == 2 ? (muteInner.width - 16f) * 0.5f : muteInner.width;
                for (int i = 0; i < kindCount; i++)
                {
                    PowerDiagnosticKind kind = (PowerDiagnosticKind)i;
                    int r = i / cols;
                    int c = i % cols;
                    float rx = muteInner.x + c * (colW + 16f);
                    float ry = muteInner.y + 28f + r * 28f;
                    bool muted = s.IsMuted(kind);
                    bool updated = muted;
                    DrawCheckboxDirect(new Rect(rx, ry, colW, 25f),
                        PowerGridUtilities.DiagnosticKindLabelKey(kind).Translate(),
                        ref updated,
                        PowerGridUtilities.DiagnosticKindTipKey(kind).Translate());
                    if (updated != muted)
                    {
                        s.SetMuted(kind, updated);
                        Save();
                    }
                }

                y += muteHeight + 16f;
                cachedHeight = y;
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private static void DrawCheckboxRow(ref float cy, float x, float width,
            string label, ref bool value, string tooltip)
        {
            Rect row = new Rect(x, cy, width, 25f);
            bool before = value;
            DrawCheckboxDirect(row, label, ref value, tooltip);
            if (value != before)
                Save();
            cy += 26f;
        }

        private static void DrawCheckboxDirect(Rect row, string label, ref bool value, string tooltip)
        {
            if (Mouse.IsOver(row))
                Widgets.DrawBoxSolid(row, PowerUiDraw.BgHover);

            Color prevColor = GUI.color;
            GameFont prevFont = Text.Font;
            try
            {
                Text.Font = GameFont.Small;
                GUI.color = PowerUiDraw.TextPrimary;
                Widgets.CheckboxLabeled(new Rect(row.x + 4f, row.y, Mathf.Max(0f, row.width - 8f), row.height),
                    label, ref value);
            }
            finally
            {
                GUI.color = prevColor;
                Text.Font = prevFont;
            }

            if (!string.IsNullOrEmpty(tooltip))
                PowerUiDraw.Tip(row, tooltip);
        }

        private static float DrawSliderRow(ref float cy, float x, float width,
            string label, float value, float min, float max,
            string tooltipKey, string leftLabel, string rightLabel)
        {
            Rect labelRect = new Rect(x, cy, width, 18f);
            PowerUiDraw.Label(labelRect, label, GameFont.Small, PowerUiDraw.TextPrimary, TextAnchor.MiddleLeft);
            Rect sliderRect = new Rect(x, cy + 18f, width, 24f);
            string tip = tooltipKey.Translate();
            PowerUiDraw.Tip(labelRect, tip);
            TooltipHandler.TipRegion(sliderRect, new TipSignal(tip));
            float next = Widgets.HorizontalSlider(sliderRect, value, min, max, true, null, leftLabel, rightLabel);
            cy += 44f;
            return next;
        }

        private static void OpenRecoveryModeMenu(PowerGridManagerSettings settings)
        {
            var options = new List<FloatMenuOption>(3);
            LoadSheddingRecoveryMode[] modes =
            {
                LoadSheddingRecoveryMode.Staged, LoadSheddingRecoveryMode.All,
                LoadSheddingRecoveryMode.Manual
            };
            for (int i = 0; i < modes.Length; i++)
            {
                LoadSheddingRecoveryMode mode = modes[i];
                options.Add(new FloatMenuOption(RecoveryModeLabel(mode), delegate
                {
                    settings.LoadShedRecovery = mode;
                    settings.NormalizeManagement();
                    Save();
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string RecoveryModeLabel(LoadSheddingRecoveryMode mode)
        {
            switch (mode)
            {
                case LoadSheddingRecoveryMode.All: return "PGM_Mgmt_Recovery_All".Translate();
                case LoadSheddingRecoveryMode.Manual: return "PGM_Mgmt_Recovery_Manual".Translate();
                default: return "PGM_Mgmt_Recovery_Staged".Translate();
            }
        }

        private static string PerformanceModeLabel(PerformanceMode mode)
        {
            switch (mode)
            {
                case PerformanceMode.Lightweight: return "PGM_PerformanceMode_Lightweight";
                case PerformanceMode.Detailed: return "PGM_PerformanceMode_Detailed";
                default: return "PGM_PerformanceMode_Balanced";
            }
        }

        private static string PerformanceModeTip(PerformanceMode mode)
        {
            switch (mode)
            {
                case PerformanceMode.Lightweight:
                    return "PGM_PerformanceMode_LightweightTip";
                case PerformanceMode.Detailed:
                    return "PGM_PerformanceMode_DetailedTip";
                default:
                    return "PGM_PerformanceMode_BalancedTip";
            }
        }
    }
}
