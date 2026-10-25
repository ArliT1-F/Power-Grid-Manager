using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Historical charts over the recorded aggregate samples. Chart geometry is cached
    /// and only rebuilt when the sample set, the range or the pixel size changes, so
    /// drawing is cheap and does not allocate per frame.
    /// </summary>
    public sealed class PowerHistoryPanel
    {
        private const float RangeRowHeight = 28f;
        private const float ChartGap = 6f;

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

        public void Draw(Rect rect, PowerHistoryTracker history, PowerUiState state,
            PowerGridManagerSettings settings, int now)
        {
            float y = rect.y;
            DrawRangeRow(new Rect(rect.x, y, rect.width, RangeRowHeight), state, history, settings, now);
            y += RangeRowHeight + 4f;

            Rect chartArea = new Rect(rect.x, y, rect.width, rect.yMax - y);
            if (chartArea.height < 90f) return;

            if (history == null || !history.HasSamples)
            {
                PowerUiDraw.Panel(chartArea);
                string message = settings != null && !settings.HistoryEnabled
                    ? "PGM_History_Disabled".Translate()
                    : "PGM_History_Empty".Translate(PowerFormatting.FormatDuration(
                        settings == null ? 2500 : settings.SampleIntervalTicks));
                PowerUiDraw.WrappedText(new Rect(chartArea.x + 10f, chartArea.y + 10f, chartArea.width - 20f, 90f),
                    message, PowerUiDraw.Muted);
                return;
            }

            if (history.Count < 2)
            {
                PowerUiDraw.Panel(chartArea);
                PowerUiDraw.WrappedText(new Rect(chartArea.x + 10f, chartArea.y + 10f, chartArea.width - 20f, 70f),
                    "PGM_History_NotEnoughSamples".Translate(), PowerUiDraw.Muted);
                return;
            }

            float chartHeight = (chartArea.height - ChartGap * 3f) / 4f;
            EnsureCharts(chartArea.width, chartHeight, history, state.Range);

            int endTick = history.NewestTick;
            int startTick = endTick - PowerChartBuilder.TicksFor(state.Range);
            string startLabel = "PGM_Chart_Ago".Translate(
                PowerFormatting.FormatDuration(endTick - startTick));
            string endLabel = "PGM_Chart_NewestSample".Translate(
                PowerFormatting.FormatDuration(Mathf.Max(0, now - endTick)));

            Rect first = new Rect(chartArea.x, chartArea.y, chartArea.width, chartHeight);
            Rect second = new Rect(chartArea.x, first.yMax + ChartGap, chartArea.width, chartHeight);
            Rect third = new Rect(chartArea.x, second.yMax + ChartGap, chartArea.width, chartHeight);
            Rect fourth = new Rect(chartArea.x, third.yMax + ChartGap, chartArea.width, chartHeight);

            PowerChartWidget.Draw(first, generationChart, "PGM_Chart_GenerationTitle".Translate(),
                "PGM_Unit_Watt".Translate(), "PGM_Chart_LegendGeneration".Translate(),
                "PGM_Chart_LegendConsumption".Translate(), startLabel, endLabel);
            PowerChartWidget.Draw(second, netChart, "PGM_Chart_NetTitle".Translate(),
                "PGM_Unit_Watt".Translate(), "PGM_Chart_LegendNet".Translate(), null, startLabel, endLabel);
            PowerChartWidget.Draw(third, chargeChart, "PGM_Chart_ChargeTitle".Translate(), "%",
                "PGM_Chart_LegendCharge".Translate(), null, startLabel, endLabel);
            PowerChartWidget.Draw(fourth, loadChart, "PGM_Chart_LoadTitle".Translate(), "%",
                "PGM_Chart_LegendLoad".Translate(), null, startLabel, endLabel);
        }

        private void DrawRangeRow(Rect rect, PowerUiState state, PowerHistoryTracker history,
            PowerGridManagerSettings settings, int now)
        {
            // Five range buttons plus optional summary text; shrink the buttons rather than clip.
            float buttonWidth = Mathf.Clamp((rect.width - 16f) / 5f, 54f, 96f);
            float x = rect.x;
            PowerTimeRange[] ranges =
            {
                PowerTimeRange.LastHour, PowerTimeRange.LastSixHours, PowerTimeRange.LastDay,
                PowerTimeRange.LastThreeDays, PowerTimeRange.LastSevenDays
            };
            for (int i = 0; i < ranges.Length; i++)
            {
                Rect button = new Rect(x, rect.y, buttonWidth, 26f);
                bool selected = state.Range == ranges[i];
                if (selected) Widgets.DrawBoxSolid(button, new Color(1f, 1f, 1f, 0.14f));
                if (Widgets.ButtonText(button, PowerGridUtilities.TimeRangeLabelKey(ranges[i]).Translate()))
                {
                    state.Range = ranges[i];
                }
                x += buttonWidth + 4f;
            }

            Rect infoRect = new Rect(x + 8f, rect.y, Mathf.Max(0f, rect.xMax - x - 8f), 26f);
            bool showInfo = infoRect.width > 120f;
            if (!showInfo) return;
            if (history == null || !history.HasSamples)
            {
                PowerUiDraw.TextEllipsized(infoRect, "PGM_History_NoSamplesShort".Translate(), PowerUiDraw.Muted,
                    TextAnchor.MiddleRight);
                return;
            }

            string info = "PGM_History_Info".Translate(history.Count,
                PowerFormatting.FormatDuration(history.LastSampleIntervalTicks));
            PowerUiDraw.TextEllipsized(infoRect, info, PowerUiDraw.Muted, TextAnchor.MiddleRight);
            PowerUiDraw.Tip(infoRect, "PGM_Tip_HistoryInfo".Translate(
                PowerFormatting.FormatDuration(history.LastSampleIntervalTicks),
                settings == null ? 7 : settings.RetentionDays,
                PowerFormatting.FormatDuration(history.NewestTick - history.OldestTick)));

            if (history.DiscardedSampleCount > 0)
            {
                PowerUiDraw.DrawText(new Rect(rect.x, rect.yMax - 2f, rect.width, 16f),
                    "PGM_History_Discarded".Translate(history.DiscardedSampleCount), PowerUiDraw.Warn);
            }
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
