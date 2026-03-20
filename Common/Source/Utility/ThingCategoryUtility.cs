namespace NewHarvestPatches
{
    internal static class ThingCategoryUtility
    {
        internal static List<ThingCategoryDef> GetThingCategoryDefs()
        {
            return
            [
                NHCP_ThingCategoryDefOf.VV_NHCP_DummyCategory_AnimalFoods,
                NHCP_ThingCategoryDefOf.VV_NHCP_DummyCategory_Fruit,
                NHCP_ThingCategoryDefOf.VV_NHCP_DummyCategory_Grains,
                NHCP_ThingCategoryDefOf.VV_NHCP_DummyCategory_Nuts,
                NHCP_ThingCategoryDefOf.VV_NHCP_DummyCategory_Vegetables,
                NHCP_ThingCategoryDefOf.VV_NHCP_DummyCategory_Fungus
            ];
        }
    }
}