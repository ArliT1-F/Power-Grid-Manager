using UnityEngine;
using Verse;

namespace PowerGridManager
{
    public sealed class PowerGridManagerMod : Mod
    {
        private readonly PowerGridManagerSettings settings;
        private Vector2 settingsScroll;
        private float settingsContentHeight = 1920f;
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
        /// Flushes the settings to disk. Muting an advisory kind should survive a restart, so the
        /// diagnostics panel calls this instead of waiting for the settings window to close.
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
            // Monitoring, diagnostics, forecast, management and the ten mute toggles do not fit in
            // one screen at any UI scale, so the whole panel scrolls rather than clipping controls.
            // Listing_Standard only knows its own height after it has drawn, so we start with a
            // generous floor and update the scroll height from CurHeight on each pass.
            Rect viewRect = new Rect(0f, 0f, inRect.width - 18f, settingsContentHeight);
            Widgets.BeginScrollView(inRect, ref settingsScroll, viewRect);
            try
            {
                Listing_Standard listing = new Listing_Standard();
                listing.Begin(viewRect);

                Section(listing, "PGM_Settings_Monitoring");
                listing.Label("PGM_RefreshInterval_Label".Translate(settings.RefreshIntervalTicks));
                settings.RefreshIntervalTicks = Mathf.RoundToInt(Slider(listing, settings.RefreshIntervalTicks, 60f, 600f,
                    "PGM_RefreshInterval_Tip", null, null));
                listing.Gap(8f);

                listing.CheckboxLabeled("PGM_HistoryEnabled_Label".Translate(), ref settings.HistoryEnabled,
                    "PGM_HistoryEnabled_Tip".Translate());

                listing.Label("PGM_SampleInterval_Label".Translate(settings.SampleIntervalTicks));
                settings.SampleIntervalTicks = Mathf.RoundToInt(Slider(listing, settings.SampleIntervalTicks, 250f, 15000f,
                    "PGM_SampleInterval_Tip", null, null));
                listing.Gap(8f);

                listing.Label("PGM_Retention_Label".Translate(settings.RetentionDays));
                settings.RetentionDays = Mathf.RoundToInt(Slider(listing, settings.RetentionDays, 1f, 30f,
                    "PGM_Retention_Tip", "1", "30"));
                listing.Gap(8f);

                listing.Label("PGM_HighLoad_Label".Translate((settings.HighLoadThreshold * 100f).ToString("F0")));
                settings.HighLoadThreshold = Slider(listing, settings.HighLoadThreshold, 0.50f, 1.50f,
                    "PGM_HighLoad_Tip", "50%", "150%");
                listing.Gap(8f);

                listing.Label("PGM_HighConsumption_Label".Translate(settings.HighConsumptionWatts.ToString("F0")));
                settings.HighConsumptionWatts = Slider(listing, settings.HighConsumptionWatts, 0f, 5000f,
                    "PGM_HighConsumption_Tip", "0 W", "5 kW");
                listing.Gap(12f);

                Section(listing, "PGM_Settings_Diagnostics");
                listing.CheckboxLabeled("PGM_DiagnosticsEnabled_Label".Translate(), ref settings.DiagnosticsEnabled,
                    "PGM_DiagnosticsEnabled_Tip".Translate());
                listing.CheckboxLabeled("PGM_ShowInfoDiagnostics_Label".Translate(), ref settings.ShowInfoDiagnostics,
                    "PGM_ShowInfoDiagnostics_Tip".Translate());
                listing.CheckboxLabeled("PGM_ShowPowerMessages_Label".Translate(), ref settings.ShowPowerMessages,
                    "PGM_ShowPowerMessages_Tip".Translate());
                listing.Gap(6f);

                listing.Label("PGM_DeficitAlert_Label".Translate(settings.DeficitAlertWatts.ToString("F0")));
                settings.DeficitAlertWatts = Slider(listing, settings.DeficitAlertWatts, 1f, 2000f,
                    "PGM_DeficitAlert_Tip", "1 W", "2 kW");
                listing.Gap(8f);

                listing.Label("PGM_LowBattery_Label".Translate((settings.LowBatteryWarningFraction * 100f).ToString("F0")));
                settings.LowBatteryWarningFraction = Slider(listing, settings.LowBatteryWarningFraction, 0.01f, 0.90f,
                    "PGM_LowBattery_Tip", "1%", "90%");
                listing.Gap(8f);

                listing.Label("PGM_PersistentDeficit_Label".Translate(
                    PowerFormatting.FormatDuration(settings.PersistentDeficitTicks)));
                settings.PersistentDeficitTicks = Mathf.RoundToInt(Slider(listing, settings.PersistentDeficitTicks,
                    60f, PowerTime.TicksPerDay * 7f, "PGM_PersistentDeficit_Tip", null, null));
                listing.Gap(8f);

                listing.Label("PGM_GeneratorIdle_Label".Translate(
                    PowerFormatting.FormatDuration(settings.GeneratorUnderOutputTicks)));
                settings.GeneratorUnderOutputTicks = Mathf.RoundToInt(Slider(listing, settings.GeneratorUnderOutputTicks,
                    PowerTime.TicksPerHour, PowerTime.TicksPerDay * 7f, "PGM_GeneratorIdle_Tip", null, null));
                listing.Gap(8f);

                listing.Label("PGM_AlertCooldown_Label".Translate(
                    PowerFormatting.FormatDuration(settings.AlertCooldownTicks)));
                settings.AlertCooldownTicks = Mathf.RoundToInt(Slider(listing, settings.AlertCooldownTicks,
                    0f, PowerTime.TicksPerDay * 7f, "PGM_AlertCooldown_Tip", null, null));
                listing.Gap(12f);

                Section(listing, "PGM_Settings_Forecast");
                listing.CheckboxLabeled("PGM_SelfDischarge_Label".Translate(), ref settings.ForecastIncludeSelfDischarge,
                    "PGM_SelfDischarge_Tip".Translate());
                listing.Gap(6f);
                listing.Label("PGM_Settings_ForecastNote".Translate());
                listing.Gap(12f);

                Section(listing, "PGM_Settings_Management");
                listing.CheckboxLabeled("PGM_Mgmt_Master_Label".Translate(), ref settings.ManagementEnabled,
                    "PGM_Mgmt_Master_Tip".Translate());
                listing.CheckboxLabeled("PGM_Mgmt_Automation_Label".Translate(),
                    ref settings.LoadSheddingEnabled, "PGM_Mgmt_Automation_Tip".Translate());
                listing.CheckboxLabeled("PGM_Mgmt_RequireDeficit_Label".Translate(),
                    ref settings.LoadShedRequireDeficit, "PGM_Mgmt_RequireDeficit_Tip".Translate());
                listing.Gap(6f);

                listing.Label("PGM_Mgmt_LowSetting_Label".Translate(
                    PowerFormatting.FormatCharge(settings.LoadShedLowThreshold)));
                settings.LoadShedLowThreshold = Slider(listing, settings.LoadShedLowThreshold, 0.05f, 0.60f,
                    "PGM_Mgmt_LowSetting_Tip", "5%", "60%");
                listing.Gap(8f);

                listing.Label("PGM_Mgmt_RestoreSetting_Label".Translate(
                    PowerFormatting.FormatCharge(settings.LoadShedRestoreThreshold)));
                settings.LoadShedRestoreThreshold = Slider(listing, settings.LoadShedRestoreThreshold,
                    0.20f, 1f, "PGM_Mgmt_RestoreSetting_Tip", "20%", "100%");
                listing.Gap(8f);

                listing.Label("PGM_Mgmt_Stable_Label".Translate(
                    PowerFormatting.FormatDuration(settings.LoadShedStableTicks)));
                settings.LoadShedStableTicks = Mathf.RoundToInt(Slider(listing, settings.LoadShedStableTicks,
                    0f, PowerTime.TicksPerDay, "PGM_Mgmt_Stable_Tip", null, null));
                listing.Gap(8f);

                listing.Label("PGM_Mgmt_Cooldown_Label".Translate(
                    PowerFormatting.FormatDuration(settings.LoadShedCooldownTicks)));
                settings.LoadShedCooldownTicks = Mathf.RoundToInt(Slider(listing,
                    settings.LoadShedCooldownTicks, 0f, PowerTime.TicksPerDay,
                    "PGM_Mgmt_Cooldown_Tip", null, null));
                listing.Gap(8f);

                listing.Label("PGM_Mgmt_MaxActions_Label".Translate(settings.LoadShedMaxActionsPerPass));
                settings.LoadShedMaxActionsPerPass = Mathf.RoundToInt(Slider(listing,
                    settings.LoadShedMaxActionsPerPass, 1f, 16f, "PGM_Mgmt_MaxActions_Tip", null, null));
                listing.Gap(8f);

                Rect recoveryRect = listing.GetRect(26f);
                if (Widgets.ButtonText(recoveryRect, "PGM_Mgmt_RecoverySetting_Label".Translate(
                        RecoveryModeLabel(settings.LoadShedRecovery))))
                {
                    OpenRecoveryModeMenu(settings);
                }
                PowerUiDraw.Tip(recoveryRect, "PGM_Mgmt_RecoverySetting_Tip".Translate());
                listing.Gap(8f);
                listing.Label("PGM_Mgmt_SettingsNote".Translate());
                settings.NormalizeManagement();
                listing.Gap(12f);

                Section(listing, "PGM_Settings_Muted");
                DrawMuteToggles(listing);

                settingsContentHeight = Mathf.Max(1920f, listing.CurHeight + 24f);
                listing.End();
            }
            finally
            {
                Widgets.EndScrollView();
            }

            base.DoSettingsWindowContents(inRect);
        }

        private static void OpenRecoveryModeMenu(PowerGridManagerSettings settings)
        {
            var options = new System.Collections.Generic.List<FloatMenuOption>(3);
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

        /// <summary>One checkbox per advisory kind, so muting is discoverable rather than a bitmask.</summary>
        private void DrawMuteToggles(Listing_Standard listing)
        {
            int total = System.Enum.GetValues(typeof(PowerDiagnosticKind)).Length;
            for (int i = 0; i < total; i++)
            {
                PowerDiagnosticKind kind = (PowerDiagnosticKind)i;
                bool muted = settings.IsMuted(kind);
                bool updated = muted;
                listing.CheckboxLabeled(PowerGridUtilities.DiagnosticKindLabelKey(kind).Translate(), ref updated,
                    PowerGridUtilities.DiagnosticKindTipKey(kind).Translate());
                if (updated != muted) settings.SetMuted(kind, updated);
            }
        }

        private static void Section(Listing_Standard listing, string key)
        {
            listing.Gap(2f);
            listing.Label(key.Translate());
            listing.Gap(2f);
        }

        /// <summary>
        /// Slider whose explanation is a real tooltip. Vanilla's slider "label" argument is
        /// drawn as a caption under the rail, so a sentence there would render as clutter.
        /// </summary>
        private static float Slider(Listing_Standard listing, float value, float min, float max,
            string tooltipKey, string leftLabel, string rightLabel)
        {
            Rect rect = listing.GetRect(26f);
            TooltipHandler.TipRegion(rect, new TipSignal(tooltipKey.Translate()));
            return Widgets.HorizontalSlider(rect, value, min, max, true, null, leftLabel, rightLabel);
        }
    }
}
