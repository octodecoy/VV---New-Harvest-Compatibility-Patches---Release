namespace NewHarvestPatches;

/// <summary>
/// Base for this mod's BRANCHING patch operations - ones that test a condition and then run
/// <c>caseTrue</c> or <c>caseFalse</c>, rather than editing nodes themselves. Deliberately not derived
/// from PatchOperationPathed: these need no xpath, because the nested operation carries its own.
/// Fields are filled by RimWorld's XML loader through reflection.
/// </summary>
internal abstract class PatchOperationExtended : PatchOperation
{
    protected readonly CompareLogic logic = CompareLogic.Or;
    protected readonly PatchOperation caseTrue = null;
    protected readonly PatchOperation caseFalse = null;
}
