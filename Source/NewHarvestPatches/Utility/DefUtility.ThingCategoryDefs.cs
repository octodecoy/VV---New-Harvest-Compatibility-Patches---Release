namespace NewHarvestPatches;

public static partial class DefUtility
{
    public static class ThingCategoryDefs
    {
        private static List<ThingCategoryDef> s_thingCategoryDefsInt;

        /// <summary>
        /// Every ThingCategoryDef this mod defines, harvested by reflecting over
        /// <see cref="InternalThingCategoryDefOf"/>'s fields rather than kept as a second hand-maintained
        /// list - adding a category to the DefOf is enough for it to appear here. Null-valued fields (a
        /// category whose def failed to load) are skipped, so callers never iterate a null.
        /// </summary>
        public static List<ThingCategoryDef> InternalThingCategoryDefs
        {
            get
            {
                if (s_thingCategoryDefsInt == null)
                {
                    s_thingCategoryDefsInt = [];

                    var fields = typeof(InternalThingCategoryDefOf).GetFields(BindingFlags.Public | BindingFlags.Static);
                    foreach (var f in fields)
                    {
                        if (f.FieldType == typeof(ThingCategoryDef))
                        {
                            if (f.GetValue(null) is ThingCategoryDef value)
                            {
                                s_thingCategoryDefsInt.Add(value);
                            }
                        }
                    } 
                }

                return s_thingCategoryDefsInt;
            }
        }
    }
}