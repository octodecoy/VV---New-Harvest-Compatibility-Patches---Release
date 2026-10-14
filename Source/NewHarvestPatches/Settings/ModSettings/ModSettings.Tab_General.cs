namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    private void DoGeneralTab(Listing_Standard listing)
    {
        SettingCheckbox(
            listing,
            nameof(Logging),
            ref Logging,
            v => Logging = v,
            restartRequired: false,
            iconTexPath: UITextureCache.IconFolder + "LoggingIcon");
    }
}
