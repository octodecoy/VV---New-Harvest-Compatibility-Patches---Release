namespace NewHarvestPatches; 

[DefOf]
public static class InternalThingDefOf
{
    [MayRequireAnyOf($"{ModName.PackageId.MainId},{ModName.PackageId.MushroomsId}")]
    public static ThingDef VV_BlackTruffles;
    static InternalThingDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(InternalThingDefOf));
    }  
}