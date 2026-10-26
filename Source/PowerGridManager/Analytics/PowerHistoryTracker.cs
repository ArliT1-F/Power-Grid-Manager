using System.Collections.Generic;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Bounded, map-scoped history of aggregate power samples.
    ///
    /// Design constraints:
    /// * Only primitive aggregates are stored (no building/network references).
    /// * Storage is bounded by the retention window and a hard sample cap.
    /// * Sampling is driven by a configurable interval, never by a per-tick scan.
    /// * Loading tolerates missing/extra/invalid serialized data from older versions.
    /// </summary>
    public sealed class PowerHistoryTracker : IExposable
    {
        public const int CurrentFormatVersion = 1;

        /// <summary>Hard upper bound, independent of settings, so a save cannot grow without limit.</summary>
        public const int HardSampleCap = 20000;

        private const int MinimumIntervalTicks = 60;
        private const int MaximumIntervalTicks = 15000;

        private readonly List<PowerHistorySample> samples = new List<PowerHistorySample>(256);

        // Scratch lists used only while saving/loading; kept to avoid per-save allocations.
        // They are not readonly because Scribe_Collections.Look takes them by ref.
        private List<int> serializedTicks = new List<int>(256);
        private List<float> serializedGeneration = new List<float>(256);
        private List<float> serializedConsumption = new List<float>(256);
        private List<float> serializedStored = new List<float>(256);
        private List<float> serializedCapacity = new List<float>(256);

        private int formatVersion = CurrentFormatVersion;
        private int lastSampleIntervalTicks = 2500;
        private int lastSampleTick = -1;
        private int discardedSampleCount;

        public int FormatVersion => formatVersion;
        public IReadOnlyList<PowerHistorySample> Samples => samples;
        public int Count => samples.Count;
        public bool HasSamples => samples.Count > 0;

        /// <summary>Interval that was in effect when the newest sample was taken.</summary>
        public int LastSampleIntervalTicks => lastSampleIntervalTicks;

        public int LastSampleTick => lastSampleTick;
        public int OldestTick => samples.Count == 0 ? -1 : samples[0].Tick;
        public int NewestTick => samples.Count == 0 ? -1 : samples[samples.Count - 1].Tick;

        /// <summary>Number of stored rows rejected while loading (corrupt or non-monotonic data).</summary>
        public int DiscardedSampleCount => discardedSampleCount;

        /// <summary>True once at least two samples exist, i.e. a trend can be drawn.</summary>
        public bool CanDrawTrend => samples.Count > 1;

        /// <summary>
        /// Records a sample when the configured interval has elapsed. Returns true when a
        /// sample was stored. Never samples while the game is paused: callers only invoke
        /// this from the map tick hook.
        ///
        /// The stored tick is the snapshot's own measurement tick, not "now": a cached
        /// snapshot can be a refresh interval old, and recording it as if it were current
        /// would shift every chart point. <paramref name="now"/> only drives the interval.
        /// </summary>
        public bool MaybeSample(PowerGridSnapshot snapshot, int now, int intervalTicks, int retentionTicks)
        {
            if (snapshot == null) return false;
            int interval = Clamp(intervalTicks, MinimumIntervalTicks, MaximumIntervalTicks);
            int retention = retentionTicks < 1 ? 1 : retentionTicks;
            int sampleTick = snapshot.Tick >= 0 && snapshot.Tick <= now ? snapshot.Tick : now;

            if (lastSampleTick >= 0)
            {
                if (now < lastSampleTick)
                {
                    // Tick rollback (debug tooling, or a loaded save whose tick is behind a
                    // sample we already recorded). Keep the series monotonic instead of
                    // fabricating a backwards jump.
                    TrimNewerThan(now);
                    if (samples.Count > 0)
                    {
                        lastSampleTick = samples[samples.Count - 1].Tick;
                        if (now - lastSampleTick < interval) return false;
                    }
                    else
                    {
                        lastSampleTick = -1;
                    }
                }
                else if (now - lastSampleTick < interval)
                {
                    return false;
                }
            }

            // A cached snapshot can be older than the previous sample; storing it again
            // would duplicate a tick and break monotonicity (which the loader rejects).
            if (lastSampleTick >= 0 && sampleTick <= lastSampleTick) return false;

            samples.Add(new PowerHistorySample(sampleTick, Sanitize(snapshot.GenerationWatts),
                Sanitize(snapshot.ConsumptionWatts), Sanitize(snapshot.StoredWattDays),
                Sanitize(snapshot.CapacityWattDays)));
            lastSampleTick = sampleTick;
            lastSampleIntervalTicks = interval;
            Prune(sampleTick, retention, interval);
            return true;
        }

        /// <summary>Drops samples older than the retention window and enforces the hard cap.</summary>
        public void Prune(int now, int retentionTicks, int intervalTicks)
        {
            int retention = retentionTicks < 1 ? 1 : retentionTicks;
            int interval = Clamp(intervalTicks, MinimumIntervalTicks, MaximumIntervalTicks);
            int oldestAllowed = now - retention;

            int removeCount = 0;
            while (removeCount < samples.Count && samples[removeCount].Tick < oldestAllowed) removeCount++;

            int cap = ComputeSampleCap(retention, interval);
            if (samples.Count - removeCount > cap) removeCount = samples.Count - cap;
            if (removeCount > 0) samples.RemoveRange(0, removeCount);
        }

        /// <summary>Upper bound for the retained sample count for a retention window.</summary>
        public static int ComputeSampleCap(int retentionTicks, int intervalTicks)
        {
            int interval = Clamp(intervalTicks, MinimumIntervalTicks, MaximumIntervalTicks);
            int retention = retentionTicks < 1 ? 1 : retentionTicks;
            // +2 tolerates boundary samples at both ends of the window.
            int cap = retention / interval + 2;
            if (cap < 16) cap = 16;
            if (cap > HardSampleCap) cap = HardSampleCap;
            return cap;
        }

        public PowerHistorySample this[int index] => samples[index];

        public void Clear()
        {
            samples.Clear();
            lastSampleTick = -1;
            discardedSampleCount = 0;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref formatVersion, "formatVersion", CurrentFormatVersion);
            Scribe_Values.Look(ref lastSampleIntervalTicks, "sampleInterval", 2500);
            Scribe_Values.Look(ref lastSampleTick, "lastSampleTick", -1);

            // The five parallel lists below are written in the plain Saving pass, which is
            // the pass every game build performs. LoadSaveMode has gained and lost members
            // between game generations, so only long-standing values are referenced here.
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                FillSerializedLists();
            }

            Scribe_Collections.Look(ref serializedTicks, "ticks", LookMode.Value);
            Scribe_Collections.Look(ref serializedGeneration, "generationWatts", LookMode.Value);
            Scribe_Collections.Look(ref serializedConsumption, "consumptionWatts", LookMode.Value);
            Scribe_Collections.Look(ref serializedStored, "storedWattDays", LookMode.Value);
            Scribe_Collections.Look(ref serializedCapacity, "capacityWattDays", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                RebuildFromSerializedLists();
            }
            else if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ClampLoadedState();
            }
        }

        private void FillSerializedLists()
        {
            serializedTicks.Clear();
            serializedGeneration.Clear();
            serializedConsumption.Clear();
            serializedStored.Clear();
            serializedCapacity.Clear();
            for (int i = 0; i < samples.Count; i++)
            {
                PowerHistorySample sample = samples[i];
                serializedTicks.Add(sample.Tick);
                serializedGeneration.Add(sample.GenerationWatts);
                serializedConsumption.Add(sample.ConsumptionWatts);
                serializedStored.Add(sample.StoredWattDays);
                serializedCapacity.Add(sample.CapacityWattDays);
            }
        }

        private void RebuildFromSerializedLists()
        {
            samples.Clear();
            discardedSampleCount = 0;
            if (serializedTicks == null) return;

            int count = serializedTicks.Count;
            if (serializedGeneration != null && serializedGeneration.Count < count) count = serializedGeneration.Count;
            if (serializedConsumption != null && serializedConsumption.Count < count) count = serializedConsumption.Count;
            if (serializedStored != null && serializedStored.Count < count) count = serializedStored.Count;
            if (serializedCapacity != null && serializedCapacity.Count < count) count = serializedCapacity.Count;

            // Length mismatches mean a partially written or hand-edited save: keep the
            // shared prefix and record how many rows were dropped.
            int declared = serializedTicks.Count;
            int previousTick = int.MinValue;
            for (int i = 0; i < count; i++)
            {
                int tick = serializedTicks[i];
                float generation = serializedGeneration[i];
                float consumption = serializedConsumption[i];
                float stored = serializedStored[i];
                float capacity = serializedCapacity[i];
                if (tick < 0 || tick <= previousTick || !IsFinite(generation) || !IsFinite(consumption) ||
                    !IsFinite(stored) || !IsFinite(capacity))
                {
                    discardedSampleCount++;
                    continue;
                }
                samples.Add(new PowerHistorySample(tick, generation, consumption, stored, capacity));
                previousTick = tick;
            }
            discardedSampleCount += declared - count;
            if (samples.Count > HardSampleCap) samples.RemoveRange(0, samples.Count - HardSampleCap);
            if (samples.Count > 0) lastSampleTick = samples[samples.Count - 1].Tick;
        }

        private void ClampLoadedState()
        {
            lastSampleIntervalTicks = Clamp(lastSampleIntervalTicks, MinimumIntervalTicks, MaximumIntervalTicks);
            if (samples.Count > 0) lastSampleTick = samples[samples.Count - 1].Tick;
            else lastSampleTick = -1;
            if (formatVersion > CurrentFormatVersion) formatVersion = CurrentFormatVersion;
        }

        private void TrimNewerThan(int tick)
        {
            while (samples.Count > 0 && samples[samples.Count - 1].Tick > tick) samples.RemoveAt(samples.Count - 1);
        }

        private static float Sanitize(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            return value;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }
    }
}
