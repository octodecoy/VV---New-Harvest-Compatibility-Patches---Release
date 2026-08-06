namespace NewHarvestPatches; 

[DefOf]
public static class InternalTerrainAffordanceDefOf
{
    public static TerrainAffordanceDef Diggable;
    static InternalTerrainAffordanceDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(InternalTerrainAffordanceDefOf));
    }
}