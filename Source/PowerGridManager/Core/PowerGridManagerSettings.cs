using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Persistent user preferences for monitoring cadence and alert sensitivity.
    /// These settings do not alter RimWorld's power simulation.
    /// </summary>
    public sealed class PowerGridManagerSettings : ModSettings
    {
        /// <summary>Default history cadence, quoted by UI text that explains how estimates are fed.</summary>
        public const int DefaultSampleIntervalTicks = 2500;

        public int RefreshIntervalTicks = 120;
        public int SampleIntervalTicks = 2500;
        public int RetentionDays = 7;
        public float HighLoadThreshold = 0.90f;

        /// <summary>Current draw above which a building is flagged as high consumption.</summary>
        public float HighConsumptionWatts = 500f;

        /// <summary>When false, no history samples are recorded (aggregates still refresh).</summary>
        public bool HistoryEnabled = true;

        // --- Diagnostics ------------------------------------------------------

        /// <summary>Master switch for the diagnostics engine. Monitoring keeps working without it.</summary>
        public bool DiagnosticsEnabled = true;

        /// <summary>Smallest deficit worth reporting, in watts.</summary>
        public float DeficitAlertWatts = 50f;

        /// <summary>Battery charge at or below which a draining network becomes a critical advisory.</summary>
        public float LowBatteryWarningFraction = 0.25f;

        /// <summary>How long a deficit must persist before it is reported as persistent.</summary>
        public int PersistentDeficitTicks = PowerTime.TicksPerHour;

        /// <summary>
        /// How long a generator must stay far below its configured output before it is reported.
        /// Long by default: solar and wind generators idle legitimately, and a short delay would
        /// raise an advisory every single night.
        /// </summary>
        public int GeneratorUnderOutputTicks = PowerTime.TicksPerDay;

        /// <summary>Whether informational advisories are listed at all.</summary>
        public bool ShowInfoDiagnostics = true;

        /// <summary>Whether new Warning/Critical advisories also raise the game's on-screen message.</summary>
        public bool ShowPowerMessages = true;

        /// <summary>Minimum gap between two on-screen messages, and the window in which a returning
        /// condition counts as the same episode rather than a new alert.</summary>
        public int AlertCooldownTicks = PowerTime.TicksPerDay;

        /// <summary>Bitmask of muted <see cref="PowerDiagnosticKind"/> values.</summary>
        public int MutedDiagnosticKinds;

        // --- Forecast ---------------------------------------------------------

        /// <summary>
        /// Subtract the game's verified 5 W per battery self-discharge from the balance used by the
        /// endurance forecast. On by default because the trader balance excludes it.
        /// </summary>
        public bool ForecastIncludeSelfDischarge = true;

        // --- Optional management (Phase 7) ------------------------------------
        // Both switches are OFF by default. Monitoring, history, diagnostics and the
        // forecast never read them, so turning management off cannot affect readings.

        /// <summary>Master switch for the optional control UI. Off by default.</summary>
        public bool ManagementEnabled = false;

        /// <summary>Automatic load shedding. Off by default and requires ManagementEnabled.</summary>
        public bool LoadSheddingEnabled = false;

        /// <summary>Usable battery charge at or below which shedding is considered.</summary>
        public float LoadShedLowThreshold = 0.25f;

        /// <summary>Charge at or above which shedding is reversed. Must exceed the low threshold.</summary>
        public float LoadShedRestoreThreshold = 0.55f;

        /// <summary>How long a trigger condition must hold before any switch is changed.</summary>
        public int LoadShedStableTicks = PowerTime.TicksPerHour / 2;

        /// <summary>Minimum gap between two batches of automatic switch actions.</summary>
        public int LoadShedCooldownTicks = PowerTime.TicksPerHour;

        /// <summary>Maximum number of switches changed in one evaluation pass.</summary>
        public int LoadShedMaxActionsPerPass = 4;

        /// <summary>How buildings switched off by the policy are brought back.</summary>
        public LoadSheddingRecoveryMode LoadShedRecovery = LoadSheddingRecoveryMode.Staged;

        /// <summary>Only shed while the map also reports a generation shortfall.</summary>
        public bool LoadShedRequireDeficit = true;

        /// <summary>Retention window expressed in game ticks.</summary>
        public int RetentionTicks => RetentionDays * PowerTime.TicksPerDay;

        // --- Muting -----------------------------------------------------------

        public bool IsMuted(PowerDiagnosticKind kind)
        {
            int bit = 1 << (int)kind;
            return (MutedDiagnosticKinds & bit) != 0;
        }

        public void SetMuted(PowerDiagnosticKind kind, bool muted)
        {
            int bit = 1 << (int)kind;
            if (muted) MutedDiagnosticKinds |= bit;
            else MutedDiagnosticKinds &= ~bit;
        }

        public int MutedKindCount
        {
            get
            {
                int count = 0;
                int mask = MutedDiagnosticKinds;
                while (mask != 0)
                {
                    count += mask & 1;
                    mask >>= 1;
                }
                return count;
            }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref RefreshIntervalTicks, "refreshIntervalTicks", 120);
            Scribe_Values.Look(ref SampleIntervalTicks, "sampleIntervalTicks", 2500);
            Scribe_Values.Look(ref RetentionDays, "retentionDays", 7);
            Scribe_Values.Look(ref HighLoadThreshold, "highLoadThreshold", 0.90f);
            Scribe_Values.Look(ref HighConsumptionWatts, "highConsumptionWatts", 500f);
            Scribe_Values.Look(ref HistoryEnabled, "historyEnabled", true);

            Scribe_Values.Look(ref DiagnosticsEnabled, "diagnosticsEnabled", true);
            Scribe_Values.Look(ref DeficitAlertWatts, "deficitAlertWatts", 50f);
            Scribe_Values.Look(ref LowBatteryWarningFraction, "lowBatteryWarningFraction", 0.25f);
            Scribe_Values.Look(ref PersistentDeficitTicks, "persistentDeficitTicks", PowerTime.TicksPerHour);
            Scribe_Values.Look(ref GeneratorUnderOutputTicks, "generatorUnderOutputTicks", PowerTime.TicksPerDay);
            Scribe_Values.Look(ref ShowInfoDiagnostics, "showInfoDiagnostics", true);
            Scribe_Values.Look(ref ShowPowerMessages, "showPowerMessages", true);
            Scribe_Values.Look(ref AlertCooldownTicks, "alertCooldownTicks", PowerTime.TicksPerDay);
            Scribe_Values.Look(ref MutedDiagnosticKinds, "mutedDiagnosticKinds", 0);
            Scribe_Values.Look(ref ForecastIncludeSelfDischarge, "forecastIncludeSelfDischarge", true);

            Scribe_Values.Look(ref ManagementEnabled, "managementEnabled", false);
            Scribe_Values.Look(ref LoadSheddingEnabled, "loadSheddingEnabled", false);
            Scribe_Values.Look(ref LoadShedLowThreshold, "loadShedLowThreshold", 0.25f);
            Scribe_Values.Look(ref LoadShedRestoreThreshold, "loadShedRestoreThreshold", 0.55f);
            Scribe_Values.Look(ref LoadShedStableTicks, "loadShedStableTicks", PowerTime.TicksPerHour / 2);
            Scribe_Values.Look(ref LoadShedCooldownTicks, "loadShedCooldownTicks", PowerTime.TicksPerHour);
            Scribe_Values.Look(ref LoadShedMaxActionsPerPass, "loadShedMaxActionsPerPass", 4);
            Scribe_Values.Look(ref LoadShedRecovery, "loadShedRecovery", LoadSheddingRecoveryMode.Staged);
            Scribe_Values.Look(ref LoadShedRequireDeficit, "loadShedRequireDeficit", true);

            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RefreshIntervalTicks = Clamp(RefreshIntervalTicks, 60, 600);
                SampleIntervalTicks = Clamp(SampleIntervalTicks, 250, 15000);
                RetentionDays = Clamp(RetentionDays, 1, 30);
                HighLoadThreshold = Clamp(HighLoadThreshold, 0.50f, 1.50f);
                HighConsumptionWatts = Clamp(HighConsumptionWatts, 0f, 100000f);

                DeficitAlertWatts = Clamp(DeficitAlertWatts, 1f, 100000f);
                LowBatteryWarningFraction = Clamp(LowBatteryWarningFraction, 0.01f, 0.99f);
                PersistentDeficitTicks = Clamp(PersistentDeficitTicks, 60, PowerTime.TicksPerDay * 7);
                GeneratorUnderOutputTicks = Clamp(GeneratorUnderOutputTicks, PowerTime.TicksPerHour,
                    PowerTime.TicksPerDay * 7);
                AlertCooldownTicks = Clamp(AlertCooldownTicks, 0, PowerTime.TicksPerDay * 7);
                // Unknown bits (written by a newer version, or by hand) are dropped rather than
                // being allowed to mute kinds this build does not define.
                MutedDiagnosticKinds = Clamp(MutedDiagnosticKinds, 0, (1 << KnownDiagnosticKindCount) - 1);

                NormalizeManagement();
            }
        }

        /// <summary>
        /// Keeps the optional-management numbers inside ranges the policy engine can reason
        /// about. Called after loading settings and after the settings UI changes a slider, so a
        /// hand-edited save cannot invert the hysteresis band or ask for an unbounded batch.
        /// </summary>
        public void NormalizeManagement()
        {
            LoadShedLowThreshold = Clamp(LoadShedLowThreshold, 0.01f, 0.90f);
            LoadShedRestoreThreshold = Clamp(LoadShedRestoreThreshold, LoadShedLowThreshold + 0.05f, 1f);
            LoadShedStableTicks = Clamp(LoadShedStableTicks, 0, PowerTime.TicksPerDay * 7);
            LoadShedCooldownTicks = Clamp(LoadShedCooldownTicks, 0, PowerTime.TicksPerDay * 7);
            LoadShedMaxActionsPerPass = Clamp(LoadShedMaxActionsPerPass, 1, 32);
            if (!System.Enum.IsDefined(typeof(LoadSheddingRecoveryMode), LoadShedRecovery))
                LoadShedRecovery = LoadSheddingRecoveryMode.Staged;
        }

        /// <summary>Number of advisory kinds this build defines; bounds the persisted mute mask.</summary>
        private static readonly int KnownDiagnosticKindCount = System.Enum.GetValues(typeof(PowerDiagnosticKind)).Length;

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : value > max ? max : value;
        }
    }
}
