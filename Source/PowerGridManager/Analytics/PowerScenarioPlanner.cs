using System;

namespace PowerGridManager
{
    /// <summary>
    /// Projects the effect of hypothetical power-system changes. Pure arithmetic over the
    /// current snapshot; it never creates buildings, spends resources, or modifies the live
    /// electrical system. Battery endurance is computed by reusing
    /// <see cref="BatteryForecast.Evaluate"/> so scenario and live forecasts share one
    /// verified method.
    /// </summary>
    public static class PowerScenarioPlanner
    {
        /// <summary>
        /// Evaluates one scenario. The returned scenario's <see cref="PowerScenario.Assumptions"/>
        /// describe exactly what the projection assumes so the player can judge reliability.
        /// </summary>
        public static PowerScenario Evaluate(PowerGridSnapshot snapshot, PowerScenario scenario,
            PowerGridManagerSettings settings, float reserveMarginWatts)
        {
            var result = new PowerScenario();
            result.IsValid = snapshot != null && settings != null;
            if (!result.IsValid) return result;

            // Sanitize inputs so a malformed scenario can never project nonsense.
            float addedGen = Clamp(scenario.AddedGenerationWatts, 0f, float.MaxValue);
            float addedCap = Clamp(scenario.AddedBatteryCapacityWattDays, 0f, float.MaxValue);
            float addedCons = Clamp(scenario.AddedConsumerWatts, 0f, float.MaxValue);
            float switchedOff = Clamp(scenario.SwitchedOffConsumerWatts, 0f,
                snapshot.ConsumptionWatts);

            float projectedGeneration = snapshot.GenerationWatts + addedGen;
            float projectedConsumption = Math.Max(0f,
                snapshot.ConsumptionWatts + addedCons - switchedOff);
            float projectedNet = projectedGeneration - projectedConsumption;

            result.ProjectedGenerationWatts = projectedGeneration;
            result.ProjectedConsumptionWatts = projectedConsumption;
            result.ProjectedNetWatts = projectedNet;

            // Endurance is projected from usable stored energy against the projected net
            // balance, with any added battery capacity added to the configured capacity.
            float stored = snapshot.AvailableStoredWattDays;
            float capacity = snapshot.CapacityWattDays + addedCap;
            bool includeSelfDischarge = settings.ForecastIncludeSelfDischarge;

            BatteryForecastResult forecast = BatteryForecast.Evaluate(
                stored, stored, capacity, projectedGeneration, projectedConsumption,
                snapshot.BatteryCount, snapshot.ChargeEfficiency, null, 0,
                includeSelfDischarge);

            result.ProjectedChargeFraction = forecast.ChargeFraction;
            result.ProjectedTicksToEmpty = forecast.TicksToEmpty;
            result.ProjectedTicksToFull = forecast.TicksToFull;
            result.ProjectedKind = forecast.Kind;
            result.ProjectedConfidence = forecast.Confidence;

            // Reserve target: a positive projected net balance is headroom above demand.
            result.ReserveMarginWatts = projectedNet;
            result.ReserveTargetSatisfied = projectedNet >= reserveMarginWatts;

            BuildAssumptions(result, snapshot, scenario, addedGen, addedCap, addedCons,
                switchedOff, includeSelfDischarge);
            return result;
        }

        private static void BuildAssumptions(PowerScenario result, PowerGridSnapshot snapshot,
            PowerScenario scenario, float addedGen, float addedCap, float addedCons,
            float switchedOff, bool includeSelfDischarge)
        {
            result.Assumptions.Add(
                "The projection assumes current generation and consumption stay constant; " +
                "real demand and solar/wind output vary over time.");
            if (addedGen > 0f)
            {
                result.Assumptions.Add(
                    "Added generator output is assumed to be constant. Solar and wind output " +
                    "depends on weather and time of day, so treat it as an upper bound.");
            }
            if (addedCons > 0f)
            {
                result.Assumptions.Add(
                    "Added consumers are assumed to draw continuously at their nominal rate.");
            }
            if (switchedOff > 0f)
            {
                result.Assumptions.Add(
                    "Switched-off consumers are assumed to stay off for the whole projection.");
            }
            if (snapshot.BatteryCount > 0)
            {
                result.Assumptions.Add(
                    "Battery endurance uses the measured charge efficiency where available; " +
                    "otherwise it assumes no charging loss. " +
                    (includeSelfDischarge
                        ? "Vanilla's 5 W per-battery self-discharge is included."
                        : "Self-discharge is not included."));
            }
            else
            {
                result.Assumptions.Add(
                    "There are no batteries, so there is no stored energy to deplete or fill.");
            }
            result.Assumptions.Add(
                "This scenario does not modify the live game; it is a planning aid only.");
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }
    }
}
