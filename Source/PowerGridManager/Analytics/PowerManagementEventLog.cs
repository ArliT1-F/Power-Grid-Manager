using System.Collections.Generic;
using Verse;

namespace PowerGridManager
{
    public enum PowerManagementEventKind
    {
        ManualOff = 0,
        ManualOn,
        AutomaticOff,
        AutomaticOn,
        RestoreAll,
        Refused,
        GroupChanged,
        PriorityChanged,
        PolicyChanged,
        TrackedCleared
    }

    /// <summary>One auditable management action. Primitives only; safe to persist.</summary>
    public struct PowerManagementEvent
    {
        public int Tick;
        public PowerManagementEventKind Kind;
        public string ThingId;
        public string Label;
        public bool Success;
        public string Detail;
    }

    /// <summary>
    /// Small, bounded history of what the optional management layer actually did. It is saved
    /// with the map so a player can see what happened after a blackout, and it never stores a
    /// Thing reference. Newest events are last; the list is capped and pruned on load.
    /// </summary>
    public sealed class PowerManagementEventLog : IExposable
    {
        public const int CurrentFormatVersion = 1;
        public const int MaxEvents = 40;

        private const int MaxTextLength = 96;

        private readonly List<PowerManagementEvent> events = new List<PowerManagementEvent>(MaxEvents);
        private readonly IReadOnlyList<PowerManagementEvent> view;

        private List<int> savedTicks = new List<int>(MaxEvents);
        private List<int> savedKinds = new List<int>(MaxEvents);
        private List<string> savedIds = new List<string>(MaxEvents);
        private List<string> savedLabels = new List<string>(MaxEvents);
        private List<bool> savedSuccess = new List<bool>(MaxEvents);
        private List<string> savedDetails = new List<string>(MaxEvents);

        private int formatVersion = CurrentFormatVersion;
        private int droppedEvents;

        public PowerManagementEventLog()
        {
            view = events.AsReadOnly();
        }

        public IReadOnlyList<PowerManagementEvent> Events => view;
        public int Count => events.Count;
        public int DroppedEvents => droppedEvents;
        public PowerManagementEvent this[int index] => events[index];

        public void Add(int tick, PowerManagementEventKind kind, string thingId, string label,
            bool success, string detail = null)
        {
            if (events.Count >= MaxEvents)
            {
                events.RemoveAt(0);
                droppedEvents++;
            }
            events.Add(new PowerManagementEvent
            {
                Tick = tick < 0 ? 0 : tick,
                Kind = kind,
                ThingId = Trim(thingId, MaxTextLength),
                Label = Trim(label, MaxTextLength),
                Success = success,
                Detail = Trim(detail, MaxTextLength)
            });
        }

        public void Clear()
        {
            events.Clear();
            droppedEvents = 0;
            formatVersion = CurrentFormatVersion;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref formatVersion, "formatVersion", CurrentFormatVersion);
            if (Scribe.mode == LoadSaveMode.Saving) Fill();

            Scribe_Collections.Look(ref savedTicks, "ticks", LookMode.Value);
            Scribe_Collections.Look(ref savedKinds, "kinds", LookMode.Value);
            Scribe_Collections.Look(ref savedIds, "ids", LookMode.Value);
            Scribe_Collections.Look(ref savedLabels, "labels", LookMode.Value);
            Scribe_Collections.Look(ref savedSuccess, "success", LookMode.Value);
            Scribe_Collections.Look(ref savedDetails, "details", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Rebuild();
                ClampLoaded();
            }
            else if (Scribe.mode == LoadSaveMode.PostLoadInit) ClampLoaded();
        }

        private void Fill()
        {
            savedTicks.Clear();
            savedKinds.Clear();
            savedIds.Clear();
            savedLabels.Clear();
            savedSuccess.Clear();
            savedDetails.Clear();
            for (int i = 0; i < events.Count; i++)
            {
                PowerManagementEvent entry = events[i];
                savedTicks.Add(entry.Tick);
                savedKinds.Add((int)entry.Kind);
                savedIds.Add(entry.ThingId ?? "");
                savedLabels.Add(entry.Label ?? "");
                savedSuccess.Add(entry.Success);
                savedDetails.Add(entry.Detail ?? "");
            }
        }

        private void Rebuild()
        {
            events.Clear();
            droppedEvents = 0;
            if (savedTicks == null || savedKinds == null) return;

            int count = savedTicks.Count;
            if (savedKinds.Count < count) count = savedKinds.Count;
            if (savedIds != null && savedIds.Count < count) count = savedIds.Count;
            if (savedLabels != null && savedLabels.Count < count) count = savedLabels.Count;
            if (savedSuccess != null && savedSuccess.Count < count) count = savedSuccess.Count;
            if (savedDetails != null && savedDetails.Count < count) count = savedDetails.Count;

            int start = count > MaxEvents ? count - MaxEvents : 0;
            droppedEvents += start;
            for (int i = start; i < count; i++)
            {
                int kind = savedKinds[i];
                if (kind < 0 || kind > (int)PowerManagementEventKind.TrackedCleared)
                    kind = (int)PowerManagementEventKind.Refused;
                events.Add(new PowerManagementEvent
                {
                    Tick = savedTicks[i] < 0 ? 0 : savedTicks[i],
                    Kind = (PowerManagementEventKind)kind,
                    ThingId = Trim(savedIds == null || i >= savedIds.Count ? "" : savedIds[i], MaxTextLength),
                    Label = Trim(savedLabels == null || i >= savedLabels.Count ? "" : savedLabels[i], MaxTextLength),
                    Success = savedSuccess != null && i < savedSuccess.Count && savedSuccess[i],
                    Detail = Trim(savedDetails == null || i >= savedDetails.Count ? "" : savedDetails[i], MaxTextLength)
                });
            }
        }

        private void ClampLoaded()
        {
            if (formatVersion > CurrentFormatVersion || formatVersion < 1) formatVersion = CurrentFormatVersion;
            while (events.Count > MaxEvents)
            {
                events.RemoveAt(0);
                droppedEvents++;
            }
        }

        private static string Trim(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= maximum ? value : value.Substring(0, maximum);
        }
    }
}
