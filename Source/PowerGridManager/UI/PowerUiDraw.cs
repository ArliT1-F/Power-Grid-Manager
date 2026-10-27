using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Shared visual design system and IMGUI drawing primitives for Power Grid Manager.
    ///
    /// Implements a cohesive dark electrical-grid monitoring palette using only standard
    /// Verse/Unity IMGUI primitives supported across RimWorld 1.5, 1.6, and 1.7. Every
    /// helper restores global <see cref="GUI.color"/>, <see cref="Text.Font"/>,
    /// <see cref="Text.Anchor"/>, and <see cref="Text.WordWrap"/> before returning.
    /// </summary>
    public static class PowerUiDraw
    {
        // Spacing scale (px).
        public const float SpaceTight = 4f;
        public const float SpaceRelated = 8f;
        public const float PadPanel = 12f;
        public const float SpaceSection = 16f;
        public const float SpaceMajor = 20f;

        public const float RowHeight = 24f;
        public const float TileHeight = 76f;
        public const float SectionGap = 12f;

        // Global dark infrastructure monitoring palette:
        // Main background:      #071321
        // Secondary background: #0D1D2C
        // Elevated panels:      #122638
        // Panel borders:        #24445C
        // Primary accent:       #168FE5 (electric blue)
        // Secondary accent:     #27D5F2 (cyan)
        // Positive status:      #36D99A (green)
        // Warning status:       #FFB547 (amber)
        // Critical status:      #FF5C68 (red)
        // Primary text:         #EDF5FF (near-white)
        // Secondary text:       #9EB2C8 (muted blue-gray)
        public static readonly Color BgMain = new Color(7f / 255f, 19f / 255f, 33f / 255f, 0.985f);
        public static readonly Color BgSecondary = new Color(13f / 255f, 29f / 255f, 44f / 255f, 1f);
        public static readonly Color BgElevated = new Color(18f / 255f, 38f / 255f, 56f / 255f, 1f);
        public static readonly Color BgRecessed = new Color(5f / 255f, 14f / 255f, 25f / 255f, 1f);
        public static readonly Color BgHeader = new Color(10f / 255f, 24f / 255f, 39f / 255f, 1f);
        public static readonly Color BgRowAlt = new Color(18f / 255f, 38f / 255f, 56f / 255f, 0.45f);
        public static readonly Color BgHover = new Color(22f / 255f, 143f / 255f, 229f / 255f, 0.16f);
        public static readonly Color BgSelected = new Color(22f / 255f, 143f / 255f, 229f / 255f, 0.26f);
        public static readonly Color BgButtonHover = new Color(25f / 255f, 53f / 255f, 78f / 255f, 1f);
        public static readonly Color BgButtonActive = new Color(20f / 255f, 68f / 255f, 108f / 255f, 1f);
        public static readonly Color BgButtonDisabled = new Color(13f / 255f, 26f / 255f, 38f / 255f, 0.75f);

        public static readonly Color BorderPanel = new Color(36f / 255f, 68f / 255f, 92f / 255f, 1f);
        public static readonly Color BorderSubtle = new Color(36f / 255f, 68f / 255f, 92f / 255f, 0.55f);

        public static readonly Color AccentPrimary = new Color(22f / 255f, 143f / 255f, 229f / 255f, 1f);
        public static readonly Color AccentCyan = new Color(39f / 255f, 213f / 255f, 242f / 255f, 1f);

        private static readonly Color GoodColor = new Color(54f / 255f, 217f / 255f, 154f / 255f, 1f);
        private static readonly Color WarnColor = new Color(255f / 255f, 181f / 255f, 71f / 255f, 1f);
        private static readonly Color DeficitColor = new Color(255f / 255f, 92f / 255f, 104f / 255f, 1f);
        private static readonly Color UnknownStatusColor = new Color(132f / 255f, 152f / 255f, 174f / 255f, 1f);

        public static readonly Color TextPrimary = new Color(237f / 255f, 245f / 255f, 255f / 255f, 1f);
        public static readonly Color TextSecondary = new Color(158f / 255f, 178f / 255f, 200f / 255f, 1f);
        public static readonly Color TextDisabled = new Color(108f / 255f, 126f / 255f, 146f / 255f, 0.85f);

        public static Color Good => GoodColor;
        public static Color Warn => WarnColor;
        public static Color NoticeColor => DeficitColor;
        public static Color Info => AccentCyan;
        public static Color Muted => TextSecondary;

        public static Color StatusColor(NetworkStatus status)
        {
            switch (status)
            {
                case NetworkStatus.Stable:
                    return GoodColor;
                case NetworkStatus.BatteryCharging:
                    return AccentCyan;
                case NetworkStatus.HighLoad:
                    return WarnColor;
                case NetworkStatus.BatteryDischarging:
                    return WarnColor;
                case NetworkStatus.Deficit:
                case NetworkStatus.NoGeneration:
                    return DeficitColor;
                case NetworkStatus.Unknown:
                default:
                    return UnknownStatusColor;
            }
        }

        public static Color StatusColorForUnknown()
        {
            return UnknownStatusColor;
        }

        /// <summary>
        /// Non-color symbol paired with status indicators so network state is never conveyed by color alone.
        /// </summary>
        public static string StatusSymbol(NetworkStatus status)
        {
            switch (status)
            {
                case NetworkStatus.Stable:
                    return "\u25CF";
                case NetworkStatus.BatteryCharging:
                    return "+";
                case NetworkStatus.BatteryDischarging:
                    return "\u25BC";
                case NetworkStatus.HighLoad:
                    return "\u25B2";
                case NetworkStatus.Deficit:
                case NetworkStatus.NoGeneration:
                    return "!";
                case NetworkStatus.Unknown:
                default:
                    return "?";
            }
        }

        public static Color SeverityColor(PowerDiagnosticSeverity severity)
        {
            switch (severity)
            {
                case PowerDiagnosticSeverity.Critical:
                    return DeficitColor;
                case PowerDiagnosticSeverity.Warning:
                    return WarnColor;
                case PowerDiagnosticSeverity.Info:
                default:
                    return AccentPrimary;
            }
        }

        public static string SeveritySymbol(PowerDiagnosticSeverity severity)
        {
            switch (severity)
            {
                case PowerDiagnosticSeverity.Critical:
                    return "!!";
                case PowerDiagnosticSeverity.Warning:
                    return "!";
                case PowerDiagnosticSeverity.Info:
                default:
                    return "i";
            }
        }

        public static Color SignedColor(float netWatts)
        {
            if (netWatts > 0.01f) return GoodColor;
            if (netWatts < -0.01f) return DeficitColor;
            return TextSecondary;
        }

        public static Color BalanceColor(PowerGridSnapshot snapshot, PowerGridManagerSettings settings)
        {
            if (snapshot == null) return UnknownStatusColor;
            float net = snapshot.NetWatts;
            if (net < -0.01f)
            {
                float lowFraction = settings != null ? settings.LowBatteryWarningFraction : 0.25f;
                bool hasUsableReserve = snapshot.BatteryCount > 0 && snapshot.AvailableStoredWattDays > 0.005f;
                bool aboveLowThreshold = snapshot.ChargeFraction.HasValue && snapshot.ChargeFraction.Value > lowFraction;
                if (!hasUsableReserve || !aboveLowThreshold)
                    return DeficitColor;
                return WarnColor;
            }

            if (settings != null && snapshot.LoadRatio.HasValue && snapshot.LoadRatio.Value >= settings.HighLoadThreshold)
                return WarnColor;
            if (net > 0.01f)
                return GoodColor;
            return TextPrimary;
        }

        public static Color ChargeColor(float? chargeFraction, float lowWarningFraction = 0.25f)
        {
            if (!chargeFraction.HasValue) return UnknownStatusColor;
            float v = chargeFraction.Value;
            if (v <= lowWarningFraction) return DeficitColor;
            if (v <= Mathf.Max(0.45f, lowWarningFraction + 0.15f)) return WarnColor;
            return GoodColor;
        }

        public static void DrawBorder(Rect rect, Color color, int thickness = 1)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            Color prev = GUI.color;
            try
            {
                GUI.color = color;
                Widgets.DrawBox(rect, thickness);
            }
            finally
            {
                GUI.color = prev;
            }
        }

        public static void WindowChrome(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            Widgets.DrawBoxSolid(rect, BgMain);
            if (rect.height >= 4f)
            {
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 2f),
                    new Color(AccentPrimary.r, AccentPrimary.g, AccentPrimary.b, 0.85f));
            }
            DrawBorder(rect, BorderPanel, 1);
        }

        public static void DrawWindowBackground(Rect rect)
        {
            WindowChrome(rect);
        }

        public static void Panel(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            Widgets.DrawBoxSolid(rect, BgSecondary);
            DrawBorder(rect, BorderPanel, 1);
        }

        public static void ElevatedPanel(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            Widgets.DrawBoxSolid(rect, BgElevated);
            DrawBorder(rect, BorderPanel, 1);
        }

        public static void RecessedBox(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            Widgets.DrawBoxSolid(rect, BgRecessed);
            DrawBorder(rect, BorderSubtle, 1);
        }

        public static void AccentCard(Rect rect, Color accent, bool elevated = true)
        {
            AccentCard(rect, accent, elevated ? BgElevated : BgSecondary);
        }

        public static void AccentCard(Rect rect, Color accent, Color fill)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            Widgets.DrawBoxSolid(rect, fill);
            DrawBorder(rect, BorderPanel, 1);
            float stripeW = Mathf.Min(3f, rect.width);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, stripeW, rect.height), accent);
        }

        public static void HorizontalLine(float x, float y, float width, Color color)
        {
            if (width <= 0f) return;
            Widgets.DrawLineHorizontal(x, y, width, color);
        }

        public static void VerticalLine(float x, float y, float height, Color color)
        {
            if (height <= 0f) return;
            Widgets.DrawBoxSolid(new Rect(x, y, 1f, height), color);
        }

        public static void SectionHeader(Rect rect, string label, string rightText = null)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            float pipH = Mathf.Min(13f, Mathf.Max(4f, rect.height - 8f));
            float pipY = rect.y + (rect.height - 2f - pipH) * 0.5f;
            Widgets.DrawBoxSolid(new Rect(rect.x, pipY, 3f, pipH), AccentPrimary);

            float rightWidth = 0f;
            if (!string.IsNullOrEmpty(rightText) && rect.width > 160f)
            {
                rightWidth = Mathf.Min(rect.width * 0.48f, Text.CalcSize(rightText).x + 8f);
                Label(new Rect(rect.xMax - rightWidth, rect.y, rightWidth, rect.height - 2f),
                    rightText, GameFont.Small, TextSecondary, TextAnchor.MiddleRight);
            }

            float titleW = Mathf.Max(0f, rect.width - 8f - rightWidth);
            Label(new Rect(rect.x + 8f, rect.y, titleW, rect.height - 2f),
                label, GameFont.Small, TextPrimary, TextAnchor.MiddleLeft);
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width, BorderPanel);
        }

        public static void DrawText(Rect rect, string text, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft, GameFont? font = null)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;
            GameFont previousFont = Text.Font;
            try
            {
                if (font.HasValue) Text.Font = font.Value;
                GUI.color = color;
                Text.Anchor = anchor;
                Widgets.Label(rect, text ?? string.Empty);
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                GUI.color = previousColor;
            }
        }

        public static void TextEllipsized(Rect rect, string text, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft, GameFont? font = null)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            GameFont previousFont = Text.Font;
            try
            {
                if (font.HasValue) Text.Font = font.Value;
                string clamped = Text.ClampTextWithEllipsis(rect, text ?? string.Empty);
                DrawText(rect, clamped, color, anchor, font);
            }
            finally
            {
                Text.Font = previousFont;
            }
        }

        public static void WrappedText(Rect rect, string text, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;
            try
            {
                GUI.color = color;
                Text.WordWrap = true;
                Widgets.Label(rect, text ?? string.Empty);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
            }
        }

        public static void Label(Rect rect, string text, GameFont font = GameFont.Small,
            Color? color = null, TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            GameFont prevFont = Text.Font;
            Color prevColor = GUI.color;
            TextAnchor prevAnchor = Text.Anchor;
            bool prevWrap = Text.WordWrap;
            try
            {
                Text.Font = font;
                GUI.color = color ?? TextPrimary;
                Text.Anchor = anchor;
                Text.WordWrap = wrap;
                string safe = text ?? string.Empty;
                string rendered = wrap ? safe : Text.ClampTextWithEllipsis(rect, safe);
                Widgets.Label(rect, rendered);
            }
            finally
            {
                Text.Font = prevFont;
                GUI.color = prevColor;
                Text.Anchor = prevAnchor;
                Text.WordWrap = prevWrap;
            }
        }

        public static void LabelValue(Rect rect, string label, string value, float labelWidth,
            Color? valueColor = null)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            float lw = Mathf.Min(labelWidth, Mathf.Max(0f, rect.width - 8f));
            TextEllipsized(new Rect(rect.x, rect.y, lw, rect.height), label, TextSecondary);
            Rect valueRect = new Rect(rect.x + lw, rect.y, Mathf.Max(0f, rect.width - lw), rect.height);
            TextEllipsized(valueRect, value, valueColor ?? TextPrimary, TextAnchor.MiddleRight);
        }

        public static void KeyValueRow(Rect rect, string label, string value, Color? valueColor = null,
            string tooltip = null)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            float leftW = Mathf.Min(rect.width * 0.54f, Mathf.Max(60f, Text.CalcSize(label ?? string.Empty).x + 8f));
            Label(new Rect(rect.x, rect.y, leftW, rect.height), label, GameFont.Small, TextSecondary, TextAnchor.MiddleLeft);
            Label(new Rect(rect.x + leftW + 4f, rect.y, Mathf.Max(0f, rect.width - leftW - 4f), rect.height),
                value, GameFont.Small, valueColor ?? TextPrimary, TextAnchor.MiddleRight);
            if (!string.IsNullOrEmpty(tooltip))
                Tip(rect, tooltip);
        }

        public static void MetricTile(Rect rect, string label, string value, Color valueColor, string tooltip = null)
        {
            StatCard(rect, label, value, valueColor, AccentPrimary, null, tooltip);
        }

        public static void StatCard(Rect rect, string label, string value, Color valueColor,
            Color accentColor, string subtitle = null, string tooltip = null,
            float? barFraction = null, Color? barColor = null)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            Widgets.DrawBoxSolid(rect, BgElevated);
            if (rect.height >= 6f)
            {
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 2f),
                    new Color(accentColor.r, accentColor.g, accentColor.b, 0.85f));
            }
            DrawBorder(rect, BorderPanel, 1);

            Rect inner = rect.ContractedBy(8f);
            if (inner.width <= 4f || inner.height <= 4f) return;

            bool compact = rect.height < 60f;
            float labelH = compact ? Mathf.Min(16f, inner.height * 0.35f) : 18f;
            Label(new Rect(inner.x, inner.y, inner.width, labelH),
                label, GameFont.Small, TextSecondary, TextAnchor.UpperLeft);

            bool hasSubtitle = !string.IsNullOrEmpty(subtitle) && inner.height >= 46f;
            bool hasBar = barFraction.HasValue && inner.height >= 42f;

            float bottomReserve = 0f;
            if (hasSubtitle) bottomReserve += 15f;
            if (hasBar) bottomReserve += hasSubtitle ? 8f : 10f;

            float valueY = inner.y + labelH;
            float valueH = Mathf.Max(16f, inner.height - labelH - bottomReserve);
            GameFont valueFont = inner.height >= 52f && inner.width >= 92f ? GameFont.Medium : GameFont.Small;
            Label(new Rect(inner.x, valueY, inner.width, valueH),
                value, valueFont, valueColor, TextAnchor.MiddleLeft);

            float cursorBottom = inner.yMax;
            if (hasSubtitle)
            {
                cursorBottom -= 15f;
                Label(new Rect(inner.x, cursorBottom, inner.width, 15f),
                    subtitle, GameFont.Small, TextSecondary, TextAnchor.LowerLeft);
            }

            if (hasBar)
            {
                float barH = hasSubtitle ? 6f : 7f;
                float barY = cursorBottom - barH - (hasSubtitle ? 2f : 0f);
                Bar(new Rect(inner.x, barY, inner.width, barH),
                    barFraction.Value, barColor ?? valueColor);
            }

            if (!string.IsNullOrEmpty(tooltip))
                Tip(rect, tooltip);
        }

        public static void Bar(Rect rect, float fraction, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            float clamped = Mathf.Clamp01(fraction);
            Widgets.DrawBoxSolid(rect, BgRecessed);
            if (clamped > 0.001f)
            {
                float fillW = Mathf.Max(1f, rect.width * clamped);
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, fillW, rect.height),
                    new Color(color.r, color.g, color.b, 0.88f));
                if (fillW >= 3f && clamped < 0.995f)
                {
                    Widgets.DrawBoxSolid(new Rect(rect.x + fillW - 1f, rect.y, 1f, rect.height),
                        new Color(Mathf.Min(1f, color.r + 0.2f), Mathf.Min(1f, color.g + 0.2f), Mathf.Min(1f, color.b + 0.2f), 1f));
                }
            }
            DrawBorder(rect, BorderPanel, 1);
        }

        public static void BatteryBar(Rect rect, float fraction, Color color, float? thresholdMarker = null)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            Bar(rect, fraction, color);
            if (rect.width >= 96f && rect.height >= 8f)
            {
                Color tickColor = new Color(BorderPanel.r, BorderPanel.g, BorderPanel.b, 0.7f);
                for (int i = 1; i <= 3; i++)
                {
                    float tx = rect.x + rect.width * (i * 0.25f);
                    Widgets.DrawBoxSolid(new Rect(tx, rect.y + 1f, 1f, Mathf.Max(2f, rect.height - 2f)), tickColor);
                }
            }

            if (thresholdMarker.HasValue)
            {
                float m = Mathf.Clamp01(thresholdMarker.Value);
                float mx = rect.x + rect.width * m;
                Widgets.DrawBoxSolid(new Rect(mx, rect.y, 2f, rect.height), WarnColor);
            }
        }

        public static void BatteryBar(Rect rect, float fraction, string label, Color color,
            float? thresholdMarker = null)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            BatteryBar(rect, fraction, color, thresholdMarker);
            if (!string.IsNullOrEmpty(label) && rect.height >= 12f)
            {
                TextEllipsized(new Rect(rect.x + 6f, rect.y, Mathf.Max(0f, rect.width - 12f), rect.height),
                    label, TextPrimary, TextAnchor.MiddleCenter, GameFont.Tiny);
            }
        }

        public static void StatusPill(Rect rect, string label, Color color)
        {
            StatusBadge(rect, label, color, null);
        }

        public static void StatusBadge(Rect rect, string label, Color color, string symbol = null)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            Widgets.DrawBoxSolid(rect, new Color(color.r, color.g, color.b, 0.16f));
            DrawBorder(rect, new Color(color.r, color.g, color.b, 0.68f), 1);

            string display = label ?? string.Empty;
            if (!string.IsNullOrEmpty(symbol) && rect.width >= 58f)
                display = symbol + " " + display;

            Rect inner = new Rect(rect.x + 4f, rect.y, Mathf.Max(0f, rect.width - 8f), rect.height);
            Label(inner, display, GameFont.Small, color, TextAnchor.MiddleCenter);
        }

        public static void RowBackground(Rect rect, int rowIndex, bool selected)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            bool hovered = Mouse.IsOver(rect);
            if (selected)
            {
                Widgets.DrawBoxSolid(rect, BgSelected);
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, Mathf.Min(3f, rect.width), rect.height), AccentCyan);
            }
            else if (hovered)
            {
                Widgets.DrawBoxSolid(rect, BgHover);
            }
            else if ((rowIndex & 1) == 1)
            {
                Widgets.DrawBoxSolid(rect, BgRowAlt);
            }
        }

        public static bool Button(Rect rect, string label, bool selected = false, bool primary = false,
            bool enabled = true, Color? accentOverride = null)
        {
            if (rect.width <= 0f || rect.height <= 0f) return false;
            bool hovered = enabled && Mouse.IsOver(rect);
            Color accent = accentOverride ?? (selected ? AccentCyan : AccentPrimary);

            Color bg;
            Color border;
            Color textCol;

            if (!enabled)
            {
                bg = BgButtonDisabled;
                border = BorderSubtle;
                textCol = TextDisabled;
            }
            else if (selected)
            {
                bg = BgButtonActive;
                border = accent;
                textCol = TextPrimary;
            }
            else if (primary)
            {
                bg = hovered
                    ? new Color(accent.r * 0.36f, accent.g * 0.36f, accent.b * 0.38f, 1f)
                    : new Color(accent.r * 0.24f, accent.g * 0.24f, accent.b * 0.28f, 1f);
                border = accent;
                textCol = TextPrimary;
            }
            else
            {
                bg = hovered ? BgButtonHover : BgElevated;
                border = hovered ? AccentPrimary : BorderPanel;
                textCol = TextPrimary;
            }

            Widgets.DrawBoxSolid(rect, bg);
            if (selected && rect.height >= 6f)
            {
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), accent);
            }
            DrawBorder(rect, border, 1);

            Rect labelRect = new Rect(rect.x + 5f, rect.y, Mathf.Max(0f, rect.width - 10f), rect.height);
            Label(labelRect, label, GameFont.Small, textCol, TextAnchor.MiddleCenter);

            bool clicked = Widgets.ButtonText(rect, string.Empty, false, enabled, enabled, TextAnchor.MiddleCenter);
            return enabled && clicked;
        }

        public static bool TabItem(Rect rect, string label, bool selected, int alertCount = 0,
            bool criticalAlert = false, string tooltip = null)
        {
            if (rect.width <= 0f || rect.height <= 0f) return false;
            bool hovered = Mouse.IsOver(rect);

            if (selected)
            {
                Widgets.DrawBoxSolid(rect, BgElevated);
                DrawBorder(rect, BorderPanel, 1);
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), AccentCyan);
            }
            else if (hovered)
            {
                Widgets.DrawBoxSolid(rect, BgHover);
            }

            float badgeReserve = 0f;
            if (alertCount > 0 && rect.width >= 58f)
            {
                string countText = alertCount > 99 ? "99+" : alertCount.ToString();
                float pillW = Mathf.Min(28f, Mathf.Max(18f, Text.CalcSize(countText).x + 8f));
                float pillH = Mathf.Min(16f, rect.height - 6f);
                Rect pillRect = new Rect(rect.xMax - pillW - 5f, rect.y + (rect.height - pillH) * 0.5f, pillW, pillH);
                Color badgeCol = criticalAlert ? DeficitColor : WarnColor;
                Widgets.DrawBoxSolid(pillRect, new Color(badgeCol.r, badgeCol.g, badgeCol.b, 0.24f));
                DrawBorder(pillRect, badgeCol, 1);
                Label(pillRect, countText, GameFont.Small, badgeCol, TextAnchor.MiddleCenter);
                badgeReserve = pillW + 6f;
            }

            Rect textRect = new Rect(rect.x + 6f, rect.y,
                Mathf.Max(0f, rect.width - 12f - badgeReserve), rect.height - (selected ? 2f : 0f));
            Label(textRect, label, GameFont.Small,
                selected ? TextPrimary : (hovered ? TextPrimary : TextSecondary),
                TextAnchor.MiddleCenter);

            if (!string.IsNullOrEmpty(tooltip))
                Tip(rect, tooltip);

            return Widgets.ButtonInvisible(rect);
        }

        public static void NotificationCardShell(Rect rect, Color severityColor, bool ongoing = true)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            Widgets.DrawBoxSolid(rect, ongoing ? BgElevated : BgSecondary);
            DrawBorder(rect, ongoing ? BorderPanel : BorderSubtle, 1);
            Color stripe = ongoing
                ? severityColor
                : new Color(severityColor.r, severityColor.g, severityColor.b, 0.45f);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, Mathf.Min(4f, rect.width), rect.height), stripe);
        }

        public static void PositiveStatusCard(Rect rect, string badgeLabel, string title, string detail)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            NotificationCardShell(rect, GoodColor, true);
            Rect inner = new Rect(rect.x + 12f, rect.y + 8f,
                Mathf.Max(0f, rect.width - 20f), Mathf.Max(0f, rect.height - 16f));
            if (inner.width <= 4f || inner.height <= 4f) return;

            float pillW = Mathf.Min(inner.width * 0.45f, Mathf.Max(68f, Text.CalcSize(badgeLabel ?? string.Empty).x + 22f));
            float headerH = Mathf.Min(22f, inner.height);
            StatusBadge(new Rect(inner.x, inner.y, pillW, headerH), badgeLabel, GoodColor, "\u25CF");

            float titleX = inner.x + pillW + 8f;
            Label(new Rect(titleX, inner.y, Mathf.Max(0f, inner.xMax - titleX), headerH),
                title, GameFont.Small, TextPrimary, TextAnchor.MiddleLeft);

            if (!string.IsNullOrEmpty(detail) && inner.height > headerH + 6f)
            {
                Label(new Rect(inner.x, inner.y + headerH + 4f, inner.width, inner.height - headerH - 4f),
                    detail, GameFont.Small, TextSecondary, TextAnchor.UpperLeft, true);
            }
        }

        public static void Tip(Rect rect, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (rect.width <= 0f || rect.height <= 0f) return;
            TooltipHandler.TipRegion(rect, new TipSignal(text));
        }

        public static Rect TakeRow(ref float y, Rect area, float height, float gap = 2f)
        {
            Rect row = new Rect(area.x, y, area.width, height);
            y += height + gap;
            return row;
        }
    }
}
