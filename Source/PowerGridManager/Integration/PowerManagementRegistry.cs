using System.Collections.Generic;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// One building's optional management preferences. Contains no Thing, Component or
    /// Unity references, so it is safe to keep in a save. <see cref="ThingId"/> is the game's
    /// own <c>GetUniqueLoadID()</c> ("Thing_" + ThingID), the identifier the game itself uses
    /// to resolve cross-references in saves. Runtime object references are never persisted.
    /// </summary>
    public sealed class PowerManagementRecord
    {
        public string ThingId = "";
        public string Label = "";
        public string DefName = "";
        public int PositionX;
        public int PositionZ;
        public string Group = "";
        public bool Sheddable;
        public int ShedPriority = 3;
        public bool ExcludeFromManagement;
        public bool ExcludeFromDiagnostics;
        public int LastSeenTick;

        /// <summary>Runtime flag: no live building with this ID was seen in the last snapshot.</summary>
        public bool IsMissing;
    }

    /// <summary>
    /// Map-scoped, bounded registry of management preferences and monitoring groups.
    /// Only primitive values are serialized, in parallel lists that tolerate missing,
    /// truncated or duplicated rows from older versions of the mod.
    /// </summary>
    public sealed class PowerManagementRegistry : IExposable
    {
        public const int CurrentFormatVersion = 1;
        public const int MaxRecords = 2000;
        public const int MaxGroups = 16;
        public const int MinPriority = 1;
        public const int MaxPriority = 6;
        public const int DefaultPriority = 3;
        public const int OrphanRetentionTicks = PowerTime.TicksPerDay * 30;

        private const int MaxIdLength = 160;
        private const int MaxNameLength = 48;
        private const int MaxLabelLength = 80;

        private readonly List<PowerManagementRecord> records = new List<PowerManagementRecord>(32);
        private readonly Dictionary<string, PowerManagementRecord> byId =
            new Dictionary<string, PowerManagementRecord>(32);
        private readonly List<string> groups = new List<string>(8);
        private readonly List<int> groupPriorities = new List<int>(8);
        private readonly IReadOnlyList<PowerManagementRecord> recordView;
        private readonly IReadOnlyList<string> groupView;

        // Scratch lists used only while saving/loading.
        private List<string> savedIds = new List<string>(32);
        private List<string> savedLabels = new List<string>(32);
        private List<string> savedDefNames = new List<string>(32);
        private List<int> savedX = new List<int>(32);
        private List<int> savedZ = new List<int>(32);
        private List<string> savedGroups = new List<string>(32);
        private List<bool> savedSheddable = new List<bool>(32);
        private List<int> savedPriorities = new List<int>(32);
        private List<bool> savedExcludeManagement = new List<bool>(32);
        private List<bool> savedExcludeDiagnostics = new List<bool>(32);
        private List<int> savedLastSeen = new List<int>(32);
        private List<string> savedGroupNames = new List<string>(8);
        private List<int> savedGroupPriorities = new List<int>(8);

        private int formatVersion = CurrentFormatVersion;

        public PowerManagementRegistry()
        {
            recordView = records.AsReadOnly();
            groupView = groups.AsReadOnly();
        }

        public int FormatVersion => formatVersion;
        public IReadOnlyList<PowerManagementRecord> Records => recordView;
        public IReadOnlyList<string> Groups => groupView;
        public int Count => records.Count;
        public int GroupCount => groups.Count;

        /// <summary>
        /// Vanilla's save reference ID for a Thing. Null when the building has no usable
        /// identity (destroyed, no def, or a deliberately exotic implementation).
        /// </summary>
        public static string StableId(Building building)
        {
            if (building == null || building.def == null) return null;
            string id = building.GetUniqueLoadID();
            if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength) return null;
            return id;
        }

        public PowerManagementRecord Find(string thingId)
        {
            if (string.IsNullOrEmpty(thingId)) return null;
            PowerManagementRecord record;
            return byId.TryGetValue(thingId, out record) ? record : null;
        }

        public PowerManagementRecord Find(Building building)
        {
            return Find(StableId(building));
        }

        /// <summary>
        /// Gets or creates the preference row for a building. Returns null when the building
        /// has no stable ID or the bounded registry is full, so callers never invent identity.
        /// </summary>
        public PowerManagementRecord Ensure(Building building, int tick)
        {
            string id = StableId(building);
            if (id == null) return null;
            PowerManagementRecord existing = Find(id);
            if (existing != null)
            {
                Refresh(existing, building, tick);
                return existing;
            }
            if (records.Count >= MaxRecords && !DropOldestMissing()) return null;

            var created = new PowerManagementRecord
            {
                ThingId = id,
                ShedPriority = DefaultPriority,
                LastSeenTick = tick
            };
            Refresh(created, building, tick);
            records.Add(created);
            byId[id] = created;
            return created;
        }

        private static void Refresh(PowerManagementRecord record, Building building, int tick)
        {
            record.Label = Trim(PowerGridUtilities.BuildingLabel(building), MaxLabelLength);
            record.DefName = building.def == null || string.IsNullOrEmpty(building.def.defName)
                ? "" : Trim(building.def.defName, MaxNameLength);
            record.PositionX = building.Position.x;
            record.PositionZ = building.Position.z;
            record.LastSeenTick = tick;
            record.IsMissing = false;
        }

        /// <summary>
        /// Marks which configured buildings are still present. Called once per refresh, not
        /// per tick and not per frame. Missing rows are kept briefly so the UI can explain a
        /// destroyed or uninstalled building instead of silently forgetting it.
        /// </summary>
        public void MarkSeen(PowerGridSnapshot snapshot, int tick)
        {
            if (records.Count == 0) return;
            for (int i = 0; i < records.Count; i++) records[i].IsMissing = true;
            if (snapshot == null || snapshot.Buildings == null) return;
            IReadOnlyList<BuildingPowerSnapshot> buildings = snapshot.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                PowerManagementRecord record = Find(buildings[i].Building);
                if (record == null) continue;
                Refresh(record, buildings[i].Building, tick);
            }
        }

        /// <summary>Drops long-missing rows. The hard cap is enforced on load and in Ensure.</summary>
        public void Prune(int tick)
        {
            if (tick < 0) return;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                PowerManagementRecord record = records[i];
                if (!record.IsMissing || tick - record.LastSeenTick <= OrphanRetentionTicks) continue;
                byId.Remove(record.ThingId);
                records.RemoveAt(i);
            }
        }

        private bool DropOldestMissing()
        {
            int index = -1;
            int oldest = int.MaxValue;
            for (int i = 0; i < records.Count; i++)
            {
                if (!records[i].IsMissing || records[i].LastSeenTick >= oldest) continue;
                oldest = records[i].LastSeenTick;
                index = i;
            }
            if (index < 0) return false;
            byId.Remove(records[index].ThingId);
            records.RemoveAt(index);
            return true;
        }

        public bool IsExcludedFromManagement(Building building)
        {
            PowerManagementRecord record = Find(building);
            return record != null && record.ExcludeFromManagement;
        }

        public bool IsExcludedFromDiagnostics(Building building)
        {
            PowerManagementRecord record = Find(building);
            return record != null && record.ExcludeFromDiagnostics;
        }

        public bool IsSheddable(Building building)
        {
            PowerManagementRecord record = Find(building);
            return record != null && record.Sheddable && !record.ExcludeFromManagement;
        }

        public int PriorityOf(Building building)
        {
            PowerManagementRecord record = Find(building);
            return record == null ? DefaultPriority : ClampPriority(record.ShedPriority);
        }

        public void SetGroup(Building building, string group, int tick)
        {
            PowerManagementRecord record = Ensure(building, tick);
            if (record != null) record.Group = Trim(group ?? "", MaxNameLength);
        }

        public void SetSheddable(Building building, bool value, int tick)
        {
            PowerManagementRecord record = Ensure(building, tick);
            if (record != null) record.Sheddable = value;
        }

        public void SetPriority(Building building, int value, int tick)
        {
            PowerManagementRecord record = Ensure(building, tick);
            if (record != null) record.ShedPriority = ClampPriority(value);
        }

        public void SetExcludedFromManagement(Building building, bool value, int tick)
        {
            PowerManagementRecord record = Ensure(building, tick);
            if (record != null) record.ExcludeFromManagement = value;
        }

        public void SetExcludedFromDiagnostics(Building building, bool value, int tick)
        {
            PowerManagementRecord record = Ensure(building, tick);
            if (record != null) record.ExcludeFromDiagnostics = value;
        }

        /// <summary>Group membership is a plain label; groups never assume control by themselves.</summary>
        public bool AddGroup(string name, int priority = DefaultPriority)
        {
            string trimmed = Trim(name ?? "", MaxNameLength);
            if (trimmed.Length == 0 || groups.Contains(trimmed) || groups.Count >= MaxGroups) return false;
            groups.Add(trimmed);
            groupPriorities.Add(ClampPriority(priority));
            return true;
        }

        public bool RemoveGroup(string name)
        {
            int index = groups.IndexOf(name ?? "");
            if (index < 0) return false;
            for (int i = 0; i < records.Count; i++)
                if (records[i].Group == groups[index]) records[i].Group = "";
            groups.RemoveAt(index);
            groupPriorities.RemoveAt(index);
            return true;
        }

        public int GroupPriority(string name)
        {
            int index = groups.IndexOf(name ?? "");
            return index < 0 ? DefaultPriority : ClampPriority(groupPriorities[index]);
        }

        /// <summary>Changes a group's suggested priority without touching its members.</summary>
        public bool SetGroupPriority(string name, int priority)
        {
            int index = groups.IndexOf(name ?? "");
            if (index < 0) return false;
            groupPriorities[index] = ClampPriority(priority);
            return true;
        }

        /// <summary>
        /// Explicit bulk action, only ever called from a button the player pressed. The UI shows
        /// the member list first; nothing here runs automatically.
        /// </summary>
        public int ApplyGroupPriority(string name, int priority)
        {
            return ForEachMember(name, delegate (PowerManagementRecord record)
            {
                record.ShedPriority = ClampPriority(priority);
            });
        }

        public int SetGroupSheddable(string name, bool sheddable)
        {
            return ForEachMember(name, delegate (PowerManagementRecord record)
            {
                record.Sheddable = sheddable;
            });
        }

        public int GroupMemberCount(string name)
        {
            int count = 0;
            for (int i = 0; i < records.Count; i++)
                if (records[i].Group == name) count++;
            return count;
        }

        private delegate void RecordAction(PowerManagementRecord record);

        private int ForEachMember(string name, RecordAction action)
        {
            int count = 0;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Group != name) continue;
                action(records[i]);
                count++;
            }
            return count;
        }

        public void Clear()
        {
            records.Clear();
            byId.Clear();
            groups.Clear();
            groupPriorities.Clear();
            formatVersion = CurrentFormatVersion;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref formatVersion, "formatVersion", CurrentFormatVersion);
            if (Scribe.mode == LoadSaveMode.Saving) Fill();

            Scribe_Collections.Look(ref savedIds, "ids", LookMode.Value);
            Scribe_Collections.Look(ref savedLabels, "labels", LookMode.Value);
            Scribe_Collections.Look(ref savedDefNames, "defNames", LookMode.Value);
            Scribe_Collections.Look(ref savedX, "positionX", LookMode.Value);
            Scribe_Collections.Look(ref savedZ, "positionZ", LookMode.Value);
            Scribe_Collections.Look(ref savedGroups, "rowGroups", LookMode.Value);
            Scribe_Collections.Look(ref savedSheddable, "sheddable", LookMode.Value);
            Scribe_Collections.Look(ref savedPriorities, "priorities", LookMode.Value);
            Scribe_Collections.Look(ref savedExcludeManagement, "excludeManagement", LookMode.Value);
            Scribe_Collections.Look(ref savedExcludeDiagnostics, "excludeDiagnostics", LookMode.Value);
            Scribe_Collections.Look(ref savedLastSeen, "lastSeen", LookMode.Value);
            Scribe_Collections.Look(ref savedGroupNames, "groupNames", LookMode.Value);
            Scribe_Collections.Look(ref savedGroupPriorities, "groupPriorities", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Rebuild();
                ClampLoaded();
            }
            else if (Scribe.mode == LoadSaveMode.PostLoadInit) ClampLoaded();
        }

        private void Fill()
        {
            ClearLists();
            for (int i = 0; i < records.Count; i++)
            {
                PowerManagementRecord r = records[i];
                savedIds.Add(r.ThingId);
                savedLabels.Add(r.Label);
                savedDefNames.Add(r.DefName);
                savedX.Add(r.PositionX);
                savedZ.Add(r.PositionZ);
                savedGroups.Add(r.Group);
                savedSheddable.Add(r.Sheddable);
                savedPriorities.Add(r.ShedPriority);
                savedExcludeManagement.Add(r.ExcludeFromManagement);
                savedExcludeDiagnostics.Add(r.ExcludeFromDiagnostics);
                savedLastSeen.Add(r.LastSeenTick);
            }
            for (int i = 0; i < groups.Count; i++)
            {
                savedGroupNames.Add(groups[i]);
                savedGroupPriorities.Add(groupPriorities[i]);
            }
        }

        private void Rebuild()
        {
            records.Clear();
            byId.Clear();
            groups.Clear();
            groupPriorities.Clear();

            if (savedIds != null)
            {
                int count = savedIds.Count;
                count = MinCount(count, savedLabels);
                count = MinCount(count, savedDefNames);
                count = MinCount(count, savedX);
                count = MinCount(count, savedZ);
                count = MinCount(count, savedGroups);
                count = MinCount(count, savedSheddable);
                count = MinCount(count, savedPriorities);
                count = MinCount(count, savedExcludeManagement);
                count = MinCount(count, savedExcludeDiagnostics);
                count = MinCount(count, savedLastSeen);
                if (count > MaxRecords) count = MaxRecords;

                for (int i = 0; i < count; i++)
                {
                    string id = Trim(savedIds[i], MaxIdLength);
                    if (id.Length == 0 || byId.ContainsKey(id)) continue;
                    var record = new PowerManagementRecord
                    {
                        ThingId = id,
                        Label = Trim(savedLabels[i], MaxLabelLength),
                        DefName = Trim(savedDefNames[i], MaxNameLength),
                        PositionX = savedX[i],
                        PositionZ = savedZ[i],
                        Group = Trim(savedGroups[i], MaxNameLength),
                        Sheddable = savedSheddable[i],
                        ShedPriority = ClampPriority(savedPriorities[i]),
                        ExcludeFromManagement = savedExcludeManagement[i],
                        ExcludeFromDiagnostics = savedExcludeDiagnostics[i],
                        LastSeenTick = savedLastSeen[i] < 0 ? 0 : savedLastSeen[i],
                        IsMissing = true
                    };
                    records.Add(record);
                    byId[id] = record;
                }
            }

            if (savedGroupNames != null)
            {
                int count = savedGroupNames.Count;
                if (count > MaxGroups) count = MaxGroups;
                for (int i = 0; i < count; i++)
                {
                    string name = Trim(savedGroupNames[i], MaxNameLength);
                    if (name.Length == 0 || groups.Contains(name)) continue;
                    int priority = savedGroupPriorities != null && i < savedGroupPriorities.Count
                        ? savedGroupPriorities[i] : DefaultPriority;
                    groups.Add(name);
                    groupPriorities.Add(ClampPriority(priority));
                }
            }

            // Rows may reference a group that was dropped from a newer/older save. Keep the
            // label but drop sheddable status so no building is controlled by a stale group.
            for (int i = 0; i < records.Count; i++)
            {
                PowerManagementRecord record = records[i];
                if (record.Group.Length == 0 || groups.Contains(record.Group)) continue;
                record.Group = "";
                record.Sheddable = false;
            }
        }

        private void ClampLoaded()
        {
            if (formatVersion > CurrentFormatVersion || formatVersion < 1) formatVersion = CurrentFormatVersion;
            for (int i = 0; i < records.Count; i++)
            {
                PowerManagementRecord r = records[i];
                r.ShedPriority = ClampPriority(r.ShedPriority);
                r.Group = Trim(r.Group ?? "", MaxNameLength);
                r.Label = Trim(r.Label ?? "", MaxLabelLength);
                r.IsMissing = true;
            }
            while (records.Count > MaxRecords)
            {
                int last = records.Count - 1;
                byId.Remove(records[last].ThingId);
                records.RemoveAt(last);
            }
        }

        private void ClearLists()
        {
            savedIds.Clear();
            savedLabels.Clear();
            savedDefNames.Clear();
            savedX.Clear();
            savedZ.Clear();
            savedGroups.Clear();
            savedSheddable.Clear();
            savedPriorities.Clear();
            savedExcludeManagement.Clear();
            savedExcludeDiagnostics.Clear();
            savedLastSeen.Clear();
            savedGroupNames.Clear();
            savedGroupPriorities.Clear();
        }

        private static int MinCount<T>(int count, List<T> list)
        {
            if (list == null) return 0;
            return list.Count < count ? list.Count : count;
        }

        private static int ClampPriority(int value)
        {
            if (value < MinPriority) return MinPriority;
            return value > MaxPriority ? MaxPriority : value;
        }

        private static string Trim(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= maximum ? value : value.Substring(0, maximum);
        }
    }
}
