using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Primary Overview dashboard view:
    /// - Top row of four KPI statistics cards (Generation, Consumption, Net Balance, Battery Charge)
    /// - Two-column structured panel grid:
    ///   1. Power Overview (dark Generation vs Consumption graph or live measured/configured summary)
    ///   2. Network Status (honest operational status badge, topology totals, battery availability, per-network health)
    ///   3. Top Power Consumers (sortable ranked consumer table with click-to-inspect)
    ///   4. Alerts &amp; Recommendations (modern notification cards or clean nominal status card)
    /// </summary>
    public sealed class PowerOverviewPanel
    {
        private readonly List<BuildingPowerSnapshot> cachedConsumers = new List<BuildingPowerSnapshot>(32);
        private PowerGridSnapshot cachedSnapshot;
        private bool cachedAscending;
        private int cachedSwitchedOffConsumers;

        private PowerChartData cachedGenConChart;
        private PowerHistoryTracker cachedHistory;
        private int cachedHistoryCount = -1;
        private int cachedNewestTick = -1;
        private int cachedColumns = -1;

        private Vector2 fallbackScroll;
        private Vector2 fallbackConsumerScroll;
        private Vector2 fallbackAlertScroll;

        public void Draw(
            Rect rect,
            PowerGridSnapshot snapshot,
            int currentTick,
            PowerGridManagerSettings settings,
            BatteryForecastResult forecast = null,
            int criticalAdvisories = 0,
            int warningAdvisories = 0,
            PowerHistoryTracker history = null,
            IReadOnlyList<PowerDiagnostic> advisories = null,
            IReadOnlyList<PowerRecommendation> recommendations = null,
            PowerUiState state = null,
            Action<BuildingPowerSnapshot> onInspectBuilding = null,
            Action<int> onInspectNetwork = null,
            Action onOpenDiagnostics = null)
        {
            if (snapshot == null || settings == null) return;
            if (rect.width <= 4f || rect.height <= 4f) return;

            int highLoadCount = CountHighLoadNetworks(snapshot, settings.HighLoadThreshold);
            EnsureCachedConsumers(snapshot, state != null && state.OverviewConsumersAscending);

            float kpiRowHeight = rect.width < 600f ? 140f : 78f;
            float minGridHeight = rect.width < 660f ? 780f : 430f;
            float footerHeight = 30f;
            float neededTotal = kpiRowHeight + PowerUiDraw.SectionGap + minGridHeight + PowerUiDraw.SpaceRelated + footerHeight;

            if (rect.height < neededTotal)
            {
                Vector2 scroll = state != null ? state.OverviewScroll : fallbackScroll;
                float viewW = Mathf.Max(40f, rect.width - 16f);
                Rect view = new Rect(0f, 0f, viewW, neededTotal);
                Widgets.BeginScrollView(rect, ref scroll, view);
                try
                {
                    DrawDashboardBody(view, snapshot, currentTick, settings, forecast,
                        criticalAdvisories, warningAdvisories, highLoadCount,
                        history, advisories, recommendations, state,
                        onInspectBuilding, onInspectNetwork, onOpenDiagnostics);
                }
                finally
                {
                    Widgets.EndScrollView();
                }

                if (state != null) state.OverviewScroll = scroll;
                else fallbackScroll = scroll;
                return;
            }

            DrawDashboardBody(rect, snapshot, currentTick, settings, forecast,
                criticalAdvisories, warningAdvisories, highLoadCount,
                history, advisories, recommendations, state,
                onInspectBuilding, onInspectNetwork, onOpenDiagnostics);
        }

        private void DrawDashboardBody(
            Rect rect,
            PowerGridSnapshot snapshot,
            int currentTick,
            PowerGridManagerSettings settings,
            BatteryForecastResult forecast,
            int criticalAdvisories,
            int warningAdvisories,
            int highLoadCount,
            PowerHistoryTracker history,
            IReadOnlyList<PowerDiagnostic> advisories,
            IReadOnlyList<PowerRecommendation> recommendations,
            PowerUiState state,
            Action<BuildingPowerSnapshot> onInspectBuilding,
            Action<int> onInspectNetwork,
            Action onOpenDiagnostics)
        {
            float y = rect.y;

            // 1. Primary KPI Statistics Cards Row.
            float kpiH = rect.width < 600f ? 140f : 78f;
            DrawStatisticsRow(new Rect(rect.x, y, rect.width, kpiH), snapshot, settings, forecast);
            y += kpiH + PowerUiDraw.SectionGap;

            // 2. Structured 2x2 Dashboard Panels.
            float footerH = 30f;
            float gridH = Mathf.Max(180f, rect.yMax - y - footerH - PowerUiDraw.SpaceRelated);
            Rect gridRect = new Rect(rect.x, y, rect.width, gridH);

            if (rect.width < 660f)
            {
                float gap = PowerUiDraw.SpaceRelated;
                float each = Mathf.Max(110f, (gridH - gap * 3f) * 0.25f);
                float cy = gridRect.y;
                DrawPowerOverviewPanel(new Rect(gridRect.x, cy, gridRect.width, each),
                    snapshot, currentTick, settings, history);
                cy += each + gap;
                DrawNetworkStatusPanel(new Rect(gridRect.x, cy, gridRect.width, each),
                    snapshot, settings, forecast, criticalAdvisories, warningAdvisories, highLoadCount, onInspectNetwork);
                cy += each + gap;
                DrawTopConsumersPanel(new Rect(gridRect.x, cy, gridRect.width, each),
                    snapshot, settings, state, onInspectBuilding);
                cy += each + gap;
                DrawAlertsPanel(new Rect(gridRect.x, cy, gridRect.width, Mathf.Max(90f, gridRect.yMax - cy)),
                    snapshot, settings, criticalAdvisories, warningAdvisories,
                    advisories, recommendations, state, onOpenDiagnostics);
            }
            else
            {
                float colGap = PowerUiDraw.SectionGap;
                float rowGap = PowerUiDraw.SectionGap;
                float leftW = Mathf.Floor((gridRect.width - colGap) * 0.54f);
                float rightW = gridRect.width - leftW - colGap;
                float topH = Mathf.Floor((gridRect.height - rowGap) * 0.52f);
                float bottomH = gridRect.height - topH - rowGap;

                DrawPowerOverviewPanel(new Rect(gridRect.x, gridRect.y, leftW, topH),
                    snapshot, currentTick, settings, history);
                DrawNetworkStatusPanel(new Rect(gridRect.x + leftW + colGap, gridRect.y, rightW, topH),
                    snapshot, settings, forecast, criticalAdvisories, warningAdvisories, highLoadCount, onInspectNetwork);

                DrawTopConsumersPanel(new Rect(gridRect.x, gridRect.y + topH + rowGap, leftW, bottomH),
                    snapshot, settings, state, onInspectBuilding);
                DrawAlertsPanel(new Rect(gridRect.x + leftW + colGap, gridRect.y + topH + rowGap, rightW, bottomH),
                    snapshot, settings, criticalAdvisories, warningAdvisories,
                    advisories, recommendations, state, onOpenDiagnostics);
            }

            // 3. Measurement contract footer bar.
            Rect footerRect = new Rect(rect.x, gridRect.yMax + PowerUiDraw.SpaceRelated, rect.width, footerH);
            DrawMeasurementFooter(footerRect, snapshot, currentTick);
        }

        private static void DrawStatisticsRow(Rect rect, PowerGridSnapshot snapshot,
            PowerGridManagerSettings settings, BatteryForecastResult forecast)
        {
            float gap = PowerUiDraw.SpaceRelated;
            int cols = rect.width < 600f ? 2 : 4;
            int rows = cols == 2 ? 2 : 1;
            float cardW = (rect.width - gap * (cols - 1)) / cols;
            float cardH = rows == 2 ? (rect.height - gap) * 0.5f : rect.height;

            // Card 1: Total Generation.
            Color genColor = snapshot.GenerationWatts > 0.01f ? PowerUiDraw.AccentCyan : PowerUiDraw.TextSecondary;
            string genSub = "PGM_Overview_GenCardSub".Translate(
                snapshot.GeneratorCount,
                PowerFormatting.FormatWattsUnknown(snapshot.ConfiguredGenerationWatts));
            Rect c0 = CardSlot(rect, 0, cols, cardW, cardH, gap);
            PowerUiDraw.StatCard(c0,
                "PGM_Tile_CurrentGeneration".Translate(),
                PowerFormatting.FormatWatts(snapshot.GenerationWatts),
                genColor,
                PowerUiDraw.AccentCyan,
                genSub,
                "PGM_Tip_CurrentGeneration".Translate());

            // Card 2: Total Consumption.
            bool highLoad = snapshot.LoadRatio.HasValue && snapshot.LoadRatio.Value >= settings.HighLoadThreshold;
            Color conColor = highLoad ? PowerUiDraw.Warn : PowerUiDraw.TextPrimary;
            string conSub = "PGM_Overview_ConCardSub".Translate(
                snapshot.ConsumerCount,
                PowerFormatting.FormatCharge(snapshot.LoadRatio));
            float? loadBar = snapshot.LoadRatio.HasValue ? (float?)Mathf.Clamp01(snapshot.LoadRatio.Value) : null;
            Rect c1 = CardSlot(rect, 1, cols, cardW, cardH, gap);
            PowerUiDraw.StatCard(c1,
                "PGM_Tile_CurrentConsumption".Translate(),
                PowerFormatting.FormatWatts(snapshot.ConsumptionWatts),
                conColor,
                highLoad ? PowerUiDraw.Warn : PowerUiDraw.AccentPrimary,
                conSub,
                "PGM_Tip_CurrentConsumption".Translate(),
                loadBar,
                highLoad ? PowerUiDraw.Warn : PowerUiDraw.AccentPrimary);

            // Card 3: Net Power Balance.
            Color netColor = PowerUiDraw.BalanceColor(snapshot, settings);
            string netSub = BuildEnduranceSubtitle(snapshot, forecast);
            Rect c2 = CardSlot(rect, 2, cols, cardW, cardH, gap);
            PowerUiDraw.StatCard(c2,
                "PGM_Tile_NetBalance".Translate(),
                PowerFormatting.FormatWattsSigned(snapshot.NetWatts),
                netColor,
                netColor,
                netSub,
                "PGM_Tip_NetBalance".Translate());

            // Card 4: Battery Charge (with prominent horizontal progress bar).
            Color chargeColor = PowerUiDraw.ChargeColor(snapshot.ChargeFraction, settings.LowBatteryWarningFraction);
            string chargeSub = snapshot.BatteryCount > 0
                ? (string)"PGM_Overview_BatCardSub".Translate(
                    PowerFormatting.FormatEnergy(snapshot.StoredWattDays),
                    PowerFormatting.FormatEnergy(snapshot.CapacityWattDays),
                    snapshot.BatteryCount)
                : (string)"PGM_Networks_NoBatteries".Translate();
            float? chargeBar = snapshot.ChargeFraction.HasValue
                ? (float?)Mathf.Clamp01(snapshot.ChargeFraction.Value)
                : null;
            Rect c3 = CardSlot(rect, 3, cols, cardW, cardH, gap);
            PowerUiDraw.StatCard(c3,
                "PGM_Tile_Charge".Translate(),
                PowerFormatting.FormatCharge(snapshot.ChargeFraction),
                chargeColor,
                chargeColor,
                chargeSub,
                "PGM_Tip_Charge".Translate(),
                chargeBar,
                chargeColor);
        }

        private static Rect CardSlot(Rect container, int index, int cols, float w, float h, float gap)
        {
            int col = index % cols;
            int row = index / cols;
            return new Rect(container.x + col * (w + gap), container.y + row * (h + gap), w, h);
        }

        private void DrawPowerOverviewPanel(Rect rect, PowerGridSnapshot snapshot, int currentTick,
            PowerGridManagerSettings settings, PowerHistoryTracker history)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(PowerUiDraw.PadPanel);
            if (inner.width <= 8f || inner.height <= 24f) return;

            bool hasChart = settings.HistoryEnabled && history != null && history.CanDrawTrend;
            string rightNote = hasChart
                ? (string)"PGM_Range_SixHours".Translate()
                : (string)"PGM_Overview_LiveSnapshot".Translate();
            PowerUiDraw.SectionHeader(new Rect(inner.x, inner.y, inner.width, 22f),
                "PGM_Overview_Panel_Power".Translate(), rightNote);

            float bodyY = inner.y + 26f;
            float stripH = inner.height >= 150f ? 26f : 0f;
            float mainH = Mathf.Max(20f, inner.yMax - bodyY - (stripH > 0f ? stripH + 6f : 0f));
            Rect mainRect = new Rect(inner.x, bodyY, inner.width, mainH);

            if (hasChart)
            {
                int columns = Mathf.Clamp(Mathf.RoundToInt(mainRect.width / 4f), 20, 240);
                int windowTicks = PowerChartBuilder.TicksFor(PowerTimeRange.LastSixHours);
                int endTick = history.NewestTick;
                int startTick = endTick - windowTicks;
                if (!ReferenceEquals(cachedHistory, history) ||
                    cachedHistoryCount != history.Count ||
                    cachedNewestTick != endTick ||
                    cachedColumns != columns ||
                    cachedGenConChart == null)
                {
                    cachedGenConChart = PowerChartBuilder.Build(
                        history.Samples, startTick, endTick, columns,
                        PowerChartMetric.GenerationAndConsumption);
                    cachedHistory = history;
                    cachedHistoryCount = history.Count;
                    cachedNewestTick = endTick;
                    cachedColumns = columns;
                }

                int ageTicks = currentTick >= endTick ? currentTick - endTick : 0;
                PowerChartWidget.Draw(
                    mainRect,
                    cachedGenConChart,
                    "PGM_Chart_GenerationTitle".Translate(),
                    "PGM_Unit_Watt".Translate(),
                    "PGM_Chart_LegendGeneration".Translate(),
                    "PGM_Chart_LegendConsumption".Translate(),
                    "PGM_Chart_Ago".Translate(PowerFormatting.FormatDuration(windowTicks)),
                    "PGM_Chart_NewestSample".Translate(PowerFormatting.FormatDuration(ageTicks)));
            }
            else
            {
                DrawLiveComparisonBox(mainRect, snapshot, settings, history);
            }

            if (stripH > 0f)
            {
                Rect strip = new Rect(inner.x, mainRect.yMax + 6f, inner.width, stripH);
                DrawPowerSubStrip(strip, snapshot);
            }
        }

        private static void DrawLiveComparisonBox(Rect rect, PowerGridSnapshot snapshot,
            PowerGridManagerSettings settings, PowerHistoryTracker history)
        {
            PowerUiDraw.RecessedBox(rect);
            Rect inner = rect.ContractedBy(8f);
            if (inner.width <= 8f || inner.height <= 16f) return;

            float cfgGen = snapshot.ConfiguredGenerationWatts ?? 0f;
            float ceiling = Mathf.Max(1f,
                Mathf.Max(snapshot.GenerationWatts, Mathf.Max(snapshot.ConsumptionWatts, cfgGen)));
            float genFrac = Mathf.Clamp01(snapshot.GenerationWatts / ceiling);
            float conFrac = Mathf.Clamp01(snapshot.ConsumptionWatts / ceiling);

            float y = inner.y;
            float rowH = 19f;
            if (inner.height >= 64f)
            {
                PowerUiDraw.KeyValueRow(new Rect(inner.x, y, inner.width, rowH),
                    "PGM_Chart_LegendGeneration".Translate(),
                    PowerFormatting.FormatWatts(snapshot.GenerationWatts),
                    PowerUiDraw.AccentCyan);
                y += rowH;
                PowerUiDraw.Bar(new Rect(inner.x, y, inner.width, 8f), genFrac, PowerUiDraw.AccentCyan);
                y += 12f;

                PowerUiDraw.KeyValueRow(new Rect(inner.x, y, inner.width, rowH),
                    "PGM_Chart_LegendConsumption".Translate(),
                    PowerFormatting.FormatWatts(snapshot.ConsumptionWatts),
                    PowerUiDraw.Warn);
                y += rowH;
                PowerUiDraw.Bar(new Rect(inner.x, y, inner.width, 8f), conFrac, PowerUiDraw.Warn);
                y += 14f;
            }

            if (inner.yMax - y >= 18f)
            {
                string reason;
                if (!settings.HistoryEnabled)
                    reason = "PGM_History_Disabled".Translate();
                else if (history == null || history.Count == 0)
                    reason = "PGM_History_Empty".Translate(PowerFormatting.FormatDuration(settings.SampleIntervalTicks));
                else
                    reason = "PGM_History_NotEnoughSamples".Translate();

                PowerUiDraw.Label(new Rect(inner.x, y, inner.width, inner.yMax - y),
                    reason, GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.UpperLeft, true);
            }
        }

        private void DrawPowerSubStrip(Rect rect, PowerGridSnapshot snapshot)
        {
            PowerUiDraw.RecessedBox(rect);
            Rect inner = new Rect(rect.x + 6f, rect.y, Mathf.Max(0f, rect.width - 12f), rect.height);
            int cols = inner.width >= 380f ? 3 : 2;
            float colW = inner.width / cols;

            PowerUiDraw.KeyValueRow(new Rect(inner.x, inner.y, colW - 6f, inner.height),
                "PGM_Overview_CfgGenShort".Translate(),
                PowerFormatting.FormatWattsUnknown(snapshot.ConfiguredGenerationWatts),
                PowerUiDraw.TextPrimary,
                "PGM_Tip_ConfiguredGeneration".Translate());

            PowerUiDraw.KeyValueRow(new Rect(inner.x + colW + 3f, inner.y, colW - 6f, inner.height),
                "PGM_Overview_NomDrawShort".Translate(),
                PowerFormatting.FormatWattsUnknown(snapshot.NominalConsumptionWatts),
                PowerUiDraw.TextPrimary,
                "PGM_Tip_NominalDraw".Translate());

            if (cols >= 3)
            {
                PowerUiDraw.KeyValueRow(new Rect(inner.x + colW * 2f + 6f, inner.y, colW - 6f, inner.height),
                    "PGM_Overview_OffShort".Translate(),
                    cachedSwitchedOffConsumers.ToString(),
                    cachedSwitchedOffConsumers > 0 ? PowerUiDraw.TextPrimary : PowerUiDraw.TextSecondary,
                    "PGM_Tip_SwitchedOff".Translate());
            }
        }

        private static void DrawNetworkStatusPanel(
            Rect rect,
            PowerGridSnapshot snapshot,
            PowerGridManagerSettings settings,
            BatteryForecastResult forecast,
            int criticalAdvisories,
            int warningAdvisories,
            int highLoadCount,
            Action<int> onInspectNetwork)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(PowerUiDraw.PadPanel);
            if (inner.width <= 8f || inner.height <= 24f) return;

            PowerUiDraw.SectionHeader(new Rect(inner.x, inner.y, inner.width, 22f),
                "PGM_Overview_Panel_NetworkStatus".Translate());

            float y = inner.y + 26f;

            NetworkStatus overall = EvaluateColonyStatus(snapshot, settings, criticalAdvisories, warningAdvisories, highLoadCount);
            Color statusCol = PowerUiDraw.StatusColor(overall);
            string statusLabel = OverallStatusLabel(snapshot, overall);
            float badgeW = Mathf.Min(inner.width, Mathf.Max(118f, Text.CalcSize(statusLabel).x + 28f));
            PowerUiDraw.StatusBadge(new Rect(inner.x, y, badgeW, 22f),
                statusLabel, statusCol, PowerUiDraw.StatusSymbol(overall));

            if (inner.width > badgeW + 90f && forecast != null)
            {
                string confText = "PGM_Scenario_Confidence".Translate(ConfidenceLabel(forecast.Confidence));
                PowerUiDraw.Label(new Rect(inner.x + badgeW + 8f, y, inner.width - badgeW - 8f, 22f),
                    confText, GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);
            }
            y += 26f;

            float rowH = 19f;
            if (inner.yMax - y >= rowH)
            {
                string netSummary = snapshot.DeficitNetworkCount > 0
                    ? (string)"PGM_Overview_NetSummaryDeficit".Translate(snapshot.Networks.Count, snapshot.DeficitNetworkCount)
                    : (highLoadCount > 0
                        ? (string)"PGM_Overview_NetSummaryHighLoad".Translate(snapshot.Networks.Count, highLoadCount)
                        : snapshot.Networks.Count.ToString());
                Color netSummaryCol = snapshot.DeficitNetworkCount > 0
                    ? PowerUiDraw.NoticeColor
                    : (highLoadCount > 0 ? PowerUiDraw.Warn : PowerUiDraw.TextPrimary);
                PowerUiDraw.KeyValueRow(new Rect(inner.x, y, inner.width, rowH),
                    "PGM_Tile_Networks".Translate(), netSummary, netSummaryCol, "PGM_Tip_Networks".Translate());
                y += rowH;
            }

            if (inner.yMax - y >= rowH)
            {
                string bldSummary = "PGM_Overview_BuildingCounts".Translate(
                    snapshot.GeneratorCount, snapshot.ConsumerCount, snapshot.BatteryCount);
                PowerUiDraw.KeyValueRow(new Rect(inner.x, y, inner.width, rowH),
                    "PGM_Overview_ConnectedUnits".Translate(), bldSummary, PowerUiDraw.TextPrimary);
                y += rowH;
            }

            if (inner.yMax - y >= rowH)
            {
                bool empDifference = snapshot.StoredWattDays - snapshot.AvailableStoredWattDays > 0.05f;
                string availVal = snapshot.BatteryCount == 0
                    ? (string)"PGM_Endurance_NoBatteries".Translate()
                    : (empDifference
                        ? (string)"PGM_Overview_UsableVsStored".Translate(
                            PowerFormatting.FormatEnergy(snapshot.AvailableStoredWattDays),
                            PowerFormatting.FormatEnergy(snapshot.StoredWattDays))
                        : PowerFormatting.FormatEnergy(snapshot.AvailableStoredWattDays));
                PowerUiDraw.KeyValueRow(new Rect(inner.x, y, inner.width, rowH),
                    "PGM_Overview_UsableReserve".Translate(),
                    availVal,
                    empDifference ? PowerUiDraw.Warn : PowerUiDraw.TextPrimary,
                    "PGM_Tip_StoredEnergy".Translate());
                y += rowH;
            }

            if (inner.yMax - y >= rowH)
            {
                int issues = snapshot.DisconnectedConsumerCount + snapshot.UnsupportedComponentCount;
                string discText = "PGM_Overview_DisconnectedSummary".Translate(
                    snapshot.DisconnectedConsumerCount, snapshot.DataNoticeCount);
                PowerUiDraw.KeyValueRow(new Rect(inner.x, y, inner.width, rowH),
                    "PGM_Tile_Disconnected".Translate(),
                    discText,
                    issues > 0 ? PowerUiDraw.Warn : PowerUiDraw.TextSecondary,
                    "PGM_Tip_DataNotices".Translate());
                y += rowH + 4f;
            }

            float remaining = inner.yMax - y;
            if (remaining >= 24f && snapshot.Networks.Count > 0)
            {
                Widgets.DrawLineHorizontal(inner.x, y - 2f, inner.width, PowerUiDraw.BorderSubtle);
                int maxRows = Mathf.Max(1, (int)(remaining / 23f));
                int count = Mathf.Min(snapshot.Networks.Count, maxRows);
                for (int i = 0; i < count; i++)
                {
                    PowerNetworkSnapshot net = snapshot.Networks[i];
                    Rect r = new Rect(inner.x, y + i * 23f, inner.width, 22f);
                    DrawCompactNetworkRow(r, i, net, settings, onInspectNetwork);
                }
            }
        }

        private static void DrawCompactNetworkRow(Rect rect, int index, PowerNetworkSnapshot net,
            PowerGridManagerSettings settings, Action<int> onInspectNetwork)
        {
            PowerUiDraw.RowBackground(rect, index, false);
            NetworkStatus status = PowerGridUtilities.EvaluateStatus(net, settings.HighLoadThreshold);
            Color stCol = PowerUiDraw.StatusColor(status);
            string stLabel = PowerGridUtilities.StatusLabelKey(status).Translate();

            float pillW = Mathf.Min(116f, rect.width * 0.38f);
            float netW = Mathf.Min(78f, rect.width * 0.25f);
            float nameW = Mathf.Max(30f, rect.width - pillW - netW - 12f);

            PowerUiDraw.Label(new Rect(rect.x + 6f, rect.y, nameW, rect.height),
                "PGM_Network_Number".Translate(net.Id),
                GameFont.Small, PowerUiDraw.TextPrimary, TextAnchor.MiddleLeft);

            PowerUiDraw.Label(new Rect(rect.x + 6f + nameW, rect.y, netW, rect.height),
                PowerFormatting.FormatWattsSigned(net.NetWatts),
                GameFont.Small, PowerUiDraw.SignedColor(net.NetWatts), TextAnchor.MiddleRight);

            Rect badge = new Rect(rect.xMax - pillW - 2f, rect.y + 2f, pillW, rect.height - 4f);
            PowerUiDraw.StatusBadge(badge, stLabel, stCol, PowerUiDraw.StatusSymbol(status));

            if (onInspectNetwork != null && Widgets.ButtonInvisible(rect))
                onInspectNetwork(net.Id);
        }

        private void DrawTopConsumersPanel(Rect rect, PowerGridSnapshot snapshot,
            PowerGridManagerSettings settings, PowerUiState state,
            Action<BuildingPowerSnapshot> onInspectBuilding)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(PowerUiDraw.PadPanel);
            if (inner.width <= 8f || inner.height <= 24f) return;

            bool ascending = state != null && state.OverviewConsumersAscending;
            string sortLabel = ascending
                ? (string)"PGM_Overview_SortLowHigh".Translate()
                : (string)"PGM_Overview_SortHighLow".Translate();

            Rect headerRect = new Rect(inner.x, inner.y, inner.width, 22f);
            PowerUiDraw.SectionHeader(headerRect, "PGM_Overview_Panel_TopConsumers".Translate(), sortLabel);

            if (state != null && inner.width > 160f)
            {
                float sortBtnW = Mathf.Min(120f, Text.CalcSize(sortLabel).x + 14f);
                Rect sortHit = new Rect(headerRect.xMax - sortBtnW, headerRect.y, sortBtnW, headerRect.height);
                PowerUiDraw.Tip(sortHit, "PGM_Overview_SortConsumersTip".Translate());
                if (Widgets.ButtonInvisible(sortHit))
                {
                    state.OverviewConsumersAscending = !state.OverviewConsumersAscending;
                    EnsureCachedConsumers(snapshot, state.OverviewConsumersAscending);
                }
            }

            Rect listRect = new Rect(inner.x, inner.y + 26f, inner.width, Mathf.Max(0f, inner.height - 26f));
            if (cachedConsumers.Count == 0)
            {
                PowerUiDraw.RecessedBox(listRect);
                PowerUiDraw.Label(listRect.ContractedBy(8f),
                    "PGM_Overview_NoActiveConsumers".Translate(),
                    GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.MiddleCenter, true);
                return;
            }

            float maxDraw = 1f;
            for (int i = 0; i < cachedConsumers.Count; i++)
            {
                if (cachedConsumers[i].ConsumptionWatts > maxDraw)
                    maxDraw = cachedConsumers[i].ConsumptionWatts;
            }

            int limit = Mathf.Min(cachedConsumers.Count, 12);
            const float rowH = 26f;
            Vector2 scroll = state != null ? state.OverviewConsumersScroll : fallbackConsumerScroll;
            Rect view = new Rect(0f, 0f, Mathf.Max(40f, listRect.width - 16f), limit * rowH);
            Widgets.BeginScrollView(listRect, ref scroll, view);
            try
            {
                for (int i = 0; i < limit; i++)
                {
                    BuildingPowerSnapshot b = cachedConsumers[i];
                    Rect row = new Rect(0f, i * rowH, view.width, rowH - 1f);
                    DrawConsumerRow(row, i, b, maxDraw, settings, state, onInspectBuilding);
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }

            if (state != null) state.OverviewConsumersScroll = scroll;
            else fallbackConsumerScroll = scroll;
        }

        private static void DrawConsumerRow(
            Rect rect,
            int index,
            BuildingPowerSnapshot building,
            float maxDraw,
            PowerGridManagerSettings settings,
            PowerUiState state,
            Action<BuildingPowerSnapshot> onInspectBuilding)
        {
            bool selected = state != null && state.SelectedBuilding != null && state.SelectedBuilding == building.Building;
            PowerUiDraw.RowBackground(rect, index, selected);

            bool isHigh = PowerGridUtilities.IsHighConsumption(building, settings.HighConsumptionWatts);
            Color accent = isHigh ? PowerUiDraw.Warn : PowerUiDraw.AccentPrimary;
            string bLabel = PowerGridUtilities.BuildingLabel(building.Building);

            float wattsW = Mathf.Min(82f, rect.width * 0.25f);
            float barW = rect.width >= 290f ? 58f : 0f;
            float badgeW = rect.width >= 240f ? 62f : 0f;
            float netW = rect.width >= 350f ? 52f : 0f;
            float x = rect.x + 6f;
            float nameW = Mathf.Max(30f, rect.width - 12f - wattsW - barW - badgeW - netW - 12f);

            PowerUiDraw.Label(new Rect(x, rect.y, nameW, rect.height),
                bLabel, GameFont.Small, PowerUiDraw.TextPrimary, TextAnchor.MiddleLeft);
            x += nameW + 4f;

            if (netW > 0f)
            {
                string netLabel = building.NetworkId.HasValue
                    ? "#" + building.NetworkId.Value
                    : (string)"PGM_Connection_NoNetwork".Translate();
                PowerUiDraw.Label(new Rect(x, rect.y, netW, rect.height),
                    netLabel, GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.MiddleCenter);
                x += netW + 4f;
            }

            if (badgeW > 0f)
            {
                bool isOn = !(building.IsFlickable && building.SwitchIsOn == false) && !building.HasOffComponents;
                string statusText = isHigh
                    ? (string)"PGM_Overview_ConsumerHigh".Translate()
                    : (isOn ? (string)"PGM_TraderState_On".Translate() : (string)"PGM_Flick_Off".Translate());
                Color statusCol = isHigh
                    ? PowerUiDraw.Warn
                    : (isOn ? PowerUiDraw.Good : PowerUiDraw.TextSecondary);
                string sym = isHigh ? "\u25B2" : (isOn ? "\u25CF" : "\u2013");
                PowerUiDraw.StatusBadge(new Rect(x, rect.y + 3f, badgeW, rect.height - 6f),
                    statusText, statusCol, sym);
                x += badgeW + 4f;
            }

            if (barW > 0f)
            {
                float frac = maxDraw > 0.01f ? Mathf.Clamp01(building.ConsumptionWatts / maxDraw) : 0f;
                PowerUiDraw.Bar(new Rect(x, rect.y + (rect.height - 7f) * 0.5f, barW, 7f), frac, accent);
            }

            PowerUiDraw.Label(new Rect(rect.xMax - wattsW - 4f, rect.y, wattsW, rect.height),
                PowerFormatting.FormatWatts(building.ConsumptionWatts),
                GameFont.Small, isHigh ? PowerUiDraw.Warn : PowerUiDraw.TextPrimary, TextAnchor.MiddleRight);

            PowerUiDraw.Tip(rect, "PGM_Overview_InspectBuildingTip".Translate(bLabel));
            if (onInspectBuilding != null && Widgets.ButtonInvisible(rect))
                onInspectBuilding(building);
        }

        private void DrawAlertsPanel(
            Rect rect,
            PowerGridSnapshot snapshot,
            PowerGridManagerSettings settings,
            int criticalAdvisories,
            int warningAdvisories,
            IReadOnlyList<PowerDiagnostic> advisories,
            IReadOnlyList<PowerRecommendation> recommendations,
            PowerUiState state,
            Action onOpenDiagnostics)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(PowerUiDraw.PadPanel);
            if (inner.width <= 8f || inner.height <= 24f) return;

            int activeIssues = criticalAdvisories + warningAdvisories;
            string rightCounts = activeIssues > 0
                ? (string)"PGM_Overview_AlertSummaryCount".Translate(criticalAdvisories, warningAdvisories)
                : (string)"PGM_Overview_NoActiveAlertsShort".Translate();

            PowerUiDraw.SectionHeader(new Rect(inner.x, inner.y, inner.width, 22f),
                "PGM_Overview_Panel_Alerts".Translate(), rightCounts);

            Rect body = new Rect(inner.x, inner.y + 26f, inner.width, Mathf.Max(0f, inner.height - 26f));

            int ongoingDiagCount = CountOngoingAdvisories(advisories);
            int ongoingRecCount = CountOngoingRecommendations(recommendations);

            if (!settings.DiagnosticsEnabled && ongoingRecCount == 0)
            {
                PowerUiDraw.NotificationCardShell(body, PowerUiDraw.StatusColorForUnknown(), false);
                PowerUiDraw.Label(body.ContractedBy(10f), "PGM_Diag_Disabled".Translate(),
                    GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.MiddleLeft, true);
                return;
            }

            if (ongoingDiagCount == 0 && ongoingRecCount == 0 && activeIssues == 0)
            {
                DrawNoIssuesCard(body, snapshot);
                return;
            }

            if (ongoingDiagCount == 0 && ongoingRecCount == 0 && activeIssues > 0)
            {
                PowerDiagnosticSeverity sev = criticalAdvisories > 0
                    ? PowerDiagnosticSeverity.Critical
                    : PowerDiagnosticSeverity.Warning;
                DrawCompactAlertCard(
                    new Rect(body.x, body.y, body.width, Mathf.Min(body.height, 84f)),
                    sev,
                    "PGM_Tile_Advisories".Translate(),
                    "PGM_Overview_AlertSummaryCount".Translate(criticalAdvisories, warningAdvisories),
                    "PGM_Tip_Advisories".Translate(),
                    onOpenDiagnostics);
                return;
            }

            const float cardH = 74f;
            const float gap = 6f;
            int totalCards = Mathf.Min(8, ongoingDiagCount + ongoingRecCount);
            float contentH = totalCards * cardH + Mathf.Max(0, totalCards - 1) * gap;

            Vector2 scroll = state != null ? state.OverviewAlertsScroll : fallbackAlertScroll;
            Rect view = new Rect(0f, 0f, Mathf.Max(40f, body.width - 16f), Mathf.Max(body.height, contentH));
            Widgets.BeginScrollView(body, ref scroll, view);
            try
            {
                float cy = 0f;
                int drawn = 0;

                if (advisories != null)
                {
                    for (int i = 0; i < advisories.Count && drawn < 8; i++)
                    {
                        PowerDiagnostic d = advisories[i];
                        if (d == null || !d.Ongoing || d.Dismissed || d.Muted) continue;
                        Rect card = new Rect(0f, cy, view.width, cardH);
                        DrawCompactAlertCard(
                            card,
                            d.Severity,
                            PowerDiagnosticsPanel.FormatTitle(d),
                            PowerDiagnosticsPanel.FormatEvidence(d),
                            PowerDiagnosticsPanel.FormatAction(d),
                            onOpenDiagnostics);
                        cy += cardH + gap;
                        drawn++;
                    }
                }

                if (recommendations != null && drawn < 8)
                {
                    for (int i = 0; i < recommendations.Count && drawn < 8; i++)
                    {
                        PowerRecommendation rec = recommendations[i];
                        if (rec == null || !rec.Ongoing || rec.Dismissed) continue;
                        Rect card = new Rect(0f, cy, view.width, cardH);
                        PowerDiagnosticSeverity sev = (PowerDiagnosticSeverity)rec.Severity;
                        string title = TranslateSafe(rec.TitleKey);
                        string detail = TranslateFormattedSafe(rec.EvidenceKey, rec.EvidenceArgs);
                        string action = rec.ActionKeys != null && rec.ActionKeys.Length > 0
                            ? TranslateSafe(rec.ActionKeys[0])
                            : TranslateSafe(rec.ExplanationKey);
                        DrawCompactAlertCard(card, sev, title, detail, action, onOpenDiagnostics);
                        cy += cardH + gap;
                        drawn++;
                    }
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }

            if (state != null) state.OverviewAlertsScroll = scroll;
            else fallbackAlertScroll = scroll;
        }

        private static void DrawCompactAlertCard(
            Rect rect,
            PowerDiagnosticSeverity severity,
            string title,
            string measurementOrDesc,
            string action,
            Action onClick)
        {
            Color sevCol = PowerUiDraw.SeverityColor(severity);
            PowerUiDraw.NotificationCardShell(rect, sevCol, true);

            Rect inner = new Rect(rect.x + 10f, rect.y + 5f,
                Mathf.Max(0f, rect.width - 16f), Mathf.Max(0f, rect.height - 10f));
            if (inner.width <= 8f || inner.height <= 14f) return;

            string sevText = PowerGridUtilities.SeverityLabelKey(severity).Translate();
            float badgeW = Mathf.Min(inner.width * 0.36f, Mathf.Max(68f, Text.CalcSize(sevText).x + 24f));
            PowerUiDraw.StatusBadge(new Rect(inner.x, inner.y, badgeW, 19f),
                sevText, sevCol, PowerUiDraw.SeveritySymbol(severity));

            PowerUiDraw.Label(new Rect(inner.x + badgeW + 6f, inner.y, Mathf.Max(0f, inner.width - badgeW - 6f), 19f),
                title, GameFont.Small, PowerUiDraw.TextPrimary, TextAnchor.MiddleLeft);

            if (inner.height >= 40f)
            {
                PowerUiDraw.Label(new Rect(inner.x, inner.y + 22f, inner.width, 19f),
                    measurementOrDesc, GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.MiddleLeft);
            }

            if (inner.height >= 58f && !string.IsNullOrEmpty(action))
            {
                PowerUiDraw.Label(new Rect(inner.x, inner.y + 42f, inner.width, 19f),
                    action, GameFont.Small, PowerUiDraw.AccentCyan, TextAnchor.MiddleLeft);
            }

            if (onClick != null && Widgets.ButtonInvisible(rect))
                onClick();
        }

        private static void DrawNoIssuesCard(Rect rect, PowerGridSnapshot snapshot)
        {
            if (snapshot.Networks.Count == 0 && snapshot.Buildings.Count == 0)
            {
                PowerUiDraw.NotificationCardShell(rect, PowerUiDraw.StatusColorForUnknown(), false);
                PowerUiDraw.Label(rect.ContractedBy(10f), "PGM_Networks_Empty".Translate(),
                    GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.MiddleLeft, true);
                return;
            }

            if (snapshot.HasIncompleteData)
            {
                PowerUiDraw.NotificationCardShell(rect, PowerUiDraw.Warn, true);
                Rect inner = rect.ContractedBy(10f);
                PowerUiDraw.StatusBadge(new Rect(inner.x, inner.y, 110f, 20f),
                    "PGM_Status_Unknown".Translate(), PowerUiDraw.Warn, "?");
                PowerUiDraw.Label(new Rect(inner.x, inner.y + 24f, inner.width, Mathf.Max(0f, inner.height - 24f)),
                    "PGM_Networks_Incomplete".Translate(),
                    GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.UpperLeft, true);
                return;
            }

            string detail = "PGM_Overview_NominalDetail".Translate(
                snapshot.Networks.Count,
                PowerFormatting.FormatWattsSigned(snapshot.NetWatts));
            PowerUiDraw.PositiveStatusCard(
                rect,
                "PGM_Overview_NominalBadge".Translate(),
                "PGM_Overview_NominalTitle".Translate(),
                detail);
        }

        private static void DrawMeasurementFooter(Rect rect, PowerGridSnapshot snapshot, int currentTick)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = new Rect(rect.x + 10f, rect.y, Mathf.Max(0f, rect.width - 20f), rect.height);
            int ageTicks = currentTick >= snapshot.Tick ? currentTick - snapshot.Tick : 0;
            string age = "PGM_Overview_Age".Translate(PowerFormatting.FormatDuration(ageTicks));
            float ageW = Mathf.Min(inner.width * 0.34f, Text.CalcSize(age).x + 12f);

            string summary = "PGM_Overview_FooterNote".Translate(
                PowerFormatting.FormatWatts(snapshot.GenerationWatts),
                PowerFormatting.FormatWatts(snapshot.ConsumptionWatts));
            PowerUiDraw.Label(new Rect(inner.x, inner.y, Mathf.Max(0f, inner.width - ageW - 8f), inner.height),
                summary, GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.MiddleLeft);
            PowerUiDraw.Label(new Rect(inner.xMax - ageW, inner.y, ageW, inner.height),
                age, GameFont.Small, PowerUiDraw.AccentCyan, TextAnchor.MiddleRight);
            PowerUiDraw.Tip(rect, "PGM_Overview_Notes".Translate(
                PowerFormatting.FormatWatts(snapshot.GenerationWatts),
                PowerFormatting.FormatWatts(snapshot.ConsumptionWatts)));
        }

        public static NetworkStatus EvaluateColonyStatus(
            PowerGridSnapshot snapshot,
            PowerGridManagerSettings settings,
            int criticalAdvisories,
            int warningAdvisories,
            int highLoadCount)
        {
            if (snapshot == null || snapshot.Networks.Count == 0)
                return NetworkStatus.Unknown;
            if (criticalAdvisories > 0)
                return NetworkStatus.Deficit;

            if (snapshot.DeficitNetworkCount > 0)
            {
                bool buffered = snapshot.BatteryCount > 0 &&
                                snapshot.AvailableStoredWattDays > 0.005f &&
                                (!snapshot.ChargeFraction.HasValue ||
                                 snapshot.ChargeFraction.Value > settings.LowBatteryWarningFraction);
                return buffered ? NetworkStatus.BatteryDischarging : NetworkStatus.Deficit;
            }

            if (snapshot.HasIncompleteData)
                return NetworkStatus.Unknown;
            if (warningAdvisories > 0 || highLoadCount > 0 || snapshot.DisconnectedConsumerCount > 0)
                return NetworkStatus.HighLoad;
            if (snapshot.NetWatts > 0.01f &&
                snapshot.BatteryCount > 0 &&
                snapshot.ChargeFraction.HasValue &&
                snapshot.ChargeFraction.Value < 0.995f)
            {
                return NetworkStatus.BatteryCharging;
            }

            return NetworkStatus.Stable;
        }

        private static string OverallStatusLabel(PowerGridSnapshot snapshot, NetworkStatus status)
        {
            if (snapshot == null || (snapshot.Networks.Count == 0 && snapshot.Buildings.Count == 0))
                return "PGM_Overview_StatusNoGrid".Translate();
            return PowerGridUtilities.StatusLabelKey(status).Translate();
        }

        private void EnsureCachedConsumers(PowerGridSnapshot snapshot, bool ascending)
        {
            if (ReferenceEquals(cachedSnapshot, snapshot) && cachedAscending == ascending)
                return;

            cachedSnapshot = snapshot;
            cachedAscending = ascending;
            cachedConsumers.Clear();
            cachedSwitchedOffConsumers = 0;
            if (snapshot == null) return;

            for (int i = 0; i < snapshot.Buildings.Count; i++)
            {
                BuildingPowerSnapshot b = snapshot.Buildings[i];
                if (b.IsConsumer && b.IsFlickable && b.SwitchIsOn == false)
                    cachedSwitchedOffConsumers++;
                if (b.ConsumptionWatts > 0.01f || (b.IsConsumer && b.NominalConsumptionWatts.HasValue && b.NominalConsumptionWatts.Value > 0.01f))
                    cachedConsumers.Add(b);
            }

            cachedConsumers.Sort((a, b) =>
            {
                int cmp = a.ConsumptionWatts.CompareTo(b.ConsumptionWatts);
                if (cmp == 0)
                {
                    float na = a.NominalConsumptionWatts ?? 0f;
                    float nb = b.NominalConsumptionWatts ?? 0f;
                    cmp = na.CompareTo(nb);
                }
                if (cmp == 0)
                {
                    string la = PowerGridUtilities.BuildingLabel(a.Building);
                    string lb = PowerGridUtilities.BuildingLabel(b.Building);
                    return string.Compare(la, lb, StringComparison.OrdinalIgnoreCase);
                }
                return ascending ? cmp : -cmp;
            });
        }

        private static string BuildEnduranceSubtitle(PowerGridSnapshot snapshot, BatteryForecastResult forecast)
        {
            if (forecast != null)
            {
                switch (forecast.Kind)
                {
                    case BatteryForecastKind.Depleting:
                        if (forecast.TicksToEmpty.HasValue)
                            return "PGM_Endurance_Depleting".Translate(PowerFormatting.FormatDuration(forecast.TicksToEmpty.Value));
                        break;
                    case BatteryForecastKind.Charging:
                        if (forecast.TicksToFull.HasValue)
                            return "PGM_Endurance_Charging".Translate(PowerFormatting.FormatDuration(forecast.TicksToFull.Value));
                        return "PGM_Forecast_Full".Translate();
                    case BatteryForecastKind.Balanced:
                        return "PGM_Endurance_Balanced".Translate();
                    case BatteryForecastKind.NoBatteries:
                        return "PGM_Endurance_NoBatteries".Translate();
                }
            }

            if (snapshot.DeficitNetworkCount > 0)
                return "PGM_Overview_NetSummaryDeficit".Translate(snapshot.Networks.Count, snapshot.DeficitNetworkCount);
            return "PGM_Overview_NetCountSub".Translate(snapshot.Networks.Count);
        }

        private static int CountHighLoadNetworks(PowerGridSnapshot snapshot, float highLoadThreshold)
        {
            int count = 0;
            for (int i = 0; i < snapshot.Networks.Count; i++)
            {
                if (PowerGridUtilities.EvaluateStatus(snapshot.Networks[i], highLoadThreshold) == NetworkStatus.HighLoad)
                    count++;
            }
            return count;
        }

        private static int CountOngoingAdvisories(IReadOnlyList<PowerDiagnostic> advisories)
        {
            if (advisories == null) return 0;
            int n = 0;
            for (int i = 0; i < advisories.Count; i++)
            {
                PowerDiagnostic d = advisories[i];
                if (d != null && d.Ongoing && !d.Dismissed && !d.Muted) n++;
            }
            return n;
        }

        private static int CountOngoingRecommendations(IReadOnlyList<PowerRecommendation> recs)
        {
            if (recs == null) return 0;
            int n = 0;
            for (int i = 0; i < recs.Count; i++)
            {
                if (recs[i] != null && recs[i].Ongoing && !recs[i].Dismissed) n++;
            }
            return n;
        }

        private static string ConfidenceLabel(ForecastConfidence confidence)
        {
            switch (confidence)
            {
                case ForecastConfidence.High:
                    return "PGM_Confidence_High".Translate();
                case ForecastConfidence.Medium:
                    return "PGM_Confidence_Medium".Translate();
                case ForecastConfidence.Low:
                    return "PGM_Confidence_Low".Translate();
                default:
                    return "PGM_Confidence_Unavailable".Translate();
            }
        }

        private static string TranslateSafe(string keyOrText)
        {
            if (string.IsNullOrEmpty(keyOrText)) return string.Empty;
            if (keyOrText.StartsWith("PGM_", StringComparison.Ordinal))
                return keyOrText.Translate();
            return keyOrText;
        }

        private static string TranslateFormattedSafe(string keyOrText, object[] args)
        {
            if (string.IsNullOrEmpty(keyOrText)) return string.Empty;
            if (!keyOrText.StartsWith("PGM_", StringComparison.Ordinal))
                return keyOrText;
            if (args == null || args.Length == 0)
                return keyOrText.Translate();
            return keyOrText.Translate(args);
        }
    }
}
