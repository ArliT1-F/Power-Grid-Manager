using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Runtime episode memory for <see cref="PowerDiagnostics"/>: when a condition was first
    /// seen, whether it is ongoing or resolved, and whether it has already been announced.
    /// <para>
    /// Deliberately not persisted. Network identity is a live <see cref="PowerNet"/> object and
    /// building identity is a live <see cref="Building"/>; neither survives a save. A stale mute
    /// that hides a real problem after reloading is worse than losing the episode history, so
    /// persisted user preferences (muting a whole advisory kind, thresholds) live in
    /// <see cref="PowerGridManagerSettings"/> instead.
    /// </para>
    /// Entries are keyed by the live object, so when the game splits or merges a network the new
    /// <see cref="PowerNet"/> starts a fresh episode instead of inheriting a stale timer.
    /// </summary>
    public sealed class PowerDiagnosticTracker
    {
        /// <summary>How long a resolved episode is kept so the UI can report "resolved recently".</summary>
        public const int ResolvedRetentionTicks = PowerTime.TicksPerDay;

        private sealed class Episode
        {
            public int FirstSeenTick = -1;
            public int LastSeenTick = -1;
            public int ResolvedTick = -1;
            public bool Ongoing;
            public bool SeenThisPass;
            public bool Announced;
            public bool IsNewEpisode;
        }

        private readonly struct NetworkKey : System.IEquatable<NetworkKey>
        {
            public readonly PowerNet Net;
            public readonly int Kind;

            public NetworkKey(PowerNet net, int kind)
            {
                Net = net;
                Kind = kind;
            }

            public bool Equals(NetworkKey other) => ReferenceEquals(Net, other.Net) && Kind == other.Kind;
            public override bool Equals(object obj) => obj is NetworkKey other && Equals(other);

            public override int GetHashCode()
            {
                int hash = Net == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Net);
                return hash * 397 + Kind;
            }
        }

        private readonly struct BuildingKey : System.IEquatable<BuildingKey>
        {
            public readonly Building Building;
            public readonly int Kind;

            public BuildingKey(Building building, int kind)
            {
                Building = building;
                Kind = kind;
            }

            public bool Equals(BuildingKey other) => ReferenceEquals(Building, other.Building) && Kind == other.Kind;
            public override bool Equals(object obj) => obj is BuildingKey other && Equals(other);

            public override int GetHashCode()
            {
                int hash = Building == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Building);
                return hash * 397 + Kind;
            }
        }

        private sealed class Dismissal
        {
            public PowerDiagnosticKind Kind;
            public int? NetworkId;
            public Building Building;
            public int Tick;
        }

        private const int MaxDismissals = 200;
        private const int DismissalLifetimeTicks = ResolvedRetentionTicks * 7;

        private readonly Dictionary<NetworkKey, Episode> networkEpisodes = new Dictionary<NetworkKey, Episode>();
        private readonly Dictionary<BuildingKey, Episode> buildingEpisodes = new Dictionary<BuildingKey, Episode>();
        private readonly Dictionary<NetworkKey, PowerDiagnostic> lastNetworkAdvisory =
            new Dictionary<NetworkKey, PowerDiagnostic>();
        private readonly Dictionary<BuildingKey, PowerDiagnostic> lastBuildingAdvisory =
            new Dictionary<BuildingKey, PowerDiagnostic>();
        private readonly List<NetworkKey> deadNetworkKeys = new List<NetworkKey>();
        private readonly List<BuildingKey> deadBuildingKeys = new List<BuildingKey>();
        private readonly List<Dismissal> dismissals = new List<Dismissal>();

        private int lastAnnouncementTick = -1;

        /// <summary>Marks every known episode unseen so the evaluator can re-register the live ones.</summary>
        public void BeginEvaluation()
        {
            foreach (KeyValuePair<NetworkKey, Episode> entry in networkEpisodes) entry.Value.SeenThisPass = false;
            foreach (KeyValuePair<BuildingKey, Episode> entry in buildingEpisodes) entry.Value.SeenThisPass = false;
        }

        /// <summary>
        /// Records whether a network-level condition is currently true. Returns how many ticks the
        /// condition has been observed continuously (0 when it is not currently active).
        /// </summary>
        public int Register(PowerNet net, PowerDiagnosticKind kind, bool active, int now, int cooldownTicks)
        {
            if (net == null) return 0;
            NetworkKey key = new NetworkKey(net, (int)kind);
            Episode episode;
            if (!networkEpisodes.TryGetValue(key, out episode))
            {
                episode = new Episode();
                networkEpisodes[key] = episode;
            }
            return Update(episode, active, now, cooldownTicks);
        }

        /// <summary>Building-level counterpart of the network overload.</summary>
        public int Register(Building building, PowerDiagnosticKind kind, bool active, int now, int cooldownTicks)
        {
            if (building == null) return 0;
            BuildingKey key = new BuildingKey(building, (int)kind);
            Episode episode;
            if (!buildingEpisodes.TryGetValue(key, out episode))
            {
                episode = new Episode();
                buildingEpisodes[key] = episode;
            }
            return Update(episode, active, now, cooldownTicks);
        }

        /// <summary>True the first time an episode is seen, so callers can announce it exactly once.</summary>
        public bool IsNewEpisode(PowerNet net, PowerDiagnosticKind kind)
        {
            Episode episode;
            if (net == null || !networkEpisodes.TryGetValue(new NetworkKey(net, (int)kind), out episode)) return false;
            return episode.IsNewEpisode;
        }

        public bool IsNewEpisode(Building building, PowerDiagnosticKind kind)
        {
            Episode episode;
            if (building == null || !buildingEpisodes.TryGetValue(new BuildingKey(building, (int)kind), out episode))
            {
                return false;
            }
            return episode.IsNewEpisode;
        }

        public void MarkAnnounced(PowerNet net, PowerDiagnosticKind kind)
        {
            Episode episode;
            if (net == null || !networkEpisodes.TryGetValue(new NetworkKey(net, (int)kind), out episode)) return;
            episode.Announced = true;
        }

        public void MarkAnnounced(Building building, PowerDiagnosticKind kind)
        {
            Episode episode;
            if (building == null || !buildingEpisodes.TryGetValue(new BuildingKey(building, (int)kind), out episode))
            {
                return;
            }
            episode.Announced = true;
        }

        public bool WasAnnounced(PowerNet net, PowerDiagnosticKind kind)
        {
            Episode episode;
            if (net == null || !networkEpisodes.TryGetValue(new NetworkKey(net, (int)kind), out episode)) return false;
            return episode.Announced;
        }

        /// <summary>True when a long, already-announced episode has just stopped being true.</summary>
        public bool JustResolvedLong(PowerNet net, PowerDiagnosticKind kind, int minimumTicks)
        {
            Episode episode;
            if (net == null || !networkEpisodes.TryGetValue(new NetworkKey(net, (int)kind), out episode)) return false;
            if (episode.Ongoing || episode.ResolvedTick < 0 || !episode.Announced) return false;
            return episode.LastSeenTick - episode.FirstSeenTick >= minimumTicks;
        }

        /// <summary>
        /// Keeps the advisory produced for a condition so that, once the condition stops, the UI can
        /// still show what it was and when it cleared. Only the numbers are kept; the object still
        /// refers to live game objects and is never persisted.
        /// </summary>
        public void Remember(PowerNet net, PowerDiagnosticKind kind, PowerDiagnostic advisory)
        {
            if (net == null || advisory == null) return;
            lastNetworkAdvisory[new NetworkKey(net, (int)kind)] = advisory;
        }

        public void Remember(Building building, PowerDiagnosticKind kind, PowerDiagnostic advisory)
        {
            if (building == null || advisory == null) return;
            lastBuildingAdvisory[new BuildingKey(building, (int)kind)] = advisory;
        }

        /// <summary>
        /// Appends a copy of every recently resolved advisory to <paramref name="output"/>, so the
        /// player can see that a problem cleared rather than watching it silently disappear.
        /// </summary>
        public void CollectResolved(List<PowerDiagnostic> output, int now)
        {
            foreach (KeyValuePair<NetworkKey, Episode> entry in networkEpisodes)
            {
                Episode episode = entry.Value;
                if (episode.Ongoing || episode.ResolvedTick < 0) continue;
                if (now - episode.ResolvedTick > ResolvedRetentionTicks) continue;
                PowerDiagnostic remembered;
                if (!lastNetworkAdvisory.TryGetValue(entry.Key, out remembered)) continue;
                output.Add(BuildResolved(remembered, episode, now));
            }
            foreach (KeyValuePair<BuildingKey, Episode> entry in buildingEpisodes)
            {
                Episode episode = entry.Value;
                if (episode.Ongoing || episode.ResolvedTick < 0) continue;
                if (now - episode.ResolvedTick > ResolvedRetentionTicks) continue;
                Building building = entry.Key.Building;
                if (building != null && (building.Destroyed || !building.Spawned)) continue;
                PowerDiagnostic remembered;
                if (!lastBuildingAdvisory.TryGetValue(entry.Key, out remembered)) continue;
                output.Add(BuildResolved(remembered, episode, now));
            }
        }

        private static PowerDiagnostic BuildResolved(PowerDiagnostic remembered, Episode episode, int now)
        {
            PowerDiagnostic copy = remembered.Clone();
            copy.Ongoing = false;
            copy.FirstSeenTick = episode.FirstSeenTick < 0 ? episode.LastSeenTick : episode.FirstSeenTick;
            copy.LastSeenTick = episode.LastSeenTick;
            copy.ResolvedTick = episode.ResolvedTick;
            copy.SustainedTicks = episode.LastSeenTick - copy.FirstSeenTick;
            return copy;
        }

        /// <summary>Tick of the last announcement; used to rate-limit in-game messages.</summary>
        public int LastAnnouncementTick => lastAnnouncementTick;

        public void NoteAnnouncement(int now)
        {
            lastAnnouncementTick = now;
        }

        /// <summary>
        /// Closes the evaluation: retires the episodes that were not registered this pass and
        /// drops entries whose objects are gone, so nothing is held alive indefinitely.
        /// </summary>
        public void EndEvaluation(PowerGridSnapshot snapshot, int now)
        {
            Sweep(networkEpisodes, deadNetworkKeys, now);
            Sweep(buildingEpisodes, deadBuildingKeys, now);

            // A destroyed or despawned building is never registered again; release it now
            // instead of keeping the reference until the episode expires.
            deadBuildingKeys.Clear();
            foreach (KeyValuePair<BuildingKey, Episode> entry in buildingEpisodes)
            {
                Building building = entry.Key.Building;
                if (building == null || building.Destroyed || !building.Spawned) deadBuildingKeys.Add(entry.Key);
            }
            for (int i = 0; i < deadBuildingKeys.Count; i++) buildingEpisodes.Remove(deadBuildingKeys[i]);

            DropOrphanedAdvisories(lastNetworkAdvisory, networkEpisodes, deadNetworkKeys);
            DropOrphanedAdvisories(lastBuildingAdvisory, buildingEpisodes, deadBuildingKeys);

            PruneDismissals(now);
        }

        /// <summary>Drops remembered advisories whose episode has been retired, so they are not held alive.</summary>
        private static void DropOrphanedAdvisories<TKey>(Dictionary<TKey, PowerDiagnostic> remembered,
            Dictionary<TKey, Episode> episodes, List<TKey> dead)
        {
            dead.Clear();
            if (remembered.Count == 0) return;
            foreach (KeyValuePair<TKey, PowerDiagnostic> entry in remembered)
            {
                if (!episodes.ContainsKey(entry.Key)) dead.Add(entry.Key);
            }
            for (int i = 0; i < dead.Count; i++) remembered.Remove(dead[i]);
        }

        public void Reset()
        {
            networkEpisodes.Clear();
            buildingEpisodes.Clear();
            lastNetworkAdvisory.Clear();
            lastBuildingAdvisory.Clear();
            dismissals.Clear();
            lastAnnouncementTick = -1;
        }

        public int TrackedEpisodeCount => networkEpisodes.Count + buildingEpisodes.Count;

        // --- Dismissal (session only) ----------------------------------------

        public void Dismiss(PowerDiagnostic advisory, int now)
        {
            if (advisory == null) return;
            for (int i = 0; i < dismissals.Count; i++)
            {
                Dismissal existing = dismissals[i];
                if (existing.Kind != advisory.Kind || existing.NetworkId != advisory.NetworkId) continue;
                if (ReferenceEquals(existing.Building, advisory.Building))
                {
                    existing.Tick = now;
                    return;
                }
            }
            if (dismissals.Count >= MaxDismissals) dismissals.RemoveAt(0);
            dismissals.Add(new Dismissal
            {
                Kind = advisory.Kind,
                NetworkId = advisory.NetworkId,
                Building = advisory.Building,
                Tick = now
            });
        }

        public bool IsDismissed(PowerDiagnosticKind kind, int? networkId, Building building)
        {
            for (int i = 0; i < dismissals.Count; i++)
            {
                Dismissal entry = dismissals[i];
                if (entry.Kind == kind && entry.NetworkId == networkId && ReferenceEquals(entry.Building, building))
                {
                    return true;
                }
            }
            return false;
        }

        public int DismissedCount => dismissals.Count;

        public void ClearDismissals()
        {
            dismissals.Clear();
        }

        private void PruneDismissals(int now)
        {
            for (int i = dismissals.Count - 1; i >= 0; i--)
            {
                Dismissal entry = dismissals[i];
                bool stale = entry.Tick < 0 || now - entry.Tick > DismissalLifetimeTicks;
                bool gone = entry.Building != null && (entry.Building.Destroyed || !entry.Building.Spawned);
                if (stale || gone) dismissals.RemoveAt(i);
            }
        }

        // --- Episode bookkeeping ---------------------------------------------

        /// <summary>
        /// Applies one observation. A condition that reappears inside the cooldown window is
        /// treated as the same unresolved episode rather than a new alert, which is what stops a
        /// flickering problem from re-announcing itself every few seconds.
        /// </summary>
        private static int Update(Episode episode, bool active, int now, int cooldownTicks)
        {
            episode.SeenThisPass = true;
            episode.IsNewEpisode = false;

            if (active)
            {
                bool resumed = episode.ResolvedTick >= 0 && cooldownTicks > 0 &&
                               now - episode.ResolvedTick <= cooldownTicks;
                if (episode.FirstSeenTick < 0 || (episode.ResolvedTick >= 0 && !resumed))
                {
                    episode.FirstSeenTick = now;
                    episode.Announced = false;
                    episode.IsNewEpisode = true;
                }
                episode.LastSeenTick = now;
                episode.ResolvedTick = -1;
                episode.Ongoing = true;
                return now - episode.FirstSeenTick;
            }

            if (episode.Ongoing)
            {
                episode.Ongoing = false;
                if (episode.ResolvedTick < 0) episode.ResolvedTick = now;
            }
            return 0;
        }

        /// <summary>Closes episodes that were not seen this pass and removes the expired ones.</summary>
        private static void Sweep<TKey>(Dictionary<TKey, Episode> episodes, List<TKey> dead, int now)
        {
            dead.Clear();
            if (episodes.Count == 0) return;
            foreach (KeyValuePair<TKey, Episode> entry in episodes)
            {
                Episode episode = entry.Value;
                if (episode.SeenThisPass) continue;
                if (episode.Ongoing)
                {
                    episode.Ongoing = false;
                    if (episode.ResolvedTick < 0) episode.ResolvedTick = now;
                    continue;
                }
                if (episode.ResolvedTick >= 0 && now - episode.ResolvedTick > ResolvedRetentionTicks) dead.Add(entry.Key);
            }
            for (int i = 0; i < dead.Count; i++) episodes.Remove(dead[i]);
        }
    }
}
