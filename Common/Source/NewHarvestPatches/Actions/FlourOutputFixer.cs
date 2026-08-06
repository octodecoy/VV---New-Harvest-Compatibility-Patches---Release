namespace NewHarvestPatches;

/// <summary>
/// Scales VEF DualCropExtension flour output for our grain plants to GrainFlourOutputPercent of
/// the plant's grain harvestYield, and - when GrainsProduceVCEFlourOnly is set - swaps the plant's
/// harvested product to flour outright instead of producing it alongside the grain.
/// Mutates shared ThingDefs, so it requires a restart.
/// </summary>
internal static class FlourOutputFixer
{
    public static void TryFixFlourOutput()
    {
        ActionRunner.Run(nameof(FlourOutputFixer), nameof(TryFixFlourOutput), FixFlourOutput);
    }

    /// <summary>
    /// Flour count for a plant yielding <paramref name="harvestYield"/> grain at
    /// <paramref name="percent"/>. Shared with the settings tab so the tooltip's worked example can
    /// never disagree with what actually gets applied. Never returns 0 for a plant that yields
    /// grain - a low percent produces one flour rather than nothing.
    /// </summary>
    /// <param name="harvestYield">The plant's grain yield; truncated to a whole count first.</param>
    /// <param name="percent">The GrainFlourOutputPercent setting, as a 0-1 fraction.</param>
    /// <returns>Flour count, at least 1 unless the plant yields no whole grain at all.</returns>
    public static int GetFlourAmount(float harvestYield, float percent)
    {
        int yield = (int)harvestYield;
        return yield == 0 ? 0 : Math.Max(1, Mathf.RoundToInt(yield * percent));
    }

    /// <summary>
    /// Body of <see cref="TryFixFlourOutput"/>. Finds grain plants by defName convention rather than a
    /// fixed list so grains added by later New Harvest updates are picked up automatically, then either
    /// rescales the dual-crop secondary output or hands off to <see cref="ReplaceGrainWithFlour"/>.
    /// </summary>
    private static void FixFlourOutput()
    {
        List<ThingDef> grainPlants = [.. DefDatabase<ThingDef>.AllDefs
            .Where(td => td?.defName.StartsWith(ModName.Prefix.Parent) == true &&
                            td.IsPlant == true &&
                            td.plant?.harvestedThingDef is { } harvestedDef &&
                            harvestedDef.defName.StartsAndEndsWith(start: ModName.Prefix.Parent, end: "Grain"))
            ];

        if (grainPlants.Count == 0)
        {
            LogMessage(() => "No grain plant ThingDefs found to fix flour output.", LogMessageType.Error);
            return;
        }

        float percent = Settings.GrainFlourOutputPercent;
        bool replaceGrain = Settings.GrainsProduceVCEFlourOnly;

        foreach (var plantDef in grainPlants)
        {
            var modExtension = plantDef.GetModExtension<VEF.Plants.DualCropExtension>();
            if (modExtension == null)
            {
                LogMessage(() => $"Plant [{plantDef.defName}] missing VEF.Plants.DualCropExtension ModExtension, skipping flour output fix.", LogMessageType.Error);
                continue;
            }

            int desiredOutputAmount = GetFlourAmount(plantDef.plant.harvestYield, percent);
            if (desiredOutputAmount == 0)
                continue;

            if (replaceGrain)
            {
                ReplaceGrainWithFlour(plantDef, modExtension, desiredOutputAmount);
                continue;
            }

            int currentOutputAmount = modExtension.outPutAmount;
            if (desiredOutputAmount != currentOutputAmount)
            {
                modExtension.outPutAmount = desiredOutputAmount;
                LogMessage(() => $"Fixed flour output for plant [{plantDef.defName}] from [{currentOutputAmount}] to [{desiredOutputAmount}].");
            }
        }
    }

    /// <summary>
    /// Turns a grain plant into a flour plant: harvests the extension's secondary output directly
    /// at <paramref name="flourAmount"/>, then drops the dual-crop extension so the flour is not
    /// also spawned a second time as a secondary. The flour def is taken from the extension rather
    /// than looked up by name, keeping it in step with whatever the XML patch assigned.
    /// The extension parameter is typed as the base DefModExtension: an optional-mod type must
    /// never appear in a method signature, only inside a body.
    /// </summary>
    /// <param name="plantDef">The grain plant to convert.</param>
    /// <param name="dualCropExtension">The plant's VEF DualCropExtension, typed as its base-game base type; removed from the plant on success.</param>
    /// <param name="flourAmount">New harvest yield, from <see cref="GetFlourAmount"/>.</param>
    private static void ReplaceGrainWithFlour(ThingDef plantDef, DefModExtension dualCropExtension, int flourAmount)
    {
        ThingDef flourDef = ((VEF.Plants.DualCropExtension)dualCropExtension).secondaryOutput;
        if (flourDef == null)
        {
            LogMessage(() => $"Plant [{plantDef.defName}] has a VEF.Plants.DualCropExtension with no secondaryOutput, skipping grain replacement.", LogMessageType.Error);
            return;
        }

        ThingDef previousHarvestedDef = plantDef.plant.harvestedThingDef;
        float previousYield = plantDef.plant.harvestYield;

        plantDef.plant.harvestedThingDef = flourDef;
        plantDef.plant.harvestYield = flourAmount;
        plantDef.modExtensions?.Remove(dualCropExtension);

        LogMessage(() => $"Replaced harvest output for plant [{plantDef.defName}]: [{previousHarvestedDef?.defName}] x[{previousYield}] -> [{flourDef.defName}] x[{flourAmount}].");
    }
}
