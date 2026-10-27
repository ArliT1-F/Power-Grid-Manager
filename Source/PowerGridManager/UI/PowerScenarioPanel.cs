using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Modernized Planning tab: evaluates hypothetical power-system changes (added generation,
    /// batteries, consumers, switched-off load) and presents projected balance, reserve margin,
    /// endurance, and explicit assumptions in the dark monitoring aesthetic without touching
    /// the live simulation.
    /// </summary>
    public sealed class PowerScenarioPanel
    {
        private const float TileHeight = 68f;
        private const float TileGap = 8f;

        private string addedGenerationText = "";
        private string addedBatteryText = "";
        private string addedConsumerText = "";
        private string switchedOffText = "";
        private Vector2 outerScroll;

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings)
        {
            if (rect.width <= 8f || rect.height <= 8f) return;

            bool compact = rect.width < 680f;
            float inputH = compact ? 156f : 112f;
            float tilesH = compact ? (TileHeight * 2f + TileGap) : TileHeight;
            float resultH = 116f;
            float minAssumptionsH = 150f;
            float minTotal = inputH + TileGap + tilesH + TileGap + resultH + TileGap + minAssumptionsH;

            bool scroll = rect.height < minTotal;
            Rect view = new Rect(0f, 0f, scroll ? Mathf.Max(160f, rect.width - 18f) : rect.width,
                scroll ? minTotal : rect.height);
            if (scroll) Widgets.BeginScrollView(rect, ref outerScroll, view);
            try
            {
                float x0 = scroll ? 0f : rect.x;
                float y = scroll ? 0f : rect.y;
                float w = view.width;

                DrawInputs(new Rect(x0, y, w, inputH), compact);
                y += inputH + TileGap;

                PowerScenario scenario = ReadScenario();
                PowerScenario projected = PowerScenarioPlanner.Evaluate(snapshot, scenario, settings,
                    settings == null ? 0f : settings.ReserveMarginWatts);

                DrawTiles(new Rect(x0, y, w, tilesH), projected, compact);
                y += tilesH + TileGap;

                DrawResult(new Rect(x0, y, w, resultH), projected);
                y += resultH + TileGap;

                float remH = scroll ? minAssumptionsH : Mathf.Max(80f, (rect.yMax) - y);
                DrawAssumptions(new Rect(x0, y, w, remH), projected);
            }
            finally
            {
                if (scroll) Widgets.EndScrollView();
            }
        }

        private void DrawInputs(Rect rect, bool compact)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(10f);

            float resetW = 72f;
            PowerUiDraw.SectionHeader(new Rect(inner.x, inner.y, Mathf.Max(40f, inner.width - resetW - 8f), 20f),
                "PGM_Scenario_InputsHeader".Translate());
            Rect resetBtn = new Rect(inner.xMax - resetW, inner.y - 2f, resetW, 22f);
            if (PowerUiDraw.Button(resetBtn, "PGM_Buildings_Clear".Translate()))
            {
                addedGenerationText = "";
                addedBatteryText = "";
                addedConsumerText = "";
                switchedOffText = "";
            }

            float y = inner.y + 24f;
            if (!compact)
            {
                float fieldWidth = Mathf.Max(60f, (inner.width - 24f) / 4f);
                float x = inner.x;
                addedGenerationText = Field(new Rect(x, y, fieldWidth, 42f),
                    "PGM_Scenario_AddGeneration".Translate(), addedGenerationText);
                x += fieldWidth + 8f;
                addedBatteryText = Field(new Rect(x, y, fieldWidth, 42f),
                    "PGM_Scenario_AddBattery".Translate(), addedBatteryText);
                x += fieldWidth + 8f;
                addedConsumerText = Field(new Rect(x, y, fieldWidth, 42f),
                    "PGM_Scenario_AddConsumer".Translate(), addedConsumerText);
                x += fieldWidth + 8f;
                switchedOffText = Field(new Rect(x, y, Mathf.Max(40f, inner.xMax - x), 42f),
                    "PGM_Scenario_SwitchOff".Translate(), switchedOffText);
                y += 46f;
            }
            else
            {
                float fieldWidth = Mathf.Max(60f, (inner.width - 8f) * 0.5f);
                addedGenerationText = Field(new Rect(inner.x, y, fieldWidth, 40f),
                    "PGM_Scenario_AddGeneration".Translate(), addedGenerationText);
                addedBatteryText = Field(new Rect(inner.x + fieldWidth + 8f, y, fieldWidth, 40f),
                    "PGM_Scenario_AddBattery".Translate(), addedBatteryText);
                y += 44f;
                addedConsumerText = Field(new Rect(inner.x, y, fieldWidth, 40f),
                    "PGM_Scenario_AddConsumer".Translate(), addedConsumerText);
                switchedOffText = Field(new Rect(inner.x + fieldWidth + 8f, y, fieldWidth, 40f),
                    "PGM_Scenario_SwitchOff".Translate(), switchedOffText);
                y += 44f;
            }

            PowerUiDraw.WrappedText(new Rect(inner.x, y, inner.width, Mathf.Max(18f, inner.yMax - y)),
                "PGM_Scenario_InputsTip".Translate(), PowerUiDraw.TextSecondary);
        }

        private static string Field(Rect rect, string label, string text)
        {
            PowerUiDraw.TextEllipsized(new Rect(rect.x, rect.y, rect.width, 18f), label,
                PowerUiDraw.TextSecondary);
            Rect field = new Rect(rect.x, rect.y + 18f, rect.width, 22f);
            Widgets.DrawBoxSolid(field, PowerUiDraw.BgRecessed);
            string updated = Widgets.TextField(field, text ?? string.Empty);
            PowerUiDraw.DrawBorder(field, PowerUiDraw.BorderPanel, 1);
            return updated;
        }

        private PowerScenario ReadScenario()
        {
            return new PowerScenario
            {
                AddedGenerationWatts = ParseWatts(addedGenerationText),
                AddedBatteryCapacityWattDays = ParseWatts(addedBatteryText),
                AddedConsumerWatts = ParseWatts(addedConsumerText),
                SwitchedOffConsumerWatts = ParseWatts(switchedOffText),
            };
        }

        private static float ParseWatts(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            float value;
            if (float.TryParse(text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out value)) return value;
            return 0f;
        }

        private static void DrawTiles(Rect rect, PowerScenario projected, bool compact)
        {
            Color netCol = projected.ProjectedNetWatts < -0.01f
                ? PowerUiDraw.NoticeColor
                : (projected.ProjectedNetWatts > 0.01f ? PowerUiDraw.Good : PowerUiDraw.TextPrimary);
            Color marginCol = projected.ReserveTargetSatisfied ? PowerUiDraw.Good : PowerUiDraw.Warn;

            if (!compact)
            {
                float tileWidth = (rect.width - TileGap * 3f) / 4f;
                float x = rect.x;
                PowerUiDraw.StatCard(new Rect(x, rect.y, tileWidth, TileHeight),
                    "PGM_Scenario_ProjectedGeneration".Translate(),
                    PowerFormatting.FormatWatts(projected.ProjectedGenerationWatts),
                    PowerUiDraw.TextPrimary, PowerUiDraw.AccentCyan);
                x += tileWidth + TileGap;

                PowerUiDraw.StatCard(new Rect(x, rect.y, tileWidth, TileHeight),
                    "PGM_Scenario_ProjectedConsumption".Translate(),
                    PowerFormatting.FormatWatts(projected.ProjectedConsumptionWatts),
                    PowerUiDraw.TextPrimary, PowerUiDraw.Warn);
                x += tileWidth + TileGap;

                PowerUiDraw.StatCard(new Rect(x, rect.y, tileWidth, TileHeight),
                    "PGM_Scenario_ProjectedNet".Translate(),
                    PowerFormatting.FormatWattsSigned(projected.ProjectedNetWatts),
                    netCol, netCol);
                x += tileWidth + TileGap;

                PowerUiDraw.StatCard(new Rect(x, rect.y, Mathf.Max(40f, rect.xMax - x), TileHeight),
                    "PGM_Scenario_ReserveMargin".Translate(),
                    PowerFormatting.FormatWattsSigned(projected.ReserveMarginWatts),
                    marginCol, marginCol);
            }
            else
            {
                float tileWidth = (rect.width - TileGap) * 0.5f;
                PowerUiDraw.StatCard(new Rect(rect.x, rect.y, tileWidth, TileHeight),
                    "PGM_Scenario_ProjectedGeneration".Translate(),
                    PowerFormatting.FormatWatts(projected.ProjectedGenerationWatts),
                    PowerUiDraw.TextPrimary, PowerUiDraw.AccentCyan);
                PowerUiDraw.StatCard(new Rect(rect.x + tileWidth + TileGap, rect.y, tileWidth, TileHeight),
                    "PGM_Scenario_ProjectedConsumption".Translate(),
                    PowerFormatting.FormatWatts(projected.ProjectedConsumptionWatts),
                    PowerUiDraw.TextPrimary, PowerUiDraw.Warn);
                float r2 = rect.y + TileHeight + TileGap;
                PowerUiDraw.StatCard(new Rect(rect.x, r2, tileWidth, TileHeight),
                    "PGM_Scenario_ProjectedNet".Translate(),
                    PowerFormatting.FormatWattsSigned(projected.ProjectedNetWatts),
                    netCol, netCol);
                PowerUiDraw.StatCard(new Rect(rect.x + tileWidth + TileGap, r2, tileWidth, TileHeight),
                    "PGM_Scenario_ReserveMargin".Translate(),
                    PowerFormatting.FormatWattsSigned(projected.ReserveMarginWatts),
                    marginCol, marginCol);
            }
        }

        private static void DrawResult(Rect rect, PowerScenario projected)
        {
            bool deficit = projected.ProjectedNetWatts < -0.01f;
            Color accent = deficit ? PowerUiDraw.NoticeColor : PowerUiDraw.Good;
            PowerUiDraw.AccentCard(rect, accent, PowerUiDraw.BgSecondary);
            Rect inner = rect.ContractedBy(10f);

            string confText = "PGM_Scenario_Confidence".Translate(ConfidenceLabel(projected.ProjectedConfidence));
            float badgeW = Mathf.Min(180f, inner.width * 0.42f);
            Rect confRect = new Rect(inner.xMax - badgeW, inner.y, badgeW, 20f);
            Color confColor = ConfidenceColor(projected.ProjectedConfidence);
            PowerUiDraw.StatusBadge(confRect, confText, confColor);

            PowerUiDraw.SectionHeader(
                new Rect(inner.x, inner.y, Mathf.Max(40f, confRect.x - inner.x - 8f), 20f),
                "PGM_Scenario_ResultHeader".Translate());

            float y = inner.y + 26f;
            string balance = !deficit
                ? (string)"PGM_Scenario_Surplus".Translate(PowerFormatting.FormatWattsSigned(projected.ProjectedNetWatts))
                : (string)"PGM_Scenario_Deficit".Translate(PowerFormatting.FormatWatts(Mathf.Abs(projected.ProjectedNetWatts)));
            string prefix = deficit ? "[!!] " : "[OK] ";
            PowerUiDraw.TextEllipsized(new Rect(inner.x, y, inner.width, 22f),
                prefix + balance, accent);
            y += 24f;

            string endurance;
            if (projected.ProjectedKind == BatteryForecastKind.NoBatteries)
                endurance = "PGM_Scenario_NoBatteries".Translate();
            else if (projected.ProjectedTicksToEmpty.HasValue)
                endurance = "PGM_Scenario_Depletes".Translate(
                    PowerFormatting.FormatDuration(projected.ProjectedTicksToEmpty.Value));
            else if (projected.ProjectedTicksToFull.HasValue)
                endurance = "PGM_Scenario_Fills".Translate(
                    PowerFormatting.FormatDuration(projected.ProjectedTicksToFull.Value));
            else
                endurance = "PGM_Scenario_Balanced".Translate();

            PowerUiDraw.TextEllipsized(new Rect(inner.x, y, inner.width, 20f),
                endurance, PowerUiDraw.TextPrimary);

            if (projected.ProjectedChargeFraction.HasValue && inner.yMax - (y + 22f) >= 16f)
            {
                Rect bar = new Rect(inner.x, y + 22f, inner.width, 16f);
                PowerUiDraw.BatteryBar(bar, projected.ProjectedChargeFraction.Value,
                    PowerFormatting.FormatCharge(projected.ProjectedChargeFraction.Value), accent);
            }
        }

        private static void DrawAssumptions(Rect rect, PowerScenario projected)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(10f);
            PowerUiDraw.SectionHeader(new Rect(inner.x, inner.y, inner.width, 20f),
                "PGM_Scenario_AssumptionsHeader".Translate());
            float y = inner.y + 24f;
            for (int i = 0; i < projected.Assumptions.Count; i++)
            {
                float rem = inner.yMax - y;
                if (rem < 18f) break;
                float lineH = Mathf.Min(34f, rem);
                PowerUiDraw.WrappedText(new Rect(inner.x, y, inner.width, lineH),
                    "\u2022 " + projected.Assumptions[i], PowerUiDraw.TextSecondary);
                y += lineH;
            }
        }

        private static string ConfidenceLabel(ForecastConfidence confidence)
        {
            switch (confidence)
            {
                case ForecastConfidence.High: return "PGM_Confidence_High".Translate();
                case ForecastConfidence.Medium: return "PGM_Confidence_Medium".Translate();
                case ForecastConfidence.Low: return "PGM_Confidence_Low".Translate();
                default: return "PGM_Confidence_Unavailable".Translate();
            }
        }

        private static Color ConfidenceColor(ForecastConfidence confidence)
        {
            switch (confidence)
            {
                case ForecastConfidence.High: return PowerUiDraw.Good;
                case ForecastConfidence.Medium: return PowerUiDraw.AccentCyan;
                case ForecastConfidence.Low: return PowerUiDraw.Warn;
                default: return PowerUiDraw.TextSecondary;
            }
        }
    }
}
