using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Session-only state for recommendations: whether a condition is ongoing, when it was
    /// first seen, and which recommendations the player dismissed. Not persisted: live
    /// <see cref="PowerNet"/> and <see cref="Building"/> references do not survive a save,
    /// and a stale dismissal that hides a real problem after reloading is worse than losing
    /// the dismissal. Persisted user preferences (dismissal is session-only here; thresholds
    /// live in <see cref="PowerGridManagerSettings"/>).
    /// </summary>
    public sealed class PowerRecommendationTracker
    {
        public const int MaxDismissals = 200;
        public const int DismissalLifetimeTicks = PowerTime.TicksPerDay * 7;

        private sealed class Episode
        {
            public int FirstSeenTick = -1;
            public int LastSeenTick = -1;
            public bool Ongoing;
            public bool SeenThisPass;
        }

        private sealed class NetworkKey : System.IEquatable<NetworkKey>
        {
            public readonly PowerNet Net;
            public readonly int Kind;
            public NetworkKey(PowerNet net, int kind) { Net = net; Kind = kind; }
            public bool Equals(NetworkKey other) => ReferenceEquals(Net, other.Net) && Kind == other.Kind;
            public override bool Equals(object obj) => obj is NetworkKey other && Equals(other);
            public override int GetHashCode()
            {
                int hash = Net == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Net);
                return hash * 397 + Kind;
            }
        }

        private sealed class BuildingKey : System.IEquatable<BuildingKey>
        {
            public readonly Building Building;
            public readonly int Kind;
            public BuildingKey(Building building, int kind) { Building = building; Kind = kind; }
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
            public RecommendationKind Kind;
            public int? NetworkId;
            public Building Building;
            public int Tick;
        }

        private readonly Dictionary<NetworkKey, Episode> networkEpisodes =
            new Dictionary<NetworkKey, Episode>();
        private readonly Dictionary<BuildingKey, Episode> buildingEpisodes =
            new Dictionary<BuildingKey, Episode>();
        private readonly List<Dismissal> dismissals = new List<Dismissal>();
        private readonly List<NetworkKey> deadNetworkKeys = new List<NetworkKey>();
        private readonly List<BuildingKey> deadBuildingKeys = new List<BuildingKey>();

        public void BeginEvaluation()
        {
            foreach (var entry in networkEpisodes) entry.Value.SeenThisPass = false;
            foreach (var entry in buildingEpisodes) entry.Value.SeenThisPass = false;
        }

        public int Register(PowerNet net, RecommendationKind kind, bool active, int now)
        {
            if (net == null) return 0;
            var key = new NetworkKey(net, (int)kind);
            Episode episode;
            if (!networkEpisodes.TryGetValue(key, out episode))
            {
                episode = new Episode();
                networkEpisodes[key] = episode;
            }
            return Update(episode, active, now);
        }

        public int Register(Building building, RecommendationKind kind, bool active, int now)
        {
            if (building == null) return 0;
            var key = new BuildingKey(building, (int)kind);
            Episode episode;
            if (!buildingEpisodes.TryGetValue(key, out episode))
            {
                episode = new Episode();
                buildingEpisodes[key] = episode;
            }
            return Update(episode, active, now);
        }

        public void Dismiss(PowerRecommendation recommendation, int now)
        {
            if (recommendation == null) return;
            for (int i = 0; i < dismissals.Count; i++)
            {
                var existing = dismissals[i];
                if (existing.Kind != recommendation.Kind || existing.NetworkId != recommendation.NetworkId) continue;
                if (ReferenceEquals(existing.Building, recommendation.Building))
                {
                    existing.Tick = now;
                    return;
                }
            }
            if (dismissals.Count >= MaxDismissals) dismissals.RemoveAt(0);
            dismissals.Add(new Dismissal
            {
                Kind = recommendation.Kind,
                NetworkId = recommendation.NetworkId,
                Building = recommendation.Building,
                Tick = now
            });
        }

        public bool IsDismissed(RecommendationKind kind, int? networkId, Building building)
        {
            for (int i = 0; i < dismissals.Count; i++)
            {
                var entry = dismissals[i];
                if (entry.Kind == kind && entry.NetworkId == networkId &&
                    ReferenceEquals(entry.Building, building)) return true;
            }
            return false;
        }

        public int DismissedCount => dismissals.Count;

        public void ClearDismissals() => dismissals.Clear();

        /// <summary>Appends copies of recently resolved conditions as resolved recommendations.</summary>
        public void CollectResolved(List<PowerRecommendation> output, int now)
        {
            foreach (var entry in networkEpisodes)
            {
                var episode = entry.Value;
                if (episode.Ongoing || episode.LastSeenTick < 0) continue;
                if (now - episode.LastSeenTick > DismissalLifetimeTicks) continue;
                var key = entry.Key;
                output.Add(MakeResolved((RecommendationKind)key.Kind, key.Net, null,
                    episode.FirstSeenTick, episode.LastSeenTick));
            }
            foreach (var entry in buildingEpisodes)
            {
                var episode = entry.Value;
                if (episode.Ongoing || episode.LastSeenTick < 0) continue;
                if (now - episode.LastSeenTick > DismissalLifetimeTicks) continue;
                var key = entry.Key;
                Building building = key.Building;
                if (building != null && (building.Destroyed || !building.Spawned)) continue;
                output.Add(MakeResolved((RecommendationKind)key.Kind, null, building,
                    episode.FirstSeenTick, episode.LastSeenTick));
            }
        }

        public void EndEvaluation(PowerGridSnapshot snapshot, int now)
        {
            Sweep(networkEpisodes, deadNetworkKeys, now);
            Sweep(buildingEpisodes, deadBuildingKeys, now);
            deadBuildingKeys.Clear();
            foreach (var entry in buildingEpisodes)
            {
                Building building = entry.Key.Building;
                if (building == null || building.Destroyed || !building.Spawned)
                    deadBuildingKeys.Add(entry.Key);
            }
            for (int i = 0; i < deadBuildingKeys.Count; i++) buildingEpisodes.Remove(deadBuildingKeys[i]);
            PruneDismissals(now);
        }

        public void Reset()
        {
            networkEpisodes.Clear();
            buildingEpisodes.Clear();
            dismissals.Clear();
        }

        private static int Update(Episode episode, bool active, int now)
        {
            episode.SeenThisPass = true;
            if (active)
            {
                if (episode.FirstSeenTick < 0) episode.FirstSeenTick = now;
                episode.LastSeenTick = now;
                episode.Ongoing = true;
                return now - episode.FirstSeenTick;
            }
            if (episode.Ongoing)
            {
                episode.Ongoing = false;
                if (episode.LastSeenTick < 0) episode.LastSeenTick = now;
            }
            return 0;
        }

        private static void Sweep<TKey>(Dictionary<TKey, Episode> episodes, List<TKey> dead, int now)
        {
            dead.Clear();
            if (episodes.Count == 0) return;
            foreach (var entry in episodes)
            {
                var episode = entry.Value;
                if (episode.SeenThisPass) continue;
                if (episode.Ongoing)
                {
                    episode.Ongoing = false;
                    if (episode.LastSeenTick < 0) episode.LastSeenTick = now;
                    continue;
                }
                if (episode.LastSeenTick >= 0 && now - episode.LastSeenTick > DismissalLifetimeTicks)
                    dead.Add(entry.Key);
            }
            for (int i = 0; i < dead.Count; i++) episodes.Remove(dead[i]);
        }

        private static PowerRecommendation MakeResolved(RecommendationKind kind, PowerNet net,
            Building building, int firstSeen, int lastSeen)
        {
            if (firstSeen < 0) firstSeen = lastSeen;
            return new PowerRecommendation
            {
                Kind = kind,
                NetworkId = net == null ? (int?)null : 0,
                Net = net,
                Building = building,
                Ongoing = false,
                FirstSeenTick = firstSeen,
                LastSeenTick = lastSeen
            };
        }

        private void PruneDismissals(int now)
        {
            for (int i = dismissals.Count - 1; i >= 0; i--)
            {
                var entry = dismissals[i];
                bool stale = now - entry.Tick > DismissalLifetimeTicks;
                bool gone = entry.Building != null && (entry.Building.Destroyed || !entry.Building.Spawned);
                if (stale || gone) dismissals.RemoveAt(i);
            }
        }
    }
}
