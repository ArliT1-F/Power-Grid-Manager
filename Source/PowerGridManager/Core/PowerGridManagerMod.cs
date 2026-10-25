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
            settings.RefreshIntervalTicks = (int)Widgets.HorizontalSlider(
                listing.GetRect(26f), settings.RefreshIntervalTicks, 60f, 600f, true,
                "PGM_RefreshInterval_Tip".Translate());

            listing.Gap(10f);
            listing.Label("PGM_SampleInterval_Label".Translate(settings.SampleIntervalTicks));
            settings.SampleIntervalTicks = (int)Widgets.HorizontalSlider(
                listing.GetRect(26f), settings.SampleIntervalTicks, 250f, 15000f, true,
                "PGM_SampleInterval_Tip".Translate());

            listing.Gap(10f);
            listing.Label("PGM_Retention_Label".Translate(settings.RetentionDays));
            settings.RetentionDays = (int)Widgets.HorizontalSlider(
                listing.GetRect(26f), settings.RetentionDays, 1f, 30f, true,
                "PGM_Retention_Tip".Translate());

            listing.Gap(10f);
            listing.Label("PGM_HighLoad_Label".Translate((settings.HighLoadThreshold * 100f).ToString("F0")));
            settings.HighLoadThreshold = Widgets.HorizontalSlider(
                listing.GetRect(26f), settings.HighLoadThreshold, 0.50f, 1.50f, true,
                "PGM_HighLoad_Tip".Translate(), "50%", "150%");

            listing.End();
        }
    }
}
