namespace PowerGridManager
{
    /// <summary>How completely the mod can explain a building's power behaviour.</summary>
    public enum PowerSupportLevel
    {
        /// <summary>Recognised vanilla component contract.</summary>
        Supported = 0,
        /// <summary>Recognised contract implemented outside the game assembly (modded type).</summary>
        CustomImplementation,
        /// <summary>At least one component is not a recognised power contract.</summary>
        Unsupported,
        /// <summary>Connection could not be confirmed at scan time (pending topology).</summary>
        Pending
    }

    /// <summary>
    /// Single place that decides how much the mod claims to know about a building.
    /// No Harmony patches, no reflection and no assumptions about third-party power
    /// systems: anything that is not the recognised public contract is reported as
    /// unsupported instead of being interpreted.
    /// </summary>
    public static class CompatibilityRegistry
    {
        public static PowerSupportLevel Evaluate(BuildingPowerSnapshot building)
        {
            if (building == null) return PowerSupportLevel.Unsupported;
            if (building.HasUnsupportedComponent) return PowerSupportLevel.Unsupported;
            if (building.Connection == PowerConnectionState.MembershipPending) return PowerSupportLevel.Pending;

            bool custom = false;
            for (int i = 0; i < building.Components.Count; i++)
            {
                if (building.Components[i].IsCustomImplementation) custom = true;
            }
            return custom ? PowerSupportLevel.CustomImplementation : PowerSupportLevel.Supported;
        }

        public static string LabelKey(PowerSupportLevel level)
        {
            switch (level)
            {
                case PowerSupportLevel.Supported: return "PGM_Support_Supported";
                case PowerSupportLevel.CustomImplementation: return "PGM_Support_Custom";
                case PowerSupportLevel.Pending: return "PGM_Support_Pending";
                default: return "PGM_Support_Unsupported";
            }
        }

        public static string TooltipKey(PowerSupportLevel level)
        {
            switch (level)
            {
                case PowerSupportLevel.Supported: return "PGM_Support_SupportedTip";
                case PowerSupportLevel.CustomImplementation: return "PGM_Support_CustomTip";
                case PowerSupportLevel.Pending: return "PGM_Support_PendingTip";
                default: return "PGM_Support_UnsupportedTip";
            }
        }
    }
}
