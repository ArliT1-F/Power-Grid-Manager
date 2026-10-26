using System.Collections.Generic;
using Verse;

namespace PowerGridManager
{
    /// <summary>In-game time ranges offered by the history view.</summary>
    public enum PowerTimeRange
    {
        LastHour = 0,
        LastSixHours,
        LastDay,
        LastThreeDays,
        LastSevenDays
    }

    /// <summary>Which aggregate a chart shows.</summary>
    public enum PowerChartMetric
    {
        GenerationAndConsumption = 0,
        NetBalance,
        BatteryCharge,

        /// <summary>Consumption as a percentage of current generation (0% = idle, 100% = exactly met).</summary>
        LoadRatio
    }

    /// <summary>
    /// Screen-independent chart geometry: one entry per horizontal column, where a
    /// null entry means "no sample in this interval" (never an interpolated value).
    /// </summary>
    public sealed class PowerChartData
    {
        public PowerChartMetric Metric { get; internal set; }
        public int Columns { get; internal set; }
        public float?[] Primary { get; internal set; }
        public float?[] Secondary { get; internal set; }
        public float Min { get; internal set; }
        public float Max { get; internal set; }
        public bool HasData { get; internal set; }
        public int MissingColumnCount { get; internal set; }
        public int StartTick { get; internal set; }
        public int EndTick { get; internal set; }

        public bool HasSecondary => Secondary != null;
    }

    /// <summary>
    /// Builds chart series from bounded history samples. Pure logic: no Unity or
    /// RimWorld rendering types, so it can be verified outside the game.
    /// </summary>
    public static class PowerChartBuilder
    {
        public const int MaximumColumns = 2048;

        public static int TicksFor(PowerTimeRange range)
        {
            switch (range)
            {
                case PowerTimeRange.LastHour: return PowerTime.TicksPerHour;
                case PowerTimeRange.LastSixHours: return PowerTime.TicksPerHour * 6;
                case PowerTimeRange.LastDay: return PowerTime.TicksPerDay;
                case PowerTimeRange.LastThreeDays: return PowerTime.TicksPerDay * 3;
                default: return PowerTime.TicksPerDay * 7;
            }
        }

        public static PowerChartData Build(IReadOnlyList<PowerHistorySample> samples, int startTick, int endTick,
            int columns, PowerChartMetric metric)
        {
            if (columns < 1) columns = 1;
            if (columns > MaximumColumns) columns = MaximumColumns;

            var data = new PowerChartData
            {
                Metric = metric,
                Columns = columns,
                StartTick = startTick,
                EndTick = endTick,
                Primary = new float?[columns],
                Secondary = metric == PowerChartMetric.GenerationAndConsumption ? new float?[columns] : null
            };

            if (samples == null || samples.Count == 0 || endTick <= startTick)
            {
                data.Min = 0f;
                data.Max = 1f;
                data.HasData = false;
                data.MissingColumnCount = columns;
                return data;
            }

            var primarySum = new float[columns];
            var primaryCount = new int[columns];
            var secondarySum = new float[columns];
            var secondaryCount = new int[columns];

            float min = float.MaxValue;
            float max = float.MinValue;
            long span = endTick - startTick;

            for (int i = 0; i < samples.Count; i++)
            {
                PowerHistorySample sample = samples[i];
                if (sample.Tick < startTick || sample.Tick > endTick) continue;
                int column = (int)(((long)(sample.Tick - startTick) * columns) / span);
                if (column < 0) column = 0;
                if (column >= columns) column = columns - 1;

                switch (metric)
                {
                    case PowerChartMetric.GenerationAndConsumption:
                        Accumulate(primarySum, primaryCount, column, sample.GenerationWatts);
                        Accumulate(secondarySum, secondaryCount, column, sample.ConsumptionWatts);
                        Track(sample.GenerationWatts, ref min, ref max);
                        Track(sample.ConsumptionWatts, ref min, ref max);
                        break;
                    case PowerChartMetric.NetBalance:
                        float net = sample.NetWatts;
                        Accumulate(primarySum, primaryCount, column, net);
                        Track(net, ref min, ref max);
                        break;
                    case PowerChartMetric.BatteryCharge:
                        float? charge = sample.ChargeFraction;
                        if (!charge.HasValue) continue;
                        float percent = charge.Value * 100f;
                        Accumulate(primarySum, primaryCount, column, percent);
                        Track(percent, ref min, ref max);
                        break;
                    default:
                        // Undefined ratio when nothing is generated: recorded as a gap,
                        // never as 0% or as infinity.
                        if (sample.GenerationWatts <= 0.01f) continue;
                        float ratio = sample.ConsumptionWatts / sample.GenerationWatts * 100f;
                        Accumulate(primarySum, primaryCount, column, ratio);
                        Track(ratio, ref min, ref max);
                        break;
                }
            }

            data.HasData = min <= max;
            data.MissingColumnCount = 0;
            for (int column = 0; column < columns; column++)
            {
                if (primaryCount[column] > 0) data.Primary[column] = primarySum[column] / primaryCount[column];
                if (data.Secondary != null && secondaryCount[column] > 0)
                    data.Secondary[column] = secondarySum[column] / secondaryCount[column];
                if (data.Primary[column] == null && (data.Secondary == null || data.Secondary[column] == null))
                    data.MissingColumnCount++;
            }

            ApplyAxisBounds(data, metric, min, max);
            return data;
        }

        private static void ApplyAxisBounds(PowerChartData data, PowerChartMetric metric, float min, float max)
        {
            if (!data.HasData)
            {
                data.Min = 0f;
                data.Max = metric == PowerChartMetric.BatteryCharge || metric == PowerChartMetric.LoadRatio
                    ? 100f
                    : 1f;
                return;
            }

            if (metric == PowerChartMetric.BatteryCharge)
            {
                // Charge is a bounded percentage; a fixed axis is easier to read.
                data.Min = 0f;
                data.Max = 100f;
                return;
            }

            if (metric == PowerChartMetric.LoadRatio)
            {
                // Always include the 100% line, and keep headroom for overloads.
                data.Min = 0f;
                float top = max < 100f ? 100f : MathfCeilToTen(max);
                data.Max = top;
                return;
            }

            if (metric == PowerChartMetric.NetBalance)
            {
                // Always show the zero line so the sign of the balance is unambiguous.
                if (min > 0f) min = 0f;
                if (max < 0f) max = 0f;
            }

            float range = max - min;
            if (range < 0.001f)
            {
                // Flat series: give the line vertical space in the middle of the chart.
                float pad = max == 0f ? 1f : System.Math.Abs(max) * 0.5f;
                min -= pad;
                max += pad;
            }
            else
            {
                float pad = range * 0.08f;
                min -= pad;
                max += pad;
            }
            if (metric != PowerChartMetric.NetBalance && min < 0f) min = 0f;

            data.Min = min;
            data.Max = max;
            if (data.Max - data.Min < 0.001f) data.Max = data.Min + 1f;
        }

        private static float MathfCeilToTen(float value)
        {
            float scaled = (float)System.Math.Ceiling(value / 10f) * 10f;
            return scaled < 100f ? 100f : scaled;
        }

        private static void Accumulate(float[] sums, int[] counts, int column, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            sums[column] += value;
            counts[column]++;
        }

        private static void Track(float value, ref float min, ref float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            if (value < min) min = value;
            if (value > max) max = value;
        }
    }
}
