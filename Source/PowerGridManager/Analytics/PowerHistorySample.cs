namespace PowerGridManager
{
    /// <summary>
    /// One aggregate map-level power observation. Primitive fields only: this is the
    /// unit that is persisted, so it must stay small and free of game object references.
    /// </summary>
    public struct PowerHistorySample
    {
        public PowerHistorySample(int tick, float generationWatts, float consumptionWatts,
            float storedWattDays, float capacityWattDays)
        {
            Tick = tick;
            GenerationWatts = generationWatts;
            ConsumptionWatts = consumptionWatts;
            StoredWattDays = storedWattDays;
            CapacityWattDays = capacityWattDays;
        }

        public int Tick;
        public float GenerationWatts;
        public float ConsumptionWatts;
        public float StoredWattDays;
        public float CapacityWattDays;

        public float NetWatts => GenerationWatts - ConsumptionWatts;

        /// <summary>Charge fraction in the 0..1 range, or null when capacity is unknown.</summary>
        public float? ChargeFraction
        {
            get
            {
                if (CapacityWattDays <= 0f) return null;
                float fraction = StoredWattDays / CapacityWattDays;
                if (float.IsNaN(fraction)) return null;
                if (fraction < 0f) return 0f;
                if (fraction > 1f) return 1f;
                return fraction;
            }
        }
    }
}
