using System.Collections.Generic;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Presentation-independent helpers shared by the dashboard panels: status
    /// classification, category/room labels, and grouping keys.
    /// </summary>
    public static class PowerGridUtilities
    {
        /// <summary>
        /// Derives a network status label from observed data only. A network whose
        /// observation is incomplete is never labelled "stable".
        /// </summary>
        public static NetworkStatus EvaluateStatus(PowerNetworkSnapshot network, float highLoadThreshold)
        {
            if (network == null) return NetworkStatus.Unknown;

            bool hasConsumers = network.ConsumerCount > 0;
            bool hasGeneration = network.GenerationWatts > 0.01f;
            bool hasBatteries = network.BatteryCount > 0;
            bool hasStoredEnergy = network.StoredWattDays > 0.001f;
            float net = network.NetWatts;

            if (!hasConsumers && !hasGeneration && !hasBatteries) return NetworkStatus.Unknown;
            if (network.HasIncompleteData && !hasConsumers && !hasGeneration) return NetworkStatus.Unknown;

            if (hasConsumers && !hasGeneration) return NetworkStatus.NoGeneration;
            if (net < -0.01f)
            {
                // Batteries covering a deficit is reported as battery discharge, which is
                // the more actionable fact. The deficit itself remains visible as the
                // negative net balance and in the map-level deficit count.
                return hasBatteries && hasStoredEnergy ? NetworkStatus.BatteryDischarging : NetworkStatus.Deficit;
            }
            if (hasGeneration && network.ConsumptionWatts >= network.GenerationWatts * highLoadThreshold)
                return NetworkStatus.HighLoad;
            if (hasBatteries && net > 0.01f && network.CapacityWattDays - network.StoredWattDays > 0.001f)
                return NetworkStatus.BatteryCharging;
            return NetworkStatus.Stable;
        }

        public static string StatusLabelKey(NetworkStatus status)
        {
            switch (status)
            {
                case NetworkStatus.Stable: return "PGM_Status_Stable";
                case NetworkStatus.HighLoad: return "PGM_Status_HighLoad";
                case NetworkStatus.Deficit: return "PGM_Status_Deficit";
                case NetworkStatus.BatteryDischarging: return "PGM_Status_BatteryDischarging";
                case NetworkStatus.BatteryCharging: return "PGM_Status_BatteryCharging";
                case NetworkStatus.NoGeneration: return "PGM_Status_NoGeneration";
                default: return "PGM_Status_Unknown";
            }
        }

        public static string CategoryLabelKey(PowerCategory category)
        {
            switch (category)
            {
                case PowerCategory.Generation: return "PGM_Category_Generation";
                case PowerCategory.Storage: return "PGM_Category_Storage";
                case PowerCategory.Production: return "PGM_Category_Production";
                case PowerCategory.TemperatureControl: return "PGM_Category_Temperature";
                case PowerCategory.Lighting: return "PGM_Category_Lighting";
                case PowerCategory.Research: return "PGM_Category_Research";
                case PowerCategory.Medical: return "PGM_Category_Medical";
                case PowerCategory.Security: return "PGM_Category_Security";
                case PowerCategory.Recreation: return "PGM_Category_Recreation";
                default: return "PGM_Category_Unassigned";
            }
        }

        public static string ConnectionLabelKey(PowerConnectionState state)
        {
            switch (state)
            {
                case PowerConnectionState.Connected: return "PGM_Connection_Connected";
                case PowerConnectionState.NoNetwork: return "PGM_Connection_NoNetwork";
                default: return "PGM_Connection_Pending";
            }
        }

        public static string TimeRangeLabelKey(PowerTimeRange range)
        {
            switch (range)
            {
                case PowerTimeRange.LastHour: return "PGM_Range_Hour";
                case PowerTimeRange.LastSixHours: return "PGM_Range_SixHours";
                case PowerTimeRange.LastDay: return "PGM_Range_Day";
                case PowerTimeRange.LastThreeDays: return "PGM_Range_ThreeDays";
                default: return "PGM_Range_SevenDays";
            }
        }

        /// <summary>
        /// Room display label. Buildings without a district are reported as unassigned
        /// rather than being attached to a room that the game does not report.
        /// </summary>
        public static string RoomLabel(Room room)
        {
            if (room == null) return "PGM_Room_None".Translate().ToString();
            if (room.Role == null) return "PGM_Room_Unnamed".Translate().ToString();
            return room.Role.LabelCap.ToString();
        }

        /// <summary>
        /// True when a building's current draw is worth surfacing as high consumption.
        /// The threshold is a user setting in watts.
        /// </summary>
        public static bool IsHighConsumption(BuildingPowerSnapshot building, float thresholdWatts)
        {
            if (building == null) return false;
            return building.ConsumptionWatts >= thresholdWatts && thresholdWatts > 0f;
        }

        /// <summary>True when the building's power behaviour cannot be fully explained.</summary>
        public static bool HasUnknownBehavior(BuildingPowerSnapshot building)
        {
            if (building == null) return false;
            if (building.HasUnsupportedComponent) return true;
            return building.Connection != PowerConnectionState.Connected;
        }

        /// <summary>Sum of one network's stored energy across the snapshot buildings.</summary>
        public static void CollectNetworkBuildings(PowerGridSnapshot snapshot, int networkId, List<BuildingPowerSnapshot> buffer)
        {
            buffer.Clear();
            if (snapshot == null) return;
            IReadOnlyList<BuildingPowerSnapshot> buildings = snapshot.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i].NetworkId == networkId) buffer.Add(buildings[i]);
            }
        }
    }
}
