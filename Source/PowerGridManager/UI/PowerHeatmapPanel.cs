using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Modernized dashboard-based power heatmap. Each row is a power building with a dark
    /// intensity bar, symbol+color status indicator, mode-specific ranking and measurement,
    /// hover tooltip, and Locate / Inspect actions. Nothing here scans the map or alters the
    /// simulation.
    /// </summary>
    public sealed class PowerHeatmapPanel
    {
        private const float RowHeight = 28f;
        private const float ButtonWidth = 78f;

        public enum HeatmapMode
        {
            Consumption = 0,
            Generation,
            Battery,
            Stability,
            Isolation,
            Idle,
            Critical
        }

        private HeatmapMode mode = HeatmapMode.Consumption;
        private string searchText = "";
        private readonly List<HeatmapRow> cachedRows = new List<HeatmapRow>(128);

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, PowerManagementController management)
        {
            if (state == null || rect.width <= 8f || rect.height <= 8f) return;

            float y = rect.y;
            bool twoRowToolbar = rect.width < 820f;
            float toolbarH = twoRowToolbar ? 62f : 36f;
            DrawToolbar(new Rect(rect.x, y, rect.width, toolbarH), state, twoRowToolbar);
            y += toolbarH + 6f;

            Rect body = new Rect(rect.x, y, rect.width, Mathf.Max(60f, rect.yMax - y));
            PowerUiDraw.Panel(body);

            IReadOnlyList<BuildingPowerSnapshot> buildings = snapshot == null ? null : snapshot.Buildings;
            FilterAndRank(buildings, settings, management);
            if (cachedRows.Count == 0)
            {
                PowerUiDraw.WrappedText(new Rect(body.x + 12f, body.y + 12f, body.width - 24f, 60f),
                    "PGM_Heatmap_Empty".Translate(), PowerUiDraw.TextSecondary);
                return;
            }

            Rect outer = new Rect(body.x + 6f, body.y + 6f, body.width - 12f, body.height - 12f);
            float contentHeight = cachedRows.Count * RowHeight;
            Rect view = new Rect(0f, 0f, Mathf.Max(140f, outer.width - 18f), Mathf.Max(contentHeight, outer.height));
            Widgets.BeginScrollView(outer, ref state.HeatmapScroll, view);
            try
            {
                float rowY = 0f;
                for (int i = 0; i < cachedRows.Count; i++)
                {
                    HeatmapRow row = cachedRows[i];
                    Rect rowRect = new Rect(0f, rowY, view.width, RowHeight);
                    bool selected = ReferenceEquals(state.SelectedBuilding, row.Building.Building);
                    PowerUiDraw.RowBackground(rowRect, i, selected);
                    DrawRow(rowRect, row, settings, management);
                    if (Widgets.ButtonInvisible(new Rect(rowRect.x, rowRect.y,
                        Mathf.Max(20f, rowRect.width - ButtonWidth * 2f - 12f), RowHeight)))
                    {
                        state.SelectBuilding(row.Building.Building);
                    }
                    rowY += RowHeight;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawToolbar(Rect rect, PowerUiState state, bool twoRow)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(6f);

            HeatmapMode[] modes =
            {
                HeatmapMode.Consumption, HeatmapMode.Generation, HeatmapMode.Battery,
                HeatmapMode.Stability, HeatmapMode.Isolation, HeatmapMode.Idle,
                HeatmapMode.Critical
            };

            float searchW = twoRow
                ? Mathf.Min(220f, inner.width * 0.45f)
                : Mathf.Min(190f, Mathf.Max(90f, inner.width * 0.22f));
            float modesAreaW = twoRow ? inner.width : Mathf.Max(120f, inner.width - searchW - 8f);
            float buttonWidth = Mathf.Max(42f, (modesAreaW - (modes.Length - 1) * 4f) / modes.Length);

            float x = inner.x;
            float y = inner.y;
            for (int i = 0; i < modes.Length; i++)
            {
                HeatmapMode m = modes[i];
                Rect b = new Rect(x, y, buttonWidth, 24f);
                if (PowerUiDraw.Button(b, ModeLabel(m), this.mode == m))
                    this.mode = m;
                x += buttonWidth + 4f;
            }

            Rect search = twoRow
                ? new Rect(inner.x, inner.y + 28f, inner.width, 22f)
                : new Rect(inner.xMax - searchW, inner.y + 1f, searchW, 22f);
            Widgets.DrawBoxSolid(search, PowerUiDraw.BgRecessed);
            string updated = Widgets.TextField(search, searchText ?? string.Empty);
            PowerUiDraw.DrawBorder(search, PowerUiDraw.BorderPanel, 1);
            if (updated != searchText)
            {
                searchText = updated;
                state.Filter.MarkChanged();
            }
            PowerUiDraw.Tip(search, "PGM_Heatmap_SearchTip".Translate());
        }

        private void FilterAndRank(IReadOnlyList<BuildingPowerSnapshot> buildings,
            PowerGridManagerSettings settings, PowerManagementController management)
        {
            cachedRows.Clear();
            if (buildings == null) return;
            string search = (searchText ?? string.Empty).Trim();
            float maxHeat = 1f;

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingPowerSnapshot building = buildings[i];
                if (building == null || building.Building == null) continue;
                string label = PowerGridUtilities.BuildingLabel(building.Building);
                if (search.Length > 0 && label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                float hv = ComputeHeatValue(building, settings, management);
                if (hv > maxHeat) maxHeat = hv;
                cachedRows.Add(new HeatmapRow(building, hv, 0f));
            }

            for (int i = 0; i < cachedRows.Count; i++)
            {
                HeatmapRow r = cachedRows[i];
                float frac = mode == HeatmapMode.Battery
                    ? (r.Building.IsBattery && r.Building.CapacityWattDays > 0f
                        ? Mathf.Clamp01(r.Building.StoredWattDays / r.Building.CapacityWattDays)
                        : 0f)
                    : Mathf.Clamp01(r.HeatValue / maxHeat);
                cachedRows[i] = new HeatmapRow(r.Building, r.HeatValue, frac);
            }

            cachedRows.Sort((left, right) => right.HeatValue.CompareTo(left.HeatValue));
        }

        private float ComputeHeatValue(BuildingPowerSnapshot b, PowerGridManagerSettings settings,
            PowerManagementController management)
        {
            switch (mode)
            {
                case HeatmapMode.Consumption:
                    return b.ConsumptionWatts;
                case HeatmapMode.Generation:
                    return b.GenerationWatts;
                case HeatmapMode.Battery:
                    if (!b.IsBattery) return -1f;
                    return b.CapacityWattDays > 0f ? (1f - (b.StoredWattDays / b.CapacityWattDays)) * 1000f + b.CapacityWattDays * 0.001f : 0f;
                case HeatmapMode.Stability:
                    if (b.Connection != PowerConnectionState.Connected) return 2000f + b.ConsumptionWatts;
                    return Mathf.Max(0f, b.ConsumptionWatts - b.GenerationWatts);
                case HeatmapMode.Isolation:
                    return b.Connection == PowerConnectionState.NoNetwork ? 1000f + b.ConsumptionWatts : 0f;
                case HeatmapMode.Idle:
                    return b.IsFlickable && b.SwitchIsOn == false && b.ConsumptionWatts > 0.01f
                        ? 1000f + b.ConsumptionWatts : 0f;
                case HeatmapMode.Critical:
                    bool prot = management != null && management.Registry != null &&
                                management.Registry.Find(b.Building) != null &&
                                !management.Registry.Find(b.Building).Sheddable;
                    return prot ? 1000f + b.ConsumptionWatts : b.ConsumptionWatts * 0.1f;
                default:
                    return b.ConsumptionWatts + b.GenerationWatts;
            }
        }

        private void DrawRow(Rect rect, HeatmapRow row, PowerGridManagerSettings settings,
            PowerManagementController management)
        {
            Color heat = HeatColor(row, settings, management);
            string sym = HeatSymbol(row, settings, management);

            Rect heatCell = new Rect(rect.x + 6f, rect.y + 4f, 68f, RowHeight - 8f);
            Widgets.DrawBoxSolid(heatCell, PowerUiDraw.BgRecessed);
            float fillW = Mathf.Max(4f, (heatCell.width - 2f) * Mathf.Clamp01(row.Intensity));
            Widgets.DrawBoxSolid(new Rect(heatCell.x + 1f, heatCell.y + 1f, fillW, heatCell.height - 2f),
                new Color(heat.r, heat.g, heat.b, 0.50f));
            PowerUiDraw.DrawBorder(heatCell, heat, 1);
            PowerUiDraw.DrawText(heatCell, sym, heat, TextAnchor.MiddleCenter, GameFont.Tiny);

            bool showButtons = rect.width >= 380f;
            float actionsReserved = showButtons ? (ButtonWidth * 2f + 12f) : 6f;
            float valWidth = rect.width >= 480f ? 116f : 76f;

            string label = PowerGridUtilities.BuildingLabel(row.Building.Building);
            Rect nameRect = new Rect(heatCell.xMax + 8f, rect.y,
                Mathf.Max(40f, rect.width - (heatCell.xMax - rect.x) - valWidth - actionsReserved - 16f),
                RowHeight);
            PowerUiDraw.TextEllipsized(nameRect, label,
                row.Building.HasAnyDraw ? PowerUiDraw.TextPrimary : PowerUiDraw.TextSecondary);

            Rect valueRect = new Rect(nameRect.xMax + 4f, rect.y, valWidth, RowHeight);
            PowerUiDraw.TextEllipsized(valueRect, ValueText(row), heat, TextAnchor.MiddleRight);

            if (showButtons)
            {
                float bx = rect.xMax - 6f;
                Rect inspect = new Rect(bx - ButtonWidth + 4f, rect.y + 3f, ButtonWidth - 4f, 22f);
                if (PowerUiDraw.Button(inspect, "PGM_Heatmap_Inspect".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(row.Building.Building));
                }
                PowerUiDraw.Tip(inspect, "PGM_Heatmap_InspectTip".Translate());

                bx -= ButtonWidth;
                Rect locate = new Rect(bx, rect.y + 3f, ButtonWidth - 4f, 22f);
                if (PowerUiDraw.Button(locate, "PGM_Heatmap_Locate".Translate()))
                {
                    CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(row.Building.Building));
                }
                PowerUiDraw.Tip(locate, "PGM_Heatmap_LocateTip".Translate());
            }

            PowerUiDraw.Tip(rect, Tooltip(row, management));
        }

        private Color HeatColor(HeatmapRow row, PowerGridManagerSettings settings,
            PowerManagementController management)
        {
            switch (mode)
            {
                case HeatmapMode.Consumption:
                    return ConsumptionColor(row.Building.ConsumptionWatts,
                        settings != null ? settings.HighConsumptionWatts : 1000f);
                case HeatmapMode.Generation:
                    return GenerationColor(row.Building.GenerationWatts);
                case HeatmapMode.Battery:
                    return BatteryColor(row.Building);
                case HeatmapMode.Stability:
                    return StabilityColor(row.Building);
                case HeatmapMode.Isolation:
                    return IsolationColor(row.Building);
                case HeatmapMode.Idle:
                    return IdleColor(row.Building);
                case HeatmapMode.Critical:
                    return CriticalColor(row.Building, management);
                default:
                    return PowerUiDraw.TextSecondary;
            }
        }

        private string HeatSymbol(HeatmapRow row, PowerGridManagerSettings settings,
            PowerManagementController management)
        {
            Color c = HeatColor(row, settings, management);
            if (c == PowerUiDraw.NoticeColor) return "[!!]";
            if (c == PowerUiDraw.Warn) return "[!]";
            if (c == PowerUiDraw.Good) return "[OK]";
            return "\u2013";
        }

        private static Color ConsumptionColor(float watts, float threshold)
        {
            if (threshold > 0f && watts >= threshold) return PowerUiDraw.NoticeColor;
            if (watts > 0.01f) return PowerUiDraw.Warn;
            return PowerUiDraw.TextSecondary;
        }

        private static Color GenerationColor(float watts)
        {
            return watts > 0.01f ? PowerUiDraw.Good : PowerUiDraw.TextSecondary;
        }

        private static Color BatteryColor(BuildingPowerSnapshot building)
        {
            if (!building.IsBattery) return PowerUiDraw.TextSecondary;
            float charge = building.CapacityWattDays > 0f
                ? building.StoredWattDays / building.CapacityWattDays : 0f;
            if (charge <= 0.25f) return PowerUiDraw.NoticeColor;
            if (charge <= 0.55f) return PowerUiDraw.Warn;
            return PowerUiDraw.Good;
        }

        private static Color StabilityColor(BuildingPowerSnapshot building)
        {
            if (building.Connection != PowerConnectionState.Connected) return PowerUiDraw.Warn;
            if (building.IsConsumer && building.ConsumptionWatts > building.GenerationWatts + 0.01f)
                return PowerUiDraw.NoticeColor;
            return PowerUiDraw.Good;
        }

        private static Color IsolationColor(BuildingPowerSnapshot building)
        {
            if (building.IsConsumer && building.Connection == PowerConnectionState.NoNetwork)
                return PowerUiDraw.NoticeColor;
            return PowerUiDraw.TextSecondary;
        }

        private static Color IdleColor(BuildingPowerSnapshot building)
        {
            if (building.IsFlickable && building.SwitchIsOn == false &&
                building.ConsumptionWatts > 0.01f) return PowerUiDraw.Warn;
            return PowerUiDraw.TextSecondary;
        }

        private static Color CriticalColor(BuildingPowerSnapshot building,
            PowerManagementController management)
        {
            bool protectedBuilding = management != null &&
                management.Registry != null &&
                management.Registry.Find(building.Building) != null &&
                !management.Registry.Find(building.Building).Sheddable;
            return protectedBuilding ? PowerUiDraw.Good : PowerUiDraw.TextSecondary;
        }

        private string ValueText(HeatmapRow row)
        {
            switch (mode)
            {
                case HeatmapMode.Consumption:
                    return PowerFormatting.FormatWatts(row.Building.ConsumptionWatts);
                case HeatmapMode.Generation:
                    return row.Building.GenerationWatts > 0.01f
                        ? PowerFormatting.FormatWatts(row.Building.GenerationWatts) : "\u2014";
                case HeatmapMode.Battery:
                    if (!row.Building.IsBattery) return "\u2014";
                    return PowerFormatting.FormatEnergy(row.Building.StoredWattDays) + " / " +
                        PowerFormatting.FormatEnergy(row.Building.CapacityWattDays);
                case HeatmapMode.Stability:
                    return row.Building.Connection == PowerConnectionState.Connected && row.Building.NetworkId.HasValue
                        ? "#" + row.Building.NetworkId.Value : "\u2014";
                case HeatmapMode.Isolation:
                    return row.Building.Connection == PowerConnectionState.NoNetwork
                        ? (string)"PGM_Heatmap_Isolated".Translate() : "\u2014";
                case HeatmapMode.Idle:
                    return row.Building.ConsumptionWatts > 0.01f
                        ? PowerFormatting.FormatWatts(row.Building.ConsumptionWatts) : "\u2014";
                case HeatmapMode.Critical:
                    return row.Building.ConsumptionWatts > 0.01f
                        ? PowerFormatting.FormatWatts(row.Building.ConsumptionWatts) : "\u2014";
                default:
                    return "";
            }
        }

        private static string Tooltip(HeatmapRow row, PowerManagementController management)
        {
            BuildingPowerSnapshot b = row.Building;
            return "PGM_Heatmap_Tooltip".Translate(
                PowerGridUtilities.BuildingLabel(b.Building),
                PowerFormatting.FormatWatts(b.ConsumptionWatts),
                b.NominalConsumptionWatts.HasValue
                    ? PowerFormatting.FormatWatts(b.NominalConsumptionWatts.Value)
                    : (string)"PGM_Value_Unknown".Translate(),
                b.NetworkId.HasValue
                    ? b.NetworkId.Value.ToString()
                    : (string)"PGM_Value_Unknown".Translate(),
                b.ConsumptionWatts > 0.01f && management != null &&
                management.Registry != null && management.Registry.Find(b.Building) != null &&
                management.Registry.Find(b.Building).Sheddable
                    ? (string)"PGM_Heatmap_Policy_Sheddable".Translate()
                    : (string)"PGM_Heatmap_Policy_Protected".Translate());
        }

        private static string ModeLabel(HeatmapMode m)
        {
            switch (m)
            {
                case HeatmapMode.Consumption: return "PGM_Heatmap_Mode_Consumption".Translate();
                case HeatmapMode.Generation: return "PGM_Heatmap_Mode_Generation".Translate();
                case HeatmapMode.Battery: return "PGM_Heatmap_Mode_Battery".Translate();
                case HeatmapMode.Stability: return "PGM_Heatmap_Mode_Stability".Translate();
                case HeatmapMode.Isolation: return "PGM_Heatmap_Mode_Isolation".Translate();
                case HeatmapMode.Idle: return "PGM_Heatmap_Mode_Idle".Translate();
                default: return "PGM_Heatmap_Mode_Critical".Translate();
            }
        }

        private struct HeatmapRow
        {
            public readonly BuildingPowerSnapshot Building;
            public readonly float HeatValue;
            public readonly float Intensity;

            public HeatmapRow(BuildingPowerSnapshot building, float heatValue, float intensity)
            {
                Building = building;
                HeatValue = heatValue;
                Intensity = intensity;
            }
        }
    }
}
