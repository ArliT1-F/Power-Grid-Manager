using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Turns newly detected advisories into the game's own on-screen messages.
    /// <para>
    /// Deliberately conservative: only Warning and Critical advisories are announced, only when
    /// the episode is genuinely new (a condition that flickers back inside the cooldown window is
    /// the same episode, not a new one), and at most a couple per refresh. Everything is also
    /// gated behind a setting, so a player who wants a quiet game can turn it off; the
    /// Diagnostics tab keeps the full list either way.
    /// </summary>
    public static class PowerDiagnosticNotifier
    {
        public const int MaxMessagesPerPass = 2;

        public static void Notify(IReadOnlyList<PowerDiagnostic> advisories, PowerGridManagerSettings settings,
            PowerDiagnosticTracker tracker, int now)
        {
            if (settings == null || tracker == null || advisories == null) return;
            if (!settings.ShowPowerMessages) return;

            // The map component only ticks while playing, but the dashboard can also trigger a
            // refresh during start-up; never queue messages before the game is actually running.
            if (Current.ProgramState != ProgramState.Playing) return;

            // The cooldown is checked once, before the burst: it is the minimum gap between
            // announcements, not between the individual messages of one burst. Checking it per
            // message would mean a player with several new problems only ever hears about the
            // first one, and would wait a full cooldown to hear about the rest.
            if (tracker.LastAnnouncementTick >= 0 &&
                now - tracker.LastAnnouncementTick < settings.AlertCooldownTicks)
            {
                return;
            }

            int sent = 0;
            for (int i = 0; i < advisories.Count && sent < MaxMessagesPerPass; i++)
            {
                PowerDiagnostic advisory = advisories[i];
                if (advisory == null || !advisory.Ongoing) continue;
                if (advisory.Severity == PowerDiagnosticSeverity.Info) continue;
                if (!IsNewEpisode(advisory, tracker)) continue;

                MessageTypeDef type = advisory.Severity == PowerDiagnosticSeverity.Critical
                    ? MessageTypeDefOf.NegativeEvent
                    : MessageTypeDefOf.NeutralEvent;
                Messages.Message(MessageText(advisory), type);

                if (advisory.Net != null) tracker.MarkAnnounced(advisory.Net, advisory.Kind);
                else if (advisory.Building != null) tracker.MarkAnnounced(advisory.Building, advisory.Kind);
                tracker.NoteAnnouncement(now);
                sent++;
            }
        }

        private static bool IsNewEpisode(PowerDiagnostic advisory, PowerDiagnosticTracker tracker)
        {
            if (advisory.Net != null) return tracker.IsNewEpisode(advisory.Net, advisory.Kind);
            if (advisory.Building != null) return tracker.IsNewEpisode(advisory.Building, advisory.Kind);
            return false;
        }

        private static string MessageText(PowerDiagnostic advisory)
        {
            switch (advisory.Kind)
            {
                case PowerDiagnosticKind.PersistentDeficit:
                    return "PGM_Msg_Deficit".Translate(advisory.NetworkId ?? 0,
                        PowerFormatting.FormatWatts(advisory.DeficitWatts));
                case PowerDiagnosticKind.BatteryDischarge:
                    return "PGM_Msg_BatteryDischarge".Translate(advisory.NetworkId ?? 0,
                        PowerFormatting.FormatCharge(advisory.ChargeFraction));
                case PowerDiagnosticKind.NoGeneration:
                    return "PGM_Msg_NoGeneration".Translate(advisory.NetworkId ?? 0);
                case PowerDiagnosticKind.HighLoad:
                    return "PGM_Msg_HighLoad".Translate(advisory.NetworkId ?? 0,
                        PowerFormatting.FormatCharge(advisory.DeficitWatts));
                case PowerDiagnosticKind.IsolatedConsumer:
                    return "PGM_Msg_IsolatedConsumer".Translate(PowerGridUtilities.BuildingLabel(advisory.Building),
                        PowerFormatting.FormatWatts(advisory.ConfiguredWatts));
                case PowerDiagnosticKind.GeneratorUnderOutput:
                    return "PGM_Msg_GeneratorIdle".Translate(PowerGridUtilities.BuildingLabel(advisory.Building),
                        PowerFormatting.FormatWatts(advisory.ConfiguredWatts));
                case PowerDiagnosticKind.SwitchedOffButDrawing:
                    return "PGM_Msg_OffButDrawing".Translate(PowerGridUtilities.BuildingLabel(advisory.Building),
                        PowerFormatting.FormatWatts(advisory.MeasuredWatts));
                default:
                    return "PGM_Msg_Generic".Translate();
            }
        }
    }
}
