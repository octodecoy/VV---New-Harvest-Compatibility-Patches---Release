namespace NewHarvestPatches; 

[DefOf]
public static class InternalHediffDefOf
{
    // No purpose without Harmony.
    [MayRequire("brrainz.harmony")]
    [MayRequireAnyOf($"{ModName.PackageId.MainId},{ModName.PackageId.ForageId}")]
    public static HediffDef VV_NHCP_NourishedPet;
    static InternalHediffDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(InternalHediffDefOf));
    }
}