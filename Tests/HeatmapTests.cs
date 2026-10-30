// Heatmap cache, viewport and interaction regressions against inert UI/game doubles.
// Counting submitted widgets and snapshot reads is deterministic, but does not measure FPS.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PowerGridManager.Tests
{
    public static class HeatmapTests
    {
        private const float RowHeight = 28f;
        private static int nextId = 1000000;
        private static readonly Rect NormalViewport = new Rect(30f, 20f, 900f, 222f);

        public static int Run()
        {
            TestAssert.UsePhase("heatmap");
            int start = TestAssert.Checks;
            MapProjection();
            LargeListViewportAndActions();
            RankingAndCacheInvalidation();
            EmptyAndNarrowViews();
            Console.WriteLine("Heatmap cache/viewport checks passed: " + (TestAssert.Checks - start));
            return TestAssert.Checks - start;
        }

        private static void MapProjection()
        {
            Rect image = new Rect(10f, 20f, 200f, 100f);
            Rect footprint = PowerHeatmapPanel.MapRect(image, 2f, 50, new CellRect(3, 4, 2, 3));
            TestAssert.Equal(footprint.x, 16f, "map footprint preserves x position");
            TestAssert.Equal(footprint.y, 106f, "north is at the top of the map");
            TestAssert.Equal(footprint.width, 4f, "map footprint preserves width");
            TestAssert.Equal(footprint.height, 6f, "map footprint preserves height");
            Rect north = PowerHeatmapPanel.MapRect(image, 2f, 50, new CellRect(0, 49, 1, 1));
            TestAssert.Equal(north.y, image.y, "northern edge aligns with image top");
        }

        private static void LargeListViewportAndActions()
        {
            const int count = 10000;
            var rows = new List<BuildingPowerSnapshot>(count);
            for (int i = 0; i < count; i++)
                rows.Add(Row("load-" + i.ToString("D5"), i + 1));
            var counted = new CountingList<BuildingPowerSnapshot>(rows);
            var snapshot = new PowerGridSnapshot { Buildings = counted, Tick = 100 };
            var panel = new PowerHeatmapPanel();
            var state = new PowerUiState();
            var settings = new PowerGridManagerSettings();

            using (var ui = new WidgetProbe())
            {
                Mouse.Position = new Vector2(10f, RowHeight + 5f);
                panel.Draw(NormalViewport, snapshot, state, settings, null);
                TestAssert.Equal(counted.IndexReads, count, "first draw visits each snapshot building once");
                TestAssert.Equal(ui.Starts, 1, "large list starts one scroll view");
                TestAssert.Equal(ui.Ends, 1, "large list ends its scroll view");
                TestAssert.Equal(ui.Outer.height, 168f, "fixture viewport is exactly six rows high");
                TestAssert.Equal(ui.View.height, count * RowHeight, "full scroll range is preserved");
                TestAssert.Equal(ui.Rows.Count, 7, "top viewport submits six rows plus one overscan, not 10,000");
                TestAssert.Equal(ui.NameAt(0), "Load-09999", "top row is highest draw");
                TestAssert.Equal(ui.NameAt(5), "Load-09994", "last visible top row has the right rank");
                TestAssert.Equal(ui.NameAt(6), "Load-09993", "one row is overscanned below the viewport");
                TestAssert.Equal(ui.Rows[5].y, 5f * RowHeight, "rows retain their full-list positions");
                TestAssert.Equal(ui.RowTooltipCount, 1, "only the hovered row formats a detailed tooltip");
                TestAssert.Check(ui.TooltipAt(1).Contains("Load-09998"), "hovered tooltip uses the correct building");

                ui.Clear();
                DebugViewSettings.drawTooltipEdges = true;
                panel.Draw(NormalViewport, snapshot, state, settings, null);
                TestAssert.Equal(ui.RowTooltipCount, 7, "tooltip-edge debugger still sees submitted rows");
                TestAssert.Equal(counted.IndexReads, count, "debug tooltip pass also reuses rankings");

                ui.Clear();
                state.HeatmapScroll.y = 3000f * RowHeight;
                panel.Draw(NormalViewport, snapshot, state, settings, null);
                TestAssert.Equal(counted.IndexReads, count, "scrolling reuses filtered and ranked rows");
                TestAssert.Equal(ui.Rows.Count, 8, "middle viewport adds only one overscan row at each edge");
                TestAssert.Equal(ui.Rows[0].y, 2999f * RowHeight, "top overscan retains its absolute position");
                TestAssert.Equal(ui.Rows[1].y, 3000f * RowHeight, "middle starts at absolute row position");
                TestAssert.Equal(ui.NameAt(1), "Load-06999", "middle viewport contains the correct row");
                TestAssert.Equal(ui.NameAt(6), "Load-06994", "middle viewport includes its final row");

                ui.Clear();
                state.HeatmapScroll.y += 0.5f;
                panel.Draw(NormalViewport, snapshot, state, settings, null);
                TestAssert.Equal(ui.Rows.Count, 9, "partial rows and both overscan rows stay bounded");
                TestAssert.Equal(ui.Rows[1].y, 3000f * RowHeight, "partial top row is retained");
                TestAssert.Equal(ui.Rows[7].y, 3006f * RowHeight, "partial bottom row is retained");
                TestAssert.Equal(counted.IndexReads, count, "fractional scrolling does not rebuild");

                ui.Clear();
                state.HeatmapScroll.y = 0f;
                Widgets.ScrollPositionOverride = previous => new Vector2(previous.x, 123.5f);
                panel.Draw(NormalViewport, snapshot, state, settings, null);
                TestAssert.Equal(ui.Rows[1].y, 4f * RowHeight,
                    "viewport uses the scroll position returned by BeginScrollView");
                TestAssert.Equal(ui.Rows.Count, 9, "scroll input includes partial and overscan edge rows");
                TestAssert.Equal(counted.IndexReads, count, "scroll input does not re-read the snapshot");

                ui.Clear();
                state.HeatmapScroll.y = count * RowHeight - ui.LastOuterHeight;
                Mouse.Position = new Vector2(10f, (count - 1) * RowHeight + 5f);
                WidgetsQueue.ClickInvisibleAt(6);
                panel.Draw(NormalViewport, snapshot, state, settings, null);
                TestAssert.Equal(ui.Rows.Count, 7, "bottom viewport remains bounded");
                TestAssert.Equal(ui.NameAt(6), "Load-00000", "the final row is reachable");
                TestAssert.Same(state.SelectedBuilding, rows[0].Building, "click selects the visible bottom row");
                TestAssert.Check(ui.TooltipAt(6).Contains("Load-00000"), "bottom row retains its tooltip");

                ui.Clear();
                WidgetsQueue.ClickTextAt(7 + 6 * 2); // seven mode buttons, then Inspect/Locate per row
                panel.Draw(NormalViewport, snapshot, state, settings, null);
                TestAssert.Equal(Find.WindowStack.windows.Count, 1, "Inspect opens one info card");
                var card = Find.WindowStack.windows[0] as Dialog_InfoCard;
                TestAssert.NotNull(card, "Inspect opens a vanilla info card");
                TestAssert.Same(card.Thing, rows[0].Building, "Inspect targets the visible bottom row");
                TestAssert.Check(ui.HasSelectedBackground(6), "selected row remains highlighted after scrolling");
                Find.WindowStack.windows.Clear();

                ui.Clear();
                CameraJumper.LastJumpAndSelect = null;
                WidgetsQueue.ClickTextAt(7 + 6 * 2 + 1);
                panel.Draw(NormalViewport, snapshot, state, settings, null);
                TestAssert.Same(CameraJumper.LastJumpAndSelect, rows[0].Building,
                    "Locate targets the visible bottom row");
                TestAssert.Equal(counted.IndexReads, count, "selection and actions do not rebuild rankings");

                ui.Clear();
                var emptyManagement = new PowerManagementController();
                Thing.UniqueLoadIdCalls = 0;
                ClickMode(6);
                panel.Draw(NormalViewport, snapshot, state, settings, emptyManagement);
                TestAssert.Equal(counted.IndexReads, count * 2, "critical mode rebuilds the large ranking once");
                TestAssert.Equal(Thing.UniqueLoadIdCalls, 0,
                    "an empty management registry does not allocate per-building IDs");
                ui.Clear();
                panel.Draw(NormalViewport, snapshot, state, settings, emptyManagement);
                TestAssert.Equal(counted.IndexReads, count * 2, "critical ranking is reused on later draws");
                TestAssert.Equal(Thing.UniqueLoadIdCalls, 0, "empty-registry redraw avoids ID lookups");

                // Shrinking a scrolled list must not leave every result outside the viewport.
                var smaller = new CountingList<BuildingPowerSnapshot>(new List<BuildingPowerSnapshot>
                {
                    Row("small a", 1f), Row("small b", 2f)
                });
                ui.Clear();
                panel.Draw(NormalViewport, new PowerGridSnapshot { Buildings = smaller, Tick = 100 },
                    state, settings, null);
                TestAssert.Equal(smaller.IndexReads, 2, "new snapshot is ranked even at the same tick");
                TestAssert.Equal(state.HeatmapScroll.y, 0f, "out-of-range scroll is corrected after a shrink");
                TestAssert.Equal(ui.Rows.Count, 2, "all results of the smaller snapshot remain visible");
                TestAssert.Equal(ui.NameAt(0), "Small b", "new snapshot has its own ranking");
            }
        }

        private static void RankingAndCacheInvalidation()
        {
            BuildingPowerSnapshot alpha = Row("alpha pump", 100f, 500f);
            alpha.IsFlickable = true;
            alpha.SwitchIsOn = false;
            BuildingPowerSnapshot bravo = Row("bravo lamp", 600f);
            bravo.Connection = PowerConnectionState.NoNetwork;
            BuildingPowerSnapshot gamma = Row("gamma battery", 0f);
            gamma.IsBattery = true;
            gamma.StoredWattDays = 100f;
            gamma.CapacityWattDays = 1000f;
            BuildingPowerSnapshot delta = Row("delta generator", 0f, 900f);
            BuildingPowerSnapshot echo = Row("echo battery", 0f);
            echo.IsBattery = true;
            echo.StoredWattDays = 900f;
            echo.CapacityWattDays = 1000f;

            var counted = new CountingList<BuildingPowerSnapshot>(new List<BuildingPowerSnapshot>
                { alpha, bravo, gamma, delta, echo });
            var snapshot = new PowerGridSnapshot { Buildings = counted, Tick = 5 };
            var panel = new PowerHeatmapPanel();
            var state = new PowerUiState();
            var settings = new PowerGridManagerSettings { HighConsumptionWatts = 1000f };
            var management = new PowerManagementController();

            using (var ui = new WidgetProbe())
            {
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(counted.IndexReads, 5, "initial consumption ranking reads the snapshot");
                TestAssert.Equal(ui.NameAt(0), "Bravo lamp", "consumption ranks largest draw first");
                TestAssert.Equal(ui.NameAt(1), "Alpha pump", "consumption ranks lower draw second");
                TestAssert.Equal(ui.BarWidthAt(0), 66f, "largest draw uses full bar intensity");
                TestAssert.Equal(ui.BarWidthAt(1), 11f, "draw intensity is normalized by the maximum");
                TestAssert.Equal(ui.SymbolAt(0), "[!]", "draw below the threshold uses warning symbol");
                TestAssert.Check(ui.SymbolColorAt(0) == PowerUiDraw.Warn, "warning symbol keeps its color");

                ui.Clear();
                Mouse.Position = new Vector2(10f, 5f);
                settings.HighConsumptionWatts = 500f;
                management.SetSheddable(bravo.Building, true, 1);
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(counted.IndexReads, 5, "settings and policy do not rebuild consumption ranking");
                TestAssert.Equal(ui.SymbolAt(0), "[!!]", "threshold change immediately updates visible color/symbol");
                TestAssert.Check(ui.SymbolColorAt(0) == PowerUiDraw.NoticeColor, "threshold change updates heat color");
                TestAssert.Check(ui.TooltipAt(0).Contains("PGM_Heatmap_Policy_Sheddable"),
                    "tooltip immediately reflects the current policy");

                ui.Clear();
                ClickMode(1); // Generation
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(counted.IndexReads, 10, "mode change rebuilds the ranking once");
                TestAssert.Equal(ui.NameAt(0), "Delta generator", "generation ranks largest output first");
                TestAssert.Equal(ui.NameAt(1), "Alpha pump", "generation ranks second output correctly");
                TestAssert.Equal(ui.BarWidthAt(1), 66f * 500f / 900f,
                    "generation intensity is normalized by the new maximum");
                ui.Clear();
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(counted.IndexReads, 10, "unchanged generation draw reuses the cache");

                ui.Clear();
                ClickMode(2); // Battery
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(ui.NameAt(0), "Gamma battery", "emptier battery ranks first");
                TestAssert.Equal(ui.NameAt(1), "Echo battery", "fuller battery ranks second");
                TestAssert.Equal(ui.BarWidthAt(0), 6.6f, "battery bar represents charge, not ranking score");
                TestAssert.Equal(ui.BarWidthAt(1), 59.4f, "fuller battery has the larger charge bar");
                TestAssert.Equal(ui.SymbolAt(0), "[!!]", "low charge retains its critical indicator");
                TestAssert.Check(ui.SymbolColorAt(0) == PowerUiDraw.NoticeColor, "low charge remains red");

                ui.Clear();
                ClickMode(3); // Stability
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(ui.NameAt(0), "Bravo lamp", "disconnected building ranks first for stability");
                ui.Clear();
                ClickMode(4); // Isolation
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(ui.NameAt(0), "Bravo lamp", "isolated building ranks first for isolation");
                ui.Clear();
                ClickMode(5); // Idle
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(ui.NameAt(0), "Alpha pump", "switched-off drawing building ranks first for idle");

                management.SetSheddable(alpha.Building, false, 2);
                ui.Clear();
                ClickMode(6); // Critical
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(ui.NameAt(0), "Alpha pump", "protected record ranks first in critical mode");
                TestAssert.Equal(ui.SymbolAt(0), "[OK]", "protected record keeps its critical-mode indicator");
                TestAssert.Check(ui.SymbolColorAt(0) == PowerUiDraw.Good, "protected indicator remains green");
                int beforePolicy = counted.IndexReads;
                ui.Clear();
                management.SetSheddable(alpha.Building, true, 3);
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(counted.IndexReads, beforePolicy + 5, "policy revision rebuilds critical ranking");
                TestAssert.Equal(ui.NameAt(0), "Bravo lamp", "critical ranking changes immediately after policy change");
                ui.Clear();
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(counted.IndexReads, beforePolicy + 5, "unchanged critical ranking stays cached");

                int revision = management.Revision;
                int beforeDirectEdit = counted.IndexReads;
                management.Registry.SetSheddable(alpha.Building, false, 4);
                TestAssert.Equal(management.Revision, revision, "direct registry edit does not change controller revision");
                ui.Clear();
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(counted.IndexReads, beforeDirectEdit + 5,
                    "direct registry edit invalidates the critical ranking");
                TestAssert.Equal(ui.NameAt(0), "Alpha pump", "directly protected building is ranked immediately");
                management.Registry.Find(alpha.Building).Sheddable = true;
                ui.Clear();
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(counted.IndexReads, beforeDirectEdit + 10,
                    "direct record-field edit invalidates the critical ranking");
                TestAssert.Equal(ui.NameAt(0), "Bravo lamp", "direct field edit updates the critical ordering");
                management.Registry.Clear();
                ui.Clear();
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(counted.IndexReads, beforeDirectEdit + 15,
                    "removing registry records invalidates critical rows");
                management.Registry.SetSheddable(alpha.Building, false, 5);
                ui.Clear();
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(ui.NameAt(0), "Alpha pump", "new direct record is included in critical ranking");
                int beforeSameCountReplacement = counted.IndexReads;
                management.Registry.Clear();
                management.Registry.SetSheddable(bravo.Building, false, 6);
                ui.Clear();
                panel.Draw(NormalViewport, snapshot, state, settings, management);
                TestAssert.Equal(counted.IndexReads, beforeSameCountReplacement + 5,
                    "replacing records at the same count invalidates critical rows");
                TestAssert.Equal(ui.NameAt(0), "Bravo lamp", "same-count replacement uses the new policy");

                var firstController = new PowerManagementController();
                firstController.SetSheddable(alpha.Building, false, 1);
                var secondController = new PowerManagementController();
                secondController.SetSheddable(bravo.Building, false, 1);
                TestAssert.Equal(firstController.Revision, secondController.Revision,
                    "replacement controllers have equal revision numbers for this regression");
                ui.Clear();
                panel.Draw(NormalViewport, snapshot, state, settings, firstController);
                TestAssert.Equal(ui.NameAt(0), "Alpha pump", "first controller supplies its own critical policy");
                int beforeReplacement = counted.IndexReads;
                ui.Clear();
                panel.Draw(NormalViewport, snapshot, state, settings, secondController);
                TestAssert.Equal(counted.IndexReads, beforeReplacement + 5,
                    "different controller invalidates critical rows even at the same revision");
                TestAssert.Equal(ui.NameAt(0), "Bravo lamp", "replacement controller's protected building is ranked");
                ui.Clear();
                panel.Draw(NormalViewport, snapshot, state, settings, null);
                TestAssert.Equal(ui.NameAt(0), "Bravo lamp", "critical mode tolerates missing management");

                // The search is trimmed and case-insensitive; it must invalidate even when paused.
                ui.Clear();
                state.HeatmapScroll.y = 10000f;
                int beforeSearch = counted.IndexReads;
                WidgetsQueue.EnterText("  bAtTeRy  ");
                panel.Draw(NormalViewport, snapshot, state, settings, secondController);
                TestAssert.Equal(counted.IndexReads, beforeSearch + 5, "typing in search rebuilds once");
                TestAssert.Equal(ui.Rows.Count, 2, "search returns both battery labels");
                TestAssert.Check(ui.ContainsName("Gamma battery") && ui.ContainsName("Echo battery"),
                    "search ignores case and surrounding whitespace");
                TestAssert.Equal(state.HeatmapScroll.y, 0f, "search shrink corrects stale scroll position");
                ui.Clear();
                panel.Draw(NormalViewport, snapshot, state, settings, secondController);
                TestAssert.Equal(counted.IndexReads, beforeSearch + 5, "unchanged search uses cached rows");

                ui.Clear();
                WidgetsQueue.EnterText("no matching building");
                panel.Draw(NormalViewport, snapshot, state, settings, secondController);
                TestAssert.Equal(ui.Rows.Count, 0, "no-match search has no row controls");
                TestAssert.Equal(ui.Starts, 0, "no-match search does not start a scroll view");
                TestAssert.Check(ui.HasLabel("PGM_Heatmap_Empty"), "no-match search shows empty state");
                ui.Clear();
                WidgetsQueue.EnterText("");
                panel.Draw(NormalViewport, snapshot, state, settings, secondController);
                TestAssert.Equal(ui.Rows.Count, 5, "clearing search restores all rows");

                ui.Clear();
                panel.Draw(NormalViewport, null, state, settings, null);
                TestAssert.Equal(ui.Rows.Count, 0, "null snapshot clears cached rows");
                TestAssert.Check(ui.HasLabel("PGM_Heatmap_Empty"), "null snapshot shows empty state");
                int beforeReturn = counted.IndexReads;
                ui.Clear();
                panel.Draw(NormalViewport, snapshot, state, settings, secondController);
                TestAssert.Equal(counted.IndexReads, beforeReturn + 5, "returning from null rebuilds rows");
            }
        }

        private static void EmptyAndNarrowViews()
        {
            var panel = new PowerHeatmapPanel();
            var state = new PowerUiState();
            var settings = new PowerGridManagerSettings();
            var invalid = new CountingList<BuildingPowerSnapshot>(new List<BuildingPowerSnapshot>
                { null, new BuildingPowerSnapshot() });
            using (var ui = new WidgetProbe())
            {
                var snapshot = new PowerGridSnapshot { Buildings = invalid };
                panel.Draw(NormalViewport, snapshot, state, settings, null);
                TestAssert.Equal(ui.Rows.Count, 0, "null rows and buildings are ignored");
                TestAssert.Check(ui.HasLabel("PGM_Heatmap_Empty"), "invalid rows show empty state");
                ui.Clear();
                panel.Draw(NormalViewport, snapshot, state, settings, null);
                TestAssert.Equal(invalid.IndexReads, 2, "empty result is cached too");

                var transient = new ThrowOnceList<BuildingPowerSnapshot>(new List<BuildingPowerSnapshot>
                    { Row("retry a", 1f), Row("retry b", 2f) });
                var transientSnapshot = new PowerGridSnapshot { Buildings = transient };
                ui.Clear();
                TestAssert.Throws(() => panel.Draw(NormalViewport, transientSnapshot, state, settings, null),
                    "a failed rebuild propagates the source exception");
                ui.Clear();
                panel.Draw(NormalViewport, transientSnapshot, state, settings, null);
                TestAssert.Equal(ui.Rows.Count, 2, "failed rebuild does not cache a partial result");
                TestAssert.Equal(ui.NameAt(0), "Retry b", "retry rebuilds the complete ranking");

                var rows = new List<BuildingPowerSnapshot>();
                for (int i = 0; i < 20; i++) rows.Add(Row("narrow " + i, i + 1));
                var narrowSnapshot = new PowerGridSnapshot { Buildings = rows };
                var narrowRect = new Rect(0f, 0f, 120f, 180f);
                ui.Clear();
                state.HeatmapScroll.y = -50f;
                panel.Draw(narrowRect, narrowSnapshot, state, settings, null);
                TestAssert.Equal(state.HeatmapScroll.y, 0f, "negative scroll position is corrected");
                TestAssert.Check(ui.View.width > ui.Outer.width, "narrow viewport retains horizontal scroll space");
                TestAssert.Check(ui.Rows.Count > 0 && ui.Rows.Count < rows.Count,
                    "narrow viewport submits only nearby rows");
                TestAssert.Equal(ui.Starts, ui.Ends, "narrow scroll view remains balanced");

                ui.Clear();
                float narrowBottom = rows.Count * RowHeight - ui.LastOuterHeight + 10f;
                state.HeatmapScroll.y = narrowBottom;
                panel.Draw(narrowRect, narrowSnapshot, state, settings, null);
                TestAssert.Equal(state.HeatmapScroll.y, narrowBottom,
                    "extra bottom scroll for a horizontal bar is not forcibly clamped");
                TestAssert.Equal(ui.Rows[ui.Rows.Count - 1].y, (rows.Count - 1) * RowHeight,
                    "last row remains reachable in a horizontally scrolling viewport");

                ui.Clear();
                panel.Draw(new Rect(0f, 0f, 8f, 8f), snapshot, state, settings, null);
                TestAssert.Equal(ui.Starts, 0, "tiny invalid rectangle does not start a scroll view");
                panel.Draw(NormalViewport, snapshot, null, settings, null);
                TestAssert.Equal(ui.Starts, 0, "null UI state is ignored");
            }
        }

        private static void ClickMode(int index) => WidgetsQueue.ClickTextAt(index);

        private static BuildingPowerSnapshot Row(string label, float consumption, float generation = 0f)
        {
            var building = new Building { def = ThingDefFactory.Simple(label), thingIDNumber = nextId++ };
            return new BuildingPowerSnapshot
            {
                Building = building,
                IsConsumer = consumption > 0f,
                IsGenerator = generation > 0f,
                ConsumptionWatts = consumption,
                GenerationWatts = generation,
                Connection = PowerConnectionState.Connected,
                NetworkId = 1
            };
        }

        private sealed class CountingList<T> : IReadOnlyList<T>
        {
            private readonly List<T> items;
            public int IndexReads;
            public CountingList(List<T> items) { this.items = items; }
            public int Count => items.Count;
            public T this[int index] { get { IndexReads++; return items[index]; } }
            public IEnumerator<T> GetEnumerator() => items.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class ThrowOnceList<T> : IReadOnlyList<T>
        {
            private readonly List<T> items;
            private bool shouldThrow = true;
            public ThrowOnceList(List<T> items) { this.items = items; }
            public int Count => items.Count;
            public T this[int index]
            {
                get
                {
                    if (index == 1 && shouldThrow)
                    {
                        shouldThrow = false;
                        throw new InvalidOperationException("Simulated transient list failure");
                    }
                    return items[index];
                }
            }
            public IEnumerator<T> GetEnumerator() => items.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private struct LabelCall
        {
            public Rect Rect;
            public string Text;
            public Color Color;
        }

        private struct BoxCall
        {
            public Rect Rect;
            public Color Color;
        }

        private struct TipCall
        {
            public Rect Rect;
            public string Text;
        }

        private sealed class WidgetProbe : IDisposable
        {
            public readonly List<Rect> Rows = new List<Rect>();
            private readonly List<LabelCall> labels = new List<LabelCall>();
            private readonly List<BoxCall> boxes = new List<BoxCall>();
            private readonly List<TipCall> tips = new List<TipCall>();
            public Rect Outer;
            public Rect View;
            public float LastOuterHeight;
            public int Starts;
            public int Ends;
            private readonly Vector2 previousMouse = Mouse.Position;
            private readonly bool previousDebugTooltipEdges = DebugViewSettings.drawTooltipEdges;

            public WidgetProbe()
            {
                Find.WindowStack.windows.Clear();
                WidgetsQueue.Reset();
                Mouse.Position = new Vector2(-100f, -100f);
                DebugViewSettings.drawTooltipEdges = false;
                Widgets.InvisibleButtonObserver = rect => Rows.Add(rect);
                Widgets.LabelObserver = (rect, text, color) => labels.Add(new LabelCall { Rect = rect, Text = text, Color = color });
                Widgets.SolidBoxObserver = (rect, color) => boxes.Add(new BoxCall { Rect = rect, Color = color });
                Widgets.BeginScrollViewObserver = (outer, view) =>
                {
                    Outer = outer;
                    View = view;
                    LastOuterHeight = outer.height;
                    Starts++;
                };
                Widgets.EndScrollViewObserver = () => Ends++;
                TooltipHandler.TipObserver = (rect, text) => tips.Add(new TipCall { Rect = rect, Text = text });
            }

            public void Clear()
            {
                Rows.Clear();
                labels.Clear();
                boxes.Clear();
                tips.Clear();
                Starts = Ends = 0;
                WidgetsQueue.Reset();
                Widgets.ScrollPositionOverride = null;
                Mouse.Position = new Vector2(-100f, -100f);
                DebugViewSettings.drawTooltipEdges = false;
            }

            public int RowTooltipCount
            {
                get
                {
                    int count = 0;
                    foreach (TipCall tip in tips)
                        if (tip.Text.StartsWith("PGM_Heatmap_Tooltip")) count++;
                    return count;
                }
            }

            public string NameAt(int visibleIndex)
            {
                float y = Rows[visibleIndex].y;
                foreach (LabelCall label in labels)
                    if (label.Rect.x == 82f && label.Rect.y == y && label.Rect.height == RowHeight) return label.Text;
                throw new Exception("Missing building label at row " + visibleIndex);
            }

            public bool ContainsName(string name)
            {
                for (int i = 0; i < Rows.Count; i++) if (NameAt(i) == name) return true;
                return false;
            }

            public bool HasLabel(string text)
            {
                foreach (LabelCall label in labels) if (label.Text == text) return true;
                return false;
            }

            public string SymbolAt(int visibleIndex)
            {
                float y = Rows[visibleIndex].y + 4f;
                foreach (LabelCall label in labels)
                    if (label.Rect.x == 6f && label.Rect.y == y && label.Rect.width == 68f) return label.Text;
                throw new Exception("Missing heat symbol at row " + visibleIndex);
            }

            public Color SymbolColorAt(int visibleIndex)
            {
                float y = Rows[visibleIndex].y + 4f;
                foreach (LabelCall label in labels)
                    if (label.Rect.x == 6f && label.Rect.y == y && label.Rect.width == 68f) return label.Color;
                throw new Exception("Missing heat color at row " + visibleIndex);
            }

            public float BarWidthAt(int visibleIndex)
            {
                float y = Rows[visibleIndex].y + 5f;
                foreach (BoxCall box in boxes)
                    if (box.Rect.x == 7f && box.Rect.y == y && box.Rect.height == 18f) return box.Rect.width;
                throw new Exception("Missing heat bar at row " + visibleIndex);
            }

            public string TooltipAt(int visibleIndex)
            {
                float y = Rows[visibleIndex].y;
                foreach (TipCall tip in tips)
                    if (tip.Rect.x == 0f && tip.Rect.y == y && tip.Rect.height == RowHeight &&
                        tip.Text.StartsWith("PGM_Heatmap_Tooltip")) return tip.Text;
                throw new Exception("Missing row tooltip at row " + visibleIndex);
            }

            public bool HasSelectedBackground(int visibleIndex)
            {
                float y = Rows[visibleIndex].y;
                foreach (BoxCall box in boxes)
                    if (box.Rect.x == 0f && box.Rect.y == y && box.Rect.height == RowHeight &&
                        box.Color == PowerUiDraw.BgSelected) return true;
                return false;
            }

            public void Dispose()
            {
                Widgets.InvisibleButtonObserver = null;
                Widgets.LabelObserver = null;
                Widgets.SolidBoxObserver = null;
                Widgets.BeginScrollViewObserver = null;
                Widgets.EndScrollViewObserver = null;
                Widgets.ScrollPositionOverride = null;
                TooltipHandler.TipObserver = null;
                WidgetsQueue.Reset();
                Find.WindowStack.windows.Clear();
                CameraJumper.LastJumpAndSelect = null;
                Mouse.Position = previousMouse;
                DebugViewSettings.drawTooltipEdges = previousDebugTooltipEdges;
            }
        }
    }
}
