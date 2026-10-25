using Verse;

namespace PowerGridManager
{
    /// <summary>Map-scoped cache, refreshed independently of any UI. Reconstructed after loading.</summary>
    public sealed class PowerGridManagerComponent : MapComponent
    {
        private readonly PowerGridScanner scanner = new PowerGridScanner();
        private int lastRefreshTick = -1;
        public PowerGridSnapshot Snapshot { get; private set; }

        public PowerGridManagerComponent(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            int tick = Find.TickManager.TicksGame;
            int interval = PowerGridManagerMod.Settings == null ? 120 : PowerGridManagerMod.Settings.RefreshIntervalTicks;
            interval = System.Math.Max(60, System.Math.Min(600, interval));
            if (lastRefreshTick >= 0 && tick >= lastRefreshTick && tick - lastRefreshTick < interval) return;
            // Publish only a complete scan. No game objects are accessed by background threads.
            Snapshot = scanner.Scan(map, tick);
            lastRefreshTick = tick;
        }
    }
}
