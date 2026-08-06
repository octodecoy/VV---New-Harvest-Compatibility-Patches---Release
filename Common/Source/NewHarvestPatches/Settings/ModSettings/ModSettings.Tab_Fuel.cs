namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    /// <summary>
    /// One checkbox per fuel-capable material, built from the DefDatabase rather than a fixed list, so
    /// materials from module versions we know nothing about still appear.
    /// </summary>
    private void DoFuelTab(Listing_Standard listing)
    {
        using (new UIState(anchor: TextAnchor.MiddleCenter))
        {
            listing.Label(GetSettingLabel(SettingLabelKind.Raw, "TabSubLabel_FuelDescription"));
        }

        foreach (var fuel in DefUtility.ThingDefs.FuelThingDefs)
        {
            // Dict-backed: local temp by ref, the setter writes the dictionary slot on change.
            // Per-fuel default is wood-ness, so tab reset lands on wood-defaults, not all-false.
            bool val = FuelTypes.TryGetValue(fuel.defName, out var enabled) && enabled;
            SettingCheckbox(
                listing,
                nameof(FuelTypes),
                ref val,
                v => FuelTypes[fuel.defName] = v,
                defaultValue: DefUtility.ThingDefs.IndustrialResourceDefs[fuel].isWood,
                restartRequired: false,
                iconDef: fuel,
                labelOverride: fuel.LabelCapSafe(),
                idSuffix: fuel.defName);
        }
    }
}
