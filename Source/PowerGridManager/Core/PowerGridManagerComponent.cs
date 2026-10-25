using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Map-scoped cache and history collector. Rebuilt after loading; only the bounded
    /// history is persisted. Runs independently of the UI, so closing the window never
    /// stops data collection.
    /// </summary>
    public sealed class PowerGridManagerComponent : MapComponent
    {
        private readonly PowerGridScanner scanner = new PowerGridScanner();
        private int lastRefreshTick = -1;
        private PowerHistoryTracker history = new PowerHistoryTracker();

        public PowerGridSnapshot Snapshot { get; private set; }

        /// <summary>Bounded aggregate history. Never null.</summary>
        public PowerHistoryTracker History => history;

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
        /// </summary>
        public void RefreshIfStale(int tick)
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

            // Publish only a complete scan. No game objects are accessed by background threads.
            Snapshot = scanner.Scan(map, tick);
            lastRefreshTick = tick;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref history, "powerHistory");
            if (history == null) history = new PowerHistoryTracker();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Runtime cache is intentionally not persisted: it is rebuilt on the next tick.
                Snapshot = null;
                lastRefreshTick = -1;
            }
        }

        public override void MapRemoved()
        {
            base.MapRemoved();
            history.Clear();
            Snapshot = null;
            lastRefreshTick = -1;
        }
    }
}
