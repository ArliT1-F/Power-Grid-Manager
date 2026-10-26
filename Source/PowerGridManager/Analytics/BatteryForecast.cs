using System;
using Verse;

namespace PowerGridManager
{
    /// <summary>What the forecast concluded.</summary>
    public enum BatteryForecastKind
    {
        /// <summary>No battery on this network/map, so there is nothing to forecast.</summary>
        NoBatteries = 0,

        /// <summary>Batteries exist but no usable capacity could be read.</summary>
        NoCapacity,

        /// <summary>Generation and consumption are close enough that no time estimate is meaningful.</summary>
        Balanced,

        /// <summary>Stored energy is falling.</summary>
        Depleting,

        /// <summary>Stored energy is rising.</summary>
        Charging
    }

    /// <summary>How much the estimate can be trusted. Drives the wording, never hidden.</summary>
    public enum ForecastConfidence
    {
        /// <summary>No estimate is possible; the UI explains why instead of showing a number.</summary>
        Unavailable = 0,

        /// <summary>Only the current balance was available, or the figures could not be confirmed.</summary>
        Low,

        /// <summary>History exists but disagrees with the current balance.</summary>
        Medium,

        /// <summary>History covers the window and agrees with the current balance.</summary>
        High
    }

    /// <summary>
    /// Battery endurance estimate. The mod reports an estimate only where the underlying numbers
    /// are observable, always says what the estimate is based on, and never writes a number into
    /// the game's simulation: this is arithmetic over already-collected observations.
    /// </summary>
    public sealed class BatteryForecastResult
    {
        public BatteryForecastKind Kind;
        public ForecastConfidence Confidence;

        public int BatteryCount;
        public float StoredWattDays;
        public float AvailableWattDays;
        public float CapacityWattDays;
        public float? ChargeFraction;

        /// <summary>Generation minus consumption, as reported by the traders on this network.</summary>
        public float TraderNetWatts;

        /// <summary>Battery self-discharge, which the trader balance does not include.</summary>
        public float SelfDischargeWatts;

        /// <summary>The rate the estimate uses: trader balance less self-discharge.</summary>
        public float EffectiveNetWatts;

        /// <summary>Average net balance over the historical window, or null when unavailable.</summary>
        public float? HistoricalNetWatts;

        public int HistoricalSampleCount;

        public int? TicksToEmpty;
        public int? TicksToFull;
        public int? HistoricalTicksToEmpty;
        public int? HistoricalTicksToFull;

        /// <summary>False when at least one battery's charge efficiency could not be read.</summary>
        public bool EfficiencyKnown;

        public bool SelfDischargeIncluded;

        /// <summary>True when the estimate was clipped at the reporting horizon.</summary>
        public bool Saturated;

        public bool HasStorage => BatteryCount > 0 && CapacityWattDays > 0f;
        public bool IsDepleting => Kind == BatteryForecastKind.Depleting;
        public bool IsCharging => Kind == BatteryForecastKind.Charging;

        /// <summary>The estimate the UI should lead with.</summary>
        public int? PrimaryTicks => TicksToEmpty ?? TicksToFull;
    }

    /// <summary>
    /// Battery endurance forecasting.
    /// <para>
    /// Everything here is derived from values the game exposes and that the scanner already
    /// verified: stored energy and capacity in watt-days, signed trader output in watts, battery
    /// efficiency, and (optionally) the recorded history. One watt sustained for one in-game day
    /// is one watt-day, because <c>CompPower.WattsToWattDaysPerTick * 60000 = 1</c>.
    /// </para>
    /// </summary>
    public static class BatteryForecast
    {
        /// <summary>
        /// Verified in the game's own <c>CompPowerBattery.CompTick</c>: every battery with stored
        /// energy draws 5 W continuously. The trader balance the dashboard shows does not include
        /// it, so a forecast that ignores it would overstate endurance on small deficits.
        /// </summary>
        public const float SelfDischargeWattsPerBattery = 5f;

        /// <summary>Below this the balance is treated as flat rather than as a trend.</summary>
        public const float BalanceEpsilonWatts = 1f;

        /// <summary>Estimates beyond this are reported as "more than" instead of a raw number.</summary>
        public const int MaximumReportableTicks = PowerTime.TicksPerDay * 30;

        /// <summary>Samples needed before history can raise the confidence above Low.</summary>
        public const int MinimumHistoricalSamples = 4;

        /// <summary>Relative disagreement between current and historical balance that still counts as agreement.</summary>
        public const float AgreementTolerance = 0.25f;

        /// <summary>
        /// Core estimate. Pure arithmetic: no game objects, no simulation calls, no side effects.
        /// </summary>
        public static BatteryForecastResult Evaluate(float storedWattDays, float availableWattDays,
            float capacityWattDays, float generationWatts, float consumptionWatts, int batteryCount,
            float? chargeEfficiency, float? historicalNetWatts, int historicalSampleCount,
            bool includeSelfDischarge)
        {
            var result = new BatteryForecastResult
            {
                BatteryCount = batteryCount,
                StoredWattDays = Sanitize(storedWattDays),
                AvailableWattDays = Sanitize(availableWattDays),
                CapacityWattDays = Sanitize(capacityWattDays),
                TraderNetWatts = Sanitize(generationWatts) - Sanitize(consumptionWatts),
                SelfDischargeIncluded = includeSelfDischarge,
                HistoricalNetWatts = historicalNetWatts,
                HistoricalSampleCount = historicalSampleCount < 0 ? 0 : historicalSampleCount,
                EfficiencyKnown = chargeEfficiency.HasValue
            };

            if (result.AvailableWattDays > result.StoredWattDays) result.AvailableWattDays = result.StoredWattDays;
            if (result.AvailableWattDays < 0f) result.AvailableWattDays = 0f;
            if (result.CapacityWattDays > 0f)
            {
                if (result.StoredWattDays > result.CapacityWattDays) result.StoredWattDays = result.CapacityWattDays;
                if (result.AvailableWattDays > result.CapacityWattDays)
                {
                    result.AvailableWattDays = result.CapacityWattDays;
                }
                result.ChargeFraction = result.StoredWattDays / result.CapacityWattDays;
            }
            else
            {
                result.StoredWattDays = 0f;
                result.AvailableWattDays = 0f;
            }

            result.SelfDischargeWatts = includeSelfDischarge ? batteryCount * SelfDischargeWattsPerBattery : 0f;
            result.EffectiveNetWatts = result.TraderNetWatts - result.SelfDischargeWatts;

            if (batteryCount <= 0)
            {
                result.Kind = BatteryForecastKind.NoBatteries;
                result.Confidence = ForecastConfidence.Unavailable;
                return result;
            }
            if (result.CapacityWattDays <= 0f)
            {
                result.Kind = BatteryForecastKind.NoCapacity;
                result.Confidence = ForecastConfidence.Unavailable;
                return result;
            }

            float efficiency = chargeEfficiency ?? 1f;
            if (efficiency <= 0f || efficiency > 1f) efficiency = 1f;

            float net = result.EffectiveNetWatts;
            bool flat = net > -BalanceEpsilonWatts && net < BalanceEpsilonWatts;
            bool nothingToDraw = result.AvailableWattDays <= 0f;
            bool nothingToStore = result.CapacityWattDays - result.StoredWattDays <= 0f;

            if (flat || (net < 0f && nothingToDraw) || (net > 0f && nothingToStore))
            {
                result.Kind = BatteryForecastKind.Balanced;
                result.Confidence = ForecastConfidence.Unavailable;
                ApplyHistory(result, historicalNetWatts, historicalSampleCount, efficiency, includeSelfDischarge);
                return result;
            }

            if (net < 0f)
            {
                result.Kind = BatteryForecastKind.Depleting;
                bool emptySaturated;
                result.TicksToEmpty = ToTicks(result.AvailableWattDays / -net, out emptySaturated);
                if (emptySaturated) result.Saturated = true;
            }
            else
            {
                result.Kind = BatteryForecastKind.Charging;
                float room = result.CapacityWattDays - result.StoredWattDays;
                bool fullSaturated;
                result.TicksToFull = ToTicks(room / (net * efficiency), out fullSaturated);
                if (fullSaturated) result.Saturated = true;
            }

            ApplyHistory(result, historicalNetWatts, historicalSampleCount, efficiency, includeSelfDischarge);
            result.Confidence = Classify(result, net);
            return result;
        }

        /// <summary>Convenience wrapper for one network, using only its current balance.</summary>
        public static BatteryForecastResult ForNetwork(PowerNetworkSnapshot network, bool includeSelfDischarge)
        {
            if (network == null) return Evaluate(0f, 0f, 0f, 0f, 0f, 0, null, null, 0, includeSelfDischarge);
            return Evaluate(network.StoredWattDays, network.AvailableStoredWattDays, network.CapacityWattDays,
                network.GenerationWatts, network.ConsumptionWatts, network.BatteryCount, network.ChargeEfficiency,
                null, 0, includeSelfDischarge);
        }

        /// <summary>Convenience wrapper for the whole map, with an optional historical average.</summary>
        public static BatteryForecastResult ForMap(PowerGridSnapshot snapshot, float? historicalNetWatts,
            int historicalSampleCount, bool includeSelfDischarge)
        {
            if (snapshot == null) return Evaluate(0f, 0f, 0f, 0f, 0f, 0, null, null, 0, includeSelfDischarge);
            return Evaluate(snapshot.StoredWattDays, snapshot.AvailableStoredWattDays, snapshot.CapacityWattDays,
                snapshot.GenerationWatts, snapshot.ConsumptionWatts, snapshot.BatteryCount, snapshot.ChargeEfficiency,
                historicalNetWatts, historicalSampleCount, includeSelfDischarge);
        }

        private static void ApplyHistory(BatteryForecastResult result, float? historicalNetWatts,
            int historicalSampleCount, float efficiency, bool includeSelfDischarge)
        {
            if (!historicalNetWatts.HasValue || historicalSampleCount < MinimumHistoricalSamples) return;

            float historicalNet = historicalNetWatts.Value -
                                  (includeSelfDischarge ? result.BatteryCount * SelfDischargeWattsPerBattery : 0f);
            result.HistoricalNetWatts = historicalNet;

            if (historicalNet < -BalanceEpsilonWatts && result.AvailableWattDays > 0f)
            {
                bool ignored;
                result.HistoricalTicksToEmpty = ToTicks(result.AvailableWattDays / -historicalNet, out ignored);
            }
            else if (historicalNet > BalanceEpsilonWatts)
            {
                float room = result.CapacityWattDays - result.StoredWattDays;
                if (room > 0f)
                {
                    bool ignored;
                    result.HistoricalTicksToFull = ToTicks(room / (historicalNet * efficiency), out ignored);
                }
            }
        }

        private static ForecastConfidence Classify(BatteryForecastResult result, float currentNet)
        {
            if (result.HistoricalSampleCount < MinimumHistoricalSamples || !result.HistoricalNetWatts.HasValue)
            {
                return ForecastConfidence.Low;
            }
            float historicalNet = result.HistoricalNetWatts.Value;
            if (historicalNet > -BalanceEpsilonWatts && historicalNet < BalanceEpsilonWatts) return ForecastConfidence.Low;

            // Opposite signs are the strongest possible disagreement: the current reading says
            // one thing and the recent average says the other.
            if (currentNet < 0f != historicalNet < 0f) return ForecastConfidence.Low;

            float difference = Math.Abs(currentNet - historicalNet);
            float allowed = Math.Abs(historicalNet) * AgreementTolerance;
            if (difference <= allowed)
            {
                // An unconfirmed charge efficiency weakens the charging estimate only.
                return result.Kind == BatteryForecastKind.Charging && !result.EfficiencyKnown
                    ? ForecastConfidence.Medium
                    : ForecastConfidence.High;
            }
            return ForecastConfidence.Medium;
        }

        private static int ToTicks(float days, out bool saturated)
        {
            saturated = false;
            if (float.IsNaN(days) || float.IsInfinity(days) || days <= 0f) return 0;
            double ticks = days * PowerTime.TicksPerDay;
            if (ticks > MaximumReportableTicks)
            {
                saturated = true;
                return MaximumReportableTicks;
            }
            int rounded = (int)System.Math.Round(ticks);
            return rounded < 0 ? 0 : rounded;
        }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value;
        }
    }
}
