using System.Collections.Generic;
using Verse;

namespace PowerGridManager
{
    /// <summary>How buildings switched off by the policy are brought back.</summary>
    public enum LoadSheddingRecoveryMode
    {
        /// <summary>Restore a few at a time, most important first.</summary>
        Staged = 0,
        /// <summary>Restore every tracked building in one pass (still capped for safety).</summary>
        All = 1,
        /// <summary>Never restore automatically; the player uses "Restore now".</summary>
        Manual = 2
    }

    public enum LoadSheddingPhase
    {
        Disabled = 0,
        Idle,
        Watching,
        Shedding,
        Restoring
    }

    /// <summary>
    /// Persistent hysteresis state for one map. Only primitive values and the stable Thing IDs
    /// of buildings this mod switched off are stored; no game object references.
    /// </summary>
    public sealed class LoadSheddingState : IExposable
    {
        public const int MaxTracked = 256;

        public bool IsShedding;
        public int LastActionTick = -1;
        public int ConditionSinceTick = -1;
        public int ConditionKind;

        private List<string> autoShed = new List<string>(16);
        private readonly HashSet<string> autoShedSet = new HashSet<string>();

        /// <summary>
        /// Buildings this mod switched off and still intends to restore. Exposes the live list
        /// itself (as a read-only interface) because Scribe replaces the list on load.
        /// </summary>
        public IReadOnlyList<string> TrackedIds => autoShed;
        public int AutoShedCount => autoShed.Count;

        public bool IsTracked(string id)
        {
            return !string.IsNullOrEmpty(id) && autoShedSet.Contains(id);
        }

        public void Track(string id)
        {
            if (string.IsNullOrEmpty(id) || autoShed.Count >= MaxTracked || !autoShedSet.Add(id)) return;
            autoShed.Add(id);
        }

        public void Untrack(string id)
        {
            if (string.IsNullOrEmpty(id) || !autoShedSet.Remove(id)) return;
            for (int i = autoShed.Count - 1; i >= 0; i--)
                if (autoShed[i] == id) autoShed.RemoveAt(i);
        }

        public void ClearTracking()
        {
            autoShed.Clear();
            autoShedSet.Clear();
        }

        public void Reset()
        {
            IsShedding = false;
            LastActionTick = -1;
            ConditionSinceTick = -1;
            ConditionKind = 0;
            ClearTracking();
        }

        /// <summary>Rebuilds the runtime lookup after the serialized list was replaced.</summary>
        private void RebuildSet()
        {
            autoShedSet.Clear();
            if (autoShed == null) autoShed = new List<string>(16);
            for (int i = autoShed.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrEmpty(autoShed[i]) || !autoShedSet.Add(autoShed[i]))
                    autoShed.RemoveAt(i);
            }
            while (autoShed.Count > MaxTracked)
            {
                autoShedSet.Remove(autoShed[autoShed.Count - 1]);
                autoShed.RemoveAt(autoShed.Count - 1);
            }
        }

        private void ClampLoaded()
        {
            if (ConditionKind < 0 || ConditionKind > 3) ConditionKind = 0;
            if (ConditionSinceTick < -1) ConditionSinceTick = -1;
            if (LastActionTick < -1) LastActionTick = -1;
            if (autoShed.Count == 0) IsShedding = false;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref IsShedding, "isShedding", false);
            Scribe_Values.Look(ref LastActionTick, "lastActionTick", -1);
            Scribe_Values.Look(ref ConditionSinceTick, "conditionSinceTick", -1);
            Scribe_Values.Look(ref ConditionKind, "conditionKind", 0);
            Scribe_Collections.Look(ref autoShed, "autoShed", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                RebuildSet();
                ClampLoaded();
            }
            else if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RebuildSet();
                ClampLoaded();
            }
        }
    }

    /// <summary>
    /// A decision produced without touching the game. The UI renders it, and only
    /// <see cref="LoadSheddingEngine.Apply"/> performs switch changes.
    /// </summary>
    public sealed class LoadSheddingPlan
    {
        public LoadSheddingPhase Phase;
        public bool PolicyEnabled;
        public bool IsPreview;
        public bool ConditionsMet;
        public bool Stable;
        public bool CooldownReady;
        public bool Ready;
        public int TargetKind;
        public int StableTicks;

        /// <summary>Effective usable charge 0..1, or -1 when no battery capacity is known.</summary>
        public float ChargeFraction = -1f;
        public float NetWatts;
        public float DeficitWatts;
        public int CandidateCount;
        public float ProjectedSavingsWatts;
        public string ReasonKey = "PGM_Mgmt_Reason_Off";

        public readonly List<BuildingPowerSnapshot> ToShed = new List<BuildingPowerSnapshot>(16);
        public readonly List<BuildingPowerSnapshot> ToRestore = new List<BuildingPowerSnapshot>(16);
        public readonly List<string> MissingTrackedIds = new List<string>(8);
        public readonly List<string> AlreadyOnIds = new List<string>(8);

        public int ActionCount => ToShed.Count + ToRestore.Count;
    }
}
