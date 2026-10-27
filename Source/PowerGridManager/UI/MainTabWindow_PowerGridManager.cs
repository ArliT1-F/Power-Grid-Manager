using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Native main-tab dashboard. The window only reads the cached snapshot produced by
    /// <see cref="PowerGridManagerComponent"/>; it never scans the map while drawing, and
    /// it does not stop history sampling when closed.
    /// </summary>
    public sealed class MainTabWindow_PowerGridManager : MainTabWindow
    {
        private const float TabStripHeight = 36f;

        private readonly PowerUiState state = new PowerUiState();
        private readonly PowerOverviewPanel overviewPanel = new PowerOverviewPanel();
        private readonly PowerNetworkPanel networkPanel = new PowerNetworkPanel();
        private readonly BuildingPowerPanel buildingPanel = new BuildingPowerPanel();
        private readonly PowerHistoryPanel historyPanel = new PowerHistoryPanel();
        private readonly PowerDiagnosticsPanel diagnosticsPanel = new PowerDiagnosticsPanel();
        private readonly PowerForecastPanel forecastPanel = new PowerForecastPanel();
        private readonly PowerManagementPanel managementPanel = new PowerManagementPanel();
        private readonly PowerRecommendationsPanel recommendationsPanel = new PowerRecommendationsPanel();
        private readonly PowerScenarioPanel scenarioPanel = new PowerScenarioPanel();
        private readonly PowerHeatmapPanel heatmapPanel = new PowerHeatmapPanel();
        private List<TabRecord> tabs;

        /// <summary>Window view state. Internal so the offline harness can drive each tab.</summary>
        internal PowerUiState UiState => state;

        public override Vector2 RequestedTabSize => new Vector2(1010f, 660f);

        public override void PostOpen()
        {
            base.PostOpen();
            // One refresh at open when the cache is missing or stale. Never per frame.
            Map map = Find.CurrentMap;
            if (map == null) return;
            PowerGridManagerComponent component = map.GetComponent<PowerGridManagerComponent>();
            if (component != null) component.RefreshIfStale(Find.TickManager.TicksGame, true);
        }

        public override void DoWindowContents(Rect inRect)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                PowerUiDraw.WrappedText(new Rect(inRect.x, inRect.y, inRect.width, 60f),
                    "PGM_Window_NoMap".Translate(), PowerUiDraw.Muted);
                return;
            }

            PowerGridManagerComponent component = map.GetComponent<PowerGridManagerComponent>();
            if (component == null)
            {
                PowerUiDraw.WrappedText(new Rect(inRect.x, inRect.y, inRect.width, 60f),
                    "PGM_Window_NoComponent".Translate(), PowerUiDraw.Muted);
                return;
            }

            EnsureTabs();
            // TabDrawer draws the strip 32px above the rect it is given; offset accordingly
            // so the tabs sit inside the window and content starts below them.
            TabDrawer.DrawTabs(new Rect(inRect.x, inRect.y + 32f, inRect.width, 32f), tabs);

            Rect content = new Rect(inRect.x, inRect.y + TabStripHeight, inRect.width,
                inRect.height - TabStripHeight);

            PowerGridManagerSettings settings = PowerGridManagerMod.Settings;
            PowerGridSnapshot snapshot = component.Snapshot;
            state.LastSnapshot = snapshot;
            if (snapshot == null)
            {
                PowerUiDraw.WrappedText(new Rect(content.x, content.y + 6f, content.width, 60f),
                    "PGM_Window_Waiting".Translate(), PowerUiDraw.Muted);
                return;
            }

            int now = Find.TickManager.TicksGame;
            switch (state.Tab)
            {
                case PowerGridTab.Networks:
                    networkPanel.Draw(content, snapshot, state, settings, ShowBuildingsOnNetwork);
                    break;
                case PowerGridTab.Buildings:
                    buildingPanel.Draw(content, snapshot, state, settings, component.Management, now);
                    break;
                case PowerGridTab.History:
                    historyPanel.Draw(content, component.History, state, settings, now);
                    break;
                case PowerGridTab.Diagnostics:
                    diagnosticsPanel.Draw(content, snapshot, state, settings, component.Diagnostics,
                        component.Advisories, ShowBuildingsOnNetwork);
                    break;
                case PowerGridTab.Forecast:
                    forecastPanel.Draw(content, snapshot, component.History, state, settings, now);
                    break;
                case PowerGridTab.Management:
                    managementPanel.Draw(content, snapshot, state, settings, component.Management, now);
                    break;
                case PowerGridTab.Optimization:
                    recommendationsPanel.Draw(content, snapshot, state, settings,
                        component.RecommendationTracker, component.Recommendations, ShowBuildingsOnNetwork);
                    break;
                case PowerGridTab.Planning:
                    scenarioPanel.Draw(content, snapshot, state, settings);
                    break;
                case PowerGridTab.Heatmap:
                    heatmapPanel.Draw(content, snapshot, state, settings, component.Management);
                    break;
                default:
                    overviewPanel.Draw(content, snapshot, now, settings, component.Forecast,
                        component.CriticalAdvisoryCount, component.WarningAdvisoryCount);
                    break;
            }
        }

        private void EnsureTabs()
        {
            if (tabs != null) return;
            tabs = new List<TabRecord>
            {
                Tab("PGM_Tab_Overview", PowerGridTab.Overview, "PGM_Tab_OverviewTip"),
                Tab("PGM_Tab_Networks", PowerGridTab.Networks, "PGM_Tab_NetworksTip"),
                Tab("PGM_Tab_Buildings", PowerGridTab.Buildings, "PGM_Tab_BuildingsTip"),
                Tab("PGM_Tab_History", PowerGridTab.History, "PGM_Tab_HistoryTip"),
                Tab("PGM_Tab_Diagnostics", PowerGridTab.Diagnostics, "PGM_Tab_DiagnosticsTip"),
                Tab("PGM_Tab_Forecast", PowerGridTab.Forecast, "PGM_Tab_ForecastTip"),
                Tab("PGM_Tab_Management", PowerGridTab.Management, "PGM_Tab_ManagementTip"),
                Tab("PGM_Tab_Optimization", PowerGridTab.Optimization, "PGM_Tab_OptimizationTip"),
                Tab("PGM_Tab_Planning", PowerGridTab.Planning, "PGM_Tab_PlanningTip"),
                Tab("PGM_Tab_Heatmap", PowerGridTab.Heatmap, "PGM_Tab_HeatmapTip")
            };
        }

        private TabRecord Tab(string labelKey, PowerGridTab tab, string tooltipKey)
        {
            return new PowerTabRecord(labelKey.Translate(), () => state.Tab = tab, () => state.Tab == tab,
                tooltipKey.Translate());
        }

        /// <summary>Tab record with a tooltip, which vanilla's <see cref="TabRecord"/> does not carry.</summary>
        private sealed class PowerTabRecord : TabRecord
        {
            private readonly string tip;

            public PowerTabRecord(string label, System.Action clickedAction, System.Func<bool> selected,
                string tip) : base(label, clickedAction, selected)
            {
                this.tip = tip;
            }

            public override string GetTip()
            {
                return tip;
            }
        }

        /// <summary>Called by the network panel to jump to the filtered building table.</summary>
        private void ShowBuildingsOnNetwork(int networkId)
        {
            state.Filter.NetworkId = networkId;
            state.Filter.MarkChanged();
            state.Tab = PowerGridTab.Buildings;
        }
    }
}
