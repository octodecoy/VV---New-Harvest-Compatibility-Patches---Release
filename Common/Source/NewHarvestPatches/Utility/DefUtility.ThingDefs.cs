namespace NewHarvestPatches;

public static partial class DefUtility
{
    /// <summary>
    /// Lazily built, def-derived lists this mod's settings and actions work from. Everything is discovered
    /// by querying the DefDatabase rather than hardcoding defNames, so defs added by a later parent-mod
    /// update are picked up with no change here. Nothing may be touched before defs finish loading.
    /// </summary>
    public static class ThingDefs
    {
        private static List<ThingDef> s_fuelThingDefsListInt;
        private static Dictionary<ThingDef, (bool canBeFuel, bool isWood)> s_industrialResourceDefsDictInt;
        private static List<ThingDef> s_deciduousTreeDefsListInt;

        /// <summary>
        /// Our stuff defs usable as fuel, wood first and alphabetical within each group - the order the
        /// fuel settings tab lists them in.
        /// </summary>
        public static List<ThingDef> FuelThingDefs
        {
            get
            {
                if (s_fuelThingDefsListInt == null) BuildFuelThingDefs();

                return s_fuelThingDefsListInt;
            }
        }

        /// <summary>
        /// Wood first, then alphabetical within each group.
        /// </summary>
        private static void BuildFuelThingDefs()
        {
            s_fuelThingDefsListInt = [];

            s_fuelThingDefsListInt.AddRange(IndustrialResourceDefs
                .Where(kvp => kvp.Value.canBeFuel)
                .Select(kvp => kvp.Key)
                .OrderByDescending(td => td.HasWoodDefName())
                .ThenBy(td => td.label, CurrentCultureComparer)
            );
        }

        /// <summary>
        /// Our stuff/material defs, each tagged with whether it burns and whether it is wood. Sourced from
        /// the DefDatabase rather than our XML, so materials contributed by newer module versions appear
        /// automatically. The two flags come from different tests and do not imply each other: canBeFuel is
        /// a wood STUFF CATEGORY, isWood is a wood-suffixed DEFNAME - a modded material can have one
        /// without the other.
        /// </summary>
        public static Dictionary<ThingDef, (bool canBeFuel, bool isWood)> IndustrialResourceDefs
        {
            get
            {
                if (s_industrialResourceDefsDictInt == null) BuildIndustrialResourceDefs();

                return s_industrialResourceDefsDictInt;
            }
        }

        private static void BuildIndustrialResourceDefs()
        {
            s_industrialResourceDefsDictInt = [];

            var defs = DefDatabase<ThingDef>.AllDefs
                .Where(td => td.defName.Contains(ModName.Prefix.Parent)
                            && td.IsStuff
                            && td.stuffProps.commonality >= 0
                            && !td.stuffProps.categories.NullOrEmpty())
                            // May need to change back to td.stuffProps.categories != null if NullOrEmpty check causes issues
                .OrderBy(td => td.label, CurrentCultureComparer)
                .ToList();

            foreach (var def in defs)
            {
                s_industrialResourceDefsDictInt[def] = (
                    def.HasWoodStuffCategory(), 
                    def.HasWoodDefName());
            }
        }

        /// <summary>
        /// Our trees that actually have fall-color behaviour, so the Visuals tab only lists trees the
        /// setting can affect.
        /// </summary>
        public static List<ThingDef> DeciduousTreeDefs
        {
            get
            {
                if (s_deciduousTreeDefsListInt == null) BuildDeciduousTreeDefs();

                return s_deciduousTreeDefsListInt;
            }
        }

        /// <summary>
        /// "Deciduous" is not a def flag - it is read off the tree's graphic by looking for a
        /// _FallBehaviorEnabled shader parameter set to 1. ShaderParameter keeps its name and value
        /// private, hence the reflection; a missing field aborts the whole build rather than
        /// mis-classifying every tree.
        /// </summary>
        private static void BuildDeciduousTreeDefs()
        {
            s_deciduousTreeDefsListInt = [];

            try
            {
                var trees = DefDatabase<ThingDef>.AllDefs
                    .Where(td => td.IsNewHarvestTree())
                    .OrderBy(td => td.label, CurrentCultureComparer)
                    .ToList();
                    
                if (trees.NullOrEmpty())
                {
                    LogMessage(() => "No trees found.", LogMessageType.Error);
                    return;
                }

                var nameField = typeof(ShaderParameter).GetField("name", BindingFlags.NonPublic | BindingFlags.Instance);
                if (nameField == null)
                {
                    LogMessage(() => "Could not access ShaderParameter.name field.", LogMessageType.Error);
                    return;
                }

                var valueField = typeof(ShaderParameter).GetField("value", BindingFlags.NonPublic | BindingFlags.Instance);
                if (valueField == null)
                {
                    LogMessage(() => "Could not access ShaderParameter.value field.", LogMessageType.Error);
                    return;
                }

                const string targetName = "_FallBehaviorEnabled";
                
                foreach (var tree in trees)
                {
                    var param = tree.graphicData?.shaderParameters?.FirstOrDefault(p =>
                    {
                        return (string)nameField.GetValue(p) == targetName;
                    });

                    if (param == null)
                        continue;

                    var value = (Vector4)valueField.GetValue(param);
                    if (value.x == 1f)
                        s_deciduousTreeDefsListInt.Add(tree);
                }
            }
            catch (Exception ex)
            {
                LogException(ex, ex.TargetSite);
            }
        }

        /// <summary>
        /// Drops the caches only the settings menu needs. Deciduous trees are kept: FallColorDisabler
        /// applies live and would otherwise pay the reflection sweep again on every settings close.
        /// </summary>
        internal static void ClearCache()
        {
            s_fuelThingDefsListInt = null;
            s_industrialResourceDefsDictInt = null;
            // _deciduousTreeDefsListInt remains filled for live use.
        }
    }
}