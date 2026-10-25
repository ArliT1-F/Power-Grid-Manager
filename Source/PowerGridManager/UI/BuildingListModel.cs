using System;
using System.Collections.Generic;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Filtered and sorted view over the snapshot's buildings. Rebuilds only when the
    /// snapshot, a filter, the sort order, the grouping or the threshold changes, so the
    /// window can redraw every frame without re-filtering.
    ///
    /// Sorting is stable: equal keys fall back to the building's original scan order
    /// (and then to its label), so rows do not reshuffle between refreshes.
    /// </summary>
    public sealed class BuildingListModel
    {
        private sealed class Entry
        {
            public BuildingPowerSnapshot Building;
            public int OriginalIndex;
            public int GroupOrder;
            public string GroupLabel;
            public string Name;
        }

        private readonly List<BuildingPowerSnapshot> items = new List<BuildingPowerSnapshot>();
        private readonly List<Entry> candidates = new List<Entry>();
        private readonly List<Entry> sorted = new List<Entry>();
        private readonly Comparison<Entry> comparison;

        private PowerGridSnapshot lastSnapshot;
        private int lastRevision = int.MinValue;
        private float lastThreshold = float.NaN;
        private BuildingSortKey lastSortKey;
        private bool lastDescending;
        private BuildingGrouping lastGrouping;

        public BuildingListModel()
        {
            comparison = CompareEntries;
        }

        public IReadOnlyList<BuildingPowerSnapshot> Items => items;
        public int Count => items.Count;

        /// <summary>Group label of the row at <paramref name="index"/> (empty when ungrouped).</summary>
        public string GroupLabelAt(int index)
        {
            if (index < 0 || index >= sorted.Count) return "";
            return sorted[index].GroupLabel;
        }

        public int GroupOrderAt(int index)
        {
            if (index < 0 || index >= sorted.Count) return 0;
            return sorted[index].GroupOrder;
        }

        /// <summary>Rebuilds when inputs changed. Returns true when the row set changed.</summary>
        public bool Update(PowerGridSnapshot snapshot, BuildingFilterSettings filter, BuildingSortKey sortKey,
            bool descending, BuildingGrouping grouping, float highConsumptionThreshold)
        {
            bool sameInputs = ReferenceEquals(snapshot, lastSnapshot) &&
                              filter != null && filter.Revision == lastRevision &&
                              Math.Abs(highConsumptionThreshold - lastThreshold) < 0.01f &&
                              sortKey == lastSortKey && descending == lastDescending && grouping == lastGrouping;
            if (sameInputs) return false;

            lastSnapshot = snapshot;
            lastRevision = filter == null ? 0 : filter.Revision;
            lastThreshold = highConsumptionThreshold;
            lastSortKey = sortKey;
            lastDescending = descending;
            lastGrouping = grouping;

            candidates.Clear();
            items.Clear();
            if (snapshot != null && filter != null)
            {
                IReadOnlyList<BuildingPowerSnapshot> buildings = snapshot.Buildings;
                for (int i = 0; i < buildings.Count; i++)
                {
                    BuildingPowerSnapshot building = buildings[i];
                    if (!Matches(building, filter, highConsumptionThreshold)) continue;
                    candidates.Add(new Entry
                    {
                        Building = building,
                        OriginalIndex = i,
                        Name = LabelOf(building)
                    });
                }
                ComputeGroupKeys(candidates, grouping);
                sorted.Clear();
                sorted.AddRange(candidates);
                sorted.Sort(comparison);
                for (int i = 0; i < sorted.Count; i++) items.Add(sorted[i].Building);
            }
            else
            {
                sorted.Clear();
            }
            return true;
        }

        /// <summary>Exposed for tests and for the "would this filter match" tooltips.</summary>
        public static bool Matches(BuildingPowerSnapshot building, BuildingFilterSettings filter,
            float highConsumptionThreshold)
        {
            if (building == null || filter == null) return false;
            if (building.Building == null) return false;

            bool isConsumer = building.IsConsumer;
            bool isGenerator = building.IsGenerator;
            bool isBattery = building.IsBattery;
            bool anyRole = isConsumer || isGenerator || isBattery;

            // A building with only unrecognised power components still belongs in the table:
            // hiding it would pretend the mod can see everything. Its role is unknown, so it
            // follows the consumer toggle, which is the widest of the three role filters.
            if (!anyRole && !building.HasUnsupportedComponent) return false;
            if (anyRole)
            {
                if (!(isConsumer && filter.ShowConsumers) && !(isGenerator && filter.ShowGenerators) &&
                    !(isBattery && filter.ShowBatteries))
                {
                    return false;
                }
            }
            else if (!filter.ShowConsumers)
            {
                return false;
            }

            if (filter.NetworkId.HasValue && building.NetworkId != filter.NetworkId) return false;
            if (filter.Category.HasValue && building.Category != filter.Category.Value) return false;
            if (filter.OnlyFlickable && !building.IsFlickable) return false;
            if (filter.OnlySwitchedOff && !(building.IsFlickable && building.SwitchIsOn == false)) return false;
            if (filter.OnlyHighConsumption && !PowerGridUtilities.IsHighConsumption(building, highConsumptionThreshold)) return false;
            if (filter.OnlyWithDataNotices && !HasDataNotice(building)) return false;
            if (filter.OnlyUnknownBehavior && !PowerGridUtilities.HasUnknownBehavior(building)) return false;
            if (filter.OnlyColonistBuildings && !building.IsColonistBuilding) return false;
            if (filter.OnlyWithoutRoom && building.HasRoom) return false;

            string search = filter.SearchText;
            if (!string.IsNullOrEmpty(search) && search.Trim().Length > 0)
            {
                string needle = search.Trim();
                if (!Contains(building, needle)) return false;
            }
            return true;
        }

        private static bool HasDataNotice(BuildingPowerSnapshot building)
        {
            return building.HasUnsupportedComponent || building.HasInvalidData ||
                   building.Connection != PowerConnectionState.Connected ||
                   building.HasMultipleNetworks;
        }

        private static bool Contains(BuildingPowerSnapshot building, string needle)
        {
            ThingDef def = building.Building.def;
            if (def == null) return false;
            if (StringContains(def.defName, needle)) return true;
            if (StringContains(def.label, needle)) return true;
            string category = PowerGridUtilities.CategoryLabelKey(building.Category).Translate().ToString();
            return StringContains(category, needle);
        }

        private static bool StringContains(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack)) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string LabelOf(BuildingPowerSnapshot building)
        {
            ThingDef def = building.Building.def;
            if (def == null) return "";
            if (!string.IsNullOrEmpty(def.label)) return def.label;
            return def.defName ?? "";
        }

        private void ComputeGroupKeys(List<Entry> entries, BuildingGrouping grouping)
        {
            if (grouping == BuildingGrouping.None)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    entries[i].GroupOrder = 0;
                    entries[i].GroupLabel = "";
                }
                return;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                switch (grouping)
                {
                    case BuildingGrouping.Category:
                        entry.GroupOrder = (int)entry.Building.Category;
                        entry.GroupLabel = PowerGridUtilities.CategoryLabelKey(entry.Building.Category).Translate().ToString();
                        break;
                    case BuildingGrouping.Room:
                        entry.GroupOrder = entry.Building.HasRoom ? 1 : 0;
                        entry.GroupLabel = PowerGridUtilities.RoomLabel(entry.Building.Room);
                        break;
                    default:
                        entry.GroupOrder = entry.Building.NetworkId ?? int.MaxValue;
                        entry.GroupLabel = entry.Building.NetworkId.HasValue
                            ? "PGM_Network_Number".Translate(entry.Building.NetworkId.Value).ToString()
                            : "PGM_Network_NoneAssigned".Translate().ToString();
                        break;
                }
            }
        }

        private int CompareEntries(Entry a, Entry b)
        {
            if (a == b) return 0;
            if (lastGrouping != BuildingGrouping.None)
            {
                int group = a.GroupOrder.CompareTo(b.GroupOrder);
                if (group != 0) return group;
                // Keep same-named groups adjacent even when the ordering key collides.
                int label = string.Compare(a.GroupLabel, b.GroupLabel, StringComparison.Ordinal);
                if (label != 0) return label;
            }

            // Natural ascending comparison for the chosen key; direction is applied once.
            int primary;
            switch (lastSortKey)
            {
                case BuildingSortKey.Generation:
                    primary = a.Building.GenerationWatts.CompareTo(b.Building.GenerationWatts);
                    break;
                case BuildingSortKey.Name:
                    primary = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    break;
                case BuildingSortKey.Category:
                    primary = ((int)a.Building.Category).CompareTo((int)b.Building.Category);
                    break;
                case BuildingSortKey.Status:
                    primary = StatusRank(a.Building).CompareTo(StatusRank(b.Building));
                    break;
                default:
                    // Current draw and the daily-energy estimate rank identically,
                    // because the estimate is the current draw held for one day.
                    primary = a.Building.ConsumptionWatts.CompareTo(b.Building.ConsumptionWatts);
                    break;
            }
            if (lastDescending) primary = -primary;
            if (primary != 0) return primary;

            // Stable tie-breakers: label first, then original scan order.
            int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            if (byName != 0) return byName;
            return a.OriginalIndex.CompareTo(b.OriginalIndex);
        }

        private static int StatusRank(BuildingPowerSnapshot building)
        {
            if (building.HasUnsupportedComponent) return 3;
            if (building.Connection != PowerConnectionState.Connected) return 2;
            if (building.HasOffComponents) return 1;
            return 0;
        }
    }
}
