using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Modernized battery endurance forecast tab: displays dark KPI statistics cards,
    /// prominent battery endurance assessment with charge progress bar and confidence badge,
    /// current vs historical comparison, per-network endurance table, and explicit assumptions.
    /// </summary>
    public sealed class PowerForecastPanel
    {
        private const float RangeRowHeight = 36f;
        private const float TileHeight = 72f;
        private const float ResultHeight = 132f;
        private const float CompareHeight = 82f;
        private const float NotesHeight = 96f;
        private const float TileGap = 8f;
        private const float RowHeight = 26f;

        private readonly List<PowerNetworkSnapshot> netRows = new List<PowerNetworkSnapshot>(16);
        private Vector2 outerScroll;

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerHistoryTracker history, PowerUiState state,
            PowerGridManagerSettings settings, int now)
        {
            if (state == null || rect.width <= 8f || rect.height <= 8f) return;

            float rangeH = rect.height < 140f ? 26f : RangeRowHeight;
            DrawRangeRow(new Rect(rect.x, rect.y, rect.width, rangeH), state);
            float y = rect.y + rangeH + PowerUiDraw.SpaceRelated;

            Rect bodyRect = new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y));
            if (bodyRect.height <= 12f) return;

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

            const float minBodyH = 440f;
            if (bodyRect.height < minBodyH)
            {
                Rect view = new Rect(0f, 0f, Mathf.Max(40f, bodyRect.width - 16f), minBodyH);
                Widgets.BeginScrollView(bodyRect, ref outerScroll, view);
                try
                {
                    DrawBody(view, snapshot, forecast, sampleCount, includeSelfDischarge, state, settings);
                }
                finally
                {
                    Widgets.EndScrollView();
                }
                return;
            }

            DrawBody(bodyRect, snapshot, forecast, sampleCount, includeSelfDischarge, state, settings);
        }

        private void DrawBody(Rect area, PowerGridSnapshot snapshot, BatteryForecastResult forecast,
            int sampleCount, bool includeSelfDischarge, PowerUiState state, PowerGridManagerSettings settings)
        {
            float y = area.y;
            float lowWarn = settings != null ? settings.LowBatteryWarningFraction : 0.25f;

            // 1. KPI cards row.
            float tileWidth = (area.width - TileGap * 3f) / 4f;
            PowerUiDraw.StatCard(new Rect(area.x, y, tileWidth, TileHeight),
                "PGM_Forecast_Stored".Translate(),
                PowerFormatting.FormatEnergy(forecast.StoredWattDays),
                PowerUiDraw.TextPrimary,
                PowerUiDraw.AccentPrimary);

            PowerUiDraw.StatCard(new Rect(area.x + (tileWidth + TileGap), y, tileWidth, TileHeight),
                "PGM_Forecast_Capacity".Translate(),
                PowerFormatting.FormatEnergy(forecast.CapacityWattDays),
                PowerUiDraw.TextPrimary,
                PowerUiDraw.AccentPrimary);

            Color chargeCol = PowerUiDraw.ChargeColor(forecast.ChargeFraction, lowWarn);
            PowerUiDraw.StatCard(new Rect(area.x + (tileWidth + TileGap) * 2f, y, tileWidth, TileHeight),
                "PGM_Forecast_Charge".Translate(),
                PowerFormatting.FormatCharge(forecast.ChargeFraction),
                chargeCol,
                chargeCol,
                null,
                null,
                forecast.ChargeFraction.HasValue ? (float?)Mathf.Clamp01(forecast.ChargeFraction.Value) : null,
                chargeCol);

            Color netCol = PowerUiDraw.SignedColor(forecast.EffectiveNetWatts);
            PowerUiDraw.StatCard(new Rect(area.x + (tileWidth + TileGap) * 3f, y, tileWidth, TileHeight),
                "PGM_Forecast_Net".Translate(),
                PowerFormatting.FormatWattsSigned(forecast.EffectiveNetWatts),
                forecast.EffectiveNetWatts < -0.01f ? PowerUiDraw.NoticeColor : (forecast.EffectiveNetWatts > 0.01f ? PowerUiDraw.Good : PowerUiDraw.TextPrimary),
                netCol,
                null,
                "PGM_Forecast_NetTip".Translate(
                    PowerFormatting.FormatWattsSigned(forecast.TraderNetWatts),
                    PowerFormatting.FormatWatts(forecast.SelfDischargeWatts)));
            y += TileHeight + PowerUiDraw.SpaceRelated;

            // 2. Primary Endurance Result Card.
            DrawResult(new Rect(area.x, y, area.width, ResultHeight), forecast, state, sampleCount, lowWarn);
            y += ResultHeight + PowerUiDraw.SpaceRelated;

            // 3. Current vs Historical Comparison Card.
            DrawComparison(new Rect(area.x, y, area.width, CompareHeight), forecast, sampleCount);
            y += CompareHeight + PowerUiDraw.SpaceRelated;

            // 4. Per-network endurance table (if vertical space permits).
            float remaining = area.yMax - y - NotesHeight - PowerUiDraw.SpaceRelated;
            if (remaining > 64f)
            {
                DrawNetworks(new Rect(area.x, y, area.width, remaining), snapshot, includeSelfDischarge, state, lowWarn);
                y += remaining + PowerUiDraw.SpaceRelated;
            }

            // 5. Explicit forecast assumptions & notes.
            DrawNotes(new Rect(area.x, y, area.width, Mathf.Max(40f, area.yMax - y)), forecast, includeSelfDischarge);
        }

        private static void DrawRangeRow(Rect rect, PowerUiState state)
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
                bool selected = state.ForecastRange == ranges[i];
                if (PowerUiDraw.Button(button, PowerGridUtilities.TimeRangeLabelKey(ranges[i]).Translate(), selected))
                    state.ForecastRange = ranges[i];
                x += buttonWidth + 4f;
            }

            Rect infoRect = new Rect(x + 8f, inner.y, Mathf.Max(0f, inner.xMax - x - 10f), inner.height);
            if (infoRect.width > 120f)
            {
                PowerUiDraw.TextEllipsized(infoRect, "PGM_Forecast_RangeTip".Translate(),
                    PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);
            }
        }

        private static void DrawResult(Rect rect, BatteryForecastResult forecast, PowerUiState state,
            int sampleCount, float lowWarn)
        {
            Color colour = PowerUiDraw.TextSecondary;
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
                    colour = PowerUiDraw.Good;
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
                        : "PGM_Forecast_Depleting".Translate(
                            PowerFormatting.FormatDuration(forecast.TicksToEmpty ?? 0));
                    break;
                default:
                    colour = PowerUiDraw.AccentCyan;
                    headline = forecast.Saturated
                        ? "PGM_Forecast_ChargingLong".Translate(PowerFormatting.FormatDuration(forecast.TicksToFull ?? 0))
                        : "PGM_Forecast_Charging".Translate(PowerFormatting.FormatDuration(forecast.TicksToFull ?? 0));
                    break;
            }

            PowerUiDraw.AccentCard(rect, colour, false);
            Rect inner = rect.ContractedBy(PowerUiDraw.PadPanel);

            string confLabel = "PGM_Scenario_Confidence".Translate(ConfidenceLabel(forecast.Confidence));
            PowerUiDraw.SectionHeader(new Rect(inner.x, inner.y, inner.width, 22f),
                "PGM_Forecast_Title".Translate(), confLabel);

            PowerUiDraw.WrappedText(new Rect(inner.x, inner.y + 26f, inner.width, 28f), headline, colour);

            if (forecast.HasStorage)
            {
                Color barCol = PowerUiDraw.ChargeColor(forecast.ChargeFraction, lowWarn);
                PowerUiDraw.BatteryBar(new Rect(inner.x, inner.y + 56f, inner.width, 11f),
                    forecast.ChargeFraction ?? 0f, barCol, lowWarn);
            }

            string basis = sampleCount >= BatteryForecast.MinimumHistoricalSamples
                ? "PGM_Forecast_Basis_History".Translate(sampleCount,
                    PowerGridUtilities.TimeRangeLabelKey(state.ForecastRange).Translate())
                : "PGM_Forecast_Basis_Current".Translate();
            PowerUiDraw.TextEllipsized(new Rect(inner.x, inner.yMax - 36f, inner.width, 18f),
                "PGM_Forecast_Confidence".Translate(basis, ConfidenceLabel(forecast.Confidence)),
                PowerUiDraw.TextPrimary);
            PowerUiDraw.TextEllipsized(new Rect(inner.x, inner.yMax - 18f, inner.width, 18f),
                "PGM_Forecast_Caveat".Translate(), PowerUiDraw.TextSecondary);
        }

        private static void DrawComparison(Rect rect, BatteryForecastResult forecast, int sampleCount)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(10f);
            PowerUiDraw.SectionHeader(new Rect(inner.x, inner.y, inner.width, 20f),
                "PGM_Forecast_CompareTitle".Translate());

            if (sampleCount < BatteryForecast.MinimumHistoricalSamples)
            {
                PowerUiDraw.WrappedText(new Rect(inner.x, inner.y + 24f, inner.width, inner.height - 24f),
                    "PGM_Forecast_NoHistory".Translate(BatteryForecast.MinimumHistoricalSamples,
                        PowerFormatting.FormatDuration(PowerGridManagerSettings.DefaultSampleIntervalTicks)),
                    PowerUiDraw.TextSecondary);
                return;
            }

            string current = EstimateText(forecast.TicksToEmpty, forecast.TicksToFull);
            string historical = EstimateText(forecast.HistoricalTicksToEmpty, forecast.HistoricalTicksToFull);
            PowerUiDraw.TextEllipsized(new Rect(inner.x, inner.y + 24f, inner.width, 18f),
                "PGM_Forecast_CompareCurrent".Translate(
                    PowerFormatting.FormatWattsSigned(forecast.EffectiveNetWatts), current),
                PowerUiDraw.TextPrimary);
            PowerUiDraw.TextEllipsized(new Rect(inner.x, inner.y + 42f, inner.width, 18f),
                "PGM_Forecast_CompareHistory".Translate(
                    PowerFormatting.FormatWattsSigned(forecast.HistoricalNetWatts ?? 0f), historical, sampleCount),
                PowerUiDraw.AccentCyan);
        }

        private static string EstimateText(int? toEmpty, int? toFull)
        {
            if (toEmpty.HasValue) return PowerFormatting.FormatDuration(toEmpty.Value);
            if (toFull.HasValue) return PowerFormatting.FormatDuration(toFull.Value);
            return "PGM_Value_NotEstimated".Translate();
        }

        private void DrawNetworks(Rect rect, PowerGridSnapshot snapshot, bool includeSelfDischarge,
            PowerUiState state, float lowWarn)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(10f);
            netRows.Clear();
            if (snapshot != null && snapshot.Networks != null)
            {
                for (int i = 0; i < snapshot.Networks.Count; i++)
                {
                    PowerNetworkSnapshot network = snapshot.Networks[i];
                    if (network != null && network.BatteryCount > 0) netRows.Add(network);
                }
            }

            PowerUiDraw.SectionHeader(new Rect(inner.x, inner.y, inner.width, 22f),
                "PGM_Forecast_NetworksHeader".Translate(netRows.Count));

            if (netRows.Count == 0)
            {
                PowerUiDraw.WrappedText(new Rect(inner.x, inner.y + 26f, inner.width, 48f),
                    "PGM_Forecast_NoNetworks".Translate(), PowerUiDraw.TextSecondary);
                return;
            }

            Rect viewOuter = new Rect(inner.x, inner.y + 26f, inner.width, Mathf.Max(20f, inner.height - 26f));
            Rect view = new Rect(0f, 0f, Mathf.Max(40f, viewOuter.width - 18f),
                Mathf.Max(netRows.Count * RowHeight, viewOuter.height));
            Widgets.BeginScrollView(viewOuter, ref state.ForecastScroll, view);
            try
            {
                float y = 0f;
                for (int i = 0; i < netRows.Count; i++)
                {
                    PowerNetworkSnapshot network = netRows[i];
                    BatteryForecastResult fc = BatteryForecast.ForNetwork(network, includeSelfDischarge);
                    Rect row = new Rect(0f, y, view.width, RowHeight);
                    PowerUiDraw.RowBackground(row, i, false);

                    PowerUiDraw.TextEllipsized(new Rect(row.x + 6f, row.y, 108f, row.height),
                        "PGM_Network_Number".Translate(network.Id), PowerUiDraw.TextPrimary);

                    Color chgCol = PowerUiDraw.ChargeColor(network.ChargeFraction, lowWarn);
                    if (row.width >= 420f)
                    {
                        PowerUiDraw.Bar(new Rect(row.x + 118f, row.y + (row.height - 8f) * 0.5f, 80f, 8f),
                            network.ChargeFraction ?? 0f, chgCol);
                        PowerUiDraw.DrawText(new Rect(row.x + 202f, row.y, 64f, row.height),
                            PowerFormatting.FormatCharge(network.ChargeFraction), chgCol, TextAnchor.MiddleRight);
                        PowerUiDraw.DrawText(new Rect(row.x + 274f, row.y, 108f, row.height),
                            PowerFormatting.FormatWattsSigned(fc.EffectiveNetWatts),
                            PowerUiDraw.SignedColor(fc.EffectiveNetWatts), TextAnchor.MiddleRight);
                        PowerUiDraw.TextEllipsized(
                            new Rect(row.x + 394f, row.y, Mathf.Max(40f, row.width - 400f), row.height),
                            NetworkEstimate(fc),
                            fc.IsDepleting ? PowerUiDraw.NoticeColor : (fc.IsCharging ? PowerUiDraw.AccentCyan : PowerUiDraw.TextSecondary));
                    }
                    else
                    {
                        PowerUiDraw.DrawText(new Rect(row.x + 112f, row.y, 64f, row.height),
                            PowerFormatting.FormatCharge(network.ChargeFraction), chgCol, TextAnchor.MiddleRight);
                        PowerUiDraw.TextEllipsized(
                            new Rect(row.x + 184f, row.y, Mathf.Max(30f, row.width - 190f), row.height),
                            NetworkEstimate(fc), PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);
                    }

                    PowerUiDraw.Tip(row, "PGM_Forecast_NetworkTip".Translate(
                        PowerFormatting.FormatEnergy(network.StoredWattDays),
                        PowerFormatting.FormatEnergy(network.CapacityWattDays)));
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

        private static void DrawNotes(Rect rect, BatteryForecastResult forecast, bool includeSelfDischarge)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(10f);
            PowerUiDraw.SectionHeader(new Rect(inner.x, inner.y, inner.width, 20f),
                "PGM_Forecast_NotesTitle".Translate());

            string selfDischarge = includeSelfDischarge
                ? "PGM_Forecast_SelfDischargeOn".Translate(
                    PowerFormatting.FormatWatts(forecast.SelfDischargeWatts), forecast.BatteryCount)
                : "PGM_Forecast_SelfDischargeOff".Translate();
            string efficiency = forecast.EfficiencyKnown
                ? "PGM_Forecast_EfficiencyKnown".Translate(PowerFormatting.FormatCharge(forecast.ChargeFraction))
                : "PGM_Forecast_EfficiencyUnknown".Translate();

            PowerUiDraw.WrappedText(new Rect(inner.x, inner.y + 24f, inner.width, Mathf.Max(0f, inner.height - 24f)),
                "PGM_Forecast_Notes".Translate(selfDischarge, efficiency), PowerUiDraw.TextSecondary);
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
