using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PowerGridManager
{
    /// <summary>Why a building cannot be controlled through this mod.</summary>
    public enum PowerControlCapability
    {
        Controllable = 0,
        MissingBuilding,
        NotFlickable,
        UnsupportedComponents,
        ExcludedByUser,
        NotColonistBuilding,
        UnknownConnection
    }

    /// <summary>Outcome of a single attempt to change a flick switch.</summary>
    public enum PowerControlResult
    {
        Success = 0,
        AlreadyInState,
        NotSpawned,
        NoFlickable,
        PendingFlick,
        NoSupportedToggle,
        AmbiguousToggle,
        ControlFailed,
        VerificationFailed
    }

    /// <summary>
    /// The only place in the mod that writes to a building's power state.
    ///
    /// Vanilla <c>CompFlickable</c> keeps two flags: the switch itself
    /// (<c>SwitchIsOn</c>) and the player's desired state (<c>wantSwitchOn</c>, private).
    /// Setting only the switch would leave the gizmo showing the wrong state and could make
    /// a colonist flick it straight back. The public toggle gizmo's <c>toggleAction</c> is
    /// therefore used to synchronise the desired state first; then the public switch setter
    /// applies the change immediately (it broadcasts the same signals as <c>DoFlick</c> but
    /// without a sound per building), and <c>FlickUtility.UpdateFlickDesignation</c> removes
    /// the designation the gizmo may have created.
    ///
    /// Verified against the public 1.5.4409 and 1.6 decompilations:
    /// CompFlickable.SwitchIsOn (get/set), WantsFlick(), DoFlick(), CompGetGizmosExtra(),
    /// Command_Toggle.isActive/toggleAction, FlickUtility.UpdateFlickDesignation().
    /// A component that does not expose exactly one usable toggle gizmo is refused, never
    /// guessed at. No reflection, no Harmony, no changes to power simulation code.
    /// </summary>
    public static class BuildingPowerControl
    {
        /// <summary>At most this many distinct component types are reported to the log.</summary>
        private const int MaxLoggedFailures = 8;

        private static readonly HashSet<Type> ReportedFailureTypes = new HashSet<Type>();

        /// <summary>
        /// Conservative, allocation-free capability check for the UI. It uses only facts the
        /// scanner already collected; the gizmo contract is verified again at action time.
        /// </summary>
        public static PowerControlCapability Evaluate(BuildingPowerSnapshot snapshot,
            PowerManagementRegistry registry)
        {
            if (snapshot == null || snapshot.Building == null) return PowerControlCapability.MissingBuilding;
            Building building = snapshot.Building;
            if (building.Destroyed || !building.Spawned) return PowerControlCapability.MissingBuilding;
            if (registry != null && registry.IsExcludedFromManagement(building))
                return PowerControlCapability.ExcludedByUser;
            // Report an unrecognised power component first: it is a stronger reason than the
            // absence of a switch and must never be interpreted as a safe control target.
            if (snapshot.HasUnsupportedComponent) return PowerControlCapability.UnsupportedComponents;
            if (!snapshot.IsFlickable || !snapshot.SwitchIsOn.HasValue)
                return PowerControlCapability.NotFlickable;
            if (snapshot.Connection != PowerConnectionState.Connected)
                return PowerControlCapability.UnknownConnection;
            // The vanilla gizmo is only offered for player-faction buildings. The lister flag is
            // the same conservative signal and does not require reaching into faction state.
            if (!snapshot.IsColonistBuilding) return PowerControlCapability.NotColonistBuilding;
            return PowerControlCapability.Controllable;
        }

        public static bool CanControl(BuildingPowerSnapshot snapshot, PowerManagementRegistry registry)
        {
            return Evaluate(snapshot, registry) == PowerControlCapability.Controllable;
        }

        /// <summary>
        /// Applies a desired switch state through supported public APIs. Never throws for
        /// expected incompatibilities; <paramref name="detail"/> carries the reason for the
        /// management event log. Returns Success only after verifying the final state.
        /// </summary>
        public static PowerControlResult SetSwitch(Building building, bool desiredOn, out string detail)
        {
            string failureDetail = null;
            detail = null;
            if (building == null || building.Destroyed || !building.Spawned)
                return PowerControlResult.NotSpawned;

            CompFlickable flick = building.GetComp<CompFlickable>();
            if (flick == null) return PowerControlResult.NoFlickable;

            // A pending player-requested flick wins. Do not fight the player's colonists.
            if (flick.WantsFlick()) return PowerControlResult.PendingFlick;

            Command_Toggle toggle;
            PowerControlResult lookup = FindToggle(flick, out toggle, ref failureDetail);
            if (lookup != PowerControlResult.Success)
            {
                detail = failureDetail;
                return lookup;
            }

            try
            {
                bool changed = false;
                if (toggle.isActive() != desiredOn)
                {
                    toggle.toggleAction();
                    changed = true;
                }
                if (flick.SwitchIsOn != desiredOn)
                {
                    flick.SwitchIsOn = desiredOn;
                    changed = true;
                }

                // Clear any Flick designation the gizmo created; WantsFlick() is now false.
                FlickUtility.UpdateFlickDesignation(building);

                if (flick.SwitchIsOn != desiredOn || toggle.isActive() != desiredOn)
                    return PowerControlResult.VerificationFailed;
                return changed ? PowerControlResult.Success : PowerControlResult.AlreadyInState;
            }
            catch (Exception exception)
            {
                ReportFailure(flick, exception, ref failureDetail);
                detail = failureDetail;
                return PowerControlResult.ControlFailed;
            }
        }

        /// <summary>
        /// Finds the single public toggle gizmo that controls this flick component. More than
        /// one candidate is ambiguous (a mod may have added its own toggles), so it is refused.
        /// </summary>
        private static PowerControlResult FindToggle(CompFlickable flick, out Command_Toggle toggle,
            ref string detail)
        {
            toggle = null;
            IEnumerable<Gizmo> gizmos;
            try
            {
                gizmos = flick.CompGetGizmosExtra();
            }
            catch (Exception exception)
            {
                ReportFailure(flick, exception, ref detail);
                return PowerControlResult.ControlFailed;
            }
            if (gizmos == null) return PowerControlResult.NoSupportedToggle;

            try
            {
                foreach (Gizmo gizmo in gizmos)
                {
                    Command_Toggle candidate = gizmo as Command_Toggle;
                    if (candidate == null || candidate.toggleAction == null || candidate.isActive == null) continue;
                    if (toggle != null) return PowerControlResult.AmbiguousToggle;
                    toggle = candidate;
                }
            }
            catch (Exception exception)
            {
                ReportFailure(flick, exception, ref detail);
                return PowerControlResult.ControlFailed;
            }
            return toggle == null ? PowerControlResult.NoSupportedToggle : PowerControlResult.Success;
        }

        /// <summary>
        /// Expected incompatibilities are reported once per component type, not once per tick.
        /// The management event log still records every individual failure.
        /// </summary>
        private static void ReportFailure(CompFlickable flick, Exception exception, ref string detail)
        {
            detail = exception == null ? "unknown error" : exception.GetType().Name + ": " + exception.Message;
            if (detail != null && detail.Length > 120) detail = detail.Substring(0, 120);
            Type type = flick == null ? typeof(CompFlickable) : flick.GetType();
            if (ReportedFailureTypes.Count >= MaxLoggedFailures || !ReportedFailureTypes.Add(type)) return;
            Log.Warning("[Power Grid Manager] Unsupported flick component " + type.FullName +
                " was not controlled: " + detail + ". The building is left untouched.");
        }

        public static string ResultLabelKey(PowerControlResult result)
        {
            switch (result)
            {
                case PowerControlResult.Success: return "PGM_Mgmt_Result_Success";
                case PowerControlResult.AlreadyInState: return "PGM_Mgmt_Result_Already";
                case PowerControlResult.NotSpawned: return "PGM_Mgmt_Result_Missing";
                case PowerControlResult.NoFlickable: return "PGM_Mgmt_Result_NoFlick";
                case PowerControlResult.PendingFlick: return "PGM_Mgmt_Result_Pending";
                case PowerControlResult.NoSupportedToggle: return "PGM_Mgmt_Result_NoToggle";
                case PowerControlResult.AmbiguousToggle: return "PGM_Mgmt_Result_Ambiguous";
                case PowerControlResult.ControlFailed: return "PGM_Mgmt_Result_Failed";
                default: return "PGM_Mgmt_Result_Verify";
            }
        }

        public static string CapabilityLabelKey(PowerControlCapability capability)
        {
            switch (capability)
            {
                case PowerControlCapability.Controllable: return "PGM_Mgmt_Cap_Controllable";
                case PowerControlCapability.MissingBuilding: return "PGM_Mgmt_Cap_Missing";
                case PowerControlCapability.NotFlickable: return "PGM_Mgmt_Cap_NoFlick";
                case PowerControlCapability.UnsupportedComponents: return "PGM_Mgmt_Cap_Unsupported";
                case PowerControlCapability.ExcludedByUser: return "PGM_Mgmt_Cap_Excluded";
                case PowerControlCapability.NotColonistBuilding: return "PGM_Mgmt_Cap_NotColonist";
                default: return "PGM_Mgmt_Cap_UnknownConnection";
            }
        }
    }
}
