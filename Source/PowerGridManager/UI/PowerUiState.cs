using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>Tabs of the Power Grid Manager window.</summary>
    public enum PowerGridTab
    {
        Overview = 0,
        Networks,
        Buildings,
        History,
        Diagnostics,
        Forecast,
        Management,
        Optimization,
        Planning,
        Heatmap,
        Settings
    }

    /// <summary>Which advisories the diagnostics tab shows.</summary>
    public enum DiagnosticFilter
    {
        All = 0,
        CriticalOnly,
        WarningAndAbove,
        ResolvedOnly
    }

    public enum BuildingSortKey
    {
        CurrentDraw = 0,
        Generation,
        Name,
        Category,
        Status
    }

    public enum BuildingGrouping
    {
        None = 0,
        Category,
        Room,
        Network
    }

    /// <summary>
    /// User-driven filter state for the building table. Not persisted: it is view
    /// state only and is rebuilt on demand rather than every frame.
    /// </summary>
    public sealed class BuildingFilterSettings
    {
        public string SearchText = "";
        public int? NetworkId;
        public PowerCategory? Category;
        public bool ShowConsumers = true;
        public bool ShowGenerators = true;
        public bool ShowBatteries = true;
        public bool OnlyFlickable;
        public bool OnlySwitchedOff;
        public bool OnlyHighConsumption;
        public bool OnlyWithDataNotices;
        public bool OnlyUnknownBehavior;
        public bool OnlyColonistBuildings;
        public bool OnlyWithoutRoom;

        /// <summary>Incremented by the UI whenever any field changes.</summary>
        public int Revision = 1;

        public void MarkChanged()
        {
            Revision++;
        }

        public bool HasNonDefaultFilters
        {
            get
            {
                if (NetworkId.HasValue || Category.HasValue) return true;
                if (!ShowConsumers || !ShowGenerators || !ShowBatteries) return true;
                return OnlyFlickable || OnlySwitchedOff || OnlyHighConsumption || OnlyWithDataNotices ||
                       OnlyUnknownBehavior || OnlyColonistBuildings || OnlyWithoutRoom;
            }
        }

        public void Reset()
        {
            SearchText = "";
            NetworkId = null;
            Category = null;
            ShowConsumers = true;
            ShowGenerators = true;
            ShowBatteries = true;
            OnlyFlickable = false;
            OnlySwitchedOff = false;
            OnlyHighConsumption = false;
            OnlyWithDataNotices = false;
            OnlyUnknownBehavior = false;
            OnlyColonistBuildings = false;
            OnlyWithoutRoom = false;
            MarkChanged();
        }
    }

    /// <summary>Transient window state (selection, filters, scroll positions).</summary>
    public sealed class PowerUiState
    {
        public PowerGridTab Tab = PowerGridTab.Overview;
        public PowerTimeRange Range = PowerTimeRange.LastDay;

        /// <summary>Range used by the forecast tab; the history tab keeps its own.</summary>
        public PowerTimeRange ForecastRange = PowerTimeRange.LastDay;
        public DiagnosticFilter DiagnosticView = DiagnosticFilter.All;
        public RecommendationFilter RecommendationView = RecommendationFilter.All;
        public BuildingSortKey SortKey = BuildingSortKey.CurrentDraw;
        public bool SortDescending = true;
        public BuildingGrouping Grouping = BuildingGrouping.None;
        public BuildingFilterSettings Filter = new BuildingFilterSettings();

        public PowerGridSnapshot LastSnapshot;
        public int? SelectedNetworkId;
        public Building SelectedBuilding;

        public Vector2 OverviewScroll;
        public Vector2 OverviewConsumersScroll;
        public Vector2 OverviewAlertsScroll;
        public bool OverviewConsumersAscending;

        public Vector2 NetworkScroll;
        public Vector2 NetworkConsumersScroll;
        public Vector2 BuildingScroll;
        public Vector2 DetailScroll;
        public Vector2 DiagnosticScroll;
        public Vector2 RecommendationScroll;
        public Vector2 ForecastScroll;
        public Vector2 ManagementScroll;
        public Vector2 ScenarioScroll;
        public Vector2 HeatmapScroll;
        public Vector2 SettingsScroll;

        // Management tab view state. Never persisted: it is transient UI state only.
        public string ManagementSelectedId;
        public string ManagementGroupFilter = "";
        public string ManagementNewGroupName = "";
        public int ManagementPage;

        /// <summary>Drops a selection whose building no longer exists or was despawned.</summary>
        public void ValidateSelection()
        {
            Building building = SelectedBuilding;
            if (building == null) return;
            if (building.Destroyed || !building.Spawned) SelectedBuilding = null;
        }

        public void SelectBuilding(Building building)
        {
            SelectedBuilding = building;
        }

        public void SelectNetwork(int? networkId)
        {
            SelectedNetworkId = networkId;
        }
    }
}
