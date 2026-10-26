using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Searchable, sortable, grouped table of the map's power buildings, with a detail
    /// card for the selected building. The table is a cached view (see
    /// <see cref="BuildingListModel"/>); nothing here scans the map.
    /// </summary>
    public sealed class BuildingPowerPanel
    {
        private const float ToolbarHeight = 28f;
        private const float RowHeight = 22f;
        private const float GroupHeaderHeight = 20f;
        private const float DetailHeight = 150f;
        private const float ColumnGap = 4f;

        private readonly BuildingListModel model = new BuildingListModel();
        private readonly float[] cachedColumnWidths = new float[6];

        public BuildingListModel Model => model;

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, PowerManagementController management = null, int tick = 0)
        {
            state.ValidateSelection();

            float y = rect.y;
            Rect toolbar = new Rect(rect.x, y, rect.width, ToolbarHeight);
            y += ToolbarHeight + 4f;

            bool hasSelection = state.SelectedBuilding != null;
            bool showManagement = hasSelection && management != null && settings != null &&
                                  settings.ManagementEnabled;
            float detailSpace = hasSelection ? DetailHeight + 6f + (showManagement ? 34f : 0f) : 0f;
            float tableHeight = Mathf.Max(60f, rect.yMax - y - detailSpace);
            Rect tableRect = new Rect(rect.x, y, rect.width, tableHeight);

            DrawToolbar(toolbar, snapshot, state, settings);
            model.Update(snapshot, state.Filter, state.SortKey, state.SortDescending, state.Grouping,
                settings.HighConsumptionWatts);
            DrawTable(tableRect, snapshot, state, settings);

            if (hasSelection)
            {
                Rect detail = new Rect(rect.x, tableRect.yMax + 6f, rect.width,
                    Mathf.Max(0f, rect.yMax - tableRect.yMax - 6f));
                DrawDetail(detail, state, settings, management, tick, showManagement);
            }
        }

        private void DrawToolbar(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings)
        {
            // Compact layout below this width: category and grouping move into one menu so
            // that nothing is clipped on small resolutions.
            bool compact = rect.width < 760f;
            float x = rect.x;
            float searchWidth = Mathf.Clamp(rect.width * 0.26f, 130f, 240f);
            Rect searchRect = new Rect(x, rect.y, searchWidth, 26f);
            string updated = Widgets.TextField(searchRect, state.Filter.SearchText ?? "");
            if (updated != state.Filter.SearchText)
            {
                state.Filter.SearchText = updated;
                state.Filter.MarkChanged();
            }
            PowerUiDraw.Tip(searchRect, "PGM_Buildings_SearchTip".Translate());
            x += searchWidth + ColumnGap;

            Rect sortRect = new Rect(x, rect.y, 148f, 26f);
            if (Widgets.ButtonText(sortRect,
                    "PGM_Buildings_SortButton".Translate(SortKeyLabel(state.SortKey), state.SortDescending ? "v" : "^")))
            {
                OpenSortMenu(state);
            }
            x += sortRect.width + ColumnGap;

            Rect filterRect = new Rect(x, rect.y, 92f, 26f);
            if (Widgets.ButtonText(filterRect, "PGM_Buildings_FiltersButton".Translate()))
            {
                OpenFilterMenu(state, snapshot, settings);
            }
            if (state.Filter.HasNonDefaultFilters)
            {
                Widgets.DrawBoxSolid(new Rect(filterRect.xMax - 4f, filterRect.y + 2f, 6f, 6f), PowerUiDraw.Warn);
            }
            x += filterRect.width + ColumnGap;

            if (compact)
            {
                Rect viewRect = new Rect(x, rect.y, 92f, 26f);
                if (Widgets.ButtonText(viewRect, "PGM_Buildings_ViewButton".Translate(GroupingLabel(state.Grouping))))
                {
                    OpenViewMenu(state);
                }
                x += viewRect.width + ColumnGap;
            }
            else
            {
                Rect categoryRect = new Rect(x, rect.y, 124f, 26f);
                string categoryLabel = state.Filter.Category.HasValue
                    ? PowerGridUtilities.CategoryLabelKey(state.Filter.Category.Value).Translate()
                    : "PGM_Buildings_AllCategories".Translate();
                if (Widgets.ButtonText(categoryRect, categoryLabel)) OpenCategoryMenu(state);
                x += categoryRect.width + ColumnGap;

                Rect groupRect = new Rect(x, rect.y, 112f, 26f);
                if (Widgets.ButtonText(groupRect,
                        "PGM_Buildings_GroupButton".Translate(GroupingLabel(state.Grouping))))
                {
                    OpenGroupingMenu(state);
                }
                x += groupRect.width + ColumnGap;
            }

            if (state.Filter.HasNonDefaultFilters)
            {
                Rect resetRect = new Rect(x, rect.y, 72f, 26f);
                if (Widgets.ButtonText(resetRect, "PGM_Buildings_Reset".Translate()))
                {
                    state.Filter.Reset();
                }
                x += resetRect.width + ColumnGap;
            }

            Rect countRect = new Rect(x, rect.y, Mathf.Max(0f, rect.xMax - x), 26f);
            if (countRect.width > 80f)
            {
                PowerUiDraw.TextEllipsized(countRect,
                    "PGM_Buildings_Count".Translate(model.Count, snapshot.Buildings.Count), PowerUiDraw.Muted,
                    TextAnchor.MiddleRight);
            }
        }

        /// <summary>Compact-view menu holding the category, grouping and reset controls.</summary>
        private void OpenViewMenu(PowerUiState state)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(state.Filter.Category.HasValue
                        ? "PGM_Buildings_CategorySelected".Translate(
                            PowerGridUtilities.CategoryLabelKey(state.Filter.Category.Value).Translate())
                        : "PGM_Buildings_AllCategories".Translate(), () => OpenCategoryMenu(state)),
                new FloatMenuOption("PGM_Group_None".Translate(), () => state.Grouping = BuildingGrouping.None),
                new FloatMenuOption("PGM_Group_Category".Translate(), () => state.Grouping = BuildingGrouping.Category),
                new FloatMenuOption("PGM_Group_Room".Translate(), () => state.Grouping = BuildingGrouping.Room),
                new FloatMenuOption("PGM_Group_Network".Translate(), () => state.Grouping = BuildingGrouping.Network),
                new FloatMenuOption("PGM_Buildings_Reset".Translate(), () => state.Filter.Reset())
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawTable(Rect rect, PowerGridSnapshot snapshot, PowerUiState state, PowerGridManagerSettings settings)
        {
            PowerUiDraw.Panel(rect);
            Rect header = new Rect(rect.x + 4f, rect.y + 2f, rect.width - 8f, 18f);
            FillColumnWidths(header.width, cachedColumnWidths);
            DrawHeader(header);

            Rect outer = new Rect(rect.x + 2f, rect.y + 22f, rect.width - 4f, rect.height - 24f);
            if (model.Count == 0)
            {
                PowerUiDraw.WrappedText(new Rect(outer.x + 8f, outer.y + 6f, outer.width - 16f, 60f),
                    snapshot.Buildings.Count == 0
                        ? "PGM_Buildings_NoPowerBuildings".Translate()
                        : "PGM_Buildings_NoMatches".Translate(),
                    PowerUiDraw.Muted);
                return;
            }

            float contentHeight = 0f;
            int previousGroup = int.MinValue;
            for (int i = 0; i < model.Count; i++)
            {
                contentHeight += RowHeight;
                if (state.Grouping != BuildingGrouping.None && model.GroupOrderAt(i) != previousGroup)
                {
                    contentHeight += GroupHeaderHeight;
                    previousGroup = model.GroupOrderAt(i);
                }
            }

            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(contentHeight + 4f, outer.height));
            Widgets.BeginScrollView(outer, ref state.BuildingScroll, view);
            try
            {
                float y = 0f;
                previousGroup = int.MinValue;
                for (int i = 0; i < model.Count; i++)
                {
                    BuildingPowerSnapshot building = model.Items[i];
                    if (state.Grouping != BuildingGrouping.None && model.GroupOrderAt(i) != previousGroup)
                    {
                        previousGroup = model.GroupOrderAt(i);
                        Rect groupRect = new Rect(0f, y, view.width, GroupHeaderHeight);
                        Widgets.DrawBoxSolid(groupRect, new Color(1f, 1f, 1f, 0.06f));
                        PowerUiDraw.DrawText(new Rect(groupRect.x + 6f, groupRect.y, groupRect.width - 12f, groupRect.height),
                            model.GroupLabelAt(i), PowerUiDraw.Muted);
                        y += GroupHeaderHeight;
                    }

                    Rect row = new Rect(0f, y, view.width, RowHeight);
                    bool selected = state.SelectedBuilding != null && state.SelectedBuilding == building.Building;
                    if (selected) Widgets.DrawHighlightSelected(row);
                    else Widgets.DrawHighlightIfMouseover(row);
                    DrawRow(row, building, settings, cachedColumnWidths);
                    if (Widgets.ButtonInvisible(row)) state.SelectBuilding(building.Building);
                    y += RowHeight;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawHeader(Rect rect)
        {
            float[] widths = cachedColumnWidths;
            FillColumnWidths(rect.width, widths);
            float x = rect.x;
            PowerUiDraw.DrawText(new Rect(x, rect.y, widths[0], rect.height), "PGM_Column_Building".Translate(), PowerUiDraw.Muted);
            x += widths[0];
            PowerUiDraw.DrawText(new Rect(x, rect.y, widths[1], rect.height), "PGM_Column_Category".Translate(), PowerUiDraw.Muted);
            x += widths[1];
            PowerUiDraw.DrawText(new Rect(x, rect.y, widths[2], rect.height), "PGM_Column_Draw".Translate(), PowerUiDraw.Muted,
                TextAnchor.MiddleRight);
            x += widths[2];
            PowerUiDraw.DrawText(new Rect(x, rect.y, widths[3], rect.height), "PGM_Column_Generation".Translate(),
                PowerUiDraw.Muted, TextAnchor.MiddleRight);
            x += widths[3];
            PowerUiDraw.DrawText(new Rect(x, rect.y, widths[4], rect.height), "PGM_Column_Network".Translate(),
                PowerUiDraw.Muted, TextAnchor.MiddleRight);
            x += widths[4];
            PowerUiDraw.DrawText(new Rect(x, rect.y, widths[5], rect.height), "PGM_Column_Status".Translate(),
                PowerUiDraw.Muted, TextAnchor.MiddleRight);
        }

        /// <summary>Column layout in the order: name, category, draw, generation, network, status.</summary>
        private static void FillColumnWidths(float width, float[] widths)
        {
            float available = Mathf.Max(120f, width);
            const float draw = 92f, generation = 92f, network = 64f, status = 116f, category = 124f;
            float fixedWidth = draw + generation + network + status + category + ColumnGap * 5f;
            widths[0] = Mathf.Max(120f, available - fixedWidth);
            widths[1] = category;
            widths[2] = draw;
            widths[3] = generation;
            widths[4] = network;
            widths[5] = status;
        }

        private void DrawRow(Rect rect, BuildingPowerSnapshot building, PowerGridManagerSettings settings,
            float[] widths)
        {
            float x = rect.x;
            ThingDef def = building.Building.def;
            string label = def != null && !string.IsNullOrEmpty(def.label) ? def.label.CapitalizeFirst() : "?";
            Rect nameRect = new Rect(x, rect.y, widths[0] - 46f, rect.height);
            PowerUiDraw.TextEllipsized(nameRect, label,
                building.IsConsumer && building.HasAnyDraw ? Color.white : PowerUiDraw.Muted);
            if (building.IsFlickable)
            {
                PowerUiDraw.DrawText(new Rect(x + widths[0] - 44f, rect.y, 40f, rect.height),
                    building.SwitchIsOn == true ? "PGM_Flick_On".Translate() : "PGM_Flick_Off".Translate(),
                    building.SwitchIsOn == true ? PowerUiDraw.Muted : PowerUiDraw.Warn, TextAnchor.MiddleRight);
            }
            x += widths[0];

            PowerUiDraw.DrawText(new Rect(x, rect.y, widths[1] - 6f, rect.height),
                PowerGridUtilities.CategoryLabelKey(building.Category).Translate(), PowerUiDraw.Muted);
            x += widths[1];

            PowerUiDraw.DrawText(new Rect(x, rect.y, widths[2] - 6f, rect.height),
                PowerFormatting.FormatWatts(building.ConsumptionWatts), Color.white, TextAnchor.MiddleRight);
            x += widths[2];

            PowerUiDraw.DrawText(new Rect(x, rect.y, widths[3] - 6f, rect.height),
                building.GenerationWatts > 0.01f ? PowerFormatting.FormatWatts(building.GenerationWatts) : "—",
                building.GenerationWatts > 0.01f ? PowerUiDraw.Good : PowerUiDraw.Muted, TextAnchor.MiddleRight);
            x += widths[3];

            PowerUiDraw.DrawText(new Rect(x, rect.y, widths[4] - 6f, rect.height),
                building.NetworkId.HasValue ? building.NetworkId.Value.ToString() : "—",
                building.NetworkId.HasValue ? PowerUiDraw.Muted : PowerUiDraw.Warn, TextAnchor.MiddleRight);
            x += widths[4];

            string statusKey;
            Color statusColor;
            GetStatus(building, settings, out statusKey, out statusColor);
            PowerUiDraw.DrawText(new Rect(x, rect.y, widths[5], rect.height), statusKey.Translate(), statusColor,
                TextAnchor.MiddleRight);
        }

        private static void GetStatus(BuildingPowerSnapshot building, PowerGridManagerSettings settings,
            out string statusKey, out Color statusColor)
        {
            if (building.HasUnsupportedComponent)
            {
                statusKey = "PGM_RowStatus_Unsupported";
                statusColor = PowerUiDraw.StatusColorForUnknown();
                return;
            }
            if (building.HasMultipleNetworks)
            {
                statusKey = "PGM_RowStatus_MultipleNetworks";
                statusColor = PowerUiDraw.Warn;
                return;
            }
            if (building.Connection != PowerConnectionState.Connected)
            {
                statusKey = PowerGridUtilities.ConnectionLabelKey(building.Connection);
                statusColor = PowerUiDraw.Warn;
                return;
            }
            if (building.IsFlickable && building.SwitchIsOn == false)
            {
                statusKey = "PGM_RowStatus_SwitchedOff";
                statusColor = PowerUiDraw.Warn;
                return;
            }
            if (building.HasOffComponents)
            {
                statusKey = "PGM_RowStatus_PoweredDown";
                statusColor = PowerUiDraw.Muted;
                return;
            }
            if (PowerGridUtilities.IsHighConsumption(building, settings.HighConsumptionWatts))
            {
                statusKey = "PGM_RowStatus_HighConsumption";
                statusColor = PowerUiDraw.Warn;
                return;
            }
            statusKey = "PGM_RowStatus_Ok";
            statusColor = PowerUiDraw.Good;
        }

        private void DrawDetail(Rect rect, PowerUiState state, PowerGridManagerSettings settings,
            PowerManagementController management, int tick, bool showManagement)
        {
            PowerUiDraw.Panel(rect);
            Building building = state.SelectedBuilding;
            ThingDef def = building.def;
            float y = rect.y + 6f;

            Rect titleRect = new Rect(rect.x + 8f, y, rect.width - 200f, 22f);
            PowerUiDraw.TextEllipsized(titleRect,
                def != null && !string.IsNullOrEmpty(def.label) ? def.label.CapitalizeFirst() : "?", Color.white);
            PowerUiDraw.DrawText(new Rect(rect.x + 8f, y, rect.width - 16f, 22f), def != null ? def.defName : "",
                PowerUiDraw.Muted, TextAnchor.MiddleRight);
            y += 24f;

            BuildingPowerSnapshot snapshot = FindBuildingSnapshot(state, building);
            if (snapshot == null)
            {
                PowerUiDraw.WrappedText(new Rect(rect.x + 8f, y, rect.width - 16f, 40f),
                    "PGM_Detail_SelectionMissing".Translate(), PowerUiDraw.Warn);
                return;
            }

            // Field list rendered in one or two columns depending on the available width.
            var fields = new List<DetailField>(10)
            {
                new DetailField("PGM_Detail_CurrentDraw", PowerFormatting.FormatWatts(snapshot.ConsumptionWatts)),
                new DetailField("PGM_Detail_NominalDraw", PowerFormatting.FormatWattsUnknown(snapshot.NominalConsumptionWatts)),
                new DetailField("PGM_Detail_Generation", PowerFormatting.FormatWatts(snapshot.GenerationWatts)),
                new DetailField("PGM_Detail_TraderState", TraderStateLabel(snapshot)),
                new DetailField("PGM_Detail_Category", PowerGridUtilities.CategoryLabelKey(snapshot.Category).Translate()),
                new DetailField("PGM_Detail_Network", snapshot.NetworkId.HasValue
                    ? snapshot.NetworkId.Value.ToString()
                    : "PGM_Value_Unknown".Translate()),
                new DetailField("PGM_Detail_Position",
                    PowerFormatting.FormatPosition(building.Position.x, building.Position.z)),
                new DetailField("PGM_Detail_Room", PowerGridUtilities.RoomLabel(snapshot.Room)),
                new DetailField("PGM_Detail_EstDailyEnergy",
                    PowerFormatting.FormatEnergy(PowerFormatting.EstimateDailyWattDays(snapshot.ConsumptionWatts)),
                    "PGM_Tip_EstDailyEnergy"),
                new DetailField("PGM_Detail_History", "PGM_Value_NotRecorded".Translate(), "PGM_Tip_HistoryPerBuilding")
            };
            if (snapshot.IsBattery)
            {
                fields.Insert(4, new DetailField("PGM_Detail_StoredCharge",
                    PowerFormatting.FormatEnergy(snapshot.StoredWattDays) + " / " +
                    PowerFormatting.FormatEnergy(snapshot.CapacityWattDays)));
            }
            PowerSupportLevel support = CompatibilityRegistry.Evaluate(snapshot);
            fields.Add(new DetailField("PGM_Detail_Support", CompatibilityRegistry.LabelKey(support).Translate(),
                CompatibilityRegistry.TooltipKey(support),
                support == PowerSupportLevel.Supported ? PowerUiDraw.Good : PowerUiDraw.Warn));

            int columns = rect.width >= 700f ? 2 : 1;
            float columnWidth = (rect.width - 24f) / columns;
            float labelWidth = Mathf.Clamp(columnWidth * 0.55f, 120f, 200f);
            int rows = (fields.Count + columns - 1) / columns;
            for (int i = 0; i < fields.Count; i++)
            {
                int column = i / rows;
                int row = i % rows;
                float fieldX = rect.x + 8f + column * columnWidth;
                float fieldY = y + row * 18f;
                Rect fieldRect = new Rect(fieldX, fieldY, columnWidth, 18f);
                if (fieldY + 18f > rect.yMax - (showManagement ? 84f : 34f)) break;
                DetailField field = fields[i];
                PowerUiDraw.LabelValue(fieldRect, field.LabelKey.Translate(), field.Value,
                    Mathf.Min(labelWidth, columnWidth - 90f), field.ValueColor);
                if (field.TooltipKey != null) PowerUiDraw.Tip(fieldRect, field.TooltipKey.Translate());
            }

            float buttonY = rect.yMax - 30f;
            float buttonWidth = Mathf.Min(126f, (rect.width - 30f) / 3f);
            Rect locateRect = new Rect(rect.x + 8f, buttonY, buttonWidth, 24f);
            if (Widgets.ButtonText(locateRect, "PGM_Detail_Locate".Translate()))
            {
                CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(building));
            }
            Rect inspectRect = new Rect(locateRect.xMax + 6f, buttonY, buttonWidth, 24f);
            if (Widgets.ButtonText(inspectRect, "PGM_Detail_Inspect".Translate()))
            {
                Find.WindowStack.Add(new Dialog_InfoCard(building));
            }
            Rect clearRect = new Rect(inspectRect.xMax + 6f, buttonY, buttonWidth, 24f);
            if (Widgets.ButtonText(clearRect, "PGM_Detail_Clear".Translate()))
            {
                state.SelectedBuilding = null;
            }

            if (showManagement)
            {
                DrawManagementRow(new Rect(rect.x + 8f, buttonY - 30f, rect.width - 16f, 26f),
                    building, snapshot, snapshot, settings, management, tick);
            }
        }

        /// <summary>
        /// Optional controls on the building detail card. They are only drawn when the player has
        /// explicitly enabled management, and incompatible buildings show a reason instead of a
        /// switch button.
        /// </summary>
        private void DrawManagementRow(Rect rect, Building building, BuildingPowerSnapshot row,
            PowerGridSnapshot mapSnapshot, PowerGridManagerSettings settings,
            PowerManagementController management, int tick)
        {
            PowerManagementRegistry registry = management.Registry;
            PowerManagementRecord record = registry == null ? null : registry.Find(building);
            PowerControlSupport support = CompatibilityRegistry.EvaluateControl(row, registry);
            bool usable = support != PowerControlSupport.Unsupported &&
                          support != PowerControlSupport.Pending;

            float x = rect.x;
            float width = Mathf.Max(52f, (rect.width - 16f) / 5f);
            Rect onRect = new Rect(x, rect.y, width, 24f);
            if (Widgets.ButtonText(onRect, "PGM_Mgmt_Action_On".Translate()) && usable)
            {
                string detail;
                management.ManualSwitch(mapSnapshot, building, true, settings, tick, out detail);
            }
            x += width + 4f;
            Rect offRect = new Rect(x, rect.y, width, 24f);
            if (Widgets.ButtonText(offRect, "PGM_Mgmt_Action_Off".Translate()) && usable)
            {
                string detail;
                management.ManualSwitch(mapSnapshot, building, false, settings, tick, out detail);
            }
            x += width + 4f;
            Rect groupRect = new Rect(x, rect.y, width, 24f);
            if (Widgets.ButtonText(groupRect, "PGM_Mgmt_Action_Group".Translate()))
            {
                OpenGroupMenuForDetail(management, building, tick);
            }
            x += width + 4f;
            Rect shedRect = new Rect(x, rect.y, width, 24f);
            if (Widgets.ButtonText(shedRect, record != null && record.Sheddable
                    ? "PGM_Mgmt_Action_Protect".Translate() : "PGM_Mgmt_Action_Sheddable".Translate()))
            {
                management.SetSheddable(building, record == null || !record.Sheddable, tick);
            }
            x += width + 4f;
            Rect diagRect = new Rect(x, rect.y, Mathf.Max(0f, rect.xMax - x), 24f);
            if (Widgets.ButtonText(diagRect, record != null && record.ExcludeFromDiagnostics
                    ? "PGM_Mgmt_Action_AllowDiagnostics".Translate()
                    : "PGM_Mgmt_Action_Diagnostics".Translate()))
            {
                management.SetExcludedFromDiagnostics(building,
                    record == null || !record.ExcludeFromDiagnostics, tick);
            }

            Rect supportRect = new Rect(rect.x, rect.y - 18f, rect.width, 18f);
            PowerUiDraw.TextEllipsized(supportRect,
                "PGM_Detail_Management".Translate(
                    CompatibilityRegistry.ControlLabelKey(support).Translate(),
                    "PGM_Mgmt_Priority".Translate(record == null
                        ? PowerManagementRegistry.DefaultPriority : record.ShedPriority),
                    record == null || record.Group.Length == 0
                        ? "PGM_Mgmt_Group_None".Translate().ToString() : record.Group),
                usable ? PowerUiDraw.Muted : PowerUiDraw.Warn);
            PowerUiDraw.Tip(supportRect,
                CompatibilityRegistry.ControlTooltipKey(support).Translate());
        }

        private static void OpenGroupMenuForDetail(PowerManagementController management,
            Building building, int tick)
        {
            PowerManagementRegistry registry = management.Registry;
            var options = new List<FloatMenuOption>(registry.GroupCount + 1)
            {
                new FloatMenuOption("PGM_Mgmt_Group_None".Translate(), delegate
                {
                    management.SetGroup(building, "", tick);
                })
            };
            for (int i = 0; i < registry.GroupCount; i++)
            {
                string name = registry.Groups[i];
                options.Add(new FloatMenuOption(name, delegate
                {
                    management.SetGroup(building, name, tick);
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private struct DetailField
        {
            public readonly string LabelKey;
            public readonly string Value;
            public readonly string TooltipKey;
            public readonly Color? ValueColor;

            public DetailField(string labelKey, string value, string tooltipKey = null, Color? valueColor = null)
            {
                LabelKey = labelKey;
                Value = value;
                TooltipKey = tooltipKey;
                ValueColor = valueColor;
            }
        }

        /// <summary>
        /// Trader state as observed, not as configured. "Mixed" covers multi-component
        /// buildings where some components are powered and others are not.
        /// </summary>
        private static string TraderStateLabel(BuildingPowerSnapshot snapshot)
        {
            bool anyTrader = false;
            bool anyOn = false;
            bool anyOff = false;
            for (int i = 0; i < snapshot.Components.Count; i++)
            {
                PowerComponentSnapshot component = snapshot.Components[i];
                if (component.Behavior != PowerBehavior.Trader) continue;
                anyTrader = true;
                if (component.PowerOn == true) anyOn = true;
                else if (component.PowerOn == false) anyOff = true;
            }
            if (!anyTrader) return "PGM_Value_NotApplicable".Translate();
            if (anyOn && anyOff) return "PGM_TraderState_Mixed".Translate();
            return anyOn ? "PGM_TraderState_On".Translate() : "PGM_TraderState_Off".Translate();
        }

        private static BuildingPowerSnapshot FindBuildingSnapshot(PowerUiState state, Building building)
        {
            if (building == null) return null;
            PowerGridSnapshot snapshot = state.LastSnapshot;
            if (snapshot == null) return null;
            IReadOnlyList<BuildingPowerSnapshot> buildings = snapshot.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i].Building == building) return buildings[i];
            }
            return null;
        }

        private void OpenSortMenu(PowerUiState state)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("PGM_Sort_CurrentDraw".Translate(), () =>
                {
                    state.SortKey = BuildingSortKey.CurrentDraw;
                    state.SortDescending = true;
                }),
                new FloatMenuOption("PGM_Sort_Generation".Translate(), () =>
                {
                    state.SortKey = BuildingSortKey.Generation;
                    state.SortDescending = true;
                }),
                new FloatMenuOption("PGM_Sort_Name".Translate(), () =>
                {
                    state.SortKey = BuildingSortKey.Name;
                    state.SortDescending = false;
                }),
                new FloatMenuOption("PGM_Sort_Category".Translate(), () =>
                {
                    state.SortKey = BuildingSortKey.Category;
                    state.SortDescending = false;
                }),
                new FloatMenuOption("PGM_Sort_Status".Translate(), () =>
                {
                    state.SortKey = BuildingSortKey.Status;
                    state.SortDescending = true;
                }),
                new FloatMenuOption("PGM_Sort_ToggleDirection".Translate(), () =>
                {
                    state.SortDescending = !state.SortDescending;
                })
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenFilterMenu(PowerUiState state, PowerGridSnapshot snapshot, PowerGridManagerSettings settings)
        {
            BuildingFilterSettings filter = state.Filter;
            var options = new List<FloatMenuOption>
            {
                Toggle("PGM_Filter_Consumers".Translate(), filter.ShowConsumers, () =>
                {
                    filter.ShowConsumers = !filter.ShowConsumers;
                    filter.MarkChanged();
                }),
                Toggle("PGM_Filter_Generators".Translate(), filter.ShowGenerators, () =>
                {
                    filter.ShowGenerators = !filter.ShowGenerators;
                    filter.MarkChanged();
                }),
                Toggle("PGM_Filter_Batteries".Translate(), filter.ShowBatteries, () =>
                {
                    filter.ShowBatteries = !filter.ShowBatteries;
                    filter.MarkChanged();
                }),
                Toggle("PGM_Filter_Flickable".Translate(), filter.OnlyFlickable, () =>
                {
                    filter.OnlyFlickable = !filter.OnlyFlickable;
                    filter.MarkChanged();
                }),
                Toggle("PGM_Filter_SwitchedOff".Translate(), filter.OnlySwitchedOff, () =>
                {
                    filter.OnlySwitchedOff = !filter.OnlySwitchedOff;
                    filter.MarkChanged();
                }),
                Toggle("PGM_Filter_HighConsumption".Translate(settings.HighConsumptionWatts), filter.OnlyHighConsumption, () =>
                {
                    filter.OnlyHighConsumption = !filter.OnlyHighConsumption;
                    filter.MarkChanged();
                }),
                Toggle("PGM_Filter_DataNotices".Translate(), filter.OnlyWithDataNotices, () =>
                {
                    filter.OnlyWithDataNotices = !filter.OnlyWithDataNotices;
                    filter.MarkChanged();
                }),
                Toggle("PGM_Filter_UnknownBehavior".Translate(), filter.OnlyUnknownBehavior, () =>
                {
                    filter.OnlyUnknownBehavior = !filter.OnlyUnknownBehavior;
                    filter.MarkChanged();
                }),
                Toggle("PGM_Filter_Colonist".Translate(), filter.OnlyColonistBuildings, () =>
                {
                    filter.OnlyColonistBuildings = !filter.OnlyColonistBuildings;
                    filter.MarkChanged();
                }),
                Toggle("PGM_Filter_NoRoom".Translate(), filter.OnlyWithoutRoom, () =>
                {
                    filter.OnlyWithoutRoom = !filter.OnlyWithoutRoom;
                    filter.MarkChanged();
                }),
                new FloatMenuOption("PGM_Filter_Network".Translate(
                    filter.NetworkId.HasValue
                        ? "PGM_Network_Number".Translate(filter.NetworkId.Value)
                        : "PGM_Filter_AllNetworks".Translate()), () => OpenNetworkFilterMenu(state))
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenNetworkFilterMenu(PowerUiState state)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("PGM_Filter_AllNetworks".Translate(), () =>
                {
                    state.Filter.NetworkId = null;
                    state.Filter.MarkChanged();
                })
            };
            PowerGridSnapshot snapshot = state.LastSnapshot;
            if (snapshot != null)
            {
                for (int i = 0; i < snapshot.Networks.Count; i++)
                {
                    int id = snapshot.Networks[i].Id;
                    options.Add(new FloatMenuOption("PGM_Network_Number".Translate(id), () =>
                    {
                        state.Filter.NetworkId = id;
                        state.Filter.MarkChanged();
                    }));
                }
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenCategoryMenu(PowerUiState state)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("PGM_Buildings_AllCategories".Translate(), () =>
                {
                    state.Filter.Category = null;
                    state.Filter.MarkChanged();
                })
            };
            Array values = Enum.GetValues(typeof(PowerCategory));
            for (int i = 0; i < values.Length; i++)
            {
                PowerCategory category = (PowerCategory)values.GetValue(i);
                options.Add(new FloatMenuOption(PowerGridUtilities.CategoryLabelKey(category).Translate(), () =>
                {
                    state.Filter.Category = category;
                    state.Filter.MarkChanged();
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenGroupingMenu(PowerUiState state)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("PGM_Group_None".Translate(), () => state.Grouping = BuildingGrouping.None),
                new FloatMenuOption("PGM_Group_Category".Translate(), () => state.Grouping = BuildingGrouping.Category),
                new FloatMenuOption("PGM_Group_Room".Translate(), () => state.Grouping = BuildingGrouping.Room),
                new FloatMenuOption("PGM_Group_Network".Translate(), () => state.Grouping = BuildingGrouping.Network)
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static FloatMenuOption Toggle(string label, bool active, Action action)
        {
            return new FloatMenuOption(active ? "✔ " + label : label, action);
        }

        private static string SortKeyLabel(BuildingSortKey key)
        {
            switch (key)
            {
                case BuildingSortKey.Generation: return "PGM_Sort_Generation".Translate();
                case BuildingSortKey.Name: return "PGM_Sort_Name".Translate();
                case BuildingSortKey.Category: return "PGM_Sort_Category".Translate();
                case BuildingSortKey.Status: return "PGM_Sort_Status".Translate();
                default: return "PGM_Sort_CurrentDraw".Translate();
            }
        }

        private static string GroupingLabel(BuildingGrouping grouping)
        {
            switch (grouping)
            {
                case BuildingGrouping.Category: return "PGM_Group_Category".Translate();
                case BuildingGrouping.Room: return "PGM_Group_Room".Translate();
                case BuildingGrouping.Network: return "PGM_Group_Network".Translate();
                default: return "PGM_Group_None".Translate();
            }
        }
    }
}
