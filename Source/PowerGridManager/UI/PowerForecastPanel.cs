using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// The forecast tab: battery endurance, how the estimate was reached, and how much it can be
    /// trusted. Every number is derived from observations the dashboard already reports; nothing
    /// here feeds back into the game's power simulation.
    /// </summary>
    public sealed class PowerForecastPanel
    {
        private const float RangeRowHeight = 28f;
        private const float TileHeight = 62f;
        private const float ResultHeight = 112f;
        private const float CompareHeight = 74f;
        private const float NotesHeight = 92f;
        private const float TileGap = 6f;
        private const float RowHeight = 22f;

        private readonly List<PowerNetworkSnapshot> netRows = new List<PowerNetworkSnapshot>(16);

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerHistoryTracker history, PowerUiState state,
            PowerGridManagerSettings settings, int now)
        {
            float y = rect.y;
            DrawRangeRow(new Rect(rect.x, y, rect.width, RangeRowHeight), state);
            y += RangeRowHeight + 4f;

            bool includeSelfDischarge = settings == null || settings.ForecastIncludeSelfDischarge;
            float? average = null;
            int sampleCount = 0;
            if (history != null && history.HasSamples)
            {
                int endTick = history.NewestTick;
                average = PowerForecast.AverageNetWatts(history.Samples,
                    endTick - PowerChartBuilder.TicksFor(state.ForecastRange), endTick, out sampleCount);
            }
            BatteryForecastResult forecast = BatteryForecast.ForMap(snapshot, average, sampleCount,
                includeSelfDischarge);

            float tileWidth = (rect.width - TileGap * 3f) / 4f;
            DrawTile(rect.x, y, tileWidth, "PGM_Forecast_Stored", PowerFormatting.FormatEnergy(forecast.StoredWattDays),
                Color.white);
            DrawTile(rect.x + (tileWidth + TileGap), y, tileWidth, "PGM_Forecast_Capacity",
                PowerFormatting.FormatEnergy(forecast.CapacityWattDays), Color.white);
            DrawTile(rect.x + (tileWidth + TileGap) * 2f, y, tileWidth, "PGM_Forecast_Charge",
                PowerFormatting.FormatCharge(forecast.ChargeFraction), Color.white);
            DrawTile(rect.x + (tileWidth + TileGap) * 3f, y, tileWidth, "PGM_Forecast_Net",
                PowerFormatting.FormatWattsSigned(forecast.EffectiveNetWatts),
                forecast.EffectiveNetWatts < -0.01f ? PowerUiDraw.NoticeColor : Color.white,
                "PGM_Forecast_NetTip".Translate(PowerFormatting.FormatWattsSigned(forecast.TraderNetWatts),
                    PowerFormatting.FormatWatts(forecast.SelfDischargeWatts)));
            y += TileHeight + 6f;

            DrawResult(new Rect(rect.x, y, rect.width, ResultHeight), forecast, state, sampleCount);
            y += ResultHeight + 4f;

            DrawComparison(new Rect(rect.x, y, rect.width, CompareHeight), forecast, sampleCount);
            y += CompareHeight + 4f;

            float remaining = rect.yMax - y - NotesHeight - 4f;
            if (remaining > 60f)
            {
                DrawNetworks(new Rect(rect.x, y, rect.width, remaining), snapshot, includeSelfDischarge, state);
                y += remaining + 4f;
            }

            DrawNotes(new Rect(rect.x, y, rect.width, Mathf.Max(40f, rect.yMax - y)), forecast, includeSelfDischarge,
                settings, now);
        }

        private static void DrawTile(float x, float y, float width, string labelKey, string value, Color color,
            string tooltip = null)
        {
            Rect tile = new Rect(x, y, width, TileHeight);
            PowerUiDraw.MetricTile(tile, labelKey.Translate(), value, color, tooltip);
        }

        private static void DrawRangeRow(Rect rect, PowerUiState state)
        {
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
                if (state.ForecastRange == ranges[i]) Widgets.DrawBoxSolid(button, new Color(1f, 1f, 1f, 0.14f));
                if (Widgets.ButtonText(button, PowerGridUtilities.TimeRangeLabelKey(ranges[i]).Translate()))
                {
                    state.ForecastRange = ranges[i];
                }
                x += buttonWidth + 4f;
            }

            Rect infoRect = new Rect(x + 8f, rect.y, Mathf.Max(0f, rect.xMax - x - 8f), 26f);
            if (infoRect.width > 120f)
            {
                PowerUiDraw.TextEllipsized(infoRect, "PGM_Forecast_RangeTip".Translate(), PowerUiDraw.Muted,
                    TextAnchor.MiddleRight);
            }
        }

        private static void DrawResult(Rect rect, BatteryForecastResult forecast, PowerUiState state, int sampleCount)
        {
            PowerUiDraw.Panel(rect);
            Widgets.DrawBox(rect, 1);

            PowerUiDraw.SectionHeader(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 20f),
                "PGM_Forecast_Title".Translate());

            Color colour = PowerUiDraw.Muted;
            string headline;
            switch (forecast.Kind)
            {
                case BatteryForecastKind.NoBatteries:
                    headline = "PGM_Forecast_NoBatteries".Translate();
                    break;
                case BatteryForecastKind.NoCapacity:
                    headline = "PGM_Forecast_NoCapacity".Translate();
                    break;
                case BatteryForecastKind.Balanced:
                    headline = forecast.StoredWattDays <= 0f
                        ? "PGM_Forecast_Depleted".Translate()
                        : forecast.CapacityWattDays - forecast.StoredWattDays <= 0f
                            ? "PGM_Forecast_Full".Translate()
                            : "PGM_Forecast_Balanced".Translate();
                    break;
                case BatteryForecastKind.Depleting:
                    colour = PowerUiDraw.NoticeColor;
                    headline = forecast.Saturated
                        ? "PGM_Forecast_DepletingLong".Translate(
                            PowerFormatting.FormatDuration(forecast.TicksToEmpty ?? 0))
                        : "PGM_Forecast_Depleting".Translate(PowerFormatting.FormatDuration(forecast.TicksToEmpty ?? 0));
                    break;
                default:
                    colour = PowerUiDraw.Good;
                    headline = forecast.Saturated
                        ? "PGM_Forecast_ChargingLong".Translate(PowerFormatting.FormatDuration(forecast.TicksToFull ?? 0))
                        : "PGM_Forecast_Charging".Translate(PowerFormatting.FormatDuration(forecast.TicksToFull ?? 0));
                    break;
            }

            PowerUiDraw.WrappedText(new Rect(rect.x + 8f, rect.y + 26f, rect.width - 16f, 40f), headline, colour);

            string basis = sampleCount >= BatteryForecast.MinimumHistoricalSamples
                ? "PGM_Forecast_Basis_History".Translate(sampleCount,
                    PowerGridUtilities.TimeRangeLabelKey(state.ForecastRange).Translate())
                : "PGM_Forecast_Basis_Current".Translate();
            PowerUiDraw.DrawText(new Rect(rect.x + 8f, rect.yMax - 38f, rect.width - 16f, 18f),
                "PGM_Forecast_Confidence".Translate(basis, ConfidenceLabel(forecast.Confidence)), PowerUiDraw.Muted);
            PowerUiDraw.DrawText(new Rect(rect.x + 8f, rect.yMax - 22f, rect.width - 16f, 18f),
                "PGM_Forecast_Caveat".Translate(), PowerUiDraw.Muted);
        }

        private static void DrawComparison(Rect rect, BatteryForecastResult forecast, int sampleCount)
        {
            PowerUiDraw.Panel(rect);
            Widgets.DrawBox(rect, 1);
            PowerUiDraw.SectionHeader(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 20f),
                "PGM_Forecast_CompareTitle".Translate());

            if (sampleCount < BatteryForecast.MinimumHistoricalSamples)
            {
                PowerUiDraw.WrappedText(new Rect(rect.x + 8f, rect.y + 26f, rect.width - 16f, 40f),
                    "PGM_Forecast_NoHistory".Translate(BatteryForecast.MinimumHistoricalSamples,
                        PowerFormatting.FormatDuration(PowerGridManagerSettings.DefaultSampleIntervalTicks)),
                    PowerUiDraw.Muted);
                return;
            }

            string current = EstimateText(forecast.TicksToEmpty, forecast.TicksToFull);
            string historical = EstimateText(forecast.HistoricalTicksToEmpty, forecast.HistoricalTicksToFull);
            PowerUiDraw.DrawText(new Rect(rect.x + 8f, rect.y + 26f, rect.width - 16f, 18f),
                "PGM_Forecast_CompareCurrent".Translate(PowerFormatting.FormatWattsSigned(forecast.EffectiveNetWatts),
                    current), Color.white);
            PowerUiDraw.DrawText(new Rect(rect.x + 8f, rect.y + 44f, rect.width - 16f, 18f),
                "PGM_Forecast_CompareHistory".Translate(
                    PowerFormatting.FormatWattsSigned(forecast.HistoricalNetWatts ?? 0f), historical, sampleCount),
                Color.white);
        }

        private static string EstimateText(int? toEmpty, int? toFull)
        {
            if (toEmpty.HasValue) return PowerFormatting.FormatDuration(toEmpty.Value);
            if (toFull.HasValue) return PowerFormatting.FormatDuration(toFull.Value);
            return "PGM_Value_NotEstimated".Translate();
        }

        private void DrawNetworks(Rect rect, PowerGridSnapshot snapshot, bool includeSelfDischarge,
            PowerUiState state)
        {
            PowerUiDraw.Panel(rect);
            netRows.Clear();
            if (snapshot != null && snapshot.Networks != null)
            {
                for (int i = 0; i < snapshot.Networks.Count; i++)
                {
                    PowerNetworkSnapshot network = snapshot.Networks[i];
                    if (network != null && network.BatteryCount > 0) netRows.Add(network);
                }
            }

            PowerUiDraw.SectionHeader(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 20f),
                "PGM_Forecast_NetworksHeader".Translate(netRows.Count));

            if (netRows.Count == 0)
            {
                PowerUiDraw.WrappedText(new Rect(rect.x + 10f, rect.y + 28f, rect.width - 20f, 60f),
                    "PGM_Forecast_NoNetworks".Translate(), PowerUiDraw.Muted);
                return;
            }

            Rect viewOuter = new Rect(rect.x + 4f, rect.y + 28f, rect.width - 8f, rect.height - 32f);
            Rect view = new Rect(0f, 0f, viewOuter.width - 18f,
                Mathf.Max(netRows.Count * RowHeight, viewOuter.height));
            Widgets.BeginScrollView(viewOuter, ref state.ForecastScroll, view);
            try
            {
                float y = 0f;
                for (int i = 0; i < netRows.Count; i++)
                {
                    PowerNetworkSnapshot network = netRows[i];
                    BatteryForecastResult forecast = BatteryForecast.ForNetwork(network, includeSelfDischarge);
                    Rect row = new Rect(0f, y, view.width, RowHeight);
                    PowerUiDraw.TextEllipsized(new Rect(row.x + 4f, row.y, 110f, row.height),
                        "PGM_Network_Number".Translate(network.Id), Color.white);
                    PowerUiDraw.DrawText(new Rect(row.x + 120f, row.y, 90f, row.height),
                        PowerFormatting.FormatCharge(network.ChargeFraction), Color.white, TextAnchor.MiddleRight);
                    PowerUiDraw.DrawText(new Rect(row.x + 220f, row.y, 110f, row.height),
                        PowerFormatting.FormatWattsSigned(forecast.EffectiveNetWatts), PowerUiDraw.Muted,
                        TextAnchor.MiddleRight);
                    PowerUiDraw.TextEllipsized(
                        new Rect(row.x + 340f, row.y, Mathf.Max(40f, row.width - 344f), row.height),
                        NetworkEstimate(forecast), PowerUiDraw.Muted);
                    PowerUiDraw.Tip(row, "PGM_Forecast_NetworkTip".Translate(PowerFormatting.FormatEnergy(
                        network.StoredWattDays), PowerFormatting.FormatEnergy(network.CapacityWattDays)));
                    y += RowHeight;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private static string NetworkEstimate(BatteryForecastResult forecast)
        {
            switch (forecast.Kind)
            {
                case BatteryForecastKind.Depleting:
                    return "PGM_Forecast_Short_Depleting".Translate(
                        PowerFormatting.FormatDuration(forecast.TicksToEmpty ?? 0));
                case BatteryForecastKind.Charging:
                    return "PGM_Forecast_Short_Charging".Translate(
                        PowerFormatting.FormatDuration(forecast.TicksToFull ?? 0));
                case BatteryForecastKind.NoCapacity:
                    return "PGM_Value_Unknown".Translate();
                default:
                    return "PGM_Forecast_Short_Balanced".Translate();
            }
        }

        private static void DrawNotes(Rect rect, BatteryForecastResult forecast, bool includeSelfDischarge,
            PowerGridManagerSettings settings, int now)
        {
            PowerUiDraw.Panel(rect);
            PowerUiDraw.SectionHeader(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 20f),
                "PGM_Forecast_NotesTitle".Translate());

            string selfDischarge = includeSelfDischarge
                ? "PGM_Forecast_SelfDischargeOn".Translate(PowerFormatting.FormatWatts(forecast.SelfDischargeWatts),
                    forecast.BatteryCount)
                : "PGM_Forecast_SelfDischargeOff".Translate();
            string efficiency = forecast.EfficiencyKnown
                ? "PGM_Forecast_EfficiencyKnown".Translate(PowerFormatting.FormatCharge(forecast.ChargeFraction))
                : "PGM_Forecast_EfficiencyUnknown".Translate();

            PowerUiDraw.WrappedText(new Rect(rect.x + 8f, rect.y + 26f, rect.width - 16f, rect.height - 32f),
                "PGM_Forecast_Notes".Translate(selfDischarge, efficiency), PowerUiDraw.Muted);
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
