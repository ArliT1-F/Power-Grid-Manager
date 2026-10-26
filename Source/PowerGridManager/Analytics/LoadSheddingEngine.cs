using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Decides when an optional load-shedding policy should act. Evaluation is a read-only
    /// pass over an existing <see cref="PowerGridSnapshot"/>: it never changes a switch, never
    /// scans the map and never calls back into the power simulation. Only
    /// <see cref="Apply"/> writes, and it does so through <see cref="BuildingPowerControl"/>.
    ///
    /// Hysteresis is explicit: a low-charge trigger activates the policy, a higher recovery
    /// charge deactivates it, both conditions must hold for a configurable stable period, and
    /// a cooldown separates batches of actions. Only buildings the player explicitly marked
    /// sheddable are ever switched off.
    /// </summary>
    public static class LoadSheddingEngine
    {
        /// <summary>Hard ceiling on one restore batch, independent of user settings.</summary>
        public const int MaxRestorePerPass = 32;

        private struct Candidate
        {
            public BuildingPowerSnapshot Row;
            public int Priority;
            public float Draw;
            public int Index;

            public Candidate(BuildingPowerSnapshot row, int priority, float draw, int index)
            {
                Row = row;
                Priority = priority;
                Draw = draw;
                Index = index;
            }
        }

        private static readonly List<Candidate> Candidates = new List<Candidate>(64);
        private static readonly List<Candidate> RestoreCandidates = new List<Candidate>(64);
        private static readonly Comparison<Candidate> ShedOrder = CompareForShedding;
        private static readonly Comparison<Candidate> RestoreOrder = CompareForRestoration;

        /// <summary>
        /// Usable charge fraction (available energy / configured capacity), or -1 when there
        /// is no battery capacity. EMP-stunned batteries are excluded from available energy.
        /// </summary>
        public static float EffectiveCharge(PowerGridSnapshot snapshot)
        {
            if (snapshot == null || !(snapshot.CapacityWattDays > 0f)) return -1f;
            float charge = snapshot.AvailableStoredWattDays / snapshot.CapacityWattDays;
            if (float.IsNaN(charge) || float.IsInfinity(charge)) return -1f;
            return Clamp(charge, 0f, 1f);
        }

        public static LoadSheddingPlan Evaluate(PowerGridSnapshot snapshot, PowerGridManagerSettings settings,
            PowerManagementRegistry registry, LoadSheddingState state, int tick, bool preview)
        {
            var plan = new LoadSheddingPlan { IsPreview = preview };
            if (snapshot == null || settings == null)
            {
                plan.ReasonKey = "PGM_Mgmt_Reason_NoData";
                return plan;
            }

            bool enabled = settings.ManagementEnabled && settings.LoadSheddingEnabled;
            plan.PolicyEnabled = enabled;
            if (!enabled && !preview)
            {
                plan.Phase = LoadSheddingPhase.Disabled;
                plan.ReasonKey = "PGM_Mgmt_Reason_Off";
                return plan;
            }

            float low = Clamp(settings.LoadShedLowThreshold, 0.01f, 0.90f);
            float recovery = Clamp(settings.LoadShedRestoreThreshold, low + 0.05f, 1f);
            int stableTicks = ClampInt(settings.LoadShedStableTicks, 0, PowerTime.TicksPerDay * 7);
            int cooldown = ClampInt(settings.LoadShedCooldownTicks, 0, PowerTime.TicksPerDay * 7);

            plan.ChargeFraction = EffectiveCharge(snapshot);
            plan.NetWatts = snapshot.NetWatts;
            plan.DeficitWatts = snapshot.NetWatts < -0.01f ? -snapshot.NetWatts : 0f;

            // A preview always describes a hypothetical fresh activation, even if the policy is
            // currently off or already shedding. It never changes state.
            bool active = !preview && state != null && state.IsShedding;
            bool deficitRequired = settings.LoadShedRequireDeficit;
            bool trigger = plan.ChargeFraction >= 0f && plan.ChargeFraction <= low &&
                           (!deficitRequired || plan.DeficitWatts > 0.01f);
            // Losing battery capacity entirely is treated as a recovery condition, so we never
            // leave buildings switched off with nothing left to restore them against.
            bool recover = plan.ChargeFraction < 0f || plan.ChargeFraction >= recovery;

            // Three distinct stable conditions: first activation (1), shedding more load while
            // still below the recovery charge (2), and recovery (3). Keeping them separate stops
            // a fresh batch from being sent before its own stability window has elapsed.
            bool wantMore = active && trigger && !recover;
            bool conditionsMet = active ? (recover || wantMore) : trigger;
            plan.ConditionsMet = conditionsMet;
            plan.TargetKind = active ? (recover ? 3 : 2) : 1;

            if (state != null && conditionsMet && state.ConditionKind == plan.TargetKind &&
                state.ConditionSinceTick >= 0 && tick >= state.ConditionSinceTick)
            {
                plan.StableTicks = tick - state.ConditionSinceTick;
            }
            plan.Stable = plan.StableTicks >= stableTicks;
            plan.CooldownReady = state == null || state.LastActionTick < 0 ||
                                 tick - state.LastActionTick >= cooldown;
            plan.Ready = !preview && enabled && conditionsMet && plan.Stable && plan.CooldownReady;

            if (active) plan.Phase = recover ? LoadSheddingPhase.Restoring : LoadSheddingPhase.Shedding;
            else plan.Phase = trigger ? (plan.Stable ? LoadSheddingPhase.Shedding : LoadSheddingPhase.Watching)
                                      : LoadSheddingPhase.Idle;

            CollectCandidates(snapshot, registry, state, plan, preview);

            if (preview)
            {
                // "What would be affected" lists every opted-in consumer, not just the subset
                // needed to cover the current deficit. No switch is changed by a preview.
                for (int i = 0; i < Candidates.Count; i++) plan.ToShed.Add(Candidates[i].Row);
                plan.CandidateCount = Candidates.Count;
                plan.ReasonKey = plan.ChargeFraction < 0f
                    ? "PGM_Mgmt_Reason_NoBatteries"
                    : "PGM_Mgmt_Reason_Preview";
                return plan;
            }

            if (active && recover) BuildRestoreTargets(snapshot, registry, state, settings, plan);
            else if (trigger) BuildShedTargets(settings, plan);
            // While shedding and the recovery charge has not been reached, additional eligible
            // consumers may still be shed if the shortfall persists. Stability and cooldown
            // separate those batches from the first one.

            plan.ReasonKey = Explain(plan);
            return plan;
        }

        private static string Explain(LoadSheddingPlan plan)
        {
            if (plan.ChargeFraction < 0f) return "PGM_Mgmt_Reason_NoBatteries";
            if (!plan.ConditionsMet)
                return plan.Phase == LoadSheddingPhase.Shedding
                    ? "PGM_Mgmt_Reason_Holding" : "PGM_Mgmt_Reason_ConditionsNotMet";
            if (!plan.Stable) return "PGM_Mgmt_Reason_Stabilising";
            if (!plan.CooldownReady) return "PGM_Mgmt_Reason_Cooldown";
            if (plan.ActionCount == 0) return "PGM_Mgmt_Reason_NoCandidates";
            return "PGM_Mgmt_Reason_Ready";
        }

        /// <summary>
        /// Collects opted-in, controllable, switched-on consumers, ordered by the player's
        /// priority (higher number = shed first) and then by measured draw. Generators,
        /// batteries, unconfirmed connections and unsupported components are never candidates.
        /// </summary>
        private static void CollectCandidates(PowerGridSnapshot snapshot, PowerManagementRegistry registry,
            LoadSheddingState state, LoadSheddingPlan plan, bool preview)
        {
            Candidates.Clear();
            plan.CandidateCount = 0;
            plan.ProjectedSavingsWatts = 0f;
            // No configured rows means no eligible building; skip the per-building ID work on
            // large maps. This is the whole reason management stays opt-in.
            if (registry == null || registry.Count == 0 || snapshot.Buildings == null) return;

            IReadOnlyList<BuildingPowerSnapshot> buildings = snapshot.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingPowerSnapshot row = buildings[i];
                if (row == null || row.Building == null || row.Building.Destroyed || !row.Building.Spawned) continue;
                if (!row.IsConsumer || row.IsGenerator || row.IsBattery) continue;
                if (row.Connection != PowerConnectionState.Connected) continue;
                // A preview must show every opted-in building, including ones already off; a
                // runtime pass reads the live switch so a player's manual flick is never
                // mistaken for a building the policy switched off.
                if (!preview && !LiveSwitchOn(row)) continue;
                if (!BuildingPowerControl.CanControl(row, registry)) continue;
                if (!registry.IsSheddable(row.Building)) continue;

                string id = PowerManagementRegistry.StableId(row.Building);
                if (id == null || (!preview && state != null && state.IsTracked(id))) continue;

                float draw = row.ConsumptionWatts > 0f ? row.ConsumptionWatts : 0f;
                Candidates.Add(new Candidate(row, registry.PriorityOf(row.Building), draw, i));
            }
            Candidates.Sort(ShedOrder);
            plan.CandidateCount = Candidates.Count;
            for (int i = 0; i < Candidates.Count; i++) plan.ProjectedSavingsWatts += Candidates[i].Draw;
        }

        /// <summary>
        /// Selects at most MaxActionsPerPass switches and stops once measured savings cover
        /// the observed deficit. Savings are measured current draw, not an extrapolation.
        /// </summary>
        private static void BuildShedTargets(PowerGridManagerSettings settings, LoadSheddingPlan plan)
        {
            int limit = ClampInt(settings.LoadShedMaxActionsPerPass, 1, 32);
            float savings = 0f;
            for (int i = 0; i < Candidates.Count && plan.ToShed.Count < limit; i++)
            {
                Candidate candidate = Candidates[i];
                if (candidate.Draw <= 0.01f) continue;
                if (plan.ToShed.Count > 0 && savings >= plan.DeficitWatts - 0.01f) break;
                plan.ToShed.Add(candidate.Row);
                savings += candidate.Draw;
            }
            plan.ProjectedSavingsWatts = savings;
        }

        private static void BuildRestoreTargets(PowerGridSnapshot snapshot, PowerManagementRegistry registry,
            LoadSheddingState state, PowerGridManagerSettings settings, LoadSheddingPlan plan)
        {
            RestoreCandidates.Clear();
            if (state == null || snapshot.Buildings == null) return;

            var live = new Dictionary<string, int>(state.AutoShedCount + 4);
            IReadOnlyList<BuildingPowerSnapshot> buildings = snapshot.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i] == null || buildings[i].Building == null) continue;
                string id = PowerManagementRegistry.StableId(buildings[i].Building);
                if (id != null && !live.ContainsKey(id)) live[id] = i;
            }

            IReadOnlyList<string> tracked = state.TrackedIds;
            for (int i = 0; i < tracked.Count; i++)
            {
                string id = tracked[i];
                int index;
                if (!live.TryGetValue(id, out index))
                {
                    plan.MissingTrackedIds.Add(id);
                    continue;
                }
                BuildingPowerSnapshot row = buildings[index];
                if (LiveSwitchOn(row))
                {
                    // The player already switched it back on. Do not claim the action.
                    plan.AlreadyOnIds.Add(id);
                    continue;
                }
                int priority = registry == null
                    ? PowerManagementRegistry.DefaultPriority : registry.PriorityOf(row.Building);
                RestoreCandidates.Add(new Candidate(row, priority, row.ConsumptionWatts, index));
            }
            RestoreCandidates.Sort(RestoreOrder);

            int limit;
            switch (settings.LoadShedRecovery)
            {
                case LoadSheddingRecoveryMode.All: limit = MaxRestorePerPass; break;
                case LoadSheddingRecoveryMode.Manual: limit = 0; break;
                default: limit = ClampInt(settings.LoadShedMaxActionsPerPass, 1, 32); break;
            }
            for (int i = 0; i < RestoreCandidates.Count && plan.ToRestore.Count < limit; i++)
                plan.ToRestore.Add(RestoreCandidates[i].Row);
        }

        /// <summary>
        /// Records how long the current trigger/recovery condition has held. Called once per
        /// evaluation by the controller, never from a preview and never per frame.
        /// </summary>
        public static void Commit(LoadSheddingPlan plan, LoadSheddingState state, int tick)
        {
            if (plan == null || state == null || plan.IsPreview) return;
            int kind = plan.ConditionsMet ? plan.TargetKind : 0;
            if (kind == 0)
            {
                state.ConditionKind = 0;
                state.ConditionSinceTick = -1;
                return;
            }
            if (state.ConditionKind != kind || state.ConditionSinceTick < 0 || tick < state.ConditionSinceTick)
            {
                state.ConditionKind = kind;
                state.ConditionSinceTick = tick;
            }
        }

        /// <summary>
        /// Executes a ready plan. Each switch change is attempted through the public flick API
        /// and recorded, including refusals. Tracking is updated only after a verified change.
        /// </summary>
        public static int Apply(LoadSheddingPlan plan, LoadSheddingState state,
            PowerManagementRegistry registry, PowerManagementEventLog events, int tick)
        {
            if (plan == null || plan.IsPreview || state == null || !plan.Ready) return 0;
            int changed = 0;

            for (int i = 0; i < plan.MissingTrackedIds.Count; i++)
            {
                string id = plan.MissingTrackedIds[i];
                state.Untrack(id);
                if (events != null) events.Add(tick, PowerManagementEventKind.TrackedCleared, id,
                    "PGM_Mgmt_Event_MissingBuilding".Translate(), true, null);
            }
            for (int i = 0; i < plan.AlreadyOnIds.Count; i++)
            {
                string id = plan.AlreadyOnIds[i];
                state.Untrack(id);
                if (events != null) events.Add(tick, PowerManagementEventKind.TrackedCleared, id,
                    "PGM_Mgmt_Event_AlreadyOn".Translate(), true, null);
            }

            for (int i = 0; i < plan.ToShed.Count; i++)
            {
                BuildingPowerSnapshot row = plan.ToShed[i];
                string id = PowerManagementRegistry.StableId(row.Building);
                string detail;
                PowerControlResult result = BuildingPowerControl.SetSwitch(row.Building, false, out detail);
                if (result == PowerControlResult.Success)
                {
                    state.Track(id);
                    changed++;
                    if (events != null) events.Add(tick, PowerManagementEventKind.AutomaticOff, id,
                        Label(row), true, "PGM_Mgmt_Event_ShedReason".Translate());
                }
                else if (events != null) events.Add(tick, PowerManagementEventKind.Refused, id, Label(row),
                    false, BuildingPowerControl.ResultLabelKey(result).Translate().ToString() + (detail ?? ""));
            }

            for (int i = 0; i < plan.ToRestore.Count; i++)
            {
                BuildingPowerSnapshot row = plan.ToRestore[i];
                string id = PowerManagementRegistry.StableId(row.Building);
                string detail;
                PowerControlResult result = BuildingPowerControl.SetSwitch(row.Building, true, out detail);
                bool success = result == PowerControlResult.Success || result == PowerControlResult.AlreadyInState;
                if (success)
                {
                    state.Untrack(id);
                    changed++;
                    if (events != null) events.Add(tick, PowerManagementEventKind.AutomaticOn, id,
                        Label(row), true, "PGM_Mgmt_Event_RestoreReason".Translate());
                }
                else if (events != null) events.Add(tick, PowerManagementEventKind.Refused, id, Label(row),
                    false, BuildingPowerControl.ResultLabelKey(result).Translate().ToString() + (detail ?? ""));
            }

            if (changed > 0) state.LastActionTick = tick;
            state.IsShedding = state.AutoShedCount > 0;
            return changed;
        }

        /// <summary>
        /// Restores everything this mod switched off, ignoring the recovery mode. Used when
        /// automation is disabled and by the explicit "Restore now" button. Destroyed or
        /// despawned buildings are simply removed from tracking.
        /// </summary>
        public static int RestoreAll(PowerGridSnapshot snapshot, PowerManagementRegistry registry,
            LoadSheddingState state, PowerManagementEventLog events, int tick,
            PowerManagementEventKind kind = PowerManagementEventKind.RestoreAll)
        {
            if (state == null || state.AutoShedCount == 0) return 0;

            var live = new Dictionary<string, BuildingPowerSnapshot>(state.AutoShedCount + 4);
            if (snapshot != null && snapshot.Buildings != null)
            {
                IReadOnlyList<BuildingPowerSnapshot> buildings = snapshot.Buildings;
                for (int i = 0; i < buildings.Count; i++)
                {
                    if (buildings[i] == null || buildings[i].Building == null) continue;
                    string id = PowerManagementRegistry.StableId(buildings[i].Building);
                    if (id != null && !live.ContainsKey(id)) live[id] = buildings[i];
                }
            }

            var ids = new List<string>(state.TrackedIds);
            int changed = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                BuildingPowerSnapshot row;
                if (!live.TryGetValue(id, out row))
                {
                    state.Untrack(id);
                    if (events != null) events.Add(tick, PowerManagementEventKind.TrackedCleared, id,
                        "PGM_Mgmt_Event_MissingBuilding".Translate(), true, null);
                    continue;
                }
                if (LiveSwitchOn(row))
                {
                    state.Untrack(id);
                    continue;
                }

                string detail;
                PowerControlResult result = BuildingPowerControl.SetSwitch(row.Building, true, out detail);
                bool success = result == PowerControlResult.Success || result == PowerControlResult.AlreadyInState;
                if (success)
                {
                    state.Untrack(id);
                    changed++;
                    if (events != null) events.Add(tick, kind, id, Label(row), true,
                        "PGM_Mgmt_Event_ManualRestore".Translate());
                }
                else if (events != null) events.Add(tick, PowerManagementEventKind.Refused, id, Label(row),
                    false, BuildingPowerControl.ResultLabelKey(result).Translate().ToString() + (detail ?? ""));
            }

            if (changed > 0) state.LastActionTick = tick;
            state.IsShedding = state.AutoShedCount > 0;
            return changed;
        }

        /// <summary>Reads the current switch from the live component, not a cached snapshot.</summary>
        private static bool LiveSwitchOn(BuildingPowerSnapshot row)
        {
            if (row == null || row.Building == null) return false;
            CompFlickable flick = row.Building.GetComp<CompFlickable>();
            return flick != null && flick.SwitchIsOn;
        }

        private static string Label(BuildingPowerSnapshot row)
        {
            return row == null ? "?" : PowerGridUtilities.BuildingLabel(row.Building);
        }

        private static int CompareForShedding(Candidate left, Candidate right)
        {
            int priority = right.Priority.CompareTo(left.Priority);
            if (priority != 0) return priority;
            int draw = right.Draw.CompareTo(left.Draw);
            if (draw != 0) return draw;
            return left.Index.CompareTo(right.Index);
        }

        private static int CompareForRestoration(Candidate left, Candidate right)
        {
            // Priority 1 is the most important, so it is restored first.
            int priority = left.Priority.CompareTo(right.Priority);
            if (priority != 0) return priority;
            return left.Index.CompareTo(right.Index);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }
    }
}
