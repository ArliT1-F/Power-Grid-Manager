using UnityEngine;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Map-wide aggregate dashboard. Every tile states what it measures; measured
    /// values are never mixed with estimates, and values the mod cannot verify are
    /// shown as unavailable with an explanation instead of a fabricated number.
    /// </summary>
    public sealed class PowerOverviewPanel
    {
        private const int Columns = 4;
        private const float TileGap = 6f;

        private static int CountHighLoadNetworks(PowerGridSnapshot snapshot, float threshold)
        {
            int count = 0;
            for (int i = 0; i < snapshot.Networks.Count; i++)
            {
                if (PowerGridUtilities.EvaluateStatus(snapshot.Networks[i], threshold) == NetworkStatus.HighLoad) count++;
            }
            return count;
        }

        private static int CountSwitchedOffConsumers(PowerGridSnapshot snapshot)
        {
            int count = 0;
            for (int i = 0; i < snapshot.Buildings.Count; i++)
            {
                BuildingPowerSnapshot building = snapshot.Buildings[i];
                if (building.IsFlickable && building.SwitchIsOn == false) count++;
            }
            return count;
        }

        public void Draw(Rect rect, PowerGridSnapshot snapshot, int now, PowerGridManagerSettings settings)
        {
            float y = rect.y;

            Rect headerRect = new Rect(rect.x, y, rect.width, 24f);
            PowerUiDraw.DrawText(headerRect, "PGM_Overview_Title".Translate(), Color.white);
            string age = PowerFormatting.FormatDuration(now - snapshot.Tick);
            PowerUiDraw.DrawText(new Rect(rect.x, y, rect.width, 24f),
                "PGM_Overview_Age".Translate(age), PowerUiDraw.Muted, TextAnchor.MiddleRight);
            y += 26f;

            float tileWidth = (rect.width - TileGap * (Columns - 1)) / Columns;
            const int Rows = 5;
            const float NotesHeight = 64f;
            float gridTop = y;
            // Prefer the standard tile height, but shrink to fit the window on small screens.
            float availableForGrid = rect.yMax - gridTop - NotesHeight;
            float tileHeight = (availableForGrid - (Rows - 1) * TileGap) / Rows;
            tileHeight = Mathf.Clamp(tileHeight, 40f, PowerUiDraw.TileHeight);

            float net = snapshot.NetWatts;
            int highLoadCount = CountHighLoadNetworks(snapshot, settings.HighLoadThreshold);
            int switchedOffCount = CountSwitchedOffConsumers(snapshot);


            // Row 0: measured power.
            DrawTile(rect, gridTop, 0, 0, tileWidth, tileHeight, "PGM_Tile_CurrentGeneration",
                PowerFormatting.FormatWatts(snapshot.GenerationWatts),
                snapshot.GenerationWatts > 0.01f ? PowerUiDraw.Good : PowerUiDraw.Muted,
                "PGM_Tip_CurrentGeneration".Translate());
            DrawTile(rect, gridTop, 0, 1, tileWidth, tileHeight, "PGM_Tile_CurrentConsumption",
                PowerFormatting.FormatWatts(snapshot.ConsumptionWatts),
                snapshot.ConsumptionWatts > 0.01f ? Color.white : PowerUiDraw.Muted,
                "PGM_Tip_CurrentConsumption".Translate());
            DrawTile(rect, gridTop, 0, 2, tileWidth, tileHeight, "PGM_Tile_NetBalance",
                PowerFormatting.FormatWattsSigned(net),
                net < -0.01f ? PowerUiDraw.NoticeColor : net > 0.01f ? PowerUiDraw.Good : Color.white,
                "PGM_Tip_NetBalance".Translate());
            DrawTile(rect, gridTop, 0, 3, tileWidth, tileHeight, "PGM_Tile_LoadRatio",
                PowerFormatting.FormatCharge(snapshot.LoadRatio),
                snapshot.LoadRatio.HasValue && snapshot.LoadRatio.Value > 1f
                    ? PowerUiDraw.NoticeColor
                    : Color.white,
                "PGM_Tip_LoadRatio".Translate());

            // Row 1: storage and configured output.
            DrawTile(rect, gridTop, 1, 0, tileWidth, tileHeight, "PGM_Tile_ConfiguredGeneration",
                PowerFormatting.FormatWattsUnknown(snapshot.ConfiguredGenerationWatts), PowerUiDraw.Muted,
                "PGM_Tip_ConfiguredGeneration".Translate());
            DrawTile(rect, gridTop, 1, 1, tileWidth, tileHeight, "PGM_Tile_StoredEnergy",
                PowerFormatting.FormatEnergy(snapshot.StoredWattDays), Color.white,
                "PGM_Tip_StoredEnergy".Translate());
            DrawTile(rect, gridTop, 1, 2, tileWidth, tileHeight, "PGM_Tile_StorageCapacity",
                PowerFormatting.FormatEnergy(snapshot.CapacityWattDays), Color.white,
                "PGM_Tip_StorageCapacity".Translate());
            DrawTile(rect, gridTop, 1, 3, tileWidth, tileHeight, "PGM_Tile_Charge",
                PowerFormatting.FormatCharge(snapshot.ChargeFraction), Color.white,
                "PGM_Tip_Charge".Translate());

            // Row 2: topology counts.
            DrawTile(rect, gridTop, 2, 0, tileWidth, tileHeight, "PGM_Tile_Networks",
                snapshot.Networks.Count.ToString(), Color.white, "PGM_Tip_Networks".Translate());
            DrawTile(rect, gridTop, 2, 1, tileWidth, tileHeight, "PGM_Tile_Consumers",
                snapshot.ConsumerCount.ToString(), Color.white, "PGM_Tip_Consumers".Translate());
            DrawTile(rect, gridTop, 2, 2, tileWidth, tileHeight, "PGM_Tile_Generators",
                snapshot.GeneratorCount.ToString(), Color.white, "PGM_Tip_Generators".Translate());
            DrawTile(rect, gridTop, 2, 3, tileWidth, tileHeight, "PGM_Tile_Batteries",
                snapshot.BatteryCount.ToString(), Color.white, "PGM_Tip_Batteries".Translate());

            // Row 3: problems and data quality.
            DrawTile(rect, gridTop, 3, 0, tileWidth, tileHeight, "PGM_Tile_DeficitNetworks",
                snapshot.DeficitNetworkCount.ToString(),
                snapshot.DeficitNetworkCount > 0 ? PowerUiDraw.NoticeColor : Color.white,
                "PGM_Tip_DeficitNetworks".Translate());
            DrawTile(rect, gridTop, 3, 1, tileWidth, tileHeight, "PGM_Tile_HighLoadNetworks",
                highLoadCount.ToString(), highLoadCount > 0 ? PowerUiDraw.Warn : Color.white,
                "PGM_Tip_HighLoadNetworks".Translate());
            DrawTile(rect, gridTop, 3, 2, tileWidth, tileHeight, "PGM_Tile_Disconnected",
                snapshot.DisconnectedConsumerCount.ToString(),
                snapshot.DisconnectedConsumerCount > 0 ? PowerUiDraw.Warn : Color.white,
                "PGM_Tip_Disconnected".Translate());
            DrawTile(rect, gridTop, 3, 3, tileWidth, tileHeight, "PGM_Tile_DataNotices",
                snapshot.DataNoticeCount.ToString(),
                snapshot.DataNoticeCount > 0 ? PowerUiDraw.Warn : Color.white,
                "PGM_Tip_DataNotices".Translate());

            // Row 4: comparison figures and cadence.
            DrawTile(rect, gridTop, 4, 0, tileWidth, tileHeight, "PGM_Tile_NominalDraw",
                PowerFormatting.FormatWattsUnknown(snapshot.NominalConsumptionWatts), PowerUiDraw.Muted,
                "PGM_Tip_NominalDraw".Translate());
            DrawTile(rect, gridTop, 4, 1, tileWidth, tileHeight, "PGM_Tile_Unsupported",
                snapshot.UnsupportedComponentCount.ToString(),
                snapshot.UnsupportedComponentCount > 0 ? PowerUiDraw.Warn : Color.white,
                "PGM_Tip_Unsupported".Translate());
            DrawTile(rect, gridTop, 4, 2, tileWidth, tileHeight, "PGM_Tile_SwitchedOff",
                switchedOffCount.ToString(), Color.white, "PGM_Tip_SwitchedOff".Translate());
            DrawTile(rect, gridTop, 4, 3, tileWidth, tileHeight, "PGM_Tile_Sampling",
                PowerFormatting.FormatDuration(settings.SampleIntervalTicks),
                settings.HistoryEnabled ? Color.white : PowerUiDraw.Muted,
                "PGM_Tip_Sampling".Translate());

            y = gridTop + Rows * (tileHeight + TileGap) + 6f;
            Rect explanation = new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y));
            DrawExplanation(explanation, snapshot);
        }

        private static void DrawTile(Rect area, float gridTop, int row, int column, float tileWidth, float tileHeight,
            string labelKey, string value, Color valueColor, string tooltip)
        {
            float x = area.x + column * (tileWidth + TileGap);
            float top = gridTop + row * (tileHeight + TileGap);
            Rect tile = new Rect(x, top, tileWidth, tileHeight);
            PowerUiDraw.MetricTile(tile, labelKey.Translate(), value, valueColor, tooltip);
        }

        private static void DrawExplanation(Rect rect, PowerGridSnapshot snapshot)
        {
            if (rect.height < 30f) return;
            PowerUiDraw.Panel(rect);
            float y = rect.y + 6f;
            Rect header = new Rect(rect.x + 8f, y, rect.width - 16f, 20f);
            PowerUiDraw.SectionHeader(header, "PGM_Overview_NotesTitle".Translate());
            y += 22f;

            string notes = "PGM_Overview_Notes".Translate(
                PowerFormatting.FormatWatts(snapshot.GenerationWatts),
                PowerFormatting.FormatWatts(snapshot.ConsumptionWatts));
            PowerUiDraw.WrappedText(new Rect(rect.x + 8f, y, rect.width - 16f, rect.yMax - y - 6f), notes,
                PowerUiDraw.Muted);
        }
    }
}
