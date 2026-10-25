using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Persistent user preferences for monitoring cadence and alert sensitivity.
    /// These settings do not alter RimWorld's power simulation.
    /// </summary>
    public sealed class PowerGridManagerSettings : ModSettings
    {
        public int RefreshIntervalTicks = 120;
        public int SampleIntervalTicks = 2500;
        public int RetentionDays = 7;
        public float HighLoadThreshold = 0.90f;

        /// <summary>Current draw above which a building is flagged as high consumption.</summary>
        public float HighConsumptionWatts = 500f;

        /// <summary>When false, no history samples are recorded (aggregates still refresh).</summary>
        public bool HistoryEnabled = true;

        /// <summary>Retention window expressed in game ticks.</summary>
        public int RetentionTicks => RetentionDays * GenDate.TicksPerDay;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref RefreshIntervalTicks, "refreshIntervalTicks", 120);
            Scribe_Values.Look(ref SampleIntervalTicks, "sampleIntervalTicks", 2500);
            Scribe_Values.Look(ref RetentionDays, "retentionDays", 7);
            Scribe_Values.Look(ref HighLoadThreshold, "highLoadThreshold", 0.90f);
            Scribe_Values.Look(ref HighConsumptionWatts, "highConsumptionWatts", 500f);
            Scribe_Values.Look(ref HistoryEnabled, "historyEnabled", true);
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RefreshIntervalTicks = Clamp(RefreshIntervalTicks, 60, 600);
                SampleIntervalTicks = Clamp(SampleIntervalTicks, 250, 15000);
                RetentionDays = Clamp(RetentionDays, 1, 30);
                HighLoadThreshold = Clamp(HighLoadThreshold, 0.50f, 1.50f);
                HighConsumptionWatts = Clamp(HighConsumptionWatts, 0f, 100000f);
            }
        }

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
