using System.Collections.Generic;

namespace PowerGridManager
{
    /// <summary>
    /// Historical aggregates used by the forecast. Pure logic over the bounded sample list, so
    /// it can be verified without the game and costs one pass over at most a few hundred rows.
    /// </summary>
    public static class PowerForecast
    {
        /// <summary>Averages generation and consumption over a tick window. Returns false when empty.</summary>
        public static bool TryAverage(IReadOnlyList<PowerHistorySample> samples, int startTick, int endTick,
            out float generationWatts, out float consumptionWatts, out int sampleCount)
        {
            generationWatts = 0f;
            consumptionWatts = 0f;
            sampleCount = 0;
            if (samples == null || samples.Count == 0 || endTick <= startTick) return false;

            double generation = 0;
            double consumption = 0;
            int count = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                PowerHistorySample sample = samples[i];
                if (sample.Tick < startTick || sample.Tick > endTick) continue;
                if (!IsFinite(sample.GenerationWatts) || !IsFinite(sample.ConsumptionWatts)) continue;
                generation += sample.GenerationWatts;
                consumption += sample.ConsumptionWatts;
                count++;
            }

            if (count == 0) return false;
            generationWatts = (float)(generation / count);
            consumptionWatts = (float)(consumption / count);
            sampleCount = count;
            return true;
        }

        /// <summary>Average net balance (generation minus consumption) over a tick window.</summary>
        public static float? AverageNetWatts(IReadOnlyList<PowerHistorySample> samples, int startTick, int endTick,
            out int sampleCount)
        {
            float generation;
            float consumption;
            if (!TryAverage(samples, startTick, endTick, out generation, out consumption, out sampleCount))
            {
                sampleCount = 0;
                return null;
            }
            return generation - consumption;
        }

        /// <summary>Share of samples in the window whose net balance was negative (0..1).</summary>
        public static float DeficitShare(IReadOnlyList<PowerHistorySample> samples, int startTick, int endTick,
            out int sampleCount)
        {
            sampleCount = 0;
            if (samples == null || samples.Count == 0 || endTick <= startTick) return 0f;

            int inWindow = 0;
            int deficit = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                PowerHistorySample sample = samples[i];
                if (sample.Tick < startTick || sample.Tick > endTick) continue;
                if (!IsFinite(sample.GenerationWatts) || !IsFinite(sample.ConsumptionWatts)) continue;
                inWindow++;
                if (sample.NetWatts < -0.01f) deficit++;
            }
            sampleCount = inWindow;
            if (inWindow == 0) return 0f;
            return deficit / (float)inWindow;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
