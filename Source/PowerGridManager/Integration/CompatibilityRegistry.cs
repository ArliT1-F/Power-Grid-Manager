using System;

namespace PowerGridManager
{
    /// <summary>How much the mod can safely claim about controlling one building's switch.</summary>
    public enum PowerControlSupport
    {
        /// <summary>Vanilla flick component, confirmed as a supported control target.</summary>
        Supported = 0,
        /// <summary>Modded flick component; its public toggle gizmo is verified at action time.</summary>
        ModdedImplementation,
        /// <summary>Connection state could not be confirmed for this refresh.</summary>
        Pending,
        /// <summary>No safe control path is available.</summary>
        Unsupported
    }

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
        /// <summary>
        /// A component counts as a custom implementation when it is either in another assembly
        /// or in another namespace than the vanilla contract type it derives from. The namespace
        /// check is a deliberately conservative second signal: some mod packs ship types with no
        /// namespace, and assembly identity alone can be misleading in test harnesses.
        /// </summary>
        public static bool IsCustomComponentType(Type type, Type vanillaType)
        {
            if (type == null || vanillaType == null) return false;
            return type.Assembly != vanillaType.Assembly || type.Namespace != vanillaType.Namespace;
        }

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

        /// <summary>
        /// Isolated compatibility decision for the optional control layer. It never assumes a
        /// modded flick component behaves like vanilla: the real toggle gizmo is checked by
        /// <see cref="BuildingPowerControl"/> immediately before any switch is changed.
        /// </summary>
        public static PowerControlSupport EvaluateControl(BuildingPowerSnapshot building,
            PowerManagementRegistry registry)
        {
            PowerControlCapability capability = BuildingPowerControl.Evaluate(building, registry);
            if (capability == PowerControlCapability.UnknownConnection) return PowerControlSupport.Pending;
            if (capability != PowerControlCapability.Controllable) return PowerControlSupport.Unsupported;
            return building.IsCustomFlickable
                ? PowerControlSupport.ModdedImplementation : PowerControlSupport.Supported;
        }

        public static string ControlLabelKey(PowerControlSupport support)
        {
            switch (support)
            {
                case PowerControlSupport.Supported: return "PGM_Mgmt_Cap_Controllable";
                case PowerControlSupport.ModdedImplementation: return "PGM_Mgmt_Cap_Modded";
                case PowerControlSupport.Pending: return "PGM_Mgmt_Cap_UnknownConnection";
                default: return "PGM_Mgmt_Cap_UnsupportedControl";
            }
        }

        public static string ControlTooltipKey(PowerControlSupport support)
        {
            switch (support)
            {
                case PowerControlSupport.Supported: return "PGM_Mgmt_Cap_ControllableTip";
                case PowerControlSupport.ModdedImplementation: return "PGM_Mgmt_Cap_ModdedTip";
                case PowerControlSupport.Pending: return "PGM_Mgmt_Cap_UnknownConnectionTip";
                default: return "PGM_Mgmt_Cap_UnsupportedControlTip";
            }
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
