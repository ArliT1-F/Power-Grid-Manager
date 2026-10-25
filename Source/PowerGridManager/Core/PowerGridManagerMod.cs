using UnityEngine;
using Verse;

namespace PowerGridManager
{
    public sealed class PowerGridManagerMod : Mod
    {
        private readonly PowerGridManagerSettings settings;
        public static PowerGridManagerSettings Settings { get; private set; }

        public PowerGridManagerMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<PowerGridManagerSettings>();
            Settings = settings;
        }

        public override string SettingsCategory()
        {
            return "PGM_ModName".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label("PGM_RefreshInterval_Label".Translate(settings.RefreshIntervalTicks));
            settings.RefreshIntervalTicks = Mathf.RoundToInt(Slider(listing, settings.RefreshIntervalTicks, 60f, 600f,
                "PGM_RefreshInterval_Tip", null, null));
            listing.Gap(8f);

            listing.CheckboxLabeled("PGM_HistoryEnabled_Label".Translate(), ref settings.HistoryEnabled,
                "PGM_HistoryEnabled_Tip".Translate());

            listing.Label("PGM_SampleInterval_Label".Translate(settings.SampleIntervalTicks));
            settings.SampleIntervalTicks = Mathf.RoundToInt(Slider(listing, settings.SampleIntervalTicks, 250f, 15000f,
                "PGM_SampleInterval_Tip", null, null));
            listing.Gap(8f);

            listing.Label("PGM_Retention_Label".Translate(settings.RetentionDays));
            settings.RetentionDays = Mathf.RoundToInt(Slider(listing, settings.RetentionDays, 1f, 30f,
                "PGM_Retention_Tip", "1", "30"));
            listing.Gap(8f);

            listing.Label("PGM_HighLoad_Label".Translate((settings.HighLoadThreshold * 100f).ToString("F0")));
            settings.HighLoadThreshold = Slider(listing, settings.HighLoadThreshold, 0.50f, 1.50f,
                "PGM_HighLoad_Tip", "50%", "150%");
            listing.Gap(8f);

            listing.Label("PGM_HighConsumption_Label".Translate(settings.HighConsumptionWatts.ToString("F0")));
            settings.HighConsumptionWatts = Slider(listing, settings.HighConsumptionWatts, 0f, 5000f,
                "PGM_HighConsumption_Tip", "0 W", "5 kW");

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }

        /// <summary>
        /// Slider whose explanation is a real tooltip. Vanilla's slider "label" argument is
        /// drawn as a caption under the rail, so a sentence there would render as clutter.
        /// </summary>
        private static float Slider(Listing_Standard listing, float value, float min, float max,
            string tooltipKey, string leftLabel, string rightLabel)
        {
            Rect rect = listing.GetRect(26f);
            TooltipHandler.TipRegion(rect, new TipSignal(tooltipKey.Translate()));
            return Widgets.HorizontalSlider(rect, value, min, max, true, null, leftLabel, rightLabel);
        }
    }
}
