using System;
using System.Collections.Generic;

namespace PowerGridManager
{
    /// <summary>
    /// Inputs and projected outputs of a hypothetical power-system change. Nothing here
    /// touches the live game: all values are projected from the current snapshot and the
    /// scenario is discarded after evaluation. This is the core of the scenario planner
    /// (Feature Four) and the power-budget presentation (Feature Five).
    /// </summary>
    public sealed class PowerScenario
    {
        // --- Hypothetical inputs. None of these are applied to the live game. ----------

        /// <summary>Hypothetical added generator output, in watts (assumed constant).</summary>
        public float AddedGenerationWatts;

        /// <summary>Hypothetical added battery capacity, in watt-days.</summary>
        public float AddedBatteryCapacityWattDays;

        /// <summary>Hypothetical added consumer draw, in watts (assumed continuous).</summary>
        public float AddedConsumerWatts;

        /// <summary>Hypothetical draw switched off, in watts (clamped to current draw).</summary>
        public float SwitchedOffConsumerWatts;

        // --- Projected outputs -------------------------------------------------------

        public float ProjectedGenerationWatts;
        public float ProjectedConsumptionWatts;
        public float ProjectedNetWatts;

        /// <summary>Projected usable charge fraction, or null when there is no capacity.</summary>
        public float? ProjectedChargeFraction;

        /// <summary>Projected ticks to depletion, or null when not depleting.</summary>
        public int? ProjectedTicksToEmpty;

        /// <summary>Projected ticks to full, or null when not charging.</summary>
        public int? ProjectedTicksToFull;

        public BatteryForecastKind ProjectedKind;
        public ForecastConfidence ProjectedConfidence = ForecastConfidence.Low;

        /// <summary>Projected generation margin (net balance), in watts.</summary>
        public float ReserveMarginWatts;

        /// <summary>True when the projected margin meets the configured reserve target.</summary>
        public bool ReserveTargetSatisfied;

        /// <summary>Plain-language assumptions behind the projection.</summary>
        public List<string> Assumptions = new List<string>();

        /// <summary>False when the snapshot or settings were missing.</summary>
        public bool IsValid;
    }
}
