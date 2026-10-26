using System;
using System.Collections.Generic;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Turns one <see cref="PowerGridSnapshot"/> plus the recorded history into a ranked list of
    /// advisories. Pure observation: nothing here changes the power simulation.
    /// <para>
    /// Every advisory states what was observed, how long it has been true, and what to do about
    /// it. Where the game does not expose enough to be sure (variable-output generators, modded
    /// power systems, buildings whose idle state cannot be read) the advisory either is not
    /// produced at all, or says so explicitly in its evidence text.
    /// </para>
    /// </summary>
    public static class PowerDiagnostics
    {
        /// <summary>Hard cap on the produced list, so a heavily modded colony cannot flood the tab.</summary>
        public const int MaxAdvisories = 80;

        /// <summary>Maximum rows kept for one building-level advisory kind.</summary>
        public const int MaxBuildingRowsPerKind = 12;

        /// <summary>Below this, a negative balance is rounding noise rather than a deficit.</summary>
        public const float MinimumDeficitWatts = 1f;

        /// <summary>A generator reporting less than this share of its configured output counts as underperforming.</summary>
        public const float GeneratorUnderOutputFraction = 0.2f;

        /// <summary>Smallest configured output worth diagnosing, in watts.</summary>
        public const float MinimumGeneratorWatts = 50f;

        /// <summary>Measured draw must exceed configured draw by this factor to be reported.</summary>
        public const float DrawAboveConfiguredFactor = 1.5f;

        /// <summary>...and by at least this many watts, so tiny consumers are not flagged.</summary>
        public const float DrawAboveConfiguredMarginWatts = 25f;

        /// <summary>Batteries running out within this long escalate the advisory to critical.</summary>
        public const int CriticalDepletionTicks = PowerTime.TicksPerHour * 2;

        /// <summary>Share of recent samples that must show a deficit before history supports the advisory.</summary>
        public const float SupportingDeficitShare = 0.6f;

        /// <summary>Size of the per-kind row counters. Larger than the kind enum so it can grow.</summary>
        private const int KindSlots = 24;

        private readonly struct Candidate
        {
            public readonly PowerDiagnostic Advisory;
            public readonly float Magnitude;

            public Candidate(PowerDiagnostic advisory, float magnitude)
            {
                Advisory = advisory;
                Magnitude = magnitude;
            }
        }

        private static readonly List<Candidate> Candidates = new List<Candidate>(MaxAdvisories * 2);
        private static readonly List<PowerDiagnostic> ResolvedBuffer = new List<PowerDiagnostic>(16);
        private static readonly Comparison<Candidate> RankComparison = Rank;
        private static readonly int[] KindCounts = new int[KindSlots];

        /// <summary>
        /// Rebuilds <paramref name="output"/>. Called from the map component on refresh, never per
        /// frame and never from the UI's draw pass.
        /// </summary>
        public static void Evaluate(PowerGridSnapshot snapshot, PowerHistoryTracker history,
            PowerGridManagerSettings settings, PowerDiagnosticTracker tracker, List<PowerDiagnostic> output)
        {
            output.Clear();
            if (snapshot == null || settings == null || tracker == null) return;

            if (!settings.DiagnosticsEnabled)
            {
                // Episodes are measured against the snapshot's own tick; leaving stale ones behind
                // would report an absurd duration the moment diagnostics are switched back on.
                tracker.Reset();
                return;
            }

            int now = snapshot.Tick;
            tracker.BeginEvaluation();
            Candidates.Clear();
            for (int i = 0; i < KindCounts.Length; i++) KindCounts[i] = 0;

            EvaluateNetworks(snapshot, history, settings, tracker, now);
            EvaluateBuildings(snapshot, settings, tracker, now);

            // Conditions that cleared recently are reported as resolved rather than vanishing, so
            // the player can tell "fixed" apart from "no longer being looked at".
            ResolvedBuffer.Clear();
            tracker.CollectResolved(ResolvedBuffer, now);
            for (int i = 0; i < ResolvedBuffer.Count; i++)
            {
                PowerDiagnostic resolved = ResolvedBuffer[i];
                if (settings.IsMuted(resolved.Kind)) continue;
                if (tracker.IsDismissed(resolved.Kind, resolved.NetworkId, resolved.Building)) continue;
                if (Candidates.Count >= MaxAdvisories * 2) break;
                Candidates.Add(new Candidate(resolved, resolved.SortMagnitude));
            }

            Candidates.Sort(RankComparison);
            int limit = Candidates.Count < MaxAdvisories ? Candidates.Count : MaxAdvisories;
            for (int i = 0; i < limit; i++) output.Add(Candidates[i].Advisory);

            tracker.EndEvaluation(snapshot, now);
        }

        private static int Rank(Candidate left, Candidate right)
        {
            if (left.Advisory.Ongoing != right.Advisory.Ongoing) return left.Advisory.Ongoing ? -1 : 1;
            int severity = ((int)right.Advisory.Severity).CompareTo((int)left.Advisory.Severity);
            if (severity != 0) return severity;
            int magnitude = right.Magnitude.CompareTo(left.Magnitude);
            if (magnitude != 0) return magnitude;
            int kind = ((int)left.Advisory.Kind).CompareTo((int)right.Advisory.Kind);
            if (kind != 0) return kind;
            return (left.Advisory.NetworkId ?? 0).CompareTo(right.Advisory.NetworkId ?? 0);
        }

        // --- Networks --------------------------------------------------------

        private static void EvaluateNetworks(PowerGridSnapshot snapshot, PowerHistoryTracker history,
            PowerGridManagerSettings settings, PowerDiagnosticTracker tracker, int now)
        {
            IReadOnlyList<PowerNetworkSnapshot> networks = snapshot.Networks;
            if (networks == null) return;

            // History is recorded as map-wide aggregates, so it can only be quoted as evidence for
            // a specific network when that network is the only one on the map.
            float historicalDeficitShare = 0f;
            int historicalSamples = 0;
            if (networks.Count == 1 && history != null && history.HasSamples)
            {
                int endTick = history.NewestTick;
                historicalDeficitShare = PowerForecast.DeficitShare(history.Samples,
                    endTick - PowerTime.TicksPerDay, endTick, out historicalSamples);
            }

            float minimumDeficit = settings.DeficitAlertWatts;
            if (minimumDeficit < MinimumDeficitWatts) minimumDeficit = MinimumDeficitWatts;

            for (int i = 0; i < networks.Count; i++)
            {
                PowerNetworkSnapshot network = networks[i];
                if (network == null) continue;

                float deficit = network.ConsumptionWatts - network.GenerationWatts;
                bool inDeficit = deficit >= minimumDeficit && network.ConsumerCount > 0;
                bool hasStorage = network.BatteryCount > 0 && network.AvailableStoredWattDays > 0.001f;
                NetworkStatus status = PowerGridUtilities.EvaluateStatus(network, settings.HighLoadThreshold);

                // Register every condition on every pass, whether or not it is reported: that is
                // what lets the tracker close an episode when the condition stops being true.
                int deficitSustained = tracker.Register(network.Net, PowerDiagnosticKind.PersistentDeficit,
                    inDeficit && !hasStorage, now, settings.AlertCooldownTicks);
                int dischargeSustained = tracker.Register(network.Net, PowerDiagnosticKind.BatteryDischarge,
                    inDeficit && hasStorage, now, settings.AlertCooldownTicks);
                int highLoadSustained = tracker.Register(network.Net, PowerDiagnosticKind.HighLoad,
                    status == NetworkStatus.HighLoad, now, settings.AlertCooldownTicks);
                tracker.Register(network.Net, PowerDiagnosticKind.NoGeneration,
                    status == NetworkStatus.NoGeneration, now, settings.AlertCooldownTicks);

                if (status == NetworkStatus.NoGeneration)
                {
                    bool runningOnBatteries = network.BatteryCount > 0 && network.StoredWattDays > 0.001f;
                    Add(new PowerDiagnostic
                    {
                        Kind = PowerDiagnosticKind.NoGeneration,
                        Net = network.Net,
                        Severity = runningOnBatteries
                            ? PowerDiagnosticSeverity.Warning
                            : PowerDiagnosticSeverity.Critical,
                        NetworkId = network.Id,
                        SortMagnitude = network.ConsumptionWatts,
                        ConsumptionWatts = network.ConsumptionWatts,
                        AffectedCount = network.ConsumerCount,
                        StoredWattDays = network.StoredWattDays,
                        CapacityWattDays = network.CapacityWattDays,
                        ChargeFraction = network.ChargeFraction,
                        SustainedTicks = highLoadSustained
                    }, network.ConsumptionWatts, settings, tracker, now);
                    continue;
                }

                if (inDeficit)
                {
                    if (hasStorage)
                    {
                        AddDischargeAdvisory(network, deficit, dischargeSustained, settings, tracker, now,
                            historicalDeficitShare, historicalSamples);
                    }
                    else
                    {
                        Add(new PowerDiagnostic
                        {
                            Kind = PowerDiagnosticKind.PersistentDeficit,
                            Net = network.Net,
                            Severity = deficitSustained >= settings.PersistentDeficitTicks
                                ? PowerDiagnosticSeverity.Critical
                                : PowerDiagnosticSeverity.Warning,
                            NetworkId = network.Id,
                            SortMagnitude = deficit,
                            GenerationWatts = network.GenerationWatts,
                            ConsumptionWatts = network.ConsumptionWatts,
                            DeficitWatts = deficit,
                            SustainedTicks = deficitSustained,
                            AffectedCount = network.ConsumerCount,
                            StoredWattDays = network.StoredWattDays,
                            CapacityWattDays = network.CapacityWattDays,
                            ChargeFraction = network.ChargeFraction
                        }, deficit, settings, tracker, now);
                    }
                    continue;
                }

                if (status == NetworkStatus.HighLoad)
                {
                    float load = network.GenerationWatts > 0.01f
                        ? network.ConsumptionWatts / network.GenerationWatts
                        : 0f;
                    Add(new PowerDiagnostic
                    {
                        Kind = PowerDiagnosticKind.HighLoad,
                        Net = network.Net,
                        Severity = highLoadSustained >= settings.PersistentDeficitTicks
                            ? PowerDiagnosticSeverity.Warning
                            : PowerDiagnosticSeverity.Info,
                        NetworkId = network.Id,
                        SortMagnitude = network.ConsumptionWatts,
                        GenerationWatts = network.GenerationWatts,
                        ConsumptionWatts = network.ConsumptionWatts,
                        DeficitWatts = load,
                        SustainedTicks = highLoadSustained,
                        AffectedCount = network.ConsumerCount
                    }, network.ConsumptionWatts, settings, tracker, now);
                }
            }
        }

        private static void AddDischargeAdvisory(PowerNetworkSnapshot network, float deficit, int sustained,
            PowerGridManagerSettings settings, PowerDiagnosticTracker tracker, int now,
            float historicalDeficitShare, int historicalSamples)
        {
            BatteryForecastResult forecast = BatteryForecast.ForNetwork(network, settings.ForecastIncludeSelfDischarge);

            float charge = network.ChargeFraction ?? 0f;
            bool low = charge <= settings.LowBatteryWarningFraction;
            bool soon = forecast.TicksToEmpty.HasValue && forecast.TicksToEmpty.Value <= CriticalDepletionTicks;
            bool longRunning = sustained >= settings.PersistentDeficitTicks && charge < 0.5f;

            Add(new PowerDiagnostic
            {
                Kind = PowerDiagnosticKind.BatteryDischarge,
                Net = network.Net,
                Severity = low || soon || longRunning
                    ? PowerDiagnosticSeverity.Critical
                    : PowerDiagnosticSeverity.Warning,
                NetworkId = network.Id,
                SortMagnitude = deficit,
                GenerationWatts = network.GenerationWatts,
                ConsumptionWatts = network.ConsumptionWatts,
                DeficitWatts = deficit,
                SustainedTicks = sustained,
                StoredWattDays = network.StoredWattDays,
                CapacityWattDays = network.CapacityWattDays,
                ChargeFraction = network.ChargeFraction,
                DepletionTicks = forecast.TicksToEmpty,
                // Only meaningful when the map-wide series actually describes this network.
                AffectedCount = historicalSamples > 0 && historicalDeficitShare >= SupportingDeficitShare
                    ? historicalSamples
                    : 0
            }, deficit, settings, tracker, now);
        }

        // --- Buildings -------------------------------------------------------

        private static void EvaluateBuildings(PowerGridSnapshot snapshot, PowerGridManagerSettings settings,
            PowerDiagnosticTracker tracker, int now)
        {
            IReadOnlyList<BuildingPowerSnapshot> buildings = snapshot.Buildings;
            if (buildings == null) return;

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingPowerSnapshot building = buildings[i];
                if (building == null || building.Building == null) continue;

                bool isolated = building.Connection == PowerConnectionState.NoNetwork && building.IsConsumer;
                int isolatedSustained = tracker.Register(building.Building, PowerDiagnosticKind.IsolatedConsumer,
                    isolated, now, settings.AlertCooldownTicks);
                if (isolated && HasRoomFor(PowerDiagnosticKind.IsolatedConsumer))
                {
                    float potential = building.NominalConsumptionWatts ?? 0f;
                    Add(new PowerDiagnostic
                    {
                        Kind = PowerDiagnosticKind.IsolatedConsumer,
                        Severity = PowerDiagnosticSeverity.Warning,
                        Building = building.Building,
                        SortMagnitude = potential,
                        MeasuredWatts = building.ConsumptionWatts,
                        ConfiguredWatts = potential,
                        SustainedTicks = isolatedSustained
                    }, potential, settings, tracker, now);
                }

                bool pending = building.Connection == PowerConnectionState.MembershipPending;
                int pendingSustained = tracker.Register(building.Building, PowerDiagnosticKind.PendingMembership,
                    pending, now, settings.AlertCooldownTicks);
                if (pending && settings.ShowInfoDiagnostics && HasRoomFor(PowerDiagnosticKind.PendingMembership))
                {
                    Add(new PowerDiagnostic
                    {
                        Kind = PowerDiagnosticKind.PendingMembership,
                        Severity = PowerDiagnosticSeverity.Info,
                        Building = building.Building,
                        NetworkId = building.NetworkId,
                        SortMagnitude = building.ConsumptionWatts,
                        SustainedTicks = pendingSustained
                    }, 0f, settings, tracker, now);
                }

                if (building.HasUnsupportedComponent && HasRoomFor(PowerDiagnosticKind.UnsupportedComponent))
                {
                    Add(new PowerDiagnostic
                    {
                        Kind = PowerDiagnosticKind.UnsupportedComponent,
                        Severity = PowerDiagnosticSeverity.Info,
                        Building = building.Building,
                        NetworkId = building.NetworkId,
                        SortMagnitude = building.ConsumptionWatts + building.GenerationWatts,
                        MeasuredWatts = building.ConsumptionWatts,
                        GenerationWatts = building.GenerationWatts,
                        SustainedTicks = 0
                    }, 0f, settings, tracker, now);
                }

                // A consumer whose flick switch is off should report no draw. If it still does, the
                // switch is not controlling the part that is consuming.
                bool offButDrawing = building.IsFlickable && building.SwitchIsOn == false &&
                                     building.ConsumptionWatts > 0.01f;
                int offSustained = tracker.Register(building.Building, PowerDiagnosticKind.SwitchedOffButDrawing,
                    offButDrawing, now, settings.AlertCooldownTicks);
                if (offButDrawing && HasRoomFor(PowerDiagnosticKind.SwitchedOffButDrawing))
                {
                    Add(new PowerDiagnostic
                    {
                        Kind = PowerDiagnosticKind.SwitchedOffButDrawing,
                        Severity = PowerDiagnosticSeverity.Warning,
                        Building = building.Building,
                        NetworkId = building.NetworkId,
                        SortMagnitude = building.ConsumptionWatts,
                        MeasuredWatts = building.ConsumptionWatts,
                        ConfiguredWatts = building.NominalConsumptionWatts ?? 0f,
                        SustainedTicks = offSustained
                    }, building.ConsumptionWatts, settings, tracker, now);
                }

                EvaluateGenerator(building, settings, tracker, now);

                // Measured draw well above the configured draw: either a variable consumer or a
                // component whose real behaviour differs from its definition. Information only,
                // because both are legitimate in some mods.
                if (building.IsConsumer && building.NominalConsumptionWatts.HasValue &&
                    building.Connection == PowerConnectionState.Connected &&
                    settings.ShowInfoDiagnostics && HasRoomFor(PowerDiagnosticKind.DrawAboveConfigured))
                {
                    float nominal = building.NominalConsumptionWatts.Value;
                    float threshold = nominal * DrawAboveConfiguredFactor + DrawAboveConfiguredMarginWatts;
                    if (nominal > 0f && building.ConsumptionWatts > threshold)
                    {
                        Add(new PowerDiagnostic
                        {
                            Kind = PowerDiagnosticKind.DrawAboveConfigured,
                            Severity = PowerDiagnosticSeverity.Info,
                            Building = building.Building,
                            NetworkId = building.NetworkId,
                            SortMagnitude = building.ConsumptionWatts - nominal,
                            MeasuredWatts = building.ConsumptionWatts,
                            ConfiguredWatts = nominal,
                            SustainedTicks = 0
                        }, building.ConsumptionWatts - nominal, settings, tracker, now);
                    }
                }
            }
        }

        /// <summary>
        /// Generator output far below its configured output, reported only after it has stayed that
        /// way for a configurable period. The delay is what stops solar panels at night and wind
        /// turbines during a calm from raising an advisory every single evening; the advisory text
        /// still tells the player to rule out those causes before acting.
        /// </summary>
        private static void EvaluateGenerator(BuildingPowerSnapshot building, PowerGridManagerSettings settings,
            PowerDiagnosticTracker tracker, int now)
        {
            if (!building.IsGenerator || !building.ConfiguredGenerationWatts.HasValue) return;
            if (building.Connection != PowerConnectionState.Connected) return;

            float configured = building.ConfiguredGenerationWatts.Value;
            if (configured < MinimumGeneratorWatts) return;

            // A switched-off or powered-down generator producing nothing is expected, not a fault.
            bool expectedOff = building.SwitchIsOn == false || building.HasOffComponents;
            bool under = !expectedOff && building.GenerationWatts < configured * GeneratorUnderOutputFraction;

            int sustained = tracker.Register(building.Building, PowerDiagnosticKind.GeneratorUnderOutput, under, now,
                settings.AlertCooldownTicks);
            if (!under || sustained < settings.GeneratorUnderOutputTicks) return;
            if (!HasRoomFor(PowerDiagnosticKind.GeneratorUnderOutput)) return;

            Add(new PowerDiagnostic
            {
                Kind = PowerDiagnosticKind.GeneratorUnderOutput,
                Severity = PowerDiagnosticSeverity.Warning,
                Building = building.Building,
                NetworkId = building.NetworkId,
                SortMagnitude = configured - building.GenerationWatts,
                MeasuredWatts = building.GenerationWatts,
                ConfiguredWatts = configured,
                SustainedTicks = sustained
            }, configured - building.GenerationWatts, settings, tracker, now);
        }

        // --- Helpers ---------------------------------------------------------

        private static bool HasRoomFor(PowerDiagnosticKind kind)
        {
            int slot = (int)kind;
            if (slot < 0 || slot >= KindCounts.Length) return true;
            if (KindCounts[slot] >= MaxBuildingRowsPerKind) return false;
            KindCounts[slot]++;
            return true;
        }

        /// <summary>
        /// Adds an advisory unless its kind is muted or the player dismissed this particular one.
        /// Fills in the episode window, which every advisory reports.
        /// </summary>
        private static void Add(PowerDiagnostic advisory, float magnitude, PowerGridManagerSettings settings,
            PowerDiagnosticTracker tracker, int now)
        {
            if (settings.IsMuted(advisory.Kind)) return;
            if (tracker.IsDismissed(advisory.Kind, advisory.NetworkId, advisory.Building)) return;
            if (Candidates.Count >= MaxAdvisories * 2) return;

            advisory.Ongoing = true;
            advisory.ResolvedTick = -1;
            advisory.LastSeenTick = now;
            advisory.FirstSeenTick = now - (advisory.SustainedTicks > 0 ? advisory.SustainedTicks : 0);
            advisory.Muted = false;
            advisory.Dismissed = false;

            // Kept so the advisory can still be described after the condition clears.
            if (advisory.Net != null) tracker.Remember(advisory.Net, advisory.Kind, advisory);
            else if (advisory.Building != null) tracker.Remember(advisory.Building, advisory.Kind, advisory);

            Candidates.Add(new Candidate(advisory, magnitude));
        }
    }
}
