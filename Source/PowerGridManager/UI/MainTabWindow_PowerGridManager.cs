using RimWorld;
using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Native main-tab dashboard redesigned as a dark electrical-grid monitoring application.
    ///
    /// Reads only cached snapshots produced by <see cref="PowerGridManagerComponent"/>;
    /// never scans the map while rendering.
    /// </summary>
    public sealed class MainTabWindow_PowerGridManager : MainTabWindow
    {
        private struct NavEntry
        {
            public readonly PowerGridTab Tab;
            public readonly string LabelKey;
            public readonly string TipKey;

            public NavEntry(PowerGridTab tab, string labelKey, string tipKey)
            {
                Tab = tab;
                LabelKey = labelKey;
                TipKey = tipKey;
            }
        }

        private static readonly NavEntry[] NavEntries =
        {
            new NavEntry(PowerGridTab.Overview, "PGM_Tab_Overview", "PGM_Tab_OverviewTip"),
            new NavEntry(PowerGridTab.Networks, "PGM_Tab_Networks", "PGM_Tab_NetworksTip"),
            new NavEntry(PowerGridTab.Buildings, "PGM_Tab_Buildings", "PGM_Tab_BuildingsTip"),
            new NavEntry(PowerGridTab.Diagnostics, "PGM_Tab_Diagnostics", "PGM_Tab_DiagnosticsTip"),
            new NavEntry(PowerGridTab.History, "PGM_Tab_History", "PGM_Tab_HistoryTip"),
            new NavEntry(PowerGridTab.Forecast, "PGM_Tab_Forecast", "PGM_Tab_ForecastTip"),
            new NavEntry(PowerGridTab.Management, "PGM_Tab_Management", "PGM_Tab_ManagementTip"),
            new NavEntry(PowerGridTab.Optimization, "PGM_Tab_Optimization", "PGM_Tab_OptimizationTip"),
            new NavEntry(PowerGridTab.Planning, "PGM_Tab_Planning", "PGM_Tab_PlanningTip"),
            new NavEntry(PowerGridTab.Heatmap, "PGM_Tab_Heatmap", "PGM_Tab_HeatmapTip"),
            new NavEntry(PowerGridTab.Settings, "PGM_Tab_Settings", "PGM_Tab_SettingsTip")
        };

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

        /// <summary>Window view state. Internal so the offline harness can drive each tab.</summary>
        internal PowerUiState UiState => state;

        public override Vector2 RequestedTabSize => new Vector2(1120f, 720f);
        protected override float Margin => 0f;

        public MainTabWindow_PowerGridManager()
        {
            doCloseX = false;
            doCloseButton = false;
            doWindowBackground = false;
            forcePause = false;
        }

        public override void PostOpen()
        {
            base.PostOpen();
            Map map = Find.CurrentMap;
            if (map == null) return;
            PowerGridManagerComponent component = map.GetComponent<PowerGridManagerComponent>();
            if (component != null) component.RefreshIfStale(Find.TickManager.TicksGame, true);
        }

        public override void DoWindowContents(Rect inRect)
        {
            PowerUiDraw.WindowChrome(inRect);
            if (inRect.width <= 12f || inRect.height <= 12f) return;

            bool ultraCompact = inRect.height < 260f || inRect.width < 420f;
            float outerPad = ultraCompact ? 4f : 12f;
            Rect shell = inRect.ContractedBy(outerPad);

            Map map = Find.CurrentMap;
            PowerGridManagerComponent component = map != null ? map.GetComponent<PowerGridManagerComponent>() : null;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            PowerGridManagerSettings settings = PowerGridManagerMod.Settings ?? new PowerGridManagerSettings();
            PowerGridSnapshot snapshot = component != null ? component.Snapshot : null;
            state.LastSnapshot = snapshot;

            int critCount = component != null ? component.CriticalAdvisoryCount : 0;
            int warnCount = component != null ? component.WarningAdvisoryCount : 0;

            float headerH = ultraCompact ? 24f : 42f;
            DrawApplicationHeader(new Rect(shell.x, shell.y, shell.width, headerH),
                snapshot, settings, now, critCount, warnCount, ultraCompact);

            float navY = shell.y + headerH + (ultraCompact ? 3f : 6f);
            float navH = DrawNavigationBar(shell.x, navY, shell.width, critCount, warnCount, ultraCompact);

            float contentY = navY + navH + (ultraCompact ? 4f : PowerUiDraw.SectionGap);
            Rect content = new Rect(shell.x, contentY, shell.width, Mathf.Max(0f, shell.yMax - contentY));
            if (content.height <= 4f) return;

            if (state.Tab == PowerGridTab.Settings)
            {
                PowerGridManagerMod.DrawEmbeddedSettings(content, settings, ref state.SettingsScroll);
                return;
            }

            if (map == null)
            {
                DrawStateNotice(content, "PGM_Window_NoMap".Translate());
                return;
            }

            if (component == null)
            {
                DrawStateNotice(content, "PGM_Window_NoComponent".Translate());
                return;
            }

            if (snapshot == null)
            {
                DrawStateNotice(content, "PGM_Window_Waiting".Translate());
                return;
            }

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
                    overviewPanel.Draw(
                        content,
                        snapshot,
                        now,
                        settings,
                        component.Forecast,
                        critCount,
                        warnCount,
                        component.History,
                        component.Advisories,
                        component.Recommendations,
                        state,
                        InspectBuilding,
                        ShowNetwork,
                        OpenDiagnosticsTab);
                    break;
            }
        }

        private void DrawApplicationHeader(
            Rect rect,
            PowerGridSnapshot snapshot,
            PowerGridManagerSettings settings,
            int now,
            int critCount,
            int warnCount,
            bool compact)
        {
            Widgets.DrawBoxSolid(rect, PowerUiDraw.BgHeader);
            PowerUiDraw.DrawBorder(rect, PowerUiDraw.BorderPanel, 1);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), PowerUiDraw.AccentCyan);

            float closeSize = compact ? 20f : 26f;
            Rect closeRect = new Rect(rect.xMax - closeSize - 6f,
                rect.y + (rect.height - closeSize) * 0.5f, closeSize, closeSize);
            DrawCloseControl(closeRect);

            float badgeMaxW = compact ? Mathf.Min(110f, rect.width * 0.34f) : 188f;
            float badgeH = compact ? rect.height - 4f : 26f;
            Rect badgeRect = new Rect(closeRect.x - badgeMaxW - 8f,
                rect.y + (rect.height - badgeH) * 0.5f, badgeMaxW, badgeH);

            if (rect.width >= 260f)
            {
                int highLoad = 0;
                if (snapshot != null)
                {
                    for (int i = 0; i < snapshot.Networks.Count; i++)
                    {
                        if (PowerGridUtilities.EvaluateStatus(snapshot.Networks[i], settings.HighLoadThreshold) == NetworkStatus.HighLoad)
                            highLoad++;
                    }
                }

                NetworkStatus status = PowerOverviewPanel.EvaluateColonyStatus(
                    snapshot, settings, critCount, warnCount, highLoad);
                string statusText = snapshot == null
                    ? (string)"PGM_Status_Unknown".Translate()
                    : (snapshot.Networks.Count == 0 && snapshot.Buildings.Count == 0
                        ? (string)"PGM_Overview_StatusNoGrid".Translate()
                        : (string)PowerGridUtilities.StatusLabelKey(status).Translate());
                PowerUiDraw.StatusBadge(badgeRect, statusText,
                    PowerUiDraw.StatusColor(status), PowerUiDraw.StatusSymbol(status));
            }
            else
            {
                badgeRect = new Rect(closeRect.x, rect.y, 0f, 0f);
            }

            float leftW = Mathf.Max(20f, badgeRect.x - rect.x - 16f);
            if (compact || rect.height < 36f)
            {
                PowerUiDraw.Label(new Rect(rect.x + 10f, rect.y, leftW, rect.height),
                    "PGM_Header_Title".Translate(), GameFont.Small, PowerUiDraw.TextPrimary, TextAnchor.MiddleLeft);
            }
            else
            {
                PowerUiDraw.Label(new Rect(rect.x + 12f, rect.y + 3f, leftW, 22f),
                    "PGM_Header_Title".Translate(), GameFont.Medium, PowerUiDraw.TextPrimary, TextAnchor.MiddleLeft);

                string sub = "PGM_Header_Subtitle".Translate();
                if (snapshot != null)
                {
                    int ageTicks = now >= snapshot.Tick ? now - snapshot.Tick : 0;
                    sub = sub + "  \u00B7  " + "PGM_Overview_Age".Translate(PowerFormatting.FormatDuration(ageTicks));
                }
                PowerUiDraw.Label(new Rect(rect.x + 12f, rect.y + 23f, leftW, 16f),
                    sub, GameFont.Small, PowerUiDraw.TextSecondary, TextAnchor.MiddleLeft);
            }
        }

        private void DrawCloseControl(Rect rect)
        {
            bool hovered = Mouse.IsOver(rect);
            Widgets.DrawBoxSolid(rect, hovered
                ? new Color(PowerUiDraw.NoticeColor.r, PowerUiDraw.NoticeColor.g, PowerUiDraw.NoticeColor.b, 0.25f)
                : PowerUiDraw.BgElevated);
            PowerUiDraw.DrawBorder(rect, hovered ? PowerUiDraw.NoticeColor : PowerUiDraw.BorderPanel, 1);
            PowerUiDraw.Label(rect, "\u00D7", GameFont.Small,
                hovered ? PowerUiDraw.NoticeColor : PowerUiDraw.TextSecondary, TextAnchor.MiddleCenter);
            PowerUiDraw.Tip(rect, "PGM_Header_CloseTip".Translate());
            if (Widgets.ButtonInvisible(rect))
                Close();
        }

        private float DrawNavigationBar(float x, float y, float width,
            int critCount, int warnCount, bool ultraCompact)
        {
            int total = NavEntries.Length;
            bool twoRows = !ultraCompact && width < 920f && width >= 420f;
            float rowH = ultraCompact ? 20f : 28f;

            if (!twoRows)
            {
                Rect strip = new Rect(x, y, width, rowH);
                Widgets.DrawBoxSolid(strip, PowerUiDraw.BgSecondary);
                PowerUiDraw.DrawBorder(strip, PowerUiDraw.BorderPanel, 1);

                float itemW = width / total;
                for (int i = 0; i < total; i++)
                {
                    Rect cell = new Rect(x + i * itemW, y, itemW, rowH);
                    DrawNavCell(cell, NavEntries[i], critCount, warnCount);
                }
                return rowH;
            }
            else
            {
                int firstRow = (total + 1) / 2;
                int secondRow = total - firstRow;
                float totalH = rowH * 2f + 2f;
                Rect strip = new Rect(x, y, width, totalH);
                Widgets.DrawBoxSolid(strip, PowerUiDraw.BgSecondary);
                PowerUiDraw.DrawBorder(strip, PowerUiDraw.BorderPanel, 1);

                float w1 = width / firstRow;
                for (int i = 0; i < firstRow; i++)
                {
                    Rect cell = new Rect(x + i * w1, y, w1, rowH);
                    DrawNavCell(cell, NavEntries[i], critCount, warnCount);
                }

                float w2 = width / secondRow;
                for (int i = 0; i < secondRow; i++)
                {
                    Rect cell = new Rect(x + i * w2, y + rowH + 2f, w2, rowH);
                    DrawNavCell(cell, NavEntries[firstRow + i], critCount, warnCount);
                }

                return totalH;
            }
        }

        private void DrawNavCell(Rect cell, NavEntry entry, int critCount, int warnCount)
        {
            int badge = entry.Tab == PowerGridTab.Diagnostics ? critCount + warnCount : 0;
            bool isCrit = entry.Tab == PowerGridTab.Diagnostics && critCount > 0;
            bool selected = state.Tab == entry.Tab;
            if (PowerUiDraw.TabItem(cell, entry.LabelKey.Translate(), selected, badge, isCrit, entry.TipKey.Translate()))
                state.Tab = entry.Tab;
        }

        private void ShowBuildingsOnNetwork(int networkId)
        {
            state.Filter.NetworkId = networkId;
            state.Filter.MarkChanged();
            state.Tab = PowerGridTab.Buildings;
        }

        private void ShowNetwork(int networkId)
        {
            state.SelectNetwork(networkId);
            state.Tab = PowerGridTab.Networks;
        }

        private void InspectBuilding(BuildingPowerSnapshot building)
        {
            if (building == null) return;
            state.SelectBuilding(building.Building);
            state.Tab = PowerGridTab.Buildings;
        }

        private void OpenDiagnosticsTab()
        {
            state.Tab = PowerGridTab.Diagnostics;
        }

        private static void DrawStateNotice(Rect rect, string message)
        {
            PowerUiDraw.Panel(rect);
            PowerUiDraw.Label(rect.ContractedBy(16f), message, GameFont.Small,
                PowerUiDraw.TextSecondary, TextAnchor.MiddleCenter, true);
        }
    }
}
