namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    /// <summary>
    /// Recipe-adding settings, each shown only when the module that owns the recipe's ingredients is
    /// installed - a setting for a recipe the player could never craft is hidden rather than disabled.
    /// That gating lives entirely in each field's [RequiresMod] (see ModSettings.Expose), including the
    /// wood-conversion row standing down for Extended/Expanded Woodworking and Medieval Overhaul.
    /// Icons come from GetNamedSilentFail because they are decoration only: a missing def costs an icon,
    /// not the setting.
    /// </summary>
    private void DoCraftingTab(Listing_Standard listing)
    {
        SettingCheckbox(
            listing,
            nameof(AddHayConversionRecipe),
            ref AddHayConversionRecipe,
            v => AddHayConversionRecipe = v,
            iconDef: ThingDefOf.Hay);

        SettingCheckbox(
            listing,
            nameof(ChangeAnimalFoodRecipes),
            ref ChangeAnimalFoodRecipes,
            v => ChangeAnimalFoodRecipes = v,
            iconDef: DefDatabase<ThingDef>.GetNamedSilentFail("VV_FodderMix"));

        SettingCheckbox(
            listing,
            nameof(AddWoodConversionRecipe),
            ref AddWoodConversionRecipe,
            v => AddWoodConversionRecipe = v,
            iconDef: DefDatabase<ThingDef>.GetNamedSilentFail("VV_CedarWood"));

        SettingCheckbox(
            listing,
            nameof(AddMapleSyrupChain),
            ref AddMapleSyrupChain,
            v => AddMapleSyrupChain = v,
            iconDef: DefDatabase<ThingDef>.GetNamedSilentFail("VV_MapleSyrup"));
    }
}
