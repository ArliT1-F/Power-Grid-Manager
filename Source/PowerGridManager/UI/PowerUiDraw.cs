using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Small drawing helpers shared by the dashboard panels. They keep spacing,
    /// typography and status colours consistent, and always pair colour with text so
    /// status is never conveyed by colour alone.
    /// </summary>
    public static class PowerUiDraw
    {
        public const float RowHeight = 22f;
        public const float TileHeight = 66f;
        public const float SectionGap = 10f;

        private static readonly Color PanelColor = new Color(0.11f, 0.11f, 0.11f, 0.55f);
        private static readonly Color HeaderColor = new Color(0.78f, 0.78f, 0.78f);
        private static readonly Color MutedColor = new Color(0.68f, 0.68f, 0.68f);
        private static readonly Color DeficitColor = new Color(0.95f, 0.45f, 0.35f);
        private static readonly Color WarnColor = new Color(0.95f, 0.78f, 0.35f);
        private static readonly Color GoodColor = new Color(0.55f, 0.85f, 0.55f);
        private static readonly Color InfoColor = new Color(0.55f, 0.72f, 0.95f);
        private static readonly Color NeutralColor = new Color(0.7f, 0.7f, 0.7f);
        private static readonly Color UnknownColor = new Color(0.72f, 0.55f, 0.85f);

        public static Color StatusColor(NetworkStatus status)
        {
            switch (status)
            {
                case NetworkStatus.Stable: return GoodColor;
                case NetworkStatus.HighLoad: return WarnColor;
                case NetworkStatus.Deficit: return DeficitColor;
                case NetworkStatus.BatteryDischarging: return DeficitColor;
                case NetworkStatus.BatteryCharging: return InfoColor;
                case NetworkStatus.NoGeneration: return DeficitColor;
                case NetworkStatus.Unknown: return UnknownColor;
                default: return NeutralColor;
            }
        }

        public static Color NoticeColor => DeficitColor;

        public static Color StatusColorForUnknown()
        {
            return UnknownColor;
        }
        public static Color Muted => MutedColor;
        public static Color Good => GoodColor;
        public static Color Info => InfoColor;
        public static Color Warn => WarnColor;

        public static void Panel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
        }

        public static void SectionHeader(Rect rect, string label)
        {
            Color previous = GUI.color;
            GUI.color = HeaderColor;
            GameFont font = Text.Font;
            Text.Font = GameFont.Small;
            Widgets.Label(rect, label);
            Text.Font = font;
            GUI.color = previous;
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width, new Color(1f, 1f, 1f, 0.12f));
        }

        public static void DrawText(Rect rect, string text, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;
            GUI.color = color;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = previousAnchor;
            GUI.color = previousColor;
        }

        /// <summary>Text clipped with an ellipsis so a long label cannot bleed into the next column.</summary>
        public static void TextEllipsized(Rect rect, string text, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            DrawText(rect, Text.ClampTextWithEllipsis(rect, text ?? ""), color, anchor);
        }

        public static void WrappedText(Rect rect, string text, Color color)
        {
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;
            GUI.color = color;
            Text.WordWrap = true;
            Widgets.Label(rect, text);
            Text.WordWrap = previousWrap;
            GUI.color = previousColor;
        }

        public static void LabelValue(Rect rect, string label, string value, float labelWidth,
            Color? valueColor = null)
        {
            DrawText(rect, label, MutedColor);
            Rect valueRect = new Rect(rect.x + labelWidth, rect.y, rect.width - labelWidth, rect.height);
            DrawText(valueRect, value, valueColor ?? Color.white, TextAnchor.MiddleRight);
        }

        /// <summary>Coloured status pill: colour plus an explicit text label.</summary>
        public static void StatusPill(Rect rect, string label, Color color)
        {
            Widgets.DrawBoxSolid(rect, new Color(color.r, color.g, color.b, 0.28f));
            Widgets.DrawBox(rect, 1);
            DrawText(rect, label, color, TextAnchor.MiddleCenter);
        }

        public static void MetricTile(Rect rect, string label, string value, Color valueColor, string tooltip)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            Widgets.DrawBox(rect, 1);
            Rect labelRect = new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 18f);
            Rect valueRect = new Rect(rect.x + 8f, rect.y + 24f, rect.width - 16f, 26f);
            DrawText(labelRect, label, MutedColor);
            GameFont previous = Text.Font;
            // Small resolutions shrink tiles; drop to the small font rather than overflow.
            Text.Font = rect.height >= 60f ? GameFont.Medium : GameFont.Small;
            TextEllipsized(valueRect, value, valueColor);
            Text.Font = previous;
            if (!string.IsNullOrEmpty(tooltip)) TooltipHandler.TipRegion(rect, new TipSignal(tooltip));
        }

        public static void Bar(Rect rect, float fraction, Color color)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.08f));
            float clamped = fraction;
            if (clamped < 0f) clamped = 0f;
            if (clamped > 1f) clamped = 1f;
            float width = rect.width * clamped;
            if (width > 0.5f)
            {
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, width, rect.height), color);
            }
            Widgets.DrawBox(rect, 1);
        }

        public static void Tip(Rect rect, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            TooltipHandler.TipRegion(rect, new TipSignal(text));
        }

        /// <summary>Slices a fixed-height row out of <paramref name="area"/>.</summary>
        public static Rect TakeRow(ref float y, Rect area, float height, float gap = 2f)
        {
            Rect row = new Rect(area.x, y, area.width, height);
            y += height + gap;
            return row;
        }
    }
}
