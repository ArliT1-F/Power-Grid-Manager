using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// List of the map's simulated power networks plus a detail view for the selected
    /// one. Network identity is snapshot-local: it is a list position, not a persistent
    /// id, so a selection is revalidated on every draw.
    /// </summary>
    public sealed class PowerNetworkPanel
    {
        private const float ListWidth = 330f;
        private const float ListRowHeight = 58f;

        private readonly List<BuildingPowerSnapshot> networkBuildings = new List<BuildingPowerSnapshot>();
        private readonly List<int> rankedIndices = new List<int>();

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, Action<int> showBuildingsOnNetwork)
        {
            float listWidth = Mathf.Min(ListWidth, rect.width * 0.45f);
            Rect listRect = new Rect(rect.x, rect.y, listWidth, rect.height);
            Rect detailRect = new Rect(listRect.xMax + 8f, rect.y, rect.width - listWidth - 8f, rect.height);
            DrawList(listRect, snapshot, state, settings);
            DrawDetail(detailRect, snapshot, state, settings, showBuildingsOnNetwork);
        }

        private void DrawList(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings)
        {
            PowerUiDraw.Panel(rect);
            PowerUiDraw.SectionHeader(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 20f),
                "PGM_Networks_Header".Translate(snapshot.Networks.Count));

            Rect viewOuter = new Rect(rect.x + 4f, rect.y + 28f, rect.width - 8f, rect.height - 32f);
            float contentHeight = snapshot.Networks.Count * (ListRowHeight + 2f);
            Rect view = new Rect(0f, 0f, viewOuter.width - 18f, Mathf.Max(contentHeight, viewOuter.height));
            Widgets.BeginScrollView(viewOuter, ref state.NetworkScroll, view);
            try
            {
                if (snapshot.Networks.Count == 0)
                {
                    PowerUiDraw.WrappedText(new Rect(0f, 0f, view.width, 60f),
                        "PGM_Networks_Empty".Translate(), PowerUiDraw.Muted);
                    return;
                }

                float y = 0f;
                for (int i = 0; i < snapshot.Networks.Count; i++)
                {
                    PowerNetworkSnapshot network = snapshot.Networks[i];
                    Rect row = new Rect(0f, y, view.width, ListRowHeight);
                    bool selected = state.SelectedNetworkId.HasValue && state.SelectedNetworkId.Value == network.Id;
                    if (selected) Widgets.DrawHighlightSelected(row);
                    else Widgets.DrawHighlightIfMouseover(row);
                    Widgets.DrawBox(row, 1);

                    NetworkStatus status = PowerGridUtilities.EvaluateStatus(network, settings.HighLoadThreshold);

                    // Three compact lines that stay readable even in a narrow list pane.
                    float balanceWidth = 104f;
                    PowerUiDraw.TextEllipsized(
                        new Rect(row.x + 6f, row.y + 2f, Mathf.Max(40f, row.width - balanceWidth - 12f), 20f),
                        "PGM_Network_Number".Translate(network.Id), Color.white);
                    PowerUiDraw.DrawText(
                        new Rect(row.xMax - balanceWidth - 6f, row.y + 2f, balanceWidth, 20f),
                        PowerFormatting.FormatWattsSigned(network.NetWatts),
                        network.NetWatts < -0.01f ? PowerUiDraw.NoticeColor : Color.white, TextAnchor.MiddleRight);

                    float pillWidth = Mathf.Min(116f, row.width * 0.42f);
                    Rect pill = new Rect(row.x + 6f, row.y + 22f, pillWidth, 16f);
                    PowerUiDraw.StatusPill(pill, PowerGridUtilities.StatusLabelKey(status).Translate(),
                        PowerUiDraw.StatusColor(status));
                    Rect countsRect = new Rect(pill.xMax + 4f, row.y + 22f,
                        Mathf.Max(0f, row.xMax - pill.xMax - 10f), 16f);
                    if (countsRect.width > 66f)
                    {
                        PowerUiDraw.TextEllipsized(countsRect,
                            "PGM_Networks_RowCounts".Translate(network.ConsumerCount, network.GeneratorCount),
                            PowerUiDraw.Muted, TextAnchor.MiddleRight);
                    }

                    // Battery information: count, stored energy and charge share.
                    string batteryInfo = network.BatteryCount == 0
                        ? "PGM_Networks_NoBatteries".Translate()
                        : "PGM_Networks_BatteryInfo".Translate(network.BatteryCount,
                            PowerFormatting.FormatEnergy(network.StoredWattDays),
                            PowerFormatting.FormatEnergy(network.CapacityWattDays));
                    PowerUiDraw.TextEllipsized(
                        new Rect(row.x + 6f, row.y + 40f, Mathf.Max(40f, row.width - 58f), 16f), batteryInfo,
                        PowerUiDraw.Muted);
                    PowerUiDraw.DrawText(new Rect(row.xMax - 52f, row.y + 40f, 46f, 16f),
                        PowerFormatting.FormatCharge(network.ChargeFraction), Color.white, TextAnchor.MiddleRight);

                    PowerUiDraw.Tip(row, "PGM_Networks_RowTip".Translate(
                        PowerFormatting.FormatWatts(network.GenerationWatts),
                        PowerFormatting.FormatWatts(network.ConsumptionWatts),
                        network.ConsumerCount, network.GeneratorCount, network.BatteryCount));

                    if (Widgets.ButtonInvisible(row)) state.SelectNetwork(network.Id);
                    y += ListRowHeight + 2f;
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

            PowerNetworkSnapshot network = FindNetwork(snapshot, state.SelectedNetworkId);
            if (network == null)
            {
                PowerUiDraw.WrappedText(new Rect(rect.x + 10f, rect.y + 10f, rect.width - 20f, 80f),
                    snapshot.Networks.Count == 0
                        ? "PGM_Networks_Empty".Translate()
                        : "PGM_Networks_SelectPrompt".Translate(),
                    PowerUiDraw.Muted);
                return;
            }

            float y = rect.y + 8f;
            NetworkStatus status = PowerGridUtilities.EvaluateStatus(network, settings.HighLoadThreshold);

            float statusWidth = Mathf.Min(152f, rect.width * 0.45f);
            Rect title = new Rect(rect.x + 10f, y, Mathf.Max(40f, rect.width - statusWidth - 22f), 26f);
            PowerUiDraw.TextEllipsized(title, "PGM_Network_DetailTitle".Translate(network.Id), Color.white);
            Rect pill = new Rect(rect.xMax - statusWidth - 10f, y + 3f, statusWidth, 20f);
            PowerUiDraw.StatusPill(pill, PowerGridUtilities.StatusLabelKey(status).Translate(),
                PowerUiDraw.StatusColor(status));
            y += 30f;

            float labelWidth = Mathf.Clamp(rect.width * 0.52f, 100f, 190f);
            DrawStat(rect, ref y, "PGM_Detail_Generation", PowerFormatting.FormatWatts(network.GenerationWatts), labelWidth);
            DrawStat(rect, ref y, "PGM_Detail_Consumption", PowerFormatting.FormatWatts(network.ConsumptionWatts), labelWidth);
            DrawStat(rect, ref y, "PGM_Detail_Net", PowerFormatting.FormatWattsSigned(network.NetWatts), labelWidth,
                network.NetWatts < -0.01f ? PowerUiDraw.NoticeColor : Color.white);
            DrawStat(rect, ref y, "PGM_Detail_Consumers", network.ConsumerCount.ToString(), labelWidth);
            DrawStat(rect, ref y, "PGM_Detail_Generators", network.GeneratorCount.ToString(), labelWidth);
            DrawStat(rect, ref y, "PGM_Detail_Batteries", network.BatteryCount.ToString(), labelWidth);
            DrawStat(rect, ref y, "PGM_Detail_Stored", PowerFormatting.FormatEnergy(network.StoredWattDays), labelWidth);
            DrawStat(rect, ref y, "PGM_Detail_Capacity", PowerFormatting.FormatEnergy(network.CapacityWattDays), labelWidth);
            DrawStat(rect, ref y, "PGM_Detail_Charge", PowerFormatting.FormatCharge(network.ChargeFraction), labelWidth);
            DrawStat(rect, ref y, "PGM_Detail_LoadRatio",
                network.GenerationWatts > 0.01f
                    ? PowerFormatting.FormatCharge(network.ConsumptionWatts / network.GenerationWatts)
                    : "PGM_Value_Unknown".Translate(), labelWidth);

            Rect bar = new Rect(rect.x + 10f, y, Mathf.Min(280f, rect.width - 20f), 8f);
            PowerUiDraw.Bar(bar, network.ChargeFraction ?? 0f, PowerUiDraw.Good);
            y += 16f;

            if (network.HasIncompleteData)
            {
                PowerUiDraw.WrappedText(new Rect(rect.x + 10f, y, rect.width - 20f, 34f),
                    "PGM_Networks_Incomplete".Translate(), PowerUiDraw.Warn);
                y += 36f;
            }

            y += 4f;
            Rect headerRow = new Rect(rect.x + 10f, y, rect.width - 20f, 20f);
            PowerUiDraw.SectionHeader(headerRow, "PGM_Networks_TopConsumers".Translate());
            y += 24f;

            CollectTopConsumers(snapshot, network, 6);
            if (rankedIndices.Count == 0)
            {
                PowerUiDraw.WrappedText(new Rect(rect.x + 10f, y, rect.width - 20f, 36f),
                    "PGM_Networks_NoConsumers".Translate(), PowerUiDraw.Muted);
                y += 38f;
            }
            else
            {
                for (int i = 0; i < rankedIndices.Count; i++)
                {
                    BuildingPowerSnapshot building = networkBuildings[rankedIndices[i]];
                    Rect row = new Rect(rect.x + 10f, y, rect.width - 20f, PowerUiDraw.RowHeight);
                    if (Widgets.ButtonInvisible(row))
                    {
                        state.SelectBuilding(building.Building);
                    }
                    Widgets.DrawHighlightIfMouseover(row);
                    string label = building.Building.def != null ? building.Building.def.label : "";
                    PowerUiDraw.DrawText(new Rect(row.x, row.y, row.width - 110f, row.height), label.CapitalizeFirst(),
                        Color.white);
                    PowerUiDraw.DrawText(new Rect(row.xMax - 104f, row.y, 104f, row.height),
                        PowerFormatting.FormatWatts(building.ConsumptionWatts), PowerUiDraw.Muted,
                        TextAnchor.MiddleRight);
                    y += PowerUiDraw.RowHeight;
                }
            }

            float buttonWidth = Mathf.Min(180f, (rect.width - 26f) / 2f);
            Rect buttonRect = new Rect(rect.x + 10f, rect.yMax - 32f, buttonWidth, 26f);
            if (Widgets.ButtonText(buttonRect, "PGM_Networks_ShowBuildings".Translate()))
            {
                showBuildingsOnNetwork?.Invoke(network.Id);
            }
            PowerUiDraw.Tip(buttonRect, "PGM_Networks_ShowBuildingsTip".Translate());

            // Jumping to the largest consumer is a real relationship the game exposes:
            // the building is a confirmed member of this network.
            Rect jumpRect = new Rect(buttonRect.xMax + 6f, buttonRect.y, buttonWidth, 26f);
            bool hasJumpTarget = networkBuildings.Count > 0;
            if (hasJumpTarget && Widgets.ButtonText(jumpRect, "PGM_Networks_Locate".Translate()))
            {
                Building target = networkBuildings[rankedIndices[0]].Building;
                CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(target));
            }
            if (!hasJumpTarget) Widgets.DrawBoxSolid(jumpRect, new Color(0f, 0f, 0f, 0.25f));
            PowerUiDraw.Tip(jumpRect, "PGM_Networks_LocateTip".Translate());
        }

        private static PowerNetworkSnapshot FindNetwork(PowerGridSnapshot snapshot, int? selectedId)
        {
            if (snapshot == null || snapshot.Networks.Count == 0) return null;
            if (selectedId.HasValue)
            {
                for (int i = 0; i < snapshot.Networks.Count; i++)
                {
                    if (snapshot.Networks[i].Id == selectedId.Value) return snapshot.Networks[i];
                }
            }
            return null;
        }

        private void CollectTopConsumers(PowerGridSnapshot snapshot, PowerNetworkSnapshot network, int maximum)
        {
            networkBuildings.Clear();
            rankedIndices.Clear();
            if (snapshot == null) return;

            // Uses the shared helper so "on this network" has exactly one definition.
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

        private static void DrawStat(Rect area, ref float y, string labelKey, string value, float labelWidth,
            Color? valueColor = null)
        {
            Rect row = new Rect(area.x + 10f, y, Mathf.Max(20f, area.width - 20f), 20f);
            PowerUiDraw.LabelValue(row, labelKey.Translate(), value, Mathf.Min(labelWidth, row.width * 0.6f),
                valueColor);
            y += 20f;
        }
    }
}
