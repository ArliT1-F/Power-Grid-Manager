using System.Collections.Generic;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Map-scoped cache, history collector and diagnostics engine. Rebuilt after loading; only the
    /// bounded history is persisted. Runs independently of the UI, so closing the window never
    /// stops data collection or stops advisories from being tracked.
    /// </summary>
    public sealed class PowerGridManagerComponent : MapComponent
    {
        private readonly PowerGridScanner scanner = new PowerGridScanner();
        private readonly PowerDiagnosticTracker diagnostics = new PowerDiagnosticTracker();
        private readonly List<PowerDiagnostic> advisories = new List<PowerDiagnostic>(32);
        private PowerManagementController management = new PowerManagementController();

        private int lastRefreshTick = -1;
        private PowerHistoryTracker history = new PowerHistoryTracker();
        private BatteryForecastResult mapForecast;
        private int criticalCount;
        private int warningCount;
        private int resolvedCount;

        public PowerGridSnapshot Snapshot { get; private set; }

        /// <summary>Bounded aggregate history. Never null.</summary>
        public PowerHistoryTracker History => history;

        /// <summary>Runtime episode memory for advisories. Not persisted.</summary>
        public PowerDiagnosticTracker Diagnostics => diagnostics;

        /// <summary>Advisories from the latest evaluation, ranked worst first.</summary>
        public IReadOnlyList<PowerDiagnostic> Advisories => advisories;

        /// <summary>Map-wide endurance estimate cached at the last refresh.</summary>
        public BatteryForecastResult Forecast => mapForecast;

        /// <summary>Optional, opt-in management layer. Never null once the component exists.</summary>
        public PowerManagementController Management => management;

        public int CriticalAdvisoryCount => criticalCount;
        public int WarningAdvisoryCount => warningCount;
        public int ResolvedAdvisoryCount => resolvedCount;

        public PowerGridManagerComponent(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            int tick = Find.TickManager.TicksGame;
            PowerGridManagerSettings settings = PowerGridManagerMod.Settings;
            RefreshIfStale(tick);

            if (settings == null || !settings.HistoryEnabled) return;
            if (Snapshot == null) return;
            history.MaybeSample(Snapshot, tick, settings.SampleIntervalTicks, settings.RetentionTicks);
        }

        /// <summary>
        /// Scans when the cache is missing or older than the configured refresh interval.
        /// Called from the tick hook and once when the dashboard opens; never per frame.
        /// <paramref name="force"/> is used when the window opens so a map whose first scan is
        /// staggered does not show a waiting state to a player looking at it.
        /// </summary>
        public void RefreshIfStale(int tick, bool force = false)
        {
            PowerGridManagerSettings settings = PowerGridManagerMod.Settings;
            int interval = settings == null ? 120 : settings.RefreshIntervalTicks;
            if (interval < 60) interval = 60;
            else if (interval > 600) interval = 600;

            if (Snapshot != null && lastRefreshTick >= 0 && tick >= lastRefreshTick &&
                tick - lastRefreshTick < interval)
            {
                return;
            }

            // Several maps tick on the same frame. Spread their first scans over one in-game
            // second using the map's persisted uniqueID; later refreshes stay naturally staggered.
            if (!force && Snapshot == null && tick >= 0 && tick % StaggerWindowTicks < StaggerOffset()) return;

            // Publish only a complete scan. No game objects are accessed by background threads.
            Snapshot = scanner.Scan(map, tick);
            lastRefreshTick = tick;
            EvaluateAdvisories(tick);
            if (management == null) management = new PowerManagementController();
            management.Tick(Snapshot, settings, tick);
        }

        private const int StaggerWindowTicks = 60;

        private int StaggerOffset()
        {
            int id = map == null || map.uniqueID < 0 ? 0 : map.uniqueID;
            return (int)((uint)id % StaggerWindowTicks);
        }

        /// <summary>
        /// Re-runs the diagnostics engine over the freshly published snapshot. One pass per refresh,
        /// never per frame, and it only reads data the scanner already collected.
        /// </summary>
        private void EvaluateAdvisories(int tick)
        {
            PowerGridManagerSettings settings = PowerGridManagerMod.Settings;
            if (settings == null || Snapshot == null) return;

            PowerDiagnostics.Evaluate(Snapshot, history, settings, diagnostics, advisories,
                management == null ? null : management.Registry);

            criticalCount = 0;
            warningCount = 0;
            resolvedCount = 0;
            for (int i = 0; i < advisories.Count; i++)
            {
                PowerDiagnostic advisory = advisories[i];
                if (!advisory.Ongoing)
                {
                    resolvedCount++;
                    continue;
                }
                if (advisory.Severity == PowerDiagnosticSeverity.Critical) criticalCount++;
                else if (advisory.Severity == PowerDiagnosticSeverity.Warning) warningCount++;
            }

            mapForecast = ComputeForecast(PowerTimeRange.LastDay);

            // Announced outside the UI so problems are reported even with the dashboard closed.
            PowerDiagnosticNotifier.Notify(advisories, settings, diagnostics, tick);
        }

        /// <summary>
        /// Map-wide battery endurance estimate over an in-game time range. Cheap: one pass over the
        /// retained samples, which are capped by the retention window.
        /// </summary>
        public BatteryForecastResult ComputeForecast(PowerTimeRange range)
        {
            PowerGridManagerSettings settings = PowerGridManagerMod.Settings;
            bool includeSelfDischarge = settings == null || settings.ForecastIncludeSelfDischarge;
            if (Snapshot == null) return BatteryForecast.ForMap(null, null, 0, includeSelfDischarge);

            float? average = null;
            int sampleCount = 0;
            if (history != null && history.HasSamples)
            {
                int endTick = history.NewestTick;
                average = PowerForecast.AverageNetWatts(history.Samples,
                    endTick - PowerChartBuilder.TicksFor(range), endTick, out sampleCount);
            }
            return BatteryForecast.ForMap(Snapshot, average, sampleCount, includeSelfDischarge);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref history, "powerHistory");
            Scribe_Deep.Look(ref management, "powerManagement");
            if (history == null) history = new PowerHistoryTracker();
            if (management == null) management = new PowerManagementController();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Runtime caches are intentionally not persisted: they are rebuilt on the next tick.
                Snapshot = null;
                lastRefreshTick = -1;
                advisories.Clear();
                mapForecast = null;
                criticalCount = 0;
                warningCount = 0;
                resolvedCount = 0;
                diagnostics.Reset();
            }
        }

        public override void MapRemoved()
        {
            base.MapRemoved();
            history.Clear();
            Snapshot = null;
            lastRefreshTick = -1;
            advisories.Clear();
            mapForecast = null;
            criticalCount = 0;
            warningCount = 0;
            resolvedCount = 0;
            diagnostics.Reset();
            if (management != null) management.Reset();
        }
    }
}
