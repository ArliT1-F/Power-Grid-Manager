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

        public override void ExposeData()
        {
            Scribe_Values.Look(ref RefreshIntervalTicks, "refreshIntervalTicks", 120);
            Scribe_Values.Look(ref SampleIntervalTicks, "sampleIntervalTicks", 2500);
            Scribe_Values.Look(ref RetentionDays, "retentionDays", 7);
            Scribe_Values.Look(ref HighLoadThreshold, "highLoadThreshold", 0.90f);
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RefreshIntervalTicks = Clamp(RefreshIntervalTicks, 60, 600);
                SampleIntervalTicks = Clamp(SampleIntervalTicks, 250, 15000);
                RetentionDays = Clamp(RetentionDays, 1, 30);
                HighLoadThreshold = Clamp(HighLoadThreshold, 0.50f, 1.50f);
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
