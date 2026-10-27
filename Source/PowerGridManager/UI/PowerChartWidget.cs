using System.Globalization;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Dark-theme infrastructure chart renderer built on RimWorld's IMGUI primitives.
    ///
    /// Renders recessed #050E19 plot canvases inside #0D1D2C bordered panels with subtle
    /// #24445C gridlines, cyan generation &amp; amber consumption traces, green/red signed
    /// net balance traces, and explicit visual breaks across missing sample intervals
    /// (never interpolating unmeasured data).
    /// </summary>
    public static class PowerChartWidget
    {
        private const float AxisLabelWidth = 60f;
        private const float TitleHeight = 22f;
        private const float FooterHeight = 16f;
        private const float LegendWidth = 210f;

        private static readonly Color GenerationColor = PowerUiDraw.AccentCyan;
        private static readonly Color ConsumptionColor = PowerUiDraw.Warn;
        private static readonly Color PositiveColor = PowerUiDraw.Good;
        private static readonly Color NegativeColor = PowerUiDraw.NoticeColor;
        private static readonly Color SingleColor = PowerUiDraw.AccentPrimary;
        private static readonly Color GridColor = new Color(36f / 255f, 68f / 255f, 92f / 255f, 0.45f);
        private static readonly Color ZeroLineColor = new Color(158f / 255f, 178f / 255f, 200f / 255f, 0.42f);

        public static void Draw(Rect rect, PowerChartData data, string title, string unitLabel,
            string primaryLabel, string secondaryLabel, string startLabel, string endLabel)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            PowerUiDraw.Panel(rect);

            float legendSpace = rect.width >= 300f ? Mathf.Min(LegendWidth, rect.width * 0.46f) : 0f;
            Widgets.DrawBoxSolid(new Rect(rect.x + 8f, rect.y + 6f, 3f, 12f), PowerUiDraw.AccentPrimary);
            PowerUiDraw.TextEllipsized(
                new Rect(rect.x + 15f, rect.y + 2f, Mathf.Max(20f, rect.width - legendSpace - 24f), TitleHeight),
                title, PowerUiDraw.TextPrimary);

            float axisW = Mathf.Min(AxisLabelWidth, rect.width * 0.25f);
            float footerH = rect.height >= 80f ? FooterHeight : 0f;
            Rect area = new Rect(
                rect.x + axisW,
                rect.y + TitleHeight + 4f,
                Mathf.Max(16f, rect.width - axisW - 10f),
                Mathf.Max(16f, rect.height - TitleHeight - footerH - 8f));

            if (legendSpace > 0f)
            {
                DrawLegend(new Rect(rect.xMax - legendSpace - 8f, rect.y + 3f, legendSpace, TitleHeight),
                    data, primaryLabel, secondaryLabel);
            }

            if (data == null || !data.HasData || data.Columns <= 0)
            {
                PowerUiDraw.RecessedBox(area);
                PowerUiDraw.TextEllipsized(area, "PGM_Chart_NoData".Translate(),
                    PowerUiDraw.TextSecondary, TextAnchor.MiddleCenter);
                if (footerH > 0f) DrawFooter(area, startLabel, endLabel);
                return;
            }

            Widgets.DrawBoxSolid(area, PowerUiDraw.BgRecessed);

            // Subtle vertical quarter gridlines.
            if (area.width >= 140f)
            {
                for (int q = 1; q <= 3; q++)
                {
                    PowerUiDraw.VerticalLine(area.x + area.width * (q * 0.25f), area.y, area.height, GridColor);
                }
            }

            // Horizontal guides at top, middle, bottom (plus quarters on taller plots).
            int steps = area.height >= 76f ? 4 : 2;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float value = data.Max - (data.Max - data.Min) * t;
                float y = area.y + area.height * t;
                Widgets.DrawLineHorizontal(area.x, y, area.width, GridColor);
                if (i == 0 || i == steps || i * 2 == steps)
                {
                    PowerUiDraw.TextEllipsized(new Rect(rect.x + 2f, y - 8f, axisW - 6f, 16f),
                        FormatAxisValue(value, data.Metric), PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);
                }
            }

            float zeroY = ValueToY(area, data, 0f);
            bool zeroVisible = data.Min < 0f && data.Max > 0f;
            if (zeroVisible)
                Widgets.DrawLineHorizontal(area.x, zeroY, area.width, ZeroLineColor);

            float columnWidth = area.width / data.Columns;
            if (columnWidth < 0.5f) columnWidth = 0.5f;
            float baselineY = zeroVisible ? zeroY : area.yMax;

            // Area fill + cap bars per populated bucket.
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

            // Connected polyline segments between adjacent populated columns (explicitly broken across gaps).
            DrawConnectedLine(area, data, data.Primary, data.Metric, true);
            if (data.Secondary != null)
                DrawConnectedLine(area, data, data.Secondary, data.Metric, false);

            PowerUiDraw.DrawBorder(area, PowerUiDraw.BorderPanel, 1);

            if (footerH > 0f)
                DrawFooter(area, startLabel, endLabel);

            if (!string.IsNullOrEmpty(unitLabel))
            {
                PowerUiDraw.TextEllipsized(new Rect(rect.x + 2f, rect.y + 3f, axisW - 6f, 16f),
                    unitLabel, PowerUiDraw.AccentCyan, TextAnchor.MiddleRight);
            }

            if (data.MissingColumnCount > 0 && area.height >= 36f)
            {
                PowerUiDraw.TextEllipsized(new Rect(area.x + 4f, area.yMax - 16f, area.width - 8f, 16f),
                    "PGM_Chart_Missing".Translate(data.MissingColumnCount, data.Columns),
                    PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);
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
                    new Color(color.r, color.g, color.b, 0.16f));
            }
            Widgets.DrawBoxSolid(new Rect(x, y - 1f, width, 2f), color);
        }

        private static void DrawConnectedLine(Rect area, PowerChartData data, float?[] values,
            PowerChartMetric metric, bool isPrimary)
        {
            if (values == null || data.Columns <= 1) return;
            bool hasPrev = false;
            Vector2 prev = Vector2.zero;
            float prevVal = 0f;
            float step = area.width / data.Columns;

            for (int col = 0; col < data.Columns; col++)
            {
                float? v = values[col];
                if (!v.HasValue)
                {
                    hasPrev = false;
                    continue;
                }

                float cx = area.x + (col + 0.5f) * step;
                float cy = Mathf.Clamp(ValueToY(area, data, v.Value), area.y + 1f, area.yMax - 1f);
                Vector2 cur = new Vector2(cx, cy);
                if (hasPrev)
                {
                    Color lineCol = isPrimary
                        ? PrimaryColor(metric, v.Value < 0f || prevVal < 0f ? -1f : v.Value)
                        : ConsumptionColor;
                    Widgets.DrawLine(prev, cur, lineCol, 1.8f);
                }

                prev = cur;
                prevVal = v.Value;
                hasPrev = true;
            }
        }

        private static void DrawLegend(Rect rect, PowerChartData data, string primaryLabel, string secondaryLabel)
        {
            if (data == null || string.IsNullOrEmpty(primaryLabel)) return;
            float half = data.HasSecondary ? rect.width * 0.5f : rect.width;
            Rect primaryRect = new Rect(rect.x, rect.y, half - 2f, rect.height);
            Swatch(primaryRect, PrimaryColor(data.Metric, 1f), primaryLabel);
            if (data.HasSecondary)
            {
                Rect secondaryRect = new Rect(rect.x + half, rect.y, half, rect.height);
                Swatch(secondaryRect, ConsumptionColor, secondaryLabel);
            }
        }

        private static void Swatch(Rect rect, Color color, string label)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + (rect.height - 4f) * 0.5f, 12f, 4f), color);
            PowerUiDraw.TextEllipsized(new Rect(rect.x + 16f, rect.y, Mathf.Max(0f, rect.width - 16f), rect.height),
                label, PowerUiDraw.TextSecondary);
        }

        private static void DrawFooter(Rect area, string startLabel, string endLabel)
        {
            float y = area.yMax + 2f;
            PowerUiDraw.TextEllipsized(new Rect(area.x, y, area.width * 0.52f, FooterHeight),
                startLabel, PowerUiDraw.TextSecondary);
            PowerUiDraw.TextEllipsized(new Rect(area.x + area.width * 0.52f, y, area.width * 0.48f, FooterHeight),
                endLabel, PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);
        }

        private static Color PrimaryColor(PowerChartMetric metric, float value)
        {
            switch (metric)
            {
                case PowerChartMetric.GenerationAndConsumption:
                    return GenerationColor;
                case PowerChartMetric.NetBalance:
                    return value < 0f ? NegativeColor : PositiveColor;
                case PowerChartMetric.BatteryCharge:
                    return PositiveColor;
                default:
                    return SingleColor;
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
            string sign = value < -0.01f ? "-" : string.Empty;
            if (magnitude >= 1000f)
            {
                return sign + (magnitude / 1000f).ToString("F1", CultureInfo.InvariantCulture) + "k";
            }
            return sign + magnitude.ToString(magnitude < 10f ? "F1" : "F0", CultureInfo.InvariantCulture);
        }
    }
}
