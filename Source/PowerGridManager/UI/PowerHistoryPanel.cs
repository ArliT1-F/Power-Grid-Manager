using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Historical charts over recorded aggregate samples. Chart geometry is cached and
    /// rebuilt only when the sample set, time range, or viewport dimensions change.
    /// </summary>
    public sealed class PowerHistoryPanel
    {
        private const float RangeRowHeight = 36f;
        private const float ChartGap = 8f;

        private PowerChartData generationChart;
        private PowerChartData netChart;
        private PowerChartData chargeChart;
        private PowerChartData loadChart;

        private PowerHistoryTracker cachedTracker;
        private int cachedWidth = -1;
        private int cachedChartHeight = -1;
        private PowerTimeRange cachedRange;
        private int cachedSampleCount = -1;
        private int cachedNewestTick = -1;
        private Vector2 chartScroll;

        public void Draw(Rect rect, PowerHistoryTracker history, PowerUiState state,
            PowerGridManagerSettings settings, int now)
        {
            if (state == null || rect.width <= 8f || rect.height <= 8f) return;

            float rangeH = rect.height < 140f ? 26f : RangeRowHeight;
            DrawRangeRow(new Rect(rect.x, rect.y, rect.width, rangeH), state, history, settings);
            float y = rect.y + rangeH + PowerUiDraw.SpaceRelated;

            Rect chartArea = new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y));
            if (chartArea.height < 40f) return;

            if (history == null || !history.HasSamples)
            {
                PowerUiDraw.Panel(chartArea);
                string message = settings != null && !settings.HistoryEnabled
                    ? "PGM_History_Disabled".Translate()
                    : "PGM_History_Empty".Translate(PowerFormatting.FormatDuration(
                        settings == null ? 2500 : settings.SampleIntervalTicks));
                PowerUiDraw.Label(chartArea.ContractedBy(14f), message,
                    GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.MiddleCenter, true);
                return;
            }

            if (history.Count < 2)
            {
                PowerUiDraw.Panel(chartArea);
                PowerUiDraw.Label(chartArea.ContractedBy(14f),
                    "PGM_History_NotEnoughSamples".Translate(),
                    GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.MiddleCenter, true);
                return;
            }

            // Use a 2x2 grid on wide windows for generous vertical plot height, or stacked scroll on compact sizes.
            bool twoByTwo = chartArea.width >= 760f && chartArea.height >= 260f;
            if (twoByTwo)
            {
                float halfW = (chartArea.width - ChartGap) * 0.5f;
                float halfH = (chartArea.height - ChartGap) * 0.5f;
                EnsureCharts(halfW, halfH, history, state.Range);

                int endTick = history.NewestTick;
                int startTick = endTick - PowerChartBuilder.TicksFor(state.Range);
                string startLabel = "PGM_Chart_Ago".Translate(PowerFormatting.FormatDuration(endTick - startTick));
                string endLabel = "PGM_Chart_NewestSample".Translate(
                    PowerFormatting.FormatDuration(Mathf.Max(0, now - endTick)));

                PowerChartWidget.Draw(new Rect(chartArea.x, chartArea.y, halfW, halfH),
                    generationChart, "PGM_Chart_GenerationTitle".Translate(),
                    "PGM_Unit_Watt".Translate(), "PGM_Chart_LegendGeneration".Translate(),
                    "PGM_Chart_LegendConsumption".Translate(), startLabel, endLabel);

                PowerChartWidget.Draw(new Rect(chartArea.x + halfW + ChartGap, chartArea.y, halfW, halfH),
                    netChart, "PGM_Chart_NetTitle".Translate(),
                    "PGM_Unit_Watt".Translate(), "PGM_Chart_LegendNet".Translate(), null, startLabel, endLabel);

                PowerChartWidget.Draw(new Rect(chartArea.x, chartArea.y + halfH + ChartGap, halfW, halfH),
                    chargeChart, "PGM_Chart_ChargeTitle".Translate(), "%",
                    "PGM_Chart_LegendCharge".Translate(), null, startLabel, endLabel);

                PowerChartWidget.Draw(new Rect(chartArea.x + halfW + ChartGap, chartArea.y + halfH + ChartGap, halfW, halfH),
                    loadChart, "PGM_Chart_LoadTitle".Translate(), "%",
                    "PGM_Chart_LegendLoad".Translate(), null, startLabel, endLabel);
                return;
            }

            float minEach = 110f;
            float neededH = minEach * 4f + ChartGap * 3f;
            float viewW = chartArea.height < neededH ? Mathf.Max(40f, chartArea.width - 16f) : chartArea.width;
            float eachH = Mathf.Max(minEach, (chartArea.height - ChartGap * 3f) / 4f);
            EnsureCharts(viewW, eachH, history, state.Range);

            int endT = history.NewestTick;
            int startT = endT - PowerChartBuilder.TicksFor(state.Range);
            string sLabel = "PGM_Chart_Ago".Translate(PowerFormatting.FormatDuration(endT - startT));
            string eLabel = "PGM_Chart_NewestSample".Translate(PowerFormatting.FormatDuration(Mathf.Max(0, now - endT)));

            Rect view = new Rect(0f, 0f, viewW, eachH * 4f + ChartGap * 3f);
            Widgets.BeginScrollView(chartArea, ref chartScroll, view);
            try
            {
                float cy = 0f;
                PowerChartWidget.Draw(new Rect(0f, cy, viewW, eachH), generationChart,
                    "PGM_Chart_GenerationTitle".Translate(), "PGM_Unit_Watt".Translate(),
                    "PGM_Chart_LegendGeneration".Translate(), "PGM_Chart_LegendConsumption".Translate(),
                    sLabel, eLabel);
                cy += eachH + ChartGap;

                PowerChartWidget.Draw(new Rect(0f, cy, viewW, eachH), netChart,
                    "PGM_Chart_NetTitle".Translate(), "PGM_Unit_Watt".Translate(),
                    "PGM_Chart_LegendNet".Translate(), null, sLabel, eLabel);
                cy += eachH + ChartGap;

                PowerChartWidget.Draw(new Rect(0f, cy, viewW, eachH), chargeChart,
                    "PGM_Chart_ChargeTitle".Translate(), "%",
                    "PGM_Chart_LegendCharge".Translate(), null, sLabel, eLabel);
                cy += eachH + ChartGap;

                PowerChartWidget.Draw(new Rect(0f, cy, viewW, eachH), loadChart,
                    "PGM_Chart_LoadTitle".Translate(), "%",
                    "PGM_Chart_LegendLoad".Translate(), null, sLabel, eLabel);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private static void DrawRangeRow(Rect rect, PowerUiState state, PowerHistoryTracker history,
            PowerGridManagerSettings settings)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = new Rect(rect.x + 6f, rect.y + (rect.height - 26f) * 0.5f,
                Mathf.Max(20f, rect.width - 12f), Mathf.Min(26f, rect.height - 2f));

            float buttonWidth = Mathf.Clamp((inner.width - 20f) / 5f, 48f, 106f);
            float x = inner.x;
            PowerTimeRange[] ranges =
            {
                PowerTimeRange.LastHour, PowerTimeRange.LastSixHours, PowerTimeRange.LastDay,
                PowerTimeRange.LastThreeDays, PowerTimeRange.LastSevenDays
            };
            for (int i = 0; i < ranges.Length; i++)
            {
                Rect button = new Rect(x, inner.y, buttonWidth, inner.height);
                bool selected = state.Range == ranges[i];
                if (PowerUiDraw.Button(button, PowerGridUtilities.TimeRangeLabelKey(ranges[i]).Translate(), selected))
                    state.Range = ranges[i];
                x += buttonWidth + 4f;
            }

            Rect infoRect = new Rect(x + 8f, inner.y, Mathf.Max(0f, inner.xMax - x - 10f), inner.height);
            if (infoRect.width <= 110f) return;

            if (history == null || !history.HasSamples)
            {
                PowerUiDraw.TextEllipsized(infoRect, "PGM_History_NoSamplesShort".Translate(),
                    PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);
                return;
            }

            string info = "PGM_History_Info".Translate(history.Count,
                PowerFormatting.FormatDuration(history.LastSampleIntervalTicks));
            if (history.DiscardedSampleCount > 0)
                info = info + "  \u00B7  " + "PGM_History_Discarded".Translate(history.DiscardedSampleCount);

            PowerUiDraw.TextEllipsized(infoRect, info,
                history.DiscardedSampleCount > 0 ? PowerUiDraw.Warn : PowerUiDraw.TextSecondary,
                TextAnchor.MiddleRight);
            PowerUiDraw.Tip(infoRect, "PGM_Tip_HistoryInfo".Translate(
                PowerFormatting.FormatDuration(history.LastSampleIntervalTicks),
                settings == null ? 7 : settings.RetentionDays,
                PowerFormatting.FormatDuration(history.NewestTick - history.OldestTick)));
        }

        private void EnsureCharts(float width, float chartHeight, PowerHistoryTracker history, PowerTimeRange range)
        {
            int roundedWidth = Mathf.RoundToInt(width);
            int roundedHeight = Mathf.RoundToInt(chartHeight);
            if (ReferenceEquals(cachedTracker, history) && roundedWidth == cachedWidth &&
                roundedHeight == cachedChartHeight && range == cachedRange &&
                history.Count == cachedSampleCount && history.NewestTick == cachedNewestTick)
            {
                return;
            }

            cachedTracker = history;
            cachedWidth = roundedWidth;
            cachedChartHeight = roundedHeight;
            cachedRange = range;
            cachedSampleCount = history.Count;
            cachedNewestTick = history.NewestTick;

            int endTick = history.NewestTick;
            int startTick = endTick - PowerChartBuilder.TicksFor(range);
            int columns = Mathf.Clamp(Mathf.RoundToInt(width / 3f), 20, 600);

            generationChart = PowerChartBuilder.Build(history.Samples, startTick, endTick, columns,
                PowerChartMetric.GenerationAndConsumption);
            netChart = PowerChartBuilder.Build(history.Samples, startTick, endTick, columns,
                PowerChartMetric.NetBalance);
            chargeChart = PowerChartBuilder.Build(history.Samples, startTick, endTick, columns,
                PowerChartMetric.BatteryCharge);
            loadChart = PowerChartBuilder.Build(history.Samples, startTick, endTick, columns,
                PowerChartMetric.LoadRatio);
        }
    }
}
