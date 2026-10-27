using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// The scenario-planning section of the Planning tab. Evaluates hypothetical changes
    /// (added generation, batteries, consumers, switched-off load) and presents the
    /// projected balance and endurance. Nothing here modifies the live game.
    /// </summary>
    public sealed class PowerScenarioPanel
    {
        private const float InputRowHeight = 28f;
        private const float TileHeight = 62f;
        private const float TileGap = 6f;
        private const float RowHeight = 22f;

        private string addedGenerationText = "";
        private string addedBatteryText = "";
        private string addedConsumerText = "";
        private string switchedOffText = "";

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings)
        {
            float y = rect.y;

            Rect inputArea = new Rect(rect.x, y, rect.width, 96f);
            DrawInputs(inputArea, snapshot, settings);
            y += 96f + 6f;

            PowerScenario scenario = ReadScenario(settings);
            PowerScenario projected = PowerScenarioPlanner.Evaluate(snapshot, scenario, settings,
                settings == null ? 0f : settings.ReserveMarginWatts);

            float tileWidth = (rect.width - TileGap * 3f) / 4f;
            DrawTile(rect.x, y, tileWidth, "PGM_Scenario_ProjectedGeneration",
                PowerFormatting.FormatWatts(projected.ProjectedGenerationWatts), Color.white);
            DrawTile(rect.x + (tileWidth + TileGap), y, tileWidth,
                "PGM_Scenario_ProjectedConsumption",
                PowerFormatting.FormatWatts(projected.ProjectedConsumptionWatts), Color.white);
            DrawTile(rect.x + (tileWidth + TileGap) * 2f, y, tileWidth,
                "PGM_Scenario_ProjectedNet",
                PowerFormatting.FormatWattsSigned(projected.ProjectedNetWatts),
                projected.ProjectedNetWatts < -0.01f ? PowerUiDraw.NoticeColor : Color.white);
            DrawTile(rect.x + (tileWidth + TileGap) * 3f, y, tileWidth,
                "PGM_Scenario_ReserveMargin",
                PowerFormatting.FormatWatts(projected.ReserveMarginWatts),
                projected.ReserveTargetSatisfied ? PowerUiDraw.Good : PowerUiDraw.Warn);
            y += TileHeight + 6f;

            Rect result = new Rect(rect.x, y, rect.width, 100f);
            DrawResult(result, projected);
            y += 100f + 6f;

            DrawAssumptions(new Rect(rect.x, y, rect.width, rect.yMax - y), projected);
        }

        private void DrawInputs(Rect rect, PowerGridSnapshot snapshot,
            PowerGridManagerSettings settings)
        {
            PowerUiDraw.Panel(rect);
            PowerUiDraw.SectionHeader(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 20f),
                "PGM_Scenario_InputsHeader".Translate());

            float y = rect.y + 28f;
            float x = rect.x + 6f;
            float fieldWidth = Mathf.Min(160f, (rect.width - 30f) / 4f);

            addedGenerationText = Field(new Rect(x, y, fieldWidth, 24f),
                "PGM_Scenario_AddGeneration".Translate(), addedGenerationText);
            x += fieldWidth + 6f;
            addedBatteryText = Field(new Rect(x, y, fieldWidth, 24f),
                "PGM_Scenario_AddBattery".Translate(), addedBatteryText);
            x += fieldWidth + 6f;
            addedConsumerText = Field(new Rect(x, y, fieldWidth, 24f),
                "PGM_Scenario_AddConsumer".Translate(), addedConsumerText);
            x += fieldWidth + 6f;
            switchedOffText = Field(new Rect(x, y, fieldWidth, 24f),
                "PGM_Scenario_SwitchOff".Translate(), switchedOffText);

            y += 28f;
            PowerUiDraw.WrappedText(new Rect(rect.x + 6f, y, rect.width - 12f, 44f),
                "PGM_Scenario_InputsTip".Translate(), PowerUiDraw.Muted);
        }

        private static string Field(Rect rect, string label, string text)
        {
            PowerUiDraw.DrawText(new Rect(rect.x, rect.y, rect.width, 18f), label,
                PowerUiDraw.Muted, TextAnchor.MiddleLeft);
            Rect field = new Rect(rect.x, rect.y + 18f, rect.width, 20f);
            return Widgets.TextField(field, text);
        }

        private PowerScenario ReadScenario(PowerGridManagerSettings settings)
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

        private void DrawTile(float x, float y, float width, string labelKey, string value,
            Color color)
        {
            Rect tile = new Rect(x, y, width, TileHeight);
            PowerUiDraw.MetricTile(tile, labelKey.Translate(), value, color);
        }

        private void DrawResult(Rect rect, PowerScenario projected)
        {
            PowerUiDraw.Panel(rect);
            Widgets.DrawBox(rect, 1);
            PowerUiDraw.SectionHeader(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 20f),
                "PGM_Scenario_ResultHeader".Translate());

            float y = rect.y + 26f;
            string balance = projected.ProjectedNetWatts >= -0.01f
                ? "PGM_Scenario_Surplus".Translate(PowerFormatting.FormatWattsSigned(projected.ProjectedNetWatts))
                : "PGM_Scenario_Deficit".Translate(PowerFormatting.FormatWattsSigned(-projected.ProjectedNetWatts));
            PowerUiDraw.DrawText(new Rect(rect.x + 6f, y, rect.width - 12f, 20f), balance,
                projected.ProjectedNetWatts < -0.01f ? PowerUiDraw.NoticeColor : Color.white);
            y += 22f;

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
            PowerUiDraw.DrawText(new Rect(rect.x + 6f, y, rect.width - 12f, 20f), endurance,
                PowerUiDraw.Muted);
            y += 22f;

            PowerUiDraw.DrawText(new Rect(rect.x + 6f, y, rect.width - 12f, 20f),
                "PGM_Scenario_Confidence".Translate(
                    ConfidenceLabel(projected.ProjectedConfidence)), PowerUiDraw.Muted);
        }

        private void DrawAssumptions(Rect rect, PowerScenario projected)
        {
            PowerUiDraw.Panel(rect);
            PowerUiDraw.SectionHeader(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 20f),
                "PGM_Scenario_AssumptionsHeader".Translate());
            float y = rect.y + 26f;
            float usableHeight = rect.height - 30f;
            for (int i = 0; i < projected.Assumptions.Count; i++)
            {
                if (y + 18f > rect.yMax) break;
                PowerUiDraw.DrawText(new Rect(rect.x + 6f, y, rect.width - 12f, 18f),
                    "• " + projected.Assumptions[i], PowerUiDraw.Muted);
                y += 18f;
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
    }
}
