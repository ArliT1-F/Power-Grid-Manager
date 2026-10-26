// Minimal Verse contracts for the offline verification harness. Signatures follow the
// public 1.6 reference for the members the mod uses; the bodies are inert or simple.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;

namespace Verse
{
    public struct TaggedString
    {
        private readonly string text;
        public TaggedString(string text) { this.text = text; }
        public static implicit operator string(TaggedString value) => value.text;
        public static implicit operator TaggedString(string value) => new TaggedString(value);
        public override string ToString() => text ?? string.Empty;
    }

    public static class Translator
    {
        /// <summary>Tests return the key itself so assertions stay locale independent.</summary>
        public static TaggedString Translate(this string key) => new TaggedString(key);

        public static string Translate(this string key, params object[] args)
        {
            string text = key;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    text += "|" + (args[i] == null ? "null" : args[i].ToString());
                }
            }
            return text;
        }
    }

    public static class GenText
    {
        public static string CapitalizeFirst(this string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }

    public class Def
    {
        public string defName;
        public string label;

        public virtual TaggedString LabelCap => new TaggedString(label == null ? null : label.CapitalizeFirst());
    }

    public class ThingCategoryDef : Def
    {
        public ThingCategoryDef parent;
    }

    public class BuildingProperties
    {
        public bool bed_defaultMedical;
        public float bed_healPerDay;
        public bool isTrap;
    }

    public class ThingDef : Def
    {
        public Type thingClass;
        public BuildingProperties building;
        public List<ThingCategoryDef> thingCategories = new List<ThingCategoryDef>();
        public List<Type> compTypes = new List<Type>();

        public bool HasComp(Type compType)
        {
            for (int i = 0; i < compTypes.Count; i++)
            {
                if (compTypes[i] == compType) return true;
            }
            return false;
        }
    }

    public class RoomRoleDef : Def { }

    public class Room
    {
        public RoomRoleDef Role;
        public int CellCount;
    }

    public struct IntVec3
    {
        public int x;
        public int z;
        public IntVec3(int x, int z) { this.x = x; this.z = z; }
    }

    public class Thing
    {
        public ThingDef def;
        public int thingIDNumber = -1;
        public RimWorld.Faction factionInt = RimWorld.Faction.OfPlayer;
        public IntVec3 Position;
        public Map Map;
        public bool Spawned = true;
        public bool Destroyed;
        public Room RoomRef;
        public List<ThingComp> AllComps = new List<ThingComp>();

        /// <summary>Mirrors Verse.Thing.ThingID, which the game persists as the "id" node.</summary>
        public string ThingID => def == null ? "" : def.defName + thingIDNumber;

        /// <summary>Mirrors ILoadReferenceable.GetUniqueLoadID for a Thing.</summary>
        public string GetUniqueLoadID() => "Thing_" + ThingID;

        public RimWorld.Faction Faction => factionInt;

        public T GetComp<T>() where T : ThingComp
        {
            for (int i = 0; i < AllComps.Count; i++)
            {
                if (AllComps[i] is T typed) return typed;
            }
            return null;
        }

        public TaggedString LabelCap => new TaggedString(def == null ? "" : def.label);
    }

    public class ThingWithComps : Thing { }

    public class Building : ThingWithComps { }

    public static class RegionAndRoomQuery
    {
        public static Room GetRoom(this Thing thing) => thing.RoomRef;
    }

    public class ThingComp
    {
        // Vanilla declares ThingWithComps; every fixture in this harness parents comps to a
        // Building, so the narrower type keeps the scenario builders simple.
        public Building parent;

        public virtual IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            yield break;
        }
    }

    public class Gizmo
    {
        public string defaultLabel;
        public string defaultDesc;
        public bool disabled;
    }

    public class Command : Gizmo { }

    /// <summary>Mirrors Verse.Command_Toggle's public delegates.</summary>
    public class Command_Toggle : Command
    {
        public Func<bool> isActive;
        public Action toggleAction;
    }

    public static class Log
    {
        public static readonly List<string> Warnings = new List<string>();
        public static void Warning(string text) { Warnings.Add(text); }
    }

    public class ListerBuildings
    {
        public List<Building> allBuildingsColonist = new List<Building>();
        public List<Building> allBuildingsNonColonist = new List<Building>();
    }

    public class Map
    {
        public int uniqueID = -1;
        public RimWorld.PowerNetManager powerNetManager = new RimWorld.PowerNetManager();
        public ListerBuildings listerBuildings = new ListerBuildings();
        public Dictionary<Type, MapComponent> components = new Dictionary<Type, MapComponent>();
        /// <summary>Test-only stand-in for DesignationDefOf.Flick designations.</summary>
        public HashSet<Thing> flickDesignations = new HashSet<Thing>();

        public T GetComponent<T>() where T : MapComponent
        {
            MapComponent component;
            return components.TryGetValue(typeof(T), out component) ? (T)component : null;
        }

        public void RegisterComponent(MapComponent component)
        {
            components[component.GetType()] = component;
        }
    }

    public abstract class MapComponent : IExposable
    {
        public readonly Map map;
        protected MapComponent(Map map) { this.map = map; }
        public virtual void MapComponentUpdate() { }
        public virtual void MapComponentTick() { }
        public virtual void MapComponentOnGUI() { }
        public virtual void ExposeData() { }
        public virtual void FinalizeInit() { }
        public virtual void MapRemoved() { }
    }

    public interface IExposable
    {
        void ExposeData();
    }

    public class TickManager
    {
        public int TicksGame;
    }

    public class Selector
    {
        public object Selected;
        public void Select(object obj, bool playSound = true, bool forceDesignatorDeselect = true) { Selected = obj; }
        public bool IsSelected(object obj) => ReferenceEquals(Selected, obj);
    }

    public enum WindowLayer
    {
        Dialog,
        GameUI,
        SubSuper,
        Super
    }

    public abstract class Window
    {
        public WindowLayer layer = WindowLayer.Dialog;
        public bool doCloseX;
        public bool doCloseButton;
        public bool closeOnClickedOutside;
        public bool forcePause;
        public bool preventCameraMotion = true;
        public bool absorbInputAroundWindow;
        public bool draggable;
        public bool resizeable;
        public Rect windowRect;

        public virtual Vector2 InitialSize => new Vector2(500f, 500f);
        protected virtual float Margin => 18f;
        public bool IsOpen => Find.WindowStack.IsOpen(this);

        public abstract void DoWindowContents(Rect inRect);
        public virtual void PreOpen() { }
        public virtual void PostOpen() { }
        public virtual void PreClose() { }
        public virtual void PostClose() { }
        public virtual void WindowUpdate() { }
        public virtual bool OnCloseRequest() => true;
        public virtual void Close(bool doCloseSound = true) { Find.WindowStack.TryRemove(this); }
    }

    public class WindowStack
    {
        public readonly List<Window> windows = new List<Window>();

        public void Add(Window window)
        {
            if (window == null) return;
            windows.Add(window);
        }

        public bool TryRemove(Window window) => windows.Remove(window);
        public bool TryRemove(Type windowType) => windows.RemoveAll(w => w.GetType() == windowType) > 0;
        public bool IsOpen(Window window) => windows.Contains(window);
        public bool IsOpen(Type windowType) => windows.Exists(w => w.GetType() == windowType);
    }

    // Mirrors Verse.ProgramState / Verse.Current, which the notifier checks before queueing an
    // on-screen message.
    public enum ProgramState
    {
        None,
        Entry,
        MapInitializing,
        Playing
    }

    public static class Current
    {
        public static ProgramState ProgramState = ProgramState.Playing;
    }

    public static class Messages
    {
        /// <summary>Records every message the mod queues so tests can assert on them.</summary>
        public static readonly System.Collections.Generic.List<string> Sent =
            new System.Collections.Generic.List<string>();

        public static void Message(string text, MessageTypeDef def, bool historical = true)
        {
            Sent.Add(text);
        }

        public static void Message(string text, Verse.LookTargets lookTargets, MessageTypeDef def,
            bool historical = true)
        {
            Sent.Add(text);
        }

        public static void Clear()
        {
            Sent.Clear();
        }
    }

    public class LookTargets { }

    public static class Find
    {
        public static TickManager TickManager = new TickManager();
        public static Map CurrentMap;
        public static WindowStack WindowStack = new WindowStack();
        public static Selector Selector = new Selector();
    }

    public class TabRecord
    {
        public string label = "Tab";
        public Action clickedAction;
        public bool selected;
        public Func<bool> selectedGetter;
        public Color? labelColor;

        public TabRecord(string label, Action clickedAction, bool selected)
        {
            this.label = label;
            this.clickedAction = clickedAction;
            this.selected = selected;
        }

        public TabRecord(string label, Action clickedAction, Func<bool> selected)
        {
            this.label = label;
            this.clickedAction = clickedAction;
            selectedGetter = selected;
        }

        public bool Selected => selectedGetter != null ? selectedGetter() : selected;
        public virtual string GetTip() => null;
    }

    public static class TabDrawer
    {
        public static TabRecord DrawnTab;

        public static TabRecord DrawTabs<T>(Rect baseRect, List<T> tabs, float maxTabWidth = 200f) where T : TabRecord
        {
            // Harness: activate the recorded tab (used by smoke tests) without drawing.
            if (DrawnTab != null && tabs.Contains((T)DrawnTab)) return DrawnTab;
            return null;
        }
    }

    public static class Text
    {
        public static GameFont Font { get; set; } = GameFont.Small;
        public static TextAnchor Anchor { get; set; } = TextAnchor.UpperLeft;
        public static bool WordWrap { get; set; }
        public static float LineHeight => 20f;
        public static Vector2 CalcSize(string text) => new Vector2((text == null ? 0 : text.Length) * 7f, 20f);

        public static string ClampTextWithEllipsis(Rect rect, string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int maxChars = Math.Max(1, (int)(rect.width / 7f));
            return text.Length <= maxChars ? text : text.Substring(0, Math.Max(1, maxChars - 1)) + "...";
        }
    }

    public static class Mouse
    {
        public static Vector2 Position = Vector2.zero;
        public static bool IsOver(Rect rect) => rect.Contains(Position);
    }

    public struct TipSignal
    {
        public string text;
        public TipSignal(string text) { this.text = text; }
    }

    public static class TooltipHandler
    {
        public static string LastTip;

        public static void TipRegion(Rect rect, TipSignal tip)
        {
            LastTip = tip.text;
        }
    }

    public static class Widgets
    {
        public static float HeightOfButton = 26f;

        public static void Label(Rect rect, string label) { }
        public static void Label(Rect rect, TaggedString label) { }
        public static void LabelEllipses(Rect rect, string label) { }
        public static void DrawBoxSolid(Rect rect, Color color) { }
        public static void DrawBox(Rect rect, int thickness = 1, Texture2D lineTexture = null) { }
        public static void DrawLineHorizontal(float x, float y, float length, Color color) { }
        public static void DrawLineHorizontal(float x, float y, float length) { }
        public static void DrawHighlightSelected(Rect rect) { }
        public static void DrawHighlightIfMouseover(Rect rect) { }
        public static bool ButtonInvisible(Rect rect, bool doMouseoverSound = true) =>
            PowerGridManager.WidgetsQueue.NextButtonInvisible();

        public static bool ButtonText(Rect rect, string label) => PowerGridManager.WidgetsQueue.NextButtonText();

        public static bool ButtonText(Rect rect, string label, bool drawBackground, bool doMouseoverSound,
            bool active = true, TextAnchor? overrideTextAnchor = null) => PowerGridManager.WidgetsQueue.NextButtonText();
        public static string TextField(Rect rect, string text) => text;
        public static void BeginScrollView(Rect outRect, ref Vector2 scrollPosition, Rect viewRect, bool showScrollbars = true) { }
        public static void EndScrollView() { }
        public static float HorizontalSlider(Rect rect, float value, float min, float max, bool middleAlignment = false,
            string label = null, string leftAlignedLabel = null, string rightAlignedLabel = null, float roundTo = -1f) => value;
        public static bool CheckboxLabeled(Rect rect, string label, ref bool checkOn, bool disabled = false) => false;
    }

    public enum LookMode
    {
        Undefined,
        Value,
        Deep,
        Reference,
        Def,
        Global,
        Local
    }

    public enum LoadSaveMode
    {
        Inactive,
        Saving,
        SavingReference,
        LoadingVars,
        LoadingReference,
        PostLoadInit,
        ResolvingCrossRefs
    }

    public static class Scribe
    {
        public static LoadSaveMode mode = LoadSaveMode.Inactive;
        internal static SaveNode current;
        internal static SaveNode root;

        /// <summary>Harness hook: enables the save simulation.</summary>
        public static SaveNode BeginSaving()
        {
            root = new SaveNode("root");
            current = null;
            mode = LoadSaveMode.Saving;
            return root;
        }

        /// <summary>Harness hook: reloads a previously written tree.</summary>
        public static void BeginLoading(SaveNode tree)
        {
            root = tree;
            current = null;
            mode = LoadSaveMode.LoadingVars;
        }

        public static void Finish()
        {
            mode = LoadSaveMode.Inactive;
            current = null;
            root = null;
        }

        internal static SaveNode Node => current ?? root;
    }

    /// <summary>Minimal serialized tree used by the harness to simulate save/load.</summary>
    public class SaveNode
    {
        public readonly string name;
        public readonly Dictionary<string, object> values = new Dictionary<string, object>();
        public readonly List<SaveNode> children = new List<SaveNode>();

        public SaveNode(string name) { this.name = name; }

        public SaveNode Child(string label)
        {
            SaveNode child = new SaveNode(label);
            children.Add(child);
            return child;
        }

        public SaveNode ChildOrNull(string label)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i].name == label) return children[i];
            }
            return null;
        }
    }

    public static class Scribe_Values
    {
        public static void Look<T>(ref T value, string label, T defaultValue = default(T), bool forceSave = false)
        {
            if (Scribe.mode == LoadSaveMode.Saving || Scribe.mode == LoadSaveMode.SavingReference)
            {
                Scribe.Node.values[label] = value;
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.LoadingReference ||
                     Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
            {
                object stored;
                if (Scribe.Node.values.TryGetValue(label, out stored) && stored is T) value = (T)stored;
                else value = defaultValue;
            }
        }
    }

    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T> list, string label, LookMode lookMode = LookMode.Undefined,
            params object[] ctorArgs)
        {
            if (Scribe.mode == LoadSaveMode.Saving || Scribe.mode == LoadSaveMode.SavingReference)
            {
                Scribe.Node.values[label] = list == null ? null : new List<T>(list);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.LoadingReference)
            {
                object stored;
                if (Scribe.Node.values.TryGetValue(label, out stored) && stored is List<T> typed) list = new List<T>(typed);
                else list = new List<T>();
            }
        }
    }

    public static class Scribe_Deep
    {
        public static void Look<T>(ref T target, string label, params object[] ctorArgs) where T : class, IExposable
        {
            if (Scribe.mode == LoadSaveMode.Saving || Scribe.mode == LoadSaveMode.SavingReference)
            {
                if (target == null) return;
                SaveNode child = Scribe.Node.Child(label);
                SaveNode previous = Scribe.current;
                Scribe.current = child;
                try
                {
                    target.ExposeData();
                }
                finally
                {
                    Scribe.current = previous;
                }
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.LoadingReference)
            {
                SaveNode child = Scribe.Node.ChildOrNull(label);
                if (child == null)
                {
                    target = null;
                    return;
                }
                if (target == null) target = (T)Activator.CreateInstance(typeof(T));
                SaveNode previous = Scribe.current;
                Scribe.current = child;
                try
                {
                    target.ExposeData();
                }
                finally
                {
                    Scribe.current = previous;
                }
            }
            else if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (target == null) return;
                SaveNode child = Scribe.Node.ChildOrNull(label);
                if (child == null) return;
                SaveNode previous = Scribe.current;
                Scribe.current = child;
                try
                {
                    target.ExposeData();
                }
                finally
                {
                    Scribe.current = previous;
                }
            }
        }
    }

    public class ModSettings : IExposable
    {
        public virtual void ExposeData() { }
    }

    public class ModContentPack
    {
        public string PackageId = "test.powergridmanager";
    }

    public abstract class Mod
    {
        private readonly Dictionary<Type, ModSettings> settings = new Dictionary<Type, ModSettings>();

        protected Mod(ModContentPack content) { Content = content; }
        public ModContentPack Content { get; }

        public T GetSettings<T>() where T : ModSettings, new()
        {
            ModSettings existing;
            if (settings.TryGetValue(typeof(T), out existing)) return (T)existing;
            var created = new T();
            settings[typeof(T)] = created;
            return created;
        }

        public virtual string SettingsCategory() => "";
        public virtual void DoSettingsWindowContents(Rect inRect) { }
        public virtual void WriteSettings() { }
    }

    public class Listing_Standard
    {
        public float curY;
        public Rect ColumnWidth => new Rect(0f, 0f, 600f, 0f);

        public void Begin(Rect rect) { curY = rect.y; }
        public void End() { }
        public float GetRectHeight => 26f;
        public Rect GetRect(float height) => new Rect(0f, curY, 600f, height);
        public void Gap(float amount) { curY += amount; }
        public Rect Label(string label, float maxHeight = -1f, TipSignal? tipSignal = null) => GetRect(20f);
        public Rect Label(TaggedString label, float maxHeight = -1f, string tooltip = null) => GetRect(20f);
        public void CheckboxLabeled(string label, ref bool checkOn, string tooltip = null, float height = 0f,
            float labelPct = 1f) { }
        public bool ButtonText(string label, string highlightTag = null, float widthPct = 1f) => false;
    }

    public enum MovementMode
    {
        Pan,
        Teleport
    }

    public static class CameraJumper
    {
        public static bool TryJumpAndSelect(RimWorld.Planet.GlobalTargetInfo target,
            MovementMode mode = MovementMode.Pan) => true;

        public static bool TryJump(RimWorld.Planet.GlobalTargetInfo target,
            MovementMode mode = MovementMode.Pan) => true;
    }

    public class FloatMenuOption
    {
        public string label;
        public Action action;
        public bool Disabled;

        public FloatMenuOption(string label, Action action)
        {
            this.label = label;
            this.action = action;
        }
    }

    public class FloatMenu : Window
    {
        public readonly List<FloatMenuOption> options;

        public FloatMenu(List<FloatMenuOption> options)
        {
            this.options = options;
        }

        public override void DoWindowContents(Rect inRect) { }
    }

    public class Dialog_InfoCard : Window
    {
        public Thing Thing;

        public Dialog_InfoCard(Thing thing)
        {
            Thing = thing;
        }

        public override void DoWindowContents(Rect inRect) { }
    }
}
