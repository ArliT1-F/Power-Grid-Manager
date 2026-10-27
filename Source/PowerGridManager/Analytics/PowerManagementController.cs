using System.Collections.Generic;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Map-scoped owner of the optional management layer: preferences, event history and the
    /// load-shedding hysteresis state. It is ticked once per snapshot refresh, never per frame.
    ///
    /// All three pieces are persisted as primitives. Monitoring does not depend on this class:
    /// with management disabled the controller only maintains its (empty) registry.
    /// </summary>
    public sealed class PowerManagementController : IExposable
    {
        private PowerManagementRegistry registry = new PowerManagementRegistry();
        private PowerManagementEventLog events = new PowerManagementEventLog();
        private LoadSheddingState shedding = new LoadSheddingState();
        private LoadSheddingPlan lastPlan;

        // Emergency override is runtime-only state: it suspends automation without persisting
        // a "suspended" flag into a save, so a reloaded colony resumes under its configured
        // policy rather than silently staying suspended.
        private bool emergencyOverrideActive;
        private int emergencyOverrideSinceTick = -1;

        // The UI asks for a preview every frame. Cache it against the snapshot, settings and a
        // revision counter so a large colony is never re-scanned from the draw pass.
        private PowerGridSnapshot cachedPreviewSnapshot;
        private LoadSheddingPlan cachedPreview;
        private int cachedPreviewSettingsKey;
        private int cachedPreviewRevision = -1;
        private int revision;

        /// <summary>Incremented whenever management state changes; used to cache UI rows.</summary>
        public int Revision => revision;

        public PowerManagementRegistry Registry => registry;
        public PowerManagementEventLog Events => events;
        public LoadSheddingState Shedding => shedding;

        /// <summary>Decision from the latest refresh. Null before the first one or when disabled.</summary>
        public LoadSheddingPlan LastPlan => lastPlan;

        /// <summary>Whether any building is currently switched off by the policy.</summary>
        public int AutoShedCount => shedding == null ? 0 : shedding.AutoShedCount;

        /// <summary>Whether the emergency override is currently suspending automation.</summary>
        public bool EmergencyOverrideActive => emergencyOverrideActive;

        /// <summary>Tick the emergency override was activated, or -1 when inactive.</summary>
        public int EmergencyOverrideSinceTick => emergencyOverrideSinceTick;

        /// <summary>
        /// Overall automation status for the status indicator:
        /// <see cref="AutomationStatus.Unavailable"/> when management is disabled,
        /// <see cref="AutomationStatus.Off"/> when automation is disabled,
        /// <see cref="AutomationStatus.Suspended"/> when the emergency override is active,
        /// <see cref="AutomationStatus.Enabled"/> when automation is running normally.
        /// </summary>
        public enum AutomationStatus
        {
            Unavailable = 0,
            Off,
            Enabled,
            Suspended
        }

        /// <summary>Current automation status for the given settings.</summary>
        public AutomationStatus Status(PowerGridManagerSettings settings)
        {
            if (settings == null || !settings.ManagementEnabled) return AutomationStatus.Unavailable;
            if (!settings.LoadSheddingEnabled) return AutomationStatus.Off;
            if (emergencyOverrideActive) return AutomationStatus.Suspended;
            return AutomationStatus.Enabled;
        }

        /// <summary>
        /// Player-requested emergency override. Immediately suspends automatic switching
        /// without changing any switch the policy already flipped. Buildings the policy
        /// switched off stay off until the player uses "Restore now" or clears the override.
        /// This does not alter RimWorld's ordinary electrical simulation.
        /// </summary>
        public void ActivateEmergencyOverride(int tick)
        {
            revision++;
            if (emergencyOverrideActive) return;
            emergencyOverrideActive = true;
            emergencyOverrideSinceTick = tick;
            lastPlan = null;
            if (events != null) events.Add(tick, PowerManagementEventKind.PolicyChanged, "",
                "PGM_Mgmt_Event_EmergencyOverride".Translate(), true,
                "PGM_Mgmt_Event_EmergencyOn".Translate());
        }

        /// <summary>Clears the emergency override so normal automation can resume.</summary>
        public void ClearEmergencyOverride(int tick)
        {
            revision++;
            if (!emergencyOverrideActive) return;
            emergencyOverrideActive = false;
            emergencyOverrideSinceTick = -1;
            if (events != null) events.Add(tick, PowerManagementEventKind.PolicyChanged, "",
                "PGM_Mgmt_Event_EmergencyOverride".Translate(), true,
                "PGM_Mgmt_Event_EmergencyOff".Translate());
        }

        /// <summary>
        /// Called from the map component after a snapshot is published. Disabling automation
        /// restores the buildings we switched off, so turning the feature off is not a trap.
        /// The emergency override suspends switching without restoring, so an override is not
        /// itself disruptive to loads the policy already shed.
        /// </summary>
        public void Tick(PowerGridSnapshot snapshot, PowerGridManagerSettings settings, int tick)
        {
            if (snapshot == null) return;
            revision++;
            if (registry == null) registry = new PowerManagementRegistry();
            if (events == null) events = new PowerManagementEventLog();
            if (shedding == null) shedding = new LoadSheddingState();

            registry.MarkSeen(snapshot, tick);
            registry.Prune(tick);

            bool automationOn = settings != null && settings.ManagementEnabled && settings.LoadSheddingEnabled;
            if (!automationOn)
            {
                if (shedding.AutoShedCount > 0)
                    LoadSheddingEngine.RestoreAll(snapshot, registry, shedding, events, tick);
                lastPlan = null;
                return;
            }

            if (emergencyOverrideActive)
            {
                // Suspend automatic switching without touching any switch.
                lastPlan = null;
                return;
            }

            // Optional preset auto-activation: a charge-based preset may switch the active
            // policy. This changes settings, not switches.
            float charge = LoadSheddingEngine.EffectiveCharge(snapshot);
            if (charge >= 0f)
            {
                var presets = PowerPresetManager.LoadPresets(settings);
                string activeName = null;
                PowerPreset active = null;
                for (int i = 0; i < presets.Count; i++)
                {
                    if (presets[i].IsActive)
                    {
                        active = presets[i];
                        activeName = active.Name;
                    }
                }
                PowerPreset candidate = PowerPresetManager.CheckAutoActivation(presets, charge);
                if (candidate != null && candidate.Name != activeName)
                {
                    PowerPresetManager.Activate(settings, candidate, tick);
                    presets = PowerPresetManager.LoadPresets(settings);
                    active = null;
                    for (int i = 0; i < presets.Count; i++)
                        if (presets[i].IsActive) active = presets[i];
                }
                if (active != null && candidate == null && active.RestoreAboveCharge >= 0f &&
                    charge >= active.RestoreAboveCharge)
                {
                    PowerPresetManager.Deactivate(settings);
                }
            }

            LoadSheddingPlan plan = LoadSheddingEngine.Evaluate(snapshot, settings, registry, shedding, tick, false);
            LoadSheddingEngine.Commit(plan, shedding, tick);
            lastPlan = plan;
            bool needsCleanup = plan.MissingTrackedIds.Count > 0 || plan.AlreadyOnIds.Count > 0;
            if (plan.Ready && (plan.ActionCount > 0 || needsCleanup))
                LoadSheddingEngine.Apply(plan, shedding, registry, events, tick);
        }

        /// <summary>
        /// Read-only "what would be affected" view for the UI. It never mutates state and never
        /// touches a switch, so it is safe to request every frame the tab is open.
        /// </summary>
        public LoadSheddingPlan Preview(PowerGridSnapshot snapshot, PowerGridManagerSettings settings, int tick)
        {
            int key = SettingsKey(settings);
            if (cachedPreview != null && ReferenceEquals(cachedPreviewSnapshot, snapshot) &&
                cachedPreviewRevision == revision && cachedPreviewSettingsKey == key)
                return cachedPreview;
            cachedPreviewSnapshot = snapshot;
            cachedPreviewRevision = revision;
            cachedPreviewSettingsKey = key;
            cachedPreview = LoadSheddingEngine.Evaluate(snapshot, settings, registry, shedding, tick, true);
            return cachedPreview;
        }

        /// <summary>Forces the cached preview to be rebuilt after a settings change.</summary>
        public void InvalidatePreview()
        {
            revision++;
        }

        private static int SettingsKey(PowerGridManagerSettings settings)
        {
            if (settings == null) return 0;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (settings.ManagementEnabled ? 1 : 0);
                hash = hash * 31 + (settings.LoadSheddingEnabled ? 1 : 0);
                hash = hash * 31 + (settings.LoadShedRequireDeficit ? 1 : 0);
                hash = hash * 31 + settings.LoadShedLowThreshold.GetHashCode();
                hash = hash * 31 + settings.LoadShedRestoreThreshold.GetHashCode();
                hash = hash * 31 + settings.LoadShedStableTicks;
                hash = hash * 31 + settings.LoadShedCooldownTicks;
                hash = hash * 31 + settings.LoadShedMaxActionsPerPass;
                hash = hash * 31 + (int)settings.LoadShedRecovery;
                return hash;
            }
        }

        /// <summary>Explicit player action: switch one compatible building on or off.</summary>
        public PowerControlResult ManualSwitch(PowerGridSnapshot snapshot, Building building, bool on,
            PowerGridManagerSettings settings, int tick, out string detail)
        {
            detail = null;
            revision++;
            if (settings == null || !settings.ManagementEnabled)
            {
                detail = "PGM_Mgmt_Result_PolicyOff".Translate().ToString();
                return PowerControlResult.ControlFailed;
            }
            if (building == null || building.Destroyed || !building.Spawned)
                return PowerControlResult.NotSpawned;

            BuildingPowerSnapshot row = FindRow(snapshot, building);
            PowerControlCapability capability = BuildingPowerControl.Evaluate(row, registry);
            if (capability != PowerControlCapability.Controllable)
            {
                detail = BuildingPowerControl.CapabilityLabelKey(capability).Translate().ToString();
                Record(tick, PowerManagementEventKind.Refused, building, false, detail);
                return PowerControlResult.ControlFailed;
            }

            PowerControlResult result = BuildingPowerControl.SetSwitch(building, on, out detail);
            bool success = result == PowerControlResult.Success || result == PowerControlResult.AlreadyInState;
            Record(tick, on ? PowerManagementEventKind.ManualOn : PowerManagementEventKind.ManualOff,
                building, success, success ? BuildingPowerControl.ResultLabelKey(result).Translate().ToString()
                                           : BuildingPowerControl.ResultLabelKey(result).Translate().ToString() +
                                             (detail ?? ""));
            if (success && on) shedding.Untrack(PowerManagementRegistry.StableId(building));
            return result;
        }

        /// <summary>Player-triggered restoration. Works even when automatic recovery is Manual.</summary>
        public int RestoreNow(PowerGridSnapshot snapshot, int tick)
        {
            revision++;
            return LoadSheddingEngine.RestoreAll(snapshot, registry, shedding, events, tick);
        }

        /// <summary>
        /// Changes both master switches with one auditable action, saves settings, and restores
        /// tracked buildings when automation is switched off.
        /// </summary>
        public void SetPolicy(PowerGridManagerSettings settings, bool management, bool automation,
            PowerGridSnapshot snapshot, int tick)
        {
            if (settings == null) return;
            revision++;
            bool before = settings.ManagementEnabled && settings.LoadSheddingEnabled;
            settings.ManagementEnabled = management;
            settings.LoadSheddingEnabled = management && automation;
            settings.NormalizeManagement();
            bool after = settings.ManagementEnabled && settings.LoadSheddingEnabled;
            PowerGridManagerMod.Save();

            if (events != null) events.Add(tick, PowerManagementEventKind.PolicyChanged, "",
                "PGM_Mgmt_Event_Policy".Translate(), true,
                after ? "PGM_Mgmt_Event_PolicyOn".Translate() : "PGM_Mgmt_Event_PolicyOff".Translate());
            if (before && !after && shedding != null && shedding.AutoShedCount > 0)
                LoadSheddingEngine.RestoreAll(snapshot, registry, shedding, events, tick);
            if (!after) lastPlan = null;
        }

        /// <summary>Explicit group operations, all audited and all invalidating the UI cache.</summary>
        public bool CreateGroup(string name, int tick)
        {
            if (registry == null || !registry.AddGroup(name)) return false;
            revision++;
            if (events != null) events.Add(tick, PowerManagementEventKind.GroupChanged, "", name,
                true, "PGM_Mgmt_Event_GroupCreated".Translate());
            return true;
        }

        public bool DeleteGroup(string name, int tick)
        {
            if (registry == null || !registry.RemoveGroup(name)) return false;
            revision++;
            if (events != null) events.Add(tick, PowerManagementEventKind.GroupChanged, "", name,
                true, "PGM_Mgmt_Event_GroupDeleted".Translate());
            return true;
        }

        public int SetGroupSheddable(string name, bool value, int tick)
        {
            if (registry == null) return 0;
            int changed = registry.SetGroupSheddable(name, value);
            revision++;
            if (events != null) events.Add(tick, PowerManagementEventKind.PolicyChanged, "", name,
                true, value ? "PGM_Mgmt_Event_MarkedSheddable".Translate()
                            : "PGM_Mgmt_Event_UnmarkedSheddable".Translate());
            return changed;
        }

        public int SetGroupPriority(string name, int priority, int tick)
        {
            if (registry == null) return 0;
            registry.SetGroupPriority(name, priority);
            int changed = registry.ApplyGroupPriority(name, priority);
            revision++;
            if (events != null) events.Add(tick, PowerManagementEventKind.PriorityChanged, "", name,
                true, priority.ToString());
            return changed;
        }

        public void SetGroup(Building building, string group, int tick)
        {
            if (registry == null || building == null) return;
            revision++;
            registry.SetGroup(building, group, tick);
            Record(tick, PowerManagementEventKind.GroupChanged, building, true, group ?? "");
        }

        public void SetPriority(Building building, int priority, int tick)
        {
            if (registry == null || building == null) return;
            revision++;
            registry.SetPriority(building, priority, tick);
            Record(tick, PowerManagementEventKind.PriorityChanged, building, true,
                registry.PriorityOf(building).ToString());
        }

        public void SetSheddable(Building building, bool value, int tick)
        {
            if (registry == null || building == null) return;
            revision++;
            registry.SetSheddable(building, value, tick);
            Record(tick, PowerManagementEventKind.PolicyChanged, building, true,
                value ? "PGM_Mgmt_Event_MarkedSheddable".Translate()
                      : "PGM_Mgmt_Event_UnmarkedSheddable".Translate());
        }

        public void SetExcludedFromManagement(Building building, bool value, int tick)
        {
            if (registry == null || building == null) return;
            revision++;
            registry.SetExcludedFromManagement(building, value, tick);
            Record(tick, PowerManagementEventKind.PolicyChanged, building, true,
                value ? "PGM_Mgmt_Event_ExcludedManagement".Translate()
                      : "PGM_Mgmt_Event_AllowedManagement".Translate());
        }

        public void SetExcludedFromDiagnostics(Building building, bool value, int tick)
        {
            if (registry == null || building == null) return;
            revision++;
            registry.SetExcludedFromDiagnostics(building, value, tick);
            Record(tick, PowerManagementEventKind.PolicyChanged, building, true,
                value ? "PGM_Mgmt_Event_ExcludedDiagnostics".Translate()
                      : "PGM_Mgmt_Event_AllowedDiagnostics".Translate());
        }

        private void Record(int tick, PowerManagementEventKind kind, Building building, bool success, string detail)
        {
            if (events == null) return;
            events.Add(tick, kind, PowerManagementRegistry.StableId(building),
                PowerGridUtilities.BuildingLabel(building), success, detail);
        }

        private static BuildingPowerSnapshot FindRow(PowerGridSnapshot snapshot, Building building)
        {
            if (snapshot == null || snapshot.Buildings == null) return null;
            IReadOnlyList<BuildingPowerSnapshot> rows = snapshot.Buildings;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] != null && rows[i].Building == building) return rows[i];
            return null;
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref registry, "registry");
            Scribe_Deep.Look(ref events, "eventLog");
            Scribe_Deep.Look(ref shedding, "loadShedding");

            // Scribe_Deep itself does nothing in PostLoadInit (PostLoadInitter re-enters
            // registered objects). Defaults and cache clearing also happen while loading values
            // so they never depend on that registration detail.
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (registry == null) registry = new PowerManagementRegistry();
                if (events == null) events = new PowerManagementEventLog();
                if (shedding == null) shedding = new LoadSheddingState();
                lastPlan = null;
                cachedPreview = null;
                emergencyOverrideActive = false;
                emergencyOverrideSinceTick = -1;
                revision++;
            }
        }

        /// <summary>Drops runtime state when the map is removed. The map's things are gone too.</summary>
        public void Reset()
        {
            revision++;
            cachedPreview = null;
            emergencyOverrideActive = false;
            emergencyOverrideSinceTick = -1;
            if (registry != null) registry.Clear();
            if (events != null) events.Clear();
            if (shedding != null) shedding.Reset();
            lastPlan = null;
        }
    }
}
