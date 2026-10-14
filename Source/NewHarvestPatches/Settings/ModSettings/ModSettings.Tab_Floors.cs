namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    /// <summary>
    /// Floor settings: "add floors", plus the four "move floors into architect dropdowns" toggles that
    /// carry NoFernyFloorMenu and so disappear on their own when a menu-organising mod makes dropdowns
    /// obsolete. No mod checks here - each row is gated by its [RequiresMod] (see ModSettings.Expose).
    /// </summary>
    private void DoFloorsTab(Listing_Standard listing)
    {
        SettingCheckbox(
            listing,
            nameof(AddMoreWoodFloors),
            ref AddMoreWoodFloors,
            v => AddMoreWoodFloors = v,
            iconDef: DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor"));

        SettingCheckbox(
            listing,
            nameof(NewHarvestWoodFloorsToDropdowns),
            ref NewHarvestWoodFloorsToDropdowns,
            v => NewHarvestWoodFloorsToDropdowns = v,
            iconDef: DefDatabase<TerrainDef>.GetNamedSilentFail("VV_CedarFloor"));

        SettingCheckbox(
            listing,
            nameof(BaseWoodFloorsToDropdowns),
            ref BaseWoodFloorsToDropdowns,
            v => BaseWoodFloorsToDropdowns = v,
            iconDef: DefDatabase<TerrainDef>.GetNamedSilentFail("VV_MahoganyFloor"));

        SettingCheckbox(
            listing,
            nameof(ModWoodFloorsToDropdowns),
            ref ModWoodFloorsToDropdowns,
            v => ModWoodFloorsToDropdowns = v,
            iconDef: DefDatabase<TerrainDef>.GetNamedSilentFail("VV_RosewoodFloor"));

        SettingCheckbox(
            listing,
            nameof(NewHarvestNonWoodFloorsToDropdown),
            ref NewHarvestNonWoodFloorsToDropdown,
            v => NewHarvestNonWoodFloorsToDropdown = v,
            iconDef: HasIndustrialModule ? DefDatabase<TerrainDef>.GetNamedSilentFail("VV_RattanFloor") : HasForageModule ? DefDatabase<TerrainDef>.GetNamedSilentFail("VV_AlfalfaMatting") : null);
    }
}
