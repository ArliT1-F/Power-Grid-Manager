using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Master-detail power network view: selectable dark network cards on the left with
    /// status stripe, live balance, and battery charge bar; structured KPI cards, endurance,
    /// and top consumers on the right.
    /// </summary>
    public sealed class PowerNetworkPanel
    {
        private const float ListWidth = 340f;
        private const float ListRowHeight = 68f;

        private readonly List<BuildingPowerSnapshot> networkBuildings = new List<BuildingPowerSnapshot>();
        private readonly List<int> rankedIndices = new List<int>();
        private Vector2 detailScroll;

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, Action<int> showBuildingsOnNetwork)
        {
            if (snapshot == null || state == null || settings == null) return;
            if (rect.width <= 8f || rect.height <= 8f) return;

            if (rect.width < 520f)
            {
                float topH = Mathf.Floor(rect.height * 0.42f);
                Rect listCompact = new Rect(rect.x, rect.y, rect.width, topH);
                Rect detailCompact = new Rect(rect.x, listCompact.yMax + PowerUiDraw.SpaceRelated,
                    rect.width, Mathf.Max(0f, rect.yMax - listCompact.yMax - PowerUiDraw.SpaceRelated));
                DrawList(listCompact, snapshot, state, settings);
                DrawDetail(detailCompact, snapshot, state, settings, showBuildingsOnNetwork);
                return;
            }

            float listWidth = Mathf.Min(ListWidth, rect.width * 0.40f);
            Rect listRect = new Rect(rect.x, rect.y, listWidth, rect.height);
            Rect detailRect = new Rect(listRect.xMax + PowerUiDraw.SectionGap, rect.y,
                Mathf.Max(0f, rect.width - listWidth - PowerUiDraw.SectionGap), rect.height);
            DrawList(listRect, snapshot, state, settings);
            DrawDetail(detailRect, snapshot, state, settings, showBuildingsOnNetwork);
        }

        private void DrawList(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(PowerUiDraw.PadPanel);
            if (inner.width <= 8f || inner.height <= 24f) return;

            PowerUiDraw.SectionHeader(new Rect(inner.x, inner.y, inner.width, 22f),
                "PGM_Networks_Header".Translate(snapshot.Networks.Count));

            Rect viewOuter = new Rect(inner.x, inner.y + 28f, inner.width, Mathf.Max(10f, inner.height - 28f));
            float contentHeight = snapshot.Networks.Count * (ListRowHeight + 6f);
            Rect view = new Rect(0f, 0f, Mathf.Max(40f, viewOuter.width - 16f), Mathf.Max(contentHeight, viewOuter.height));
            Widgets.BeginScrollView(viewOuter, ref state.NetworkScroll, view);
            try
            {
                if (snapshot.Networks.Count == 0)
                {
                    PowerUiDraw.WrappedText(new Rect(4f, 4f, view.width - 8f, 60f),
                        "PGM_Networks_Empty".Translate(), PowerUiDraw.TextSecondary);
                    return;
                }

                float y = 0f;
                for (int i = 0; i < snapshot.Networks.Count; i++)
                {
                    PowerNetworkSnapshot network = snapshot.Networks[i];
                    Rect row = new Rect(0f, y, view.width, ListRowHeight);
                    bool selected = state.SelectedNetworkId.HasValue && state.SelectedNetworkId.Value == network.Id;
                    bool hovered = Mouse.IsOver(row);

                    NetworkStatus status = PowerGridUtilities.EvaluateStatus(network, settings.HighLoadThreshold);
                    Color statusCol = PowerUiDraw.StatusColor(status);

                    Widgets.DrawBoxSolid(row, selected
                        ? PowerUiDraw.BgSelected
                        : (hovered ? PowerUiDraw.BgHover : PowerUiDraw.BgElevated));
                    PowerUiDraw.DrawBorder(row, selected ? PowerUiDraw.AccentCyan : PowerUiDraw.BorderPanel, 1);
                    Widgets.DrawBoxSolid(new Rect(row.x, row.y, 4f, row.height), selected ? PowerUiDraw.AccentCyan : statusCol);

                    float balanceWidth = Mathf.Min(108f, row.width * 0.38f);
                    PowerUiDraw.TextEllipsized(
                        new Rect(row.x + 10f, row.y + 4f, Mathf.Max(36f, row.width - balanceWidth - 18f), 20f),
                        "PGM_Network_Number".Translate(network.Id), PowerUiDraw.TextPrimary);
                    PowerUiDraw.DrawText(
                        new Rect(row.xMax - balanceWidth - 8f, row.y + 4f, balanceWidth, 20f),
                        PowerFormatting.FormatWattsSigned(network.NetWatts),
                        PowerUiDraw.SignedColor(network.NetWatts), TextAnchor.MiddleRight);

                    float pillWidth = Mathf.Min(126f, row.width * 0.48f);
                    Rect pill = new Rect(row.x + 10f, row.y + 25f, pillWidth, 18f);
                    PowerUiDraw.StatusBadge(pill,
                        PowerGridUtilities.StatusLabelKey(status).Translate(),
                        statusCol,
                        PowerUiDraw.StatusSymbol(status));

                    Rect countsRect = new Rect(pill.xMax + 6f, row.y + 25f,
                        Mathf.Max(0f, row.xMax - pill.xMax - 14f), 18f);
                    if (countsRect.width > 60f)
                    {
                        PowerUiDraw.TextEllipsized(countsRect,
                            "PGM_Networks_RowCounts".Translate(network.ConsumerCount, network.GeneratorCount),
                            PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);
                    }

                    if (network.BatteryCount > 0)
                    {
                        Color batCol = PowerUiDraw.ChargeColor(network.ChargeFraction, settings.LowBatteryWarningFraction);
                        float pctW = 46f;
                        Rect barRect = new Rect(row.x + 10f, row.y + 50f,
                            Mathf.Max(20f, row.width - pctW - 22f), 8f);
                        PowerUiDraw.Bar(barRect, network.ChargeFraction ?? 0f, batCol);
                        PowerUiDraw.DrawText(new Rect(row.xMax - pctW - 6f, row.y + 45f, pctW, 18f),
                            PowerFormatting.FormatCharge(network.ChargeFraction),
                            batCol, TextAnchor.MiddleRight);
                    }
                    else
                    {
                        PowerUiDraw.TextEllipsized(
                            new Rect(row.x + 10f, row.y + 46f, Mathf.Max(30f, row.width - 18f), 16f),
                            "PGM_Networks_NoBatteries".Translate(), PowerUiDraw.TextSecondary);
                    }

                    PowerUiDraw.Tip(row, "PGM_Networks_RowTip".Translate(
                        PowerFormatting.FormatWatts(network.GenerationWatts),
                        PowerFormatting.FormatWatts(network.ConsumptionWatts),
                        network.ConsumerCount, network.GeneratorCount, network.BatteryCount));

                    if (Widgets.ButtonInvisible(row))
                        state.SelectNetwork(network.Id);
                    y += ListRowHeight + 6f;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawDetail(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, Action<int> showBuildingsOnNetwork)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(PowerUiDraw.PadPanel);
            if (inner.width <= 8f || inner.height <= 24f) return;

            PowerNetworkSnapshot network = FindNetwork(snapshot, state.SelectedNetworkId);
            if (network == null)
            {
                PowerUiDraw.RecessedBox(inner);
                PowerUiDraw.Label(inner.ContractedBy(14f),
                    snapshot.Networks.Count == 0
                        ? "PGM_Networks_Empty".Translate()
                        : "PGM_Networks_SelectPrompt".Translate(),
                    GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.MiddleCenter, true);
                return;
            }

            // Action bar reserved at bottom so buttons are always visible and maintain exact click indices.
            const float actionBarH = 30f;
            Rect bodyArea = new Rect(inner.x, inner.y, inner.width, Mathf.Max(20f, inner.height - actionBarH - 8f));
            Rect actionArea = new Rect(inner.x, inner.yMax - actionBarH, inner.width, actionBarH);

            CollectTopConsumers(snapshot, network, 6);

            const float minBodyH = 340f;
            if (bodyArea.height < minBodyH)
            {
                Rect view = new Rect(0f, 0f, Mathf.Max(40f, bodyArea.width - 16f), minBodyH);
                Widgets.BeginScrollView(bodyArea, ref detailScroll, view);
                try
                {
                    DrawDetailBody(view, network, state, settings);
                }
                finally
                {
                    Widgets.EndScrollView();
                }
            }
            else
            {
                DrawDetailBody(bodyArea, network, state, settings);
            }

            // Footer action buttons: index 0 = ShowBuildings, index 1 = Locate (when hasJumpTarget).
            float btnW = Mathf.Min(190f, (actionArea.width - 8f) * 0.5f);
            Rect showBtn = new Rect(actionArea.x, actionArea.y + 1f, btnW, 28f);
            if (PowerUiDraw.Button(showBtn, "PGM_Networks_ShowBuildings".Translate(), false, true))
            {
                if (showBuildingsOnNetwork != null)
                    showBuildingsOnNetwork(network.Id);
            }
            PowerUiDraw.Tip(showBtn, "PGM_Networks_ShowBuildingsTip".Translate());

            Rect jumpRect = new Rect(showBtn.xMax + 8f, showBtn.y, btnW, 28f);
            bool hasJumpTarget = networkBuildings.Count > 0 && rankedIndices.Count > 0;
            if (hasJumpTarget)
            {
                if (PowerUiDraw.Button(jumpRect, "PGM_Networks_Locate".Translate()))
                {
                    Building target = networkBuildings[rankedIndices[0]].Building;
                    CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(target));
                }
            }
            else
            {
                Widgets.DrawBoxSolid(jumpRect, PowerUiDraw.BgButtonDisabled);
                PowerUiDraw.DrawBorder(jumpRect, PowerUiDraw.BorderSubtle, 1);
                PowerUiDraw.Label(jumpRect, "PGM_Networks_Locate".Translate(),
                    GameFont.Small, PowerUiDraw.TextDisabled, TextAnchor.MiddleCenter);
            }
            PowerUiDraw.Tip(jumpRect, "PGM_Networks_LocateTip".Translate());
        }

        private void DrawDetailBody(Rect area, PowerNetworkSnapshot network,
            PowerUiState state, PowerGridManagerSettings settings)
        {
            float y = area.y;
            NetworkStatus status = PowerGridUtilities.EvaluateStatus(network, settings.HighLoadThreshold);
            Color statusCol = PowerUiDraw.StatusColor(status);

            // Title + status badge.
            float badgeW = Mathf.Min(164f, area.width * 0.44f);
            PowerUiDraw.Label(new Rect(area.x, y, Mathf.Max(40f, area.width - badgeW - 8f), 26f),
                "PGM_Network_DetailTitle".Translate(network.Id), GameFont.Medium, PowerUiDraw.TextPrimary);
            PowerUiDraw.StatusBadge(new Rect(area.xMax - badgeW, y + 2f, badgeW, 22f),
                PowerGridUtilities.StatusLabelKey(status).Translate(),
                statusCol, PowerUiDraw.StatusSymbol(status));
            y += 32f;

            // Mini 3-card KPI row (Generation, Consumption, Net Balance).
            if (area.width >= 320f)
            {
                float gap = 8f;
                float cw = (area.width - gap * 2f) / 3f;
                const float ch = 60f;
                PowerUiDraw.StatCard(new Rect(area.x, y, cw, ch),
                    "PGM_Detail_Generation".Translate(),
                    PowerFormatting.FormatWatts(network.GenerationWatts),
                    network.GenerationWatts > 0.01f ? PowerUiDraw.AccentCyan : PowerUiDraw.TextSecondary,
                    PowerUiDraw.AccentCyan);
                PowerUiDraw.StatCard(new Rect(area.x + cw + gap, y, cw, ch),
                    "PGM_Detail_Consumption".Translate(),
                    PowerFormatting.FormatWatts(network.ConsumptionWatts),
                    PowerUiDraw.TextPrimary,
                    PowerUiDraw.AccentPrimary);
                PowerUiDraw.StatCard(new Rect(area.x + (cw + gap) * 2f, y, cw, ch),
                    "PGM_Detail_Net".Translate(),
                    PowerFormatting.FormatWattsSigned(network.NetWatts),
                    PowerUiDraw.SignedColor(network.NetWatts),
                    PowerUiDraw.SignedColor(network.NetWatts));
                y += ch + 10f;
            }

            // Storage & topology block.
            BatteryForecastResult forecast = BatteryForecast.ForNetwork(network, settings.ForecastIncludeSelfDischarge);
            Rect metaBox = new Rect(area.x, y, area.width, 108f);
            PowerUiDraw.ElevatedPanel(metaBox);
            Rect mp = metaBox.ContractedBy(8f);

            Color chargeCol = PowerUiDraw.ChargeColor(network.ChargeFraction, settings.LowBatteryWarningFraction);
            PowerUiDraw.KeyValueRow(new Rect(mp.x, mp.y, mp.width, 18f),
                "PGM_Detail_Charge".Translate(),
                PowerFormatting.FormatCharge(network.ChargeFraction) + "  (" +
                PowerFormatting.FormatEnergy(network.StoredWattDays) + " / " +
                PowerFormatting.FormatEnergy(network.CapacityWattDays) + ")",
                chargeCol);
            PowerUiDraw.BatteryBar(new Rect(mp.x, mp.y + 20f, mp.width, 11f),
                network.ChargeFraction ?? 0f, chargeCol, settings.LowBatteryWarningFraction);

            float halfW = (mp.width - 14f) * 0.5f;
            float ry = mp.y + 36f;
            PowerUiDraw.KeyValueRow(new Rect(mp.x, ry, halfW, 18f),
                "PGM_Detail_Generators".Translate(), network.GeneratorCount.ToString());
            PowerUiDraw.KeyValueRow(new Rect(mp.x + halfW + 14f, ry, halfW, 18f),
                "PGM_Detail_Consumers".Translate(), network.ConsumerCount.ToString());
            ry += 19f;
            PowerUiDraw.KeyValueRow(new Rect(mp.x, ry, halfW, 18f),
                "PGM_Detail_Batteries".Translate(), network.BatteryCount.ToString());
            PowerUiDraw.KeyValueRow(new Rect(mp.x + halfW + 14f, ry, halfW, 18f),
                "PGM_Detail_LoadRatio".Translate(),
                network.GenerationWatts > 0.01f
                    ? PowerFormatting.FormatCharge(network.ConsumptionWatts / network.GenerationWatts)
                    : (string)"PGM_Value_Unknown".Translate());
            ry += 19f;
            PowerUiDraw.KeyValueRow(new Rect(mp.x, ry, mp.width, 18f),
                "PGM_Detail_Endurance".Translate(),
                EnduranceText(forecast),
                EnduranceColor(forecast),
                "PGM_Tip_NetworkEndurance".Translate());

            y = metaBox.yMax + 8f;

            if (network.HasIncompleteData)
            {
                PowerUiDraw.NotificationCardShell(new Rect(area.x, y, area.width, 32f), PowerUiDraw.Warn, true);
                PowerUiDraw.TextEllipsized(new Rect(area.x + 10f, y, area.width - 16f, 32f),
                    "PGM_Networks_Incomplete".Translate(), PowerUiDraw.Warn);
                y += 38f;
            }

            // Top consumers on this network.
            PowerUiDraw.SectionHeader(new Rect(area.x, y, area.width, 22f),
                "PGM_Networks_TopConsumers".Translate());
            y += 26f;

            if (rankedIndices.Count == 0)
            {
                PowerUiDraw.WrappedText(new Rect(area.x + 4f, y, area.width - 8f, 34f),
                    "PGM_Networks_NoConsumers".Translate(), PowerUiDraw.TextSecondary);
            }
            else
            {
                float maxDraw = Mathf.Max(1f, networkBuildings[rankedIndices[0]].ConsumptionWatts);
                for (int i = 0; i < rankedIndices.Count; i++)
                {
                    if (y + PowerUiDraw.RowHeight > area.yMax + 2f) break;
                    BuildingPowerSnapshot building = networkBuildings[rankedIndices[i]];
                    Rect row = new Rect(area.x, y, area.width, PowerUiDraw.RowHeight);
                    bool sel = state.SelectedBuilding != null && state.SelectedBuilding == building.Building;
                    PowerUiDraw.RowBackground(row, i, sel);

                    string bLabel = PowerGridUtilities.BuildingLabel(building.Building);
                    float wattsW = 86f;
                    float barW = row.width >= 260f ? 68f : 0f;
                    PowerUiDraw.TextEllipsized(
                        new Rect(row.x + 6f, row.y, Mathf.Max(30f, row.width - wattsW - barW - 20f), row.height),
                        bLabel, PowerUiDraw.TextPrimary);
                    if (barW > 0f)
                    {
                        PowerUiDraw.Bar(
                            new Rect(row.xMax - wattsW - barW - 8f, row.y + (row.height - 7f) * 0.5f, barW, 7f),
                            Mathf.Clamp01(building.ConsumptionWatts / maxDraw),
                            PowerUiDraw.AccentPrimary);
                    }
                    PowerUiDraw.DrawText(new Rect(row.xMax - wattsW - 4f, row.y, wattsW, row.height),
                        PowerFormatting.FormatWatts(building.ConsumptionWatts),
                        PowerUiDraw.TextPrimary, TextAnchor.MiddleRight);

                    if (Widgets.ButtonInvisible(row))
                        state.SelectBuilding(building.Building);
                    y += PowerUiDraw.RowHeight;
                }
            }
        }

        private static PowerNetworkSnapshot FindNetwork(PowerGridSnapshot snapshot, int? selectedId)
        {
            if (snapshot == null || snapshot.Networks.Count == 0 || !selectedId.HasValue) return null;
            for (int i = 0; i < snapshot.Networks.Count; i++)
            {
                if (snapshot.Networks[i].Id == selectedId.Value) return snapshot.Networks[i];
            }
            return null;
        }

        private void CollectTopConsumers(PowerGridSnapshot snapshot, PowerNetworkSnapshot network, int maximum)
        {
            networkBuildings.Clear();
            rankedIndices.Clear();
            if (snapshot == null) return;

            PowerGridUtilities.CollectNetworkBuildings(snapshot, network.Id, networkBuildings);
            for (int i = networkBuildings.Count - 1; i >= 0; i--)
            {
                if (!networkBuildings[i].HasAnyDraw) networkBuildings.RemoveAt(i);
            }

            for (int i = 0; i < networkBuildings.Count; i++) rankedIndices.Add(i);
            rankedIndices.Sort((left, right) =>
                networkBuildings[right].ConsumptionWatts.CompareTo(networkBuildings[left].ConsumptionWatts));
            if (rankedIndices.Count > maximum) rankedIndices.RemoveRange(maximum, rankedIndices.Count - maximum);
        }

        private static string EnduranceText(BatteryForecastResult forecast)
        {
            switch (forecast.Kind)
            {
                case BatteryForecastKind.NoBatteries: return "PGM_Endurance_NoBatteries".Translate();
                case BatteryForecastKind.NoCapacity: return "PGM_Value_Unknown".Translate();
                case BatteryForecastKind.Balanced: return "PGM_Endurance_Balanced".Translate();
                case BatteryForecastKind.Depleting:
                    return "PGM_Endurance_Depleting".Translate(
                        PowerFormatting.FormatDuration(forecast.TicksToEmpty ?? 0));
                default:
                    return "PGM_Endurance_Charging".Translate(PowerFormatting.FormatDuration(forecast.TicksToFull ?? 0));
            }
        }

        private static Color EnduranceColor(BatteryForecastResult forecast)
        {
            if (forecast.Kind == BatteryForecastKind.Depleting) return PowerUiDraw.NoticeColor;
            if (forecast.Kind == BatteryForecastKind.Charging) return PowerUiDraw.AccentCyan;
            return PowerUiDraw.TextSecondary;
        }
    }
}
