namespace NewHarvestPatches; 

[DefOf]
public static class InternalTabCategoryDefOf
{
    public static TabCategoryDef VV_NHCP_GeneralTab;
    public static TabCategoryDef VV_NHCP_CategoriesTab;
    public static TabCategoryDef VV_NHCP_CommonalityTab;
    public static TabCategoryDef VV_NHCP_CraftingTab;
    public static TabCategoryDef VV_NHCP_FloorsTab;
    public static TabCategoryDef VV_NHCP_FuelTab;
    public static TabCategoryDef VV_NHCP_VisualsTab;
    public static TabCategoryDef VV_NHCP_BehaviorsTab;
    public static TabCategoryDef VV_NHCP_MiscTab;
    static InternalTabCategoryDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(InternalTabCategoryDefOf));
    }
}