using System.Globalization;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Lightweight chart renderer built on RimWorld's own widgets: no external charting
    /// dependency. Columns with no sample are simply left empty, so missing data is
    /// visible rather than interpolated.
    /// </summary>
    public static class PowerChartWidget
    {
        private const float AxisLabelWidth = 56f;
        private const float TitleHeight = 20f;
        private const float FooterHeight = 16f;
        private const float LegendWidth = 190f;

        private static readonly Color GenerationColor = new Color(0.55f, 0.85f, 0.55f);
        private static readonly Color ConsumptionColor = new Color(0.95f, 0.72f, 0.45f);
        private static readonly Color PositiveColor = new Color(0.55f, 0.85f, 0.55f);
        private static readonly Color NegativeColor = new Color(0.95f, 0.45f, 0.35f);
        private static readonly Color SingleColor = new Color(0.55f, 0.72f, 0.95f);
        private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.10f);

        public static void Draw(Rect rect, PowerChartData data, string title, string unitLabel,
            string primaryLabel, string secondaryLabel, string startLabel, string endLabel)
        {
            PowerUiDraw.Panel(rect);
            Widgets.DrawBox(rect, 1);

            PowerUiDraw.DrawText(new Rect(rect.x + 8f, rect.y + 2f, rect.width - LegendWidth - 16f, TitleHeight), title,
                Color.white);

            Rect area = new Rect(rect.x + AxisLabelWidth, rect.y + TitleHeight + 4f,
                Mathf.Max(20f, rect.width - AxisLabelWidth - 10f),
                Mathf.Max(20f, rect.height - TitleHeight - FooterHeight - 8f));

            DrawLegend(new Rect(rect.xMax - LegendWidth - 8f, rect.y + 3f, LegendWidth, TitleHeight), data,
                primaryLabel, secondaryLabel);

            if (data == null || !data.HasData)
            {
                Widgets.DrawBoxSolid(area, new Color(1f, 1f, 1f, 0.04f));
                PowerUiDraw.DrawText(area, "PGM_Chart_NoData".Translate(), PowerUiDraw.Muted, TextAnchor.MiddleCenter);
                DrawFooter(rect, area, startLabel, endLabel);
                return;
            }

            Widgets.DrawBoxSolid(area, new Color(1f, 1f, 1f, 0.04f));

            // Horizontal guides at minimum, middle and maximum.
            for (int i = 0; i <= 2; i++)
            {
                float t = i / 2f;
                float value = data.Max - (data.Max - data.Min) * t;
                float y = area.y + area.height * t;
                Widgets.DrawLineHorizontal(area.x, y, area.width, GridColor);
                PowerUiDraw.DrawText(new Rect(rect.x + 2f, y - 8f, AxisLabelWidth - 6f, 16f),
                    FormatAxisValue(value, data.Metric), PowerUiDraw.Muted, TextAnchor.MiddleRight);
            }

            float zeroY = ValueToY(area, data, 0f);
            bool zeroVisible = data.Min < 0f && data.Max > 0f;
            if (zeroVisible) Widgets.DrawLineHorizontal(area.x, zeroY, area.width, new Color(1f, 1f, 1f, 0.28f));

            float columnWidth = area.width / data.Columns;
            if (columnWidth < 0.5f) columnWidth = 0.5f;
            float baselineY = zeroVisible ? zeroY : area.yMax;

            for (int column = 0; column < data.Columns; column++)
            {
                float x = area.x + column * (area.width / data.Columns);
                float drawnWidth = Mathf.Max(1f, columnWidth);

                float? primary = data.Primary[column];
                if (primary.HasValue)
                {
                    Color color = PrimaryColor(data.Metric, primary.Value);
                    DrawSeriesColumn(area, x, drawnWidth, primary.Value, data, baselineY, color, true);
                }

                if (data.Secondary != null)
                {
                    float? secondary = data.Secondary[column];
                    if (secondary.HasValue)
                    {
                        DrawSeriesColumn(area, x, drawnWidth, secondary.Value, data, baselineY, ConsumptionColor, false);
                    }
                }
            }

            DrawFooter(rect, area, startLabel, endLabel);
            PowerUiDraw.DrawText(new Rect(rect.x + 2f, rect.y + 2f, AxisLabelWidth - 6f, 16f), unitLabel,
                PowerUiDraw.Muted, TextAnchor.MiddleRight);
            if (data.MissingColumnCount > 0)
            {
                PowerUiDraw.DrawText(new Rect(area.x, area.yMax - 16f, area.width, 16f),
                    "PGM_Chart_Missing".Translate(data.MissingColumnCount, data.Columns), PowerUiDraw.Warn,
                    TextAnchor.MiddleCenter);
            }
        }

        private static void DrawSeriesColumn(Rect area, float x, float width, float value, PowerChartData data,
            float baselineY, Color color, bool withFill)
        {
            float y = ValueToY(area, data, value);
            if (y < area.y) y = area.y;
            if (y > area.yMax) y = area.yMax;

            float top = Mathf.Min(y, baselineY);
            float bottom = Mathf.Max(y, baselineY);
            if (bottom - top >= 1f && withFill)
            {
                Widgets.DrawBoxSolid(new Rect(x, top, width, bottom - top),
                    new Color(color.r, color.g, color.b, 0.30f));
            }
            // Solid cap marks the actual value so the line reads clearly over the fill.
            Widgets.DrawBoxSolid(new Rect(x, y - 1f, width, 2f), color);
        }

        private static void DrawLegend(Rect rect, PowerChartData data, string primaryLabel, string secondaryLabel)
        {
            if (data == null || string.IsNullOrEmpty(primaryLabel)) return;
            float half = data.HasSecondary ? rect.width / 2f : rect.width;
            Rect primaryRect = new Rect(rect.x, rect.y, half, rect.height);
            Swatch(primaryRect, PrimaryColor(data.Metric, 0f), primaryLabel);
            if (data.HasSecondary)
            {
                Rect secondaryRect = new Rect(rect.x + half, rect.y, half, rect.height);
                Swatch(secondaryRect, ConsumptionColor, secondaryLabel);
            }
        }

        private static void Swatch(Rect rect, Color color, string label)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 6f, 10f, 10f), color);
            PowerUiDraw.DrawText(new Rect(rect.x + 14f, rect.y, rect.width - 14f, rect.height), label,
                PowerUiDraw.Muted);
        }

        private static void DrawFooter(Rect rect, Rect area, string startLabel, string endLabel)
        {
            float y = area.yMax + 2f;
            PowerUiDraw.DrawText(new Rect(area.x, y, area.width * 0.5f, FooterHeight), startLabel, PowerUiDraw.Muted);
            PowerUiDraw.DrawText(new Rect(area.x + area.width * 0.5f, y, area.width * 0.5f, FooterHeight), endLabel,
                PowerUiDraw.Muted, TextAnchor.MiddleRight);
        }

        private static Color PrimaryColor(PowerChartMetric metric, float value)
        {
            switch (metric)
            {
                case PowerChartMetric.GenerationAndConsumption: return GenerationColor;
                case PowerChartMetric.NetBalance: return value < 0f ? NegativeColor : PositiveColor;
                default: return SingleColor;
            }
        }

        private static float ValueToY(Rect area, PowerChartData data, float value)
        {
            float range = data.Max - data.Min;
            if (range <= 0.0001f) return area.yMax;
            float fraction = (value - data.Min) / range;
            return area.yMax - fraction * area.height;
        }

        private static string FormatAxisValue(float value, PowerChartMetric metric)
        {
            if (metric == PowerChartMetric.BatteryCharge || metric == PowerChartMetric.LoadRatio)
            {
                return value.ToString("F0", CultureInfo.InvariantCulture) + "%";
            }
            float magnitude = Mathf.Abs(value);
            if (magnitude >= 1000f)
            {
                return (magnitude / 1000f).ToString("F1", CultureInfo.InvariantCulture) + "k";
            }
            return magnitude.ToString(magnitude < 10f ? "F1" : "F0", CultureInfo.InvariantCulture);
        }
    }
}
