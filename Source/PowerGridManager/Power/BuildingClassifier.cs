using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PowerGridManager
{
    /// <summary>
    /// Assigns a dashboard category using only verified definition data and
    /// component presence. Every rule is deliberately narrow: when a building does
    /// not clearly match, it stays <see cref="PowerCategory.Unassigned"/> rather than
    /// being placed in a misleading bucket.
    /// </summary>
    internal static class BuildingClassifier
    {
        // Vanilla thing-category defNames that map cleanly onto a dashboard category.
        // These are only consulted after the component/class checks below, and a
        // missing category simply yields PowerCategory.Unassigned.
        private const string CategoryProduction = "BuildingsProduction";
        private const string CategoryTemperature = "BuildingsTemperature";
        private const string CategorySecurity = "BuildingsSecurity";
        private const string CategoryJoy = "BuildingsJoy";

        public static PowerCategory Classify(Building building, IReadOnlyList<PowerComponentSnapshot> components)
        {
            if (building == null) return PowerCategory.Unassigned;

            bool hasBattery = false;
            bool hasGenerator = false;
            for (int i = 0; i < components.Count; i++)
            {
                PowerComponentSnapshot component = components[i];
                if (component.Behavior == PowerBehavior.Battery) hasBattery = true;
                if (component.IsGenerator) hasGenerator = true;
            }
            if (hasBattery) return PowerCategory.Storage;
            if (hasGenerator) return PowerCategory.Generation;

            ThingDef def = building.def;
            if (def == null) return PowerCategory.Unassigned;

            // Verified component checks. CompTempControl covers vanilla coolers/heaters
            // and modded buildings that reuse it; CompGlower covers lamp-style light.
            if (def.HasComp(typeof(CompTempControl))) return PowerCategory.TemperatureControl;
            if (def.HasComp(typeof(CompGlower))) return PowerCategory.Lighting;

            Type thingClass = def.thingClass;
            if (thingClass != null)
            {
                if (typeof(Building_ResearchBench).IsAssignableFrom(thingClass)) return PowerCategory.Research;
                if (typeof(Building_TurretGun).IsAssignableFrom(thingClass)) return PowerCategory.Security;
                if (typeof(Building_Bed).IsAssignableFrom(thingClass))
                {
                    BuildingProperties props = def.building;
                    if (props != null && (props.bed_defaultMedical || props.bed_healPerDay > 0f))
                    {
                        return PowerCategory.Medical;
                    }
                    // A plain bed is furniture: no reliable purpose category.
                    return PowerCategory.Unassigned;
                }
            }

            BuildingProperties buildingProps = def.building;
            if (buildingProps != null && buildingProps.isTrap) return PowerCategory.Security;

            string categoryDefName = FindVanillaCategoryDefName(def);
            if (categoryDefName == null) return PowerCategory.Unassigned;
            switch (categoryDefName)
            {
                case CategoryProduction: return PowerCategory.Production;
                case CategoryTemperature: return PowerCategory.TemperatureControl;
                case CategorySecurity: return PowerCategory.Security;
                case CategoryJoy: return PowerCategory.Recreation;
                default: return PowerCategory.Unassigned;
            }
        }

        private static string FindVanillaCategoryDefName(ThingDef def)
        {
            List<ThingCategoryDef> categories = def.thingCategories;
            if (categories == null) return null;
            for (int i = 0; i < categories.Count; i++)
            {
                ThingCategoryDef category = categories[i];
                // Bounded walk: a modded def graph could contain a cycle.
                for (int depth = 0; category != null && depth < 8; depth++)
                {
                    string defName = category.defName;
                    if (defName == CategoryProduction || defName == CategoryTemperature ||
                        defName == CategorySecurity || defName == CategoryJoy)
                    {
                        return defName;
                    }
                    category = category.parent;
                }
            }
            return null;
        }
    }
}
