using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Searchable, sortable, grouped dark-theme table of the map's power buildings, paired
    /// with the role-specialized <see cref="PowerBuildingInspector"/> (Generator, Battery,
    /// or Consumer inspection view) for the selected building.
    /// </summary>
    public sealed class BuildingPowerPanel
    {
        private const float ToolbarHeight = 36f;
        private const float RowHeight = 25f;
        private const float GroupHeaderHeight = 22f;
        private const float DetailBaseHeight = 166f;
        private const float ColumnGap = 6f;

        private readonly BuildingListModel model = new BuildingListModel();
        private readonly float[] cachedColumnWidths = new float[6];

        public BuildingListModel Model => model;

        public void Draw(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings, PowerManagementController management = null, int tick = 0)
        {
            if (snapshot == null || state == null || settings == null) return;
            if (rect.width <= 4f || rect.height <= 4f) return;

            state.ValidateSelection();

            float y = rect.y;
            float toolbarH = rect.height < 140f ? 26f : ToolbarHeight;
            Rect toolbar = new Rect(rect.x, y, rect.width, toolbarH);
            y += toolbarH + PowerUiDraw.SpaceRelated;

            bool hasSelection = state.SelectedBuilding != null;
            bool showManagement = hasSelection && management != null && settings.ManagementEnabled;
            float desiredDetail = DetailBaseHeight + (showManagement ? 34f : 0f);
            float maxDetail = Mathf.Max(0f, (rect.yMax - y) * 0.48f);
            float detailSpace = hasSelection ? Mathf.Min(desiredDetail, Mathf.Max(84f, maxDetail)) + PowerUiDraw.SpaceRelated : 0f;

            float tableHeight = Mathf.Max(48f, rect.yMax - y - detailSpace);
            Rect tableRect = new Rect(rect.x, y, rect.width, tableHeight);

            model.Update(snapshot, state.Filter, state.SortKey, state.SortDescending, state.Grouping,
                settings.HighConsumptionWatts);
            DrawToolbar(toolbar, snapshot, state, settings);
            DrawTable(tableRect, snapshot, state, settings);

            if (hasSelection && rect.yMax - tableRect.yMax > 12f)
            {
                Rect detail = new Rect(rect.x, tableRect.yMax + PowerUiDraw.SpaceRelated, rect.width,
                    Mathf.Max(0f, rect.yMax - tableRect.yMax - PowerUiDraw.SpaceRelated));
                BuildingPowerSnapshot selectedSnap = FindBuildingSnapshot(snapshot, state.SelectedBuilding);
                PowerBuildingInspector.Draw(detail, snapshot, state.SelectedBuilding, selectedSnap,
                    state, settings, management, tick);
            }
        }

        private void DrawToolbar(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings)
        {
            PowerUiDraw.Panel(rect);
            Rect inner = new Rect(rect.x + 6f, rect.y + (rect.height - 26f) * 0.5f,
                Mathf.Max(20f, rect.width - 12f), Mathf.Min(26f, rect.height - 2f));

            bool compact = inner.width < 760f;
            float x = inner.x;
            float searchWidth = Mathf.Clamp(inner.width * 0.25f, 90f, 230f);
            Rect searchRect = new Rect(x, inner.y, searchWidth, inner.height);
            Widgets.DrawBoxSolid(searchRect, PowerUiDraw.BgRecessed);
            string updated = Widgets.TextField(searchRect, state.Filter.SearchText ?? string.Empty);
            PowerUiDraw.DrawBorder(searchRect, PowerUiDraw.BorderPanel, 1);
            if (updated != state.Filter.SearchText)
            {
                state.Filter.SearchText = updated;
                state.Filter.MarkChanged();
            }
            PowerUiDraw.Tip(searchRect, "PGM_Buildings_SearchTip".Translate());
            x += searchWidth + ColumnGap;

            // Button 0: Sort.
            float sortW = Mathf.Min(152f, Mathf.Max(68f, (inner.xMax - x) * 0.30f));
            Rect sortRect = new Rect(x, inner.y, sortW, inner.height);
            string dirArrow = state.SortDescending ? "\u25BC" : "\u25B2";
            if (PowerUiDraw.Button(sortRect,
                "PGM_Buildings_SortButton".Translate(SortKeyLabel(state.SortKey), dirArrow)))
            {
                OpenSortMenu(state);
            }
            x += sortRect.width + ColumnGap;

            // Button 1: Filters.
            float filterW = Mathf.Min(96f, Mathf.Max(58f, (inner.xMax - x) * 0.26f));
            Rect filterRect = new Rect(x, inner.y, filterW, inner.height);
            if (PowerUiDraw.Button(filterRect, "PGM_Buildings_FiltersButton".Translate(),
                state.Filter.HasNonDefaultFilters))
            {
                OpenFilterMenu(state, settings);
            }
            if (state.Filter.HasNonDefaultFilters)
            {
                Widgets.DrawBoxSolid(new Rect(filterRect.xMax - 8f, filterRect.y + 4f, 5f, 5f), PowerUiDraw.Warn);
            }
            x += filterRect.width + ColumnGap;

            // Button 2 (+ Button 3 on wide screens): Category & Grouping, or compact View menu.
            if (compact)
            {
                float viewW = Mathf.Min(108f, Mathf.Max(54f, inner.xMax - x));
                Rect viewRect = new Rect(x, inner.y, viewW, inner.height);
                if (PowerUiDraw.Button(viewRect,
                    "PGM_Buildings_ViewButton".Translate(GroupingLabel(state.Grouping))))
                {
                    OpenViewMenu(state);
                }
                x += viewRect.width + ColumnGap;
            }
            else
            {
                Rect categoryRect = new Rect(x, inner.y, 132f, inner.height);
                string categoryLabel = state.Filter.Category.HasValue
                    ? (string)PowerGridUtilities.CategoryLabelKey(state.Filter.Category.Value).Translate()
                    : (string)"PGM_Buildings_AllCategories".Translate();
                if (PowerUiDraw.Button(categoryRect, categoryLabel, state.Filter.Category.HasValue))
                {
                    OpenCategoryMenu(state);
                }
                x += categoryRect.width + ColumnGap;

                Rect groupRect = new Rect(x, inner.y, 118f, inner.height);
                if (PowerUiDraw.Button(groupRect,
                    "PGM_Buildings_GroupButton".Translate(GroupingLabel(state.Grouping)),
                    state.Grouping != BuildingGrouping.None))
                {
                    OpenGroupingMenu(state);
                }
                x += groupRect.width + ColumnGap;
            }

            if (state.Filter.HasNonDefaultFilters && inner.xMax - x >= 68f)
            {
                Rect resetRect = new Rect(x, inner.y, 72f, inner.height);
                if (PowerUiDraw.Button(resetRect, "PGM_Buildings_Reset".Translate(), false, true, true, PowerUiDraw.Warn))
                {
                    state.Filter.Reset();
                }
                x += resetRect.width + ColumnGap;
            }

            Rect countRect = new Rect(x, inner.y, Mathf.Max(0f, inner.xMax - x - 4f), inner.height);
            if (countRect.width > 72f)
            {
                PowerUiDraw.TextEllipsized(countRect,
                    "PGM_Buildings_Count".Translate(model.Count, snapshot.Buildings.Count),
                    PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);
            }
        }

        private void OpenViewMenu(PowerUiState state)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(state.Filter.Category.HasValue
                        ? (string)"PGM_Buildings_CategorySelected".Translate(
                            PowerGridUtilities.CategoryLabelKey(state.Filter.Category.Value).Translate())
                        : (string)"PGM_Buildings_AllCategories".Translate(),
                    () => OpenCategoryMenu(state)),
                new FloatMenuOption("PGM_Group_None".Translate(), () => state.Grouping = BuildingGrouping.None),
                new FloatMenuOption("PGM_Group_Category".Translate(), () => state.Grouping = BuildingGrouping.Category),
                new FloatMenuOption("PGM_Group_Room".Translate(), () => state.Grouping = BuildingGrouping.Room),
                new FloatMenuOption("PGM_Group_Network".Translate(), () => state.Grouping = BuildingGrouping.Network),
                new FloatMenuOption("PGM_Buildings_Reset".Translate(), () => state.Filter.Reset())
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawTable(Rect rect, PowerGridSnapshot snapshot, PowerUiState state,
            PowerGridManagerSettings settings)
        {
            PowerUiDraw.Panel(rect);
            if (rect.height < 28f || rect.width < 40f) return;

            Rect headerBand = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, 24f);
            Widgets.DrawBoxSolid(headerBand, PowerUiDraw.BgElevated);
            Widgets.DrawLineHorizontal(headerBand.x, headerBand.yMax, headerBand.width, PowerUiDraw.BorderPanel);

            Rect headerInner = new Rect(rect.x + 8f, rect.y + 2f, Mathf.Max(20f, rect.width - 26f), 22f);
            FillColumnWidths(headerInner.width, cachedColumnWidths);
            DrawHeader(headerInner);

            Rect outer = new Rect(rect.x + 2f, headerBand.yMax + 2f,
                Mathf.Max(10f, rect.width - 4f), Mathf.Max(10f, rect.yMax - headerBand.yMax - 4f));
            if (model.Count == 0)
            {
                PowerUiDraw.WrappedText(new Rect(outer.x + 12f, outer.y + 10f,
                        Mathf.Max(0f, outer.width - 24f), Mathf.Max(20f, outer.height - 20f)),
                    snapshot.Buildings.Count == 0
                        ? "PGM_Buildings_NoPowerBuildings".Translate()
                        : "PGM_Buildings_NoMatches".Translate(),
                    PowerUiDraw.TextSecondary);
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

            float viewW = Mathf.Max(40f, outer.width - 18f);
            FillColumnWidths(Mathf.Max(20f, viewW - 8f), cachedColumnWidths);

            Rect view = new Rect(0f, 0f, viewW, Mathf.Max(contentHeight + 4f, outer.height));
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
                        Widgets.DrawBoxSolid(groupRect, PowerUiDraw.BgHeader);
                        Widgets.DrawBoxSolid(new Rect(0f, y + 4f, 3f, GroupHeaderHeight - 8f), PowerUiDraw.AccentPrimary);
                        PowerUiDraw.TextEllipsized(
                            new Rect(groupRect.x + 9f, groupRect.y, groupRect.width - 14f, groupRect.height),
                            model.GroupLabelAt(i), PowerUiDraw.AccentCyan);
                        Widgets.DrawLineHorizontal(0f, groupRect.yMax - 1f, view.width, PowerUiDraw.BorderSubtle);
                        y += GroupHeaderHeight;
                    }

                    Rect row = new Rect(0f, y, view.width, RowHeight);
                    bool selected = state.SelectedBuilding != null && state.SelectedBuilding == building.Building;
                    PowerUiDraw.RowBackground(row, i, selected);
                    DrawRow(new Rect(row.x + 6f, row.y, Mathf.Max(20f, row.width - 8f), row.height),
                        building, settings, cachedColumnWidths);
                    if (Widgets.ButtonInvisible(row))
                        state.SelectBuilding(building.Building);
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
            float[] w = cachedColumnWidths;
            float x = rect.x;
            PowerUiDraw.TextEllipsized(new Rect(x, rect.y, w[0], rect.height),
                "PGM_Column_Building".Translate(), PowerUiDraw.TextSecondary);
            x += w[0];
            if (w[1] > 0f)
            {
                PowerUiDraw.TextEllipsized(new Rect(x, rect.y, w[1], rect.height),
                    "PGM_Column_Category".Translate(), PowerUiDraw.TextSecondary);
                x += w[1];
            }
            PowerUiDraw.TextEllipsized(new Rect(x, rect.y, w[2], rect.height),
                "PGM_Column_Draw".Translate(), PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);
            x += w[2];
            if (w[3] > 0f)
            {
                PowerUiDraw.TextEllipsized(new Rect(x, rect.y, w[3], rect.height),
                    "PGM_Column_Generation".Translate(), PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);
                x += w[3];
            }
            if (w[4] > 0f)
            {
                PowerUiDraw.TextEllipsized(new Rect(x, rect.y, w[4], rect.height),
                    "PGM_Column_Network".Translate(), PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);
                x += w[4];
            }
            PowerUiDraw.TextEllipsized(new Rect(x, rect.y, w[5], rect.height),
                "PGM_Column_Status".Translate(), PowerUiDraw.TextSecondary, TextAnchor.MiddleRight);
        }

        private static void FillColumnWidths(float width, float[] widths)
        {
            if (width < 460f)
            {
                widths[0] = width * 0.44f;
                widths[1] = 0f;
                widths[2] = width * 0.24f;
                widths[3] = 0f;
                widths[4] = 0f;
                widths[5] = width * 0.32f;
                return;
            }

            if (width < 680f)
            {
                widths[0] = width * 0.34f;
                widths[1] = 0f;
                widths[2] = width * 0.18f;
                widths[3] = width * 0.18f;
                widths[4] = width * 0.10f;
                widths[5] = width * 0.20f;
                return;
            }

            const float draw = 94f;
            const float generation = 94f;
            const float network = 62f;
            const float status = 132f;
            const float category = 128f;
            float fixedTotal = draw + generation + network + status + category;
            widths[0] = Mathf.Max(130f, width - fixedTotal);
            widths[1] = category;
            widths[2] = draw;
            widths[3] = generation;
            widths[4] = network;
            widths[5] = status;
        }

        private static void DrawRow(Rect rect, BuildingPowerSnapshot building,
            PowerGridManagerSettings settings, float[] widths)
        {
            float x = rect.x;
            string label = PowerGridUtilities.BuildingLabel(building.Building);
            bool active = (building.IsConsumer && building.HasAnyDraw) ||
                          (building.IsGenerator && building.GenerationWatts > 0.01f) ||
                          building.IsBattery;

            float flickReserve = building.IsFlickable && widths[0] >= 100f ? 42f : 0f;
            Rect nameRect = new Rect(x, rect.y, Mathf.Max(16f, widths[0] - flickReserve - 4f), rect.height);
            PowerUiDraw.TextEllipsized(nameRect, label,
                active ? PowerUiDraw.TextPrimary : PowerUiDraw.TextSecondary);

            if (flickReserve > 0f)
            {
                bool on = building.SwitchIsOn == true;
                PowerUiDraw.TextEllipsized(
                    new Rect(x + widths[0] - flickReserve, rect.y, flickReserve - 4f, rect.height),
                    on ? "PGM_Flick_On".Translate() : "PGM_Flick_Off".Translate(),
                    on ? PowerUiDraw.TextSecondary : PowerUiDraw.Warn,
                    TextAnchor.MiddleRight);
            }
            x += widths[0];

            if (widths[1] > 0f)
            {
                PowerUiDraw.TextEllipsized(new Rect(x, rect.y, widths[1] - 6f, rect.height),
                    PowerGridUtilities.CategoryLabelKey(building.Category).Translate(),
                    PowerUiDraw.TextSecondary);
                x += widths[1];
            }

            bool highDraw = PowerGridUtilities.IsHighConsumption(building, settings.HighConsumptionWatts);
            Color drawCol = highDraw
                ? PowerUiDraw.Warn
                : (building.ConsumptionWatts > 0.01f ? PowerUiDraw.TextPrimary : PowerUiDraw.TextSecondary);
            PowerUiDraw.TextEllipsized(new Rect(x, rect.y, widths[2] - 6f, rect.height),
                building.ConsumptionWatts > 0.01f ? PowerFormatting.FormatWatts(building.ConsumptionWatts) : "\u2014",
                drawCol, TextAnchor.MiddleRight);
            x += widths[2];

            if (widths[3] > 0f)
            {
                PowerUiDraw.TextEllipsized(new Rect(x, rect.y, widths[3] - 6f, rect.height),
                    building.GenerationWatts > 0.01f ? PowerFormatting.FormatWatts(building.GenerationWatts) : "\u2014",
                    building.GenerationWatts > 0.01f ? PowerUiDraw.AccentCyan : PowerUiDraw.TextSecondary,
                    TextAnchor.MiddleRight);
                x += widths[3];
            }

            if (widths[4] > 0f)
            {
                PowerUiDraw.TextEllipsized(new Rect(x, rect.y, widths[4] - 6f, rect.height),
                    building.NetworkId.HasValue ? "#" + building.NetworkId.Value : "\u2014",
                    building.NetworkId.HasValue ? PowerUiDraw.TextSecondary : PowerUiDraw.Warn,
                    TextAnchor.MiddleRight);
                x += widths[4];
            }

            string statusKey;
            Color statusColor;
            string statusSym;
            GetStatus(building, settings, out statusKey, out statusColor, out statusSym);
            string stText = statusSym + " " + statusKey.Translate();
            PowerUiDraw.TextEllipsized(new Rect(x, rect.y, widths[5] - 4f, rect.height),
                stText, statusColor, TextAnchor.MiddleRight);
        }

        private static void GetStatus(BuildingPowerSnapshot building, PowerGridManagerSettings settings,
            out string statusKey, out Color statusColor, out string symbol)
        {
            if (building.HasUnsupportedComponent)
            {
                statusKey = "PGM_RowStatus_Unsupported";
                statusColor = PowerUiDraw.StatusColorForUnknown();
                symbol = "?";
                return;
            }
            if (building.HasMultipleNetworks)
            {
                statusKey = "PGM_RowStatus_MultipleNetworks";
                statusColor = PowerUiDraw.Warn;
                symbol = "\u25B2";
                return;
            }
            if (building.Connection != PowerConnectionState.Connected)
            {
                statusKey = PowerGridUtilities.ConnectionLabelKey(building.Connection);
                statusColor = building.Connection == PowerConnectionState.NoNetwork
                    ? PowerUiDraw.NoticeColor
                    : PowerUiDraw.Warn;
                symbol = building.Connection == PowerConnectionState.NoNetwork ? "!" : "?";
                return;
            }
            if (building.IsFlickable && building.SwitchIsOn == false)
            {
                statusKey = "PGM_RowStatus_SwitchedOff";
                statusColor = PowerUiDraw.Warn;
                symbol = "\u2013";
                return;
            }
            if (building.HasOffComponents)
            {
                statusKey = "PGM_RowStatus_PoweredDown";
                statusColor = PowerUiDraw.TextSecondary;
                symbol = "\u2013";
                return;
            }
            if (PowerGridUtilities.IsHighConsumption(building, settings.HighConsumptionWatts))
            {
                statusKey = "PGM_RowStatus_HighConsumption";
                statusColor = PowerUiDraw.Warn;
                symbol = "\u25B2";
                return;
            }
            statusKey = "PGM_RowStatus_Ok";
            statusColor = PowerUiDraw.Good;
            symbol = "\u25CF";
        }

        private static BuildingPowerSnapshot FindBuildingSnapshot(PowerGridSnapshot snapshot, Building building)
        {
            if (building == null || snapshot == null) return null;
            IReadOnlyList<BuildingPowerSnapshot> buildings = snapshot.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i].Building == building) return buildings[i];
            }
            return null;
        }

        private static void OpenSortMenu(PowerUiState state)
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

        private static void OpenFilterMenu(PowerUiState state, PowerGridManagerSettings settings)
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
                        ? (string)"PGM_Network_Number".Translate(filter.NetworkId.Value)
                        : (string)"PGM_Filter_AllNetworks".Translate()),
                    () => OpenNetworkFilterMenu(state))
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void OpenNetworkFilterMenu(PowerUiState state)
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

        private static void OpenCategoryMenu(PowerUiState state)
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

        private static void OpenGroupingMenu(PowerUiState state)
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
            return new FloatMenuOption(active ? "\u2714 " + label : label, action);
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
