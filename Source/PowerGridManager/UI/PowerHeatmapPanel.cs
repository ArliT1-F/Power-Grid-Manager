using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Dashboard-based power heatmap. Each row is a power building colour-coded by the
    /// selected mode (consumption, generation, battery charge, network stability, isolation,
    /// idle draw, critical protection or monitoring group). Rows carry a hover tooltip and
    /// Locate / Inspect actions; nothing here scans the map or changes the simulation.
    /// <para>
    /// This is a dashboard view rather than an on-map cell overlay. RimWorld does not expose
    /// a public map-drawing hook, and Power Grid Manager deliberately uses no Harmony, so an
    /// on-map overlay is not implemented without it.
    /// </para>
    /// </summary>
    public sealed class PowerHeatmapPanel
    {
        private const float RowHeight = 22f;
        private const float ButtonWidth = 84f;

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

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, PowerManagementController management)
        {
            float y = rect.y;
            Rect toolbar = new Rect(rect.x, y, rect.width, 28f);
            DrawToolbar(toolbar, state);
            y += 28f + 4f;

            Rect body = new Rect(rect.x, y, rect.width, Mathf.Max(60f, rect.yMax - y));
            PowerUiDraw.Panel(body);

            IReadOnlyList<BuildingPowerSnapshot> buildings = snapshot == null ? null
                : snapshot.Buildings;
            var rows = FilterAndRank(buildings, state, settings, management);
            if (rows.Count == 0)
            {
                PowerUiDraw.WrappedText(new Rect(body.x + 10f, body.y + 10f, body.width - 20f, 60f),
                    "PGM_Heatmap_Empty".Translate(), PowerUiDraw.Muted);
                return;
            }

            Rect outer = new Rect(body.x + 4f, body.y + 4f, body.width - 8f, body.height - 8f);
            float contentHeight = rows.Count * RowHeight;
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(contentHeight, outer.height));
            Widgets.BeginScrollView(outer, ref state.HeatmapScroll, view);
            try
            {
                float rowY = 0f;
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    Rect rowRect = new Rect(0f, rowY, view.width, RowHeight);
                    if (i % 2 == 0) Widgets.DrawBoxSolid(rowRect, new Color(1f, 1f, 1f, 0.04f));
                    DrawRow(rowRect, row, settings, management);
                    if (Widgets.ButtonInvisible(rowRect))
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

        private void DrawToolbar(Rect rect, PowerUiState state)
        {
            PowerUiDraw.Panel(rect);
            float x = rect.x + 6f;
            float y = rect.y + 4f;
            float buttonWidth = Mathf.Min(100f, (rect.width - 30f) / 7f);
            HeatmapMode[] modes =
            {
                HeatmapMode.Consumption, HeatmapMode.Generation, HeatmapMode.Battery,
                HeatmapMode.Stability, HeatmapMode.Isolation, HeatmapMode.Idle,
                HeatmapMode.Critical
            };
            for (int i = 0; i < modes.Length; i++)
            {
                HeatmapMode m = modes[i];
                Rect b = new Rect(x, y, buttonWidth, 22f);
                bool selected = this.mode == m;
                if (selected) Widgets.DrawBoxSolid(b, new Color(1f, 1f, 1f, 0.14f));
                if (Widgets.ButtonText(b, ModeLabel(m))) this.mode = m;
                x += buttonWidth + 6f;
            }
            Rect search = new Rect(x, y, Mathf.Min(200f, rect.width - x - 10f), 22f);
            string updated = Widgets.TextField(search, searchText ?? "");
            if (updated != searchText) { searchText = updated; state.Filter.MarkChanged(); }
            PowerUiDraw.Tip(search, "PGM_Heatmap_SearchTip".Translate());
        }

        private static List<HeatmapRow> FilterAndRank(IReadOnlyList<BuildingPowerSnapshot> buildings,
            PowerUiState state, PowerGridManagerSettings settings,
            PowerManagementController management)
        {
            var result = new List<HeatmapRow>();
            if (buildings == null) return result;
            string search = (state.Filter.SearchText ?? "").ToLower();
            for (int i = 0; i < buildings.Count; i++)
            {
                var building = buildings[i];
                if (building == null || building.Building == null) continue;
                string label = building.Building.def != null ? building.Building.def.label : "";
                if (search.Length > 0 && !label.ToLower().Contains(search)) continue;
                result.Add(new HeatmapRow(building));
            }
            result.Sort((left, right) => right.HeatValue.CompareTo(left.HeatValue));
            return result;
        }

        private void DrawRow(Rect rect, HeatmapRow row, PowerGridManagerSettings settings,
            PowerManagementController management)
        {
            Color heat = HeatColor(row, settings, management);
            Rect heatCell = new Rect(rect.x + 4f, rect.y + 2f, 60f, RowHeight - 4f);
            Widgets.DrawBoxSolid(heatCell, heat);
            Widgets.DrawBox(heatCell, 1);

            string label = row.Building.Building.def != null
                ? row.Building.Building.def.label.CapitalizeFirst() : "?";
            Rect nameRect = new Rect(rect.x + 70f, rect.y, rect.width - 70f - ButtonWidth * 2f - 12f,
                RowHeight);
            PowerUiDraw.TextEllipsized(nameRect, label,
                row.Building.HasAnyDraw ? Color.white : PowerUiDraw.Muted);

            Rect valueRect = new Rect(nameRect.xMax + 4f, rect.y, 92f, RowHeight);
            PowerUiDraw.DrawText(valueRect, row.ValueText, PowerUiDraw.Muted, TextAnchor.MiddleRight);

            float bx = rect.xMax - 6f;
            Rect inspect = new Rect(bx - ButtonWidth + 4f, rect.y, ButtonWidth - 4f, 22f);
            if (Widgets.ButtonText(inspect, "PGM_Heatmap_Inspect".Translate()))
            {
                Find.WindowStack.Add(new Dialog_InfoCard(row.Building.Building));
            }
            PowerUiDraw.Tip(inspect, "PGM_Heatmap_InspectTip".Translate());

            bx -= ButtonWidth;
            Rect locate = new Rect(bx, rect.y, ButtonWidth - 4f, 22f);
            if (Widgets.ButtonText(locate, "PGM_Heatmap_Locate".Translate()))
            {
                CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(row.Building.Building));
            }
            PowerUiDraw.Tip(locate, "PGM_Heatmap_LocateTip".Translate());

            PowerUiDraw.Tip(rect, Tooltip(row, settings, management));
        }

        private Color HeatColor(HeatmapRow row, PowerGridManagerSettings settings,
            PowerManagementController management)
        {
            switch (mode)
            {
                case HeatmapMode.Consumption:
                    return ConsumptionColor(row.Building.ConsumptionWatts,
                        settings.HighConsumptionWatts);
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
                    return PowerUiDraw.Muted;
            }
        }

        private static Color ConsumptionColor(float watts, float threshold)
        {
            if (threshold > 0f && watts >= threshold) return PowerUiDraw.NoticeColor;
            if (watts > 0.01f) return PowerUiDraw.Warn;
            return PowerUiDraw.Muted;
        }

        private static Color GenerationColor(float watts)
        {
            return watts > 0.01f ? PowerUiDraw.Good : PowerUiDraw.Muted;
        }

        private static Color BatteryColor(BuildingPowerSnapshot building)
        {
            if (!building.IsBattery) return PowerUiDraw.Muted;
            float charge = building.CapacityWattDays > 0f
                ? building.StoredWattDays / building.CapacityWattDays : 0f;
            if (charge <= 0.25f) return PowerUiDraw.NoticeColor;
            if (charge <= 0.55f) return PowerUiDraw.Warn;
            return PowerUiDraw.Good;
        }

        private static Color StabilityColor(BuildingPowerSnapshot building)
        {
            if (building.Connection != PowerConnectionState.Connected) return PowerUiDraw.Warn;
            if (building.IsConsumer && building.Connection == PowerConnectionState.Connected &&
                building.ConsumptionWatts > building.GenerationWatts + 0.01f)
                return PowerUiDraw.NoticeColor;
            return PowerUiDraw.Good;
        }

        private static Color IsolationColor(BuildingPowerSnapshot building)
        {
            if (building.IsConsumer && building.Connection == PowerConnectionState.NoNetwork)
                return PowerUiDraw.NoticeColor;
            return PowerUiDraw.Muted;
        }

        private static Color IdleColor(BuildingPowerSnapshot building)
        {
            if (building.IsFlickable && building.SwitchIsOn == false &&
                building.ConsumptionWatts > 0.01f) return PowerUiDraw.Warn;
            return PowerUiDraw.Muted;
        }

        private static Color CriticalColor(BuildingPowerSnapshot building,
            PowerManagementController management)
        {
            bool protectedBuilding = management != null &&
                management.Registry != null &&
                management.Registry.Find(building.Building) != null &&
                !management.Registry.Find(building.Building).Sheddable;
            return protectedBuilding ? PowerUiDraw.Good : PowerUiDraw.Muted;
        }

        private string ValueText(HeatmapRow row)
        {
            switch (mode)
            {
                case HeatmapMode.Consumption:
                    return PowerFormatting.FormatWatts(row.Building.ConsumptionWatts);
                case HeatmapMode.Generation:
                    return row.Building.GenerationWatts > 0.01f
                        ? PowerFormatting.FormatWatts(row.Building.GenerationWatts) : "—";
                case HeatmapMode.Battery:
                    if (!row.Building.IsBattery) return "—";
                    return PowerFormatting.FormatEnergy(row.Building.StoredWattDays) + " / " +
                        PowerFormatting.FormatEnergy(row.Building.CapacityWattDays);
                case HeatmapMode.Stability:
                    return row.Building.Connection == PowerConnectionState.Connected
                        ? row.Building.NetworkId.Value.ToString() : "—";
                case HeatmapMode.Isolation:
                    return row.Building.Connection == PowerConnectionState.NoNetwork
                        ? "PGM_Heatmap_Isolated".Translate() : "—";
                case HeatmapMode.Idle:
                    return row.Building.ConsumptionWatts > 0.01f
                        ? PowerFormatting.FormatWatts(row.Building.ConsumptionWatts) : "—";
                case HeatmapMode.Critical:
                    return row.Building.ConsumptionWatts > 0.01f
                        ? PowerFormatting.FormatWatts(row.Building.ConsumptionWatts) : "—";
                default:
                    return "";
            }
        }

        private string Tooltip(HeatmapRow row, PowerGridManagerSettings settings,
            PowerManagementController management)
        {
            var b = row.Building;
            return "PGM_Heatmap_Tooltip".Translate(
                PowerGridUtilities.BuildingLabel(b.Building),
                PowerFormatting.FormatWatts(b.ConsumptionWatts),
                b.NominalConsumptionWatts.HasValue
                    ? PowerFormatting.FormatWatts(b.NominalConsumptionWatts.Value) : "PGM_Value_Unknown".Translate(),
                b.NetworkId.HasValue ? b.NetworkId.Value.ToString() : "PGM_Value_Unknown".Translate(),
                b.ConsumptionWatts > 0.01f && management != null &&
                management.Registry != null && management.Registry.Find(b.Building) != null &&
                management.Registry.Find(b.Building).Sheddable
                    ? "PGM_Heatmap_Policy_Sheddable".Translate()
                    : "PGM_Heatmap_Policy_Protected".Translate());
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
            public HeatmapRow(BuildingPowerSnapshot building)
            {
                Building = building;
                HeatValue = building.ConsumptionWatts + building.GenerationWatts;
            }
        }
    }
}
