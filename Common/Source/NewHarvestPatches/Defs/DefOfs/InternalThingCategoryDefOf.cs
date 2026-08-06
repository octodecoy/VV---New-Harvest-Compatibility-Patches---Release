namespace NewHarvestPatches; 

[DefOf]
public static class InternalThingCategoryDefOf
{
    public static ThingCategoryDef VV_NHCP_DummyCategory_AnimalFoods;
    public static ThingCategoryDef VV_NHCP_DummyCategory_Fruit;
    public static ThingCategoryDef VV_NHCP_DummyCategory_Grains;
    public static ThingCategoryDef VV_NHCP_DummyCategory_Nuts;
    public static ThingCategoryDef VV_NHCP_DummyCategory_Vegetables;  
    public static ThingCategoryDef VV_NHCP_DummyCategory_Fungus;   
    static InternalThingCategoryDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(InternalThingCategoryDefOf));
    }
}