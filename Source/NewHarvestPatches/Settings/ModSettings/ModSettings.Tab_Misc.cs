namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    /// <summary>
    /// Everything that does not belong to a themed tab. No mod checks here - every row is gated by its
    /// own [RequiresMod] (see ModSettings.Expose).
    /// </summary>
    private void DoMiscTab(Listing_Standard listing)
    {
        SettingCheckbox(
            listing,
            nameof(HayNeedsCooling),
            ref HayNeedsCooling,
            v => HayNeedsCooling = v,
            defaultValue: true,
            iconDef: ThingDefOf.Hay);

        SettingCheckbox(
            listing,
            nameof(AddWoodDryads),
            ref AddWoodDryads,
            v => AddWoodDryads = v,
            iconDef: DefDatabase<PawnKindDef>.GetNamedSilentFail("VV_NHCP_Dryad_Cedarmaker"));

        SettingCheckbox(
            listing,
            nameof(AddAquaticReedsToBiomes),
            ref AddAquaticReedsToBiomes,
            v => AddAquaticReedsToBiomes = v,
            iconDef: DefDatabase<ThingDef>.GetNamedSilentFail("VV_ReedPlant"));

        DrawFlourSettings(listing);

        SettingCheckbox(
            listing,
            nameof(MoveDrinksToVBECategory),
            ref MoveDrinksToVBECategory,
            v => MoveDrinksToVBECategory = v,
            iconDef: DefDatabase<ThingDef>.GetNamedSilentFail("VV_LindenTea"));

        SettingCheckbox(
            listing,
            nameof(TreatFungusPoisoningLikeFoodPoisoning),
            ref TreatFungusPoisoningLikeFoodPoisoning,
            v => TreatFungusPoisoningLikeFoodPoisoning = v,
            iconDef: DefDatabase<ThingDef>.GetNamedSilentFail("VV_JackOLanternMushroom"));
    }

    private static readonly FloatRange s_flourOutputRange = new(0.05f, 5f);
    private const float FlourOutputStep = 0.05f;

    // Flour worked-example inputs and the two tooltips built from them. The example is a pure function
    // of the output percentage and the flour-only toggle, so the strings are rebuilt only when one of
    // those changes rather than every frame. Defs are session-permanent; the strings are
    // language-dependent, so ClearMenuSessionCaches() drops them (via the -1f percent sentinel).
    private static ThingDef s_flourDef;
    private static float s_grainHarvestYieldExample = -1f;
    private static string s_flourParentTooltip;
    private static string s_flourOnlyTooltip;
    private static float s_flourTooltipPercent = -1f;
    private static bool s_flourTooltipOnly;

    /// <summary>
    /// Flour toggle plus its two children: output percentage and the harvest-flour-instead-of-grain
    /// switch. Both children are indented one level and gated on the parent toggle.
    /// The group as a whole is gated on the parent's [RequiresMod]: the child rows would hide themselves,
    /// but this method also carves rects out of the listing, which must not happen at all when hidden.
    /// </summary>
    private void DrawFlourSettings(Listing_Standard listing)
    {
        if (!IsSettingAvailable(nameof(GrainsProduceVCEFlourSecondary)))
            return;

        s_flourDef ??= DefDatabase<ThingDef>.GetNamedSilentFail("VCE_Flour");

        EnsureFlourTooltipsCached();

        SettingCheckbox(
            listing,
            nameof(GrainsProduceVCEFlourSecondary),
            ref GrainsProduceVCEFlourSecondary,
            v => GrainsProduceVCEFlourSecondary = v,
            tooltipOverride: s_flourParentTooltip,
            iconDef: s_flourDef);

        bool flourEnabled = GrainsProduceVCEFlourSecondary;

        // The slider helpers are Rect-based only, so carve the row out of the listing by hand.
        Rect sliderRect = IndentedRect(listing.GetRect(GetSliderBlockHeight()));
        SettingFloatSliderPercent(
            sliderRect,
            nameof(GrainFlourOutputPercent),
            currentValue: GrainFlourOutputPercent,
            defaultValue: 0.5f,
            min: s_flourOutputRange.min,
            max: s_flourOutputRange.max,
            roundTo: FlourOutputStep,
            enabled: flourEnabled,
            setter: v => GrainFlourOutputPercent = v);

        listing.Gap(listing.verticalSpacing + GenUI.GapSmall);

        SettingCheckbox(
            listing,
            nameof(GrainsProduceVCEFlourOnly),
            ref GrainsProduceVCEFlourOnly,
            v => GrainsProduceVCEFlourOnly = v,
            indentLevel: 1,
            enabled: flourEnabled,
            tooltipOverride: s_flourOnlyTooltip,
            showIcon: false);

        // The library only grays a child out - the value has to be forced off here.
        if (!flourEnabled)
            GrainsProduceVCEFlourOnly = false;
    }

    /// <summary>
    /// Rebuilds the two flour tooltips when (and only when) the output percentage or the flour-only
    /// toggle has moved since they were last built. The worked example still comes from the live def
    /// and the same helper <see cref="FlourOutputFixer"/> applies, so it cannot drift from the real
    /// result - it just is not recomputed on frames where nothing feeding it changed.
    /// </summary>
    private void EnsureFlourTooltipsCached()
    {
        if (s_flourTooltipPercent == GrainFlourOutputPercent && s_flourTooltipOnly == GrainsProduceVCEFlourOnly)
            return;

        if (s_grainHarvestYieldExample < 0f)
            s_grainHarvestYieldExample = DefDatabase<ThingDef>.GetNamedSilentFail("VV_AmaranthPlant")?.plant?.harvestYield ?? 0f;

        int flourExample = FlourOutputFixer.GetFlourAmount(s_grainHarvestYieldExample, GrainFlourOutputPercent);

        // Replacing the grain means no grain is harvested at all, so the parent's "grain and flour"
        // example would be wrong - swap to the flour-only wording.
        s_flourParentTooltip = GrainsProduceVCEFlourOnly
            ? $"CheckboxTooltip_{nameof(GrainsProduceVCEFlourSecondary)}Replaced".TranslateKey(args: [flourExample])
            : $"CheckboxTooltip_{nameof(GrainsProduceVCEFlourSecondary)}".TranslateKey(args: [s_grainHarvestYieldExample, flourExample]);

        s_flourOnlyTooltip = $"CheckboxTooltip_{nameof(GrainsProduceVCEFlourOnly)}".TranslateKey(args: [flourExample]);

        s_flourTooltipPercent = GrainFlourOutputPercent;
        s_flourTooltipOnly = GrainsProduceVCEFlourOnly;
    }
}
