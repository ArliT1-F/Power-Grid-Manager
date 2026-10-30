using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Dashboard power heatmap with a schematic map and a ranked building list.
    /// The map reads terrain and building footprints when snapshots refresh; neither view
    /// changes the simulation.
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

        private static readonly HeatmapMode[] Modes =
        {
            HeatmapMode.Consumption, HeatmapMode.Generation, HeatmapMode.Battery,
            HeatmapMode.Stability, HeatmapMode.Isolation, HeatmapMode.Idle,
            HeatmapMode.Critical
        };

        private HeatmapMode mode = HeatmapMode.Consumption;
        private string searchText = "";
        private bool showMap = true;
        private Map renderedMap;
        private PowerGridSnapshot renderedSnapshot;
        private Texture2D mapTexture;
        private readonly List<HeatmapRow> cachedRows = new List<HeatmapRow>(128);
        private bool hasCachedRows;
        private PowerGridSnapshot cachedSnapshot;
        private HeatmapMode cachedMode;
        private string cachedSearchText;
        private PowerManagementController cachedManagement;
        private int cachedManagementRevision;
        private PowerManagementRegistry cachedRegistry;
        private readonly List<PolicyState> cachedPolicy = new List<PolicyState>(32);

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, PowerManagementController management, Map map = null)
        {
            if (state == null || rect.width <= 8f || rect.height <= 8f) return;

            float y = rect.y;
            bool twoRowToolbar = rect.width < 820f;
            float toolbarH = twoRowToolbar ? 62f : 36f;
            DrawToolbar(new Rect(rect.x, y, rect.width, toolbarH), state, twoRowToolbar);
            y += toolbarH + 6f;
            if (map != null)
            {
                float toggleWidth = Mathf.Min(150f, rect.width * 0.45f);
                if (PowerUiDraw.Button(new Rect(rect.x, y, toggleWidth, 24f),
                    "PGM_Heatmap_MapView".Translate(), showMap)) showMap = true;
                if (PowerUiDraw.Button(new Rect(rect.x + toggleWidth + 4f, y, toggleWidth, 24f),
                    "PGM_Heatmap_ListView".Translate(), !showMap)) showMap = false;
                y += 30f;
            }

            Rect body = new Rect(rect.x, y, rect.width, Mathf.Max(60f, rect.yMax - y));
            PowerUiDraw.Panel(body);

            FilterAndRank(snapshot, management);
            if (map != null && showMap)
            {
                DrawMap(body, map, snapshot, management);
                return;
            }
            if (cachedRows.Count == 0)
            {
                PowerUiDraw.WrappedText(new Rect(body.x + 12f, body.y + 12f, body.width - 24f, 60f),
                    "PGM_Heatmap_Empty".Translate(), PowerUiDraw.TextSecondary);
                return;
            }

            Rect outer = new Rect(body.x + 6f, body.y + 6f, body.width - 12f, body.height - 12f);
            float contentHeight = cachedRows.Count * RowHeight;
            Rect view = new Rect(0f, 0f, Mathf.Max(140f, outer.width - 18f), Mathf.Max(contentHeight, outer.height));

            // A search or a new snapshot can shrink the list while it is scrolled down.
            // Correct an offset beyond the entire list without restricting the extra scroll
            // space a horizontal scrollbar can require in very narrow windows.
            if (state.HeatmapScroll.y < 0f) state.HeatmapScroll.y = 0f;
            else if (state.HeatmapScroll.y >= contentHeight)
                state.HeatmapScroll.y = Mathf.Max(0f, contentHeight - outer.height);

            Widgets.BeginScrollView(outer, ref state.HeatmapScroll, view);
            try
            {
                // BeginScrollView clips drawing, but calling every widget is still expensive.
                // Preserve full-list positions and overscan one row at each edge so pixel-grid
                // rounding and partial scroll positions cannot leave a gap in the viewport.
                int first = Mathf.Clamp(Mathf.FloorToInt(state.HeatmapScroll.y / RowHeight) - 1,
                    0, cachedRows.Count);
                int end = Mathf.Clamp(Mathf.CeilToInt((state.HeatmapScroll.y + outer.height) / RowHeight) + 1,
                    first, cachedRows.Count);
                for (int i = first; i < end; i++)
                {
                    HeatmapRow row = cachedRows[i];
                    Rect rowRect = new Rect(0f, i * RowHeight, view.width, RowHeight);
                    bool selected = ReferenceEquals(state.SelectedBuilding, row.Building.Building);
                    PowerUiDraw.RowBackground(rowRect, i, selected);
                    DrawRow(rowRect, row, settings, management);
                    if (Widgets.ButtonInvisible(new Rect(rowRect.x, rowRect.y,
                        Mathf.Max(20f, rowRect.width - ButtonWidth * 2f - 12f), RowHeight)))
                    {
                        state.SelectBuilding(row.Building.Building);
                    }
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        public void ReleaseMapTexture()
        {
            if (mapTexture != null) UnityEngine.Object.Destroy(mapTexture);
            mapTexture = null;
            renderedMap = null;
            renderedSnapshot = null;
        }

        // Base map is rebuilt on snapshot changes, not on every IMGUI event. Building
        // footprints use the game's occupied rectangle, including rotation and modded sizes.
        private void DrawMap(Rect body, Map map, PowerGridSnapshot snapshot, PowerManagementController management)
        {
            int width = map.Size.x, height = map.Size.z;
            if (width <= 0 || height <= 0) return;
            if (!ReferenceEquals(renderedMap, map) || !ReferenceEquals(renderedSnapshot, snapshot) || mapTexture == null)
                RebuildMap(map, snapshot, width, height);

            float scale = Mathf.Min((body.width - 24f) / width, (body.height - 48f) / height);
            if (scale <= 0f) return;
            Rect image = new Rect(body.x + (body.width - width * scale) / 2f,
                body.y + 8f, width * scale, height * scale);
            GUI.DrawTexture(image, mapTexture);

            // Map colors are relative to the strongest matching building in this mode.
            // A small minimum marker makes buildings visible even on a full-size colony map.
            for (int i = 0; i < cachedRows.Count; i++)
            {
                HeatmapRow row = cachedRows[i];
                Building building = row.Building.Building;
                if (row.HeatValue <= 0f || !building.Spawned || building.Map != map ||
                    map.fogGrid.IsFogged(building.Position)) continue;
                CellRect cells = building.OccupiedRect();
                Rect marker = MapRect(image, scale, height, cells);
                float centerX = marker.x + marker.width / 2f;
                float centerY = marker.y + marker.height / 2f;
                float size = Mathf.Max(4f, Mathf.Max(marker.width, marker.height));
                marker = new Rect(centerX - size / 2f, centerY - size / 2f, size, size);
                float value = mode == HeatmapMode.Battery ? 1f - row.Intensity : row.Intensity;
                Widgets.DrawBoxSolid(marker, MapHeatColor(value));
                if (Mouse.IsOver(marker)) PowerUiDraw.Tip(marker, Tooltip(row, management));
            }
            PowerUiDraw.WrappedText(new Rect(body.x + 10f, body.yMax - 30f, body.width - 20f, 24f),
                "PGM_Heatmap_MapLegend".Translate(), PowerUiDraw.TextSecondary);
        }

        internal static Rect MapRect(Rect image, float scale, int mapHeight, CellRect cells)
        {
            return new Rect(image.x + cells.minX * scale,
                image.y + (mapHeight - cells.maxZ - 1) * scale,
                cells.Width * scale, cells.Height * scale);
        }

        private static Color MapHeatColor(float value)
        {
            value = Mathf.Clamp01(value);
            if (value < 0.5f) return new Color(0f, 0.35f + value, 1f - value, 0.9f);
            return new Color((value - 0.5f) * 2f, 0.85f - (value - 0.5f) * 1.3f, 0f, 0.9f);
        }

        private void RebuildMap(Map map, PowerGridSnapshot snapshot, int width, int height)
        {
            if (mapTexture != null) UnityEngine.Object.Destroy(mapTexture);
            mapTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            mapTexture.filterMode = FilterMode.Point;
            Color[] pixels = new Color[width * height];
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    pixels[z * width + x] = map.fogGrid.IsFogged(cell)
                        ? new Color(0.015f, 0.035f, 0.07f)
                        : TerrainColor(map.terrainGrid.TerrainAt(cell)?.defName);
                }
            PaintBuildings(map.listerBuildings.allBuildingsNonColonist, map, pixels, width, height);
            PaintBuildings(map.listerBuildings.allBuildingsColonist, map, pixels, width, height);
            mapTexture.SetPixels(pixels);
            mapTexture.Apply();
            renderedMap = map;
            renderedSnapshot = snapshot;
        }

        private static void PaintBuildings(List<Building> buildings, Map map, Color[] pixels, int width, int height)
        {
            if (buildings == null) return;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i];
                if (building == null || !building.Spawned || building.Map != map) continue;
                CellRect cells = building.OccupiedRect();
                for (int z = Mathf.Max(0, cells.minZ); z <= Mathf.Min(height - 1, cells.maxZ); z++)
                    for (int x = Mathf.Max(0, cells.minX); x <= Mathf.Min(width - 1, cells.maxX); x++)
                    {
                        IntVec3 cell = new IntVec3(x, 0, z);
                        if (!map.fogGrid.IsFogged(cell)) pixels[z * width + x] = new Color(0.39f, 0.72f, 0.83f);
                    }
            }
        }

        // A restrained blueprint palette keeps the cell layout visible without competing
        // with the heat markers. Unknown and modded terrains use the neutral background.
        private static Color TerrainColor(string name)
        {
            name = (name ?? "").ToLowerInvariant();
            if (name.Contains("water") || name.Contains("ocean") || name.Contains("river")) return new Color(0.04f, 0.18f, 0.29f);
            if (name.Contains("sand")) return new Color(0.16f, 0.25f, 0.30f);
            if (name.Contains("snow") || name.Contains("ice")) return new Color(0.28f, 0.43f, 0.49f);
            if (name.Contains("soil") || name.Contains("marsh") || name.Contains("mud")) return new Color(0.08f, 0.21f, 0.27f);
            return new Color(0.12f, 0.25f, 0.32f);
        }

        private void DrawToolbar(Rect rect, PowerUiState state, bool twoRow)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = rect.ContractedBy(6f);

            float searchW = twoRow
                ? Mathf.Min(220f, inner.width * 0.45f)
                : Mathf.Min(190f, Mathf.Max(90f, inner.width * 0.22f));
            float modesAreaW = twoRow ? inner.width : Mathf.Max(120f, inner.width - searchW - 8f);
            float buttonWidth = Mathf.Max(42f, (modesAreaW - (Modes.Length - 1) * 4f) / Modes.Length);

            float x = inner.x;
            float y = inner.y;
            for (int i = 0; i < Modes.Length; i++)
            {
                HeatmapMode m = Modes[i];
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

        private void FilterAndRank(PowerGridSnapshot snapshot, PowerManagementController management)
        {
            string currentSearch = searchText ?? string.Empty;
            int managementRevision = management == null ? -1 : management.Revision;
            if (hasCachedRows && ReferenceEquals(snapshot, cachedSnapshot) && mode == cachedMode &&
                currentSearch == cachedSearchText &&
                (mode != HeatmapMode.Critical || CriticalPolicyUnchanged(management, managementRevision)))
                return;

            // Snapshots are immutable. Only Critical ranking depends on management policy;
            // the other modes can reuse their ranking even when a policy changes. Colours and
            // tooltips still read current settings and policy while drawing visible rows.
            hasCachedRows = false;
            cachedSnapshot = snapshot;
            cachedMode = mode;
            cachedSearchText = currentSearch;
            cachedManagement = management;
            cachedManagementRevision = managementRevision;
            cachedRows.Clear();
            IReadOnlyList<BuildingPowerSnapshot> buildings = snapshot == null ? null : snapshot.Buildings;
            if (buildings == null)
            {
                CaptureCriticalPolicy(management);
                hasCachedRows = true;
                return;
            }
            string search = currentSearch.Trim();
            float maxHeat = 1f;

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingPowerSnapshot building = buildings[i];
                if (building == null || building.Building == null) continue;
                if (search.Length > 0 && PowerGridUtilities.BuildingLabel(building.Building)
                    .IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                float hv = ComputeHeatValue(building, management);
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
            CaptureCriticalPolicy(management);
            hasCachedRows = true;
        }

        private bool CriticalPolicyUnchanged(PowerManagementController management, int revision)
        {
            if (!ReferenceEquals(management, cachedManagement) || revision != cachedManagementRevision)
                return false;
            PowerManagementRegistry registry = management == null ? null : management.Registry;
            if (!ReferenceEquals(registry, cachedRegistry)) return false;
            if (registry == null) return cachedPolicy.Count == 0;
            IReadOnlyList<PowerManagementRecord> records = registry.Records;
            if (records.Count != cachedPolicy.Count) return false;

            // Records and their Sheddable field are publicly mutable, including through the
            // integration API without a controller revision. Check that bounded registry
            // (at most MaxRecords), not the potentially much larger snapshot, for such edits.
            for (int i = 0; i < records.Count; i++)
            {
                PowerManagementRecord record = records[i];
                if (!ReferenceEquals(record, cachedPolicy[i].Record) ||
                    (record != null && record.Sheddable != cachedPolicy[i].Sheddable)) return false;
            }
            return true;
        }

        private void CaptureCriticalPolicy(PowerManagementController management)
        {
            cachedPolicy.Clear();
            cachedRegistry = mode == HeatmapMode.Critical && management != null ? management.Registry : null;
            if (cachedRegistry == null) return;
            IReadOnlyList<PowerManagementRecord> records = cachedRegistry.Records;
            for (int i = 0; i < records.Count; i++)
            {
                PowerManagementRecord record = records[i];
                cachedPolicy.Add(new PolicyState(record, record != null && record.Sheddable));
            }
        }

        private float ComputeHeatValue(BuildingPowerSnapshot b, PowerManagementController management)
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
                    PowerManagementRecord record = FindManagementRecord(b.Building, management);
                    return record != null && !record.Sheddable
                        ? 1000f + b.ConsumptionWatts : b.ConsumptionWatts * 0.1f;
                default:
                    return b.ConsumptionWatts + b.GenerationWatts;
            }
        }

        private void DrawRow(Rect rect, HeatmapRow row, PowerGridManagerSettings settings,
            PowerManagementController management)
        {
            Color heat = HeatColor(row, settings, management);
            string sym = HeatSymbol(heat);

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

            // Formatting the detailed tooltip allocates several strings; only do that for
            // the hovered row (or when the game's tooltip-edge debugger is enabled).
            if (Mouse.IsOver(rect) || DebugViewSettings.drawTooltipEdges)
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

        private static string HeatSymbol(Color color)
        {
            if (color == PowerUiDraw.NoticeColor) return "[!!]";
            if (color == PowerUiDraw.Warn) return "[!]";
            if (color == PowerUiDraw.Good) return "[OK]";
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
            PowerManagementRecord record = FindManagementRecord(building.Building, management);
            return record != null && !record.Sheddable ? PowerUiDraw.Good : PowerUiDraw.TextSecondary;
        }

        private static PowerManagementRecord FindManagementRecord(Building building, PowerManagementController management)
        {
            PowerManagementRegistry registry = management == null ? null : management.Registry;
            // StableId allocates a string. Most colonies have no configured records, so avoid
            // constructing one per building just to look up an entry in an empty dictionary.
            return registry == null || registry.Count == 0 ? null : registry.Find(building);
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
            PowerManagementRecord record = b.ConsumptionWatts > 0.01f
                ? FindManagementRecord(b.Building, management) : null;
            return "PGM_Heatmap_Tooltip".Translate(
                PowerGridUtilities.BuildingLabel(b.Building),
                PowerFormatting.FormatWatts(b.ConsumptionWatts),
                b.NominalConsumptionWatts.HasValue
                    ? PowerFormatting.FormatWatts(b.NominalConsumptionWatts.Value)
                    : (string)"PGM_Value_Unknown".Translate(),
                b.NetworkId.HasValue
                    ? b.NetworkId.Value.ToString()
                    : (string)"PGM_Value_Unknown".Translate(),
                record != null && record.Sheddable
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

        private struct PolicyState
        {
            public readonly PowerManagementRecord Record;
            public readonly bool Sheddable;

            public PolicyState(PowerManagementRecord record, bool sheddable)
            {
                Record = record;
                Sheddable = sheddable;
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
