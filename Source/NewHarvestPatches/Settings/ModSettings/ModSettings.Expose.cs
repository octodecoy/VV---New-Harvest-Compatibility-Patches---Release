using RimWorld.BaseGen;

namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    public string ModVersion;
    [Ignore]
    public bool Logging = false;
    public bool MergeAnimalFoodsCategory = false;
    public bool AnimalFoodsCategoryResourceReadout = false;
    public bool MergeFruitCategory = false;
    public bool FruitCategoryResourceReadout = false;
    public bool MergeGrainsCategory = false;
    public bool GrainsCategoryResourceReadout = false;
    public bool MergeNutsCategory = false;
    public bool NutsCategoryResourceReadout = false;
    public bool MergeVegetablesCategory = false;
    public bool VegetablesCategoryResourceReadout = false;
    public bool MergeFungusCategory = false;
    public bool FungusCategoryResourceReadout = false;
    
    [RequiresMod(ModRequirement.Medicinal, ModRequirement.VanillaBrewingExpanded)]
    public bool MoveDrinksToVBECategory = false;
    [RequiresMod(ModRequirement.Industrial, ModRequirement.Odyssey)]
    public bool AddAquaticReedsToBiomes = false;
    [RequiresMod(ModRequirement.Industrial)]
    public bool AddMoreWoodFloors = false;
    // Floor dropdowns are pointless while a menu-organising mod is installed - hence NoFernyFloorMenu.
    [RequiresMod(ModRequirement.Industrial, ModRequirement.NoFernyFloorMenu)]
    public bool NewHarvestWoodFloorsToDropdowns = false;
    [RequiresMod(ModRequirement.Industrial, ModRequirement.NoFernyFloorMenu)]
    public bool BaseWoodFloorsToDropdowns = false;
    [RequiresMod(ModRequirement.Industrial, ModRequirement.NoFernyFloorMenu)]
    public bool ModWoodFloorsToDropdowns = false;
    [RequiresMod(ModRequirement.Forage, ModRequirement.Industrial, ModRequirement.NoFernyFloorMenu, AnyOf = true)]
    public bool NewHarvestNonWoodFloorsToDropdown = false; // The logic of this setting is applied via Xml patch.
    [RequiresMod(ModRequirement.Forage)]
    public bool AddHayConversionRecipe = false;
    [RequiresMod(ModRequirement.Industrial, ModRequirement.NoWoodworkingMod, ModRequirement.NoMedievalOverhaul)]
    public bool AddWoodConversionRecipe = false;
    [RequiresMod(ModRequirement.Industrial)]
    public bool UseVanillaLogGraphic = false;
    [RequiresMod(ModRequirement.Industrial, ModRequirement.Ideology)]
    public bool AddWoodDryads = false;

    [DisabledIsEnabled]
    [RequiresMod(ModRequirement.Forage)]
    public bool HayNeedsCooling = true;
    // The three flour settings gate FlourOutputFixer, whose body touches VEF.Plants.DualCropExtension.
    // VEF is NOT named here because VanillaCookingExpanded hard-depends on it - the VCE requirement is
    // what actually guarantees the type resolves. Dropping VanillaCookingExpanded from any of these
    // would let the fixer run without VEF present.
    [RequiresMod(ModRequirement.Garden, ModRequirement.VanillaCookingExpanded)]
    public bool GrainsProduceVCEFlourSecondary = false;
    [RequiresMod(ModRequirement.Garden, ModRequirement.VanillaCookingExpanded)]
    public float GrainFlourOutputPercent = 0.5f; // Fraction of the plant's grain harvestYield produced as flour.
    [RequiresMod(ModRequirement.Garden, ModRequirement.VanillaCookingExpanded)]
    public bool GrainsProduceVCEFlourOnly = false;
    [RequiresMod(ModRequirement.Mushrooms)]
    public bool AddTruffleDiggingBehavior = false;
    [RequiresMod(ModRequirement.Mushrooms)] // Inert, but just document it
    public TruffleDiggingSettings TruffleSettings = new();
    [RequiresMod(ModRequirement.Mushrooms, ModRequirement.Harmony)]
    public bool TreatFungusPoisoningLikeFoodPoisoning = false;
    [RequiresMod(ModRequirement.Forage, ModRequirement.Harmony)]
    public bool AddNourishedHediffFromAnimalFood = false;
    [RequiresMod(ModRequirement.Forage, ModRequirement.Harmony)]
    public float NourishedHediffSeverity = 0.05f; // Used directly from Xml via SetFromModSetting.
    [RequiresMod(ModRequirement.Forage, ModRequirement.Harmony)]
    public IntRange NourishedHediffDuration = new(15000, 60000); // Used directly from Xml via SetFromModSetting.
    [RequiresMod(ModRequirement.Forage)]
    public bool ChangeAnimalFoodRecipes = false;
    [RequiresMod(ModRequirement.Trees)]
    public bool AddMapleSyrupChain = false;

    // Collection-backed settings. Their [RequiresMod] gates the whole group the same way it gates a bool:
    // the Initialize*, Scribe, enabled-provider and tab-visibility paths all ask IsSettingAvailable(nameof(...)).
    [RequiresMod(ModRequirement.Industrial, ModRequirement.NoFuelFilterMod, ModRequirement.NoMedievalOverhaul)]
    public Dictionary<string, bool> FuelTypes = [];
    [RequiresMod(ModRequirement.Industrial)]
    public Dictionary<string, CommonalityInfo> StuffCommonality = [];
    [RequiresMod(ModRequirement.AnyTrees)]
    public Dictionary<string, bool> FallColorTrees = [];
    [RequiresMod(ModRequirement.Industrial)]
    public Dictionary<string, ColorInfo> MaterialColors = [];
    public List<DefCategoryAssignment> CategoryData = [];
    public Dictionary<string, CategoryLabelInfo> CategoryLabelCache = [];

    /// <summary>
    /// Reads/writes Mod.xml. Everything sits behind <see cref="IsSettingAvailable"/> / HasAnyModule, so a
    /// user running only some New Harvest modules never writes settings for the ones they do not have -
    /// and, more importantly, a Scribe_Collections call is skipped rather than writing an empty collection
    /// over stored data that belongs to a module currently uninstalled. Loading replaces collection instances outright,
    /// which is why the category index and the enabled-settings cache are explicitly invalidated here, and every
    /// <see cref="LookMode.Deep"/> collection is run through <see cref="RemoveNullEntries{T}(Dictionary{string, T})"/>.
    /// </summary>
    public override void ExposeData()
    {
        base.ExposeData();
        if (HasAnyModule)
        {
            Scribe_Values.Look(ref ModVersion, nameof(ModVersion), "");
            Scribe_Values.Look(ref Logging, nameof(Logging), false);
            Scribe_Values.Look(ref MergeAnimalFoodsCategory, nameof(MergeAnimalFoodsCategory), false);
            Scribe_Values.Look(ref AnimalFoodsCategoryResourceReadout, nameof(AnimalFoodsCategoryResourceReadout), false);
            Scribe_Values.Look(ref MergeFruitCategory, nameof(MergeFruitCategory), false);
            Scribe_Values.Look(ref FruitCategoryResourceReadout, nameof(FruitCategoryResourceReadout), false);
            Scribe_Values.Look(ref MergeGrainsCategory, nameof(MergeGrainsCategory), false);
            Scribe_Values.Look(ref GrainsCategoryResourceReadout, nameof(GrainsCategoryResourceReadout), false);
            Scribe_Values.Look(ref MergeNutsCategory, nameof(MergeNutsCategory), false);
            Scribe_Values.Look(ref NutsCategoryResourceReadout, nameof(NutsCategoryResourceReadout), false);
            Scribe_Values.Look(ref MergeVegetablesCategory, nameof(MergeVegetablesCategory), false);
            Scribe_Values.Look(ref VegetablesCategoryResourceReadout, nameof(VegetablesCategoryResourceReadout), false);
            Scribe_Values.Look(ref MergeFungusCategory, nameof(MergeFungusCategory), false);
            Scribe_Values.Look(ref FungusCategoryResourceReadout, nameof(FungusCategoryResourceReadout), false);
            Scribe_Values.Look(ref MoveDrinksToVBECategory, nameof(MoveDrinksToVBECategory), false);
            Scribe_Values.Look(ref AddAquaticReedsToBiomes, nameof(AddAquaticReedsToBiomes), false);
            Scribe_Values.Look(ref AddMoreWoodFloors, nameof(AddMoreWoodFloors), false);
            Scribe_Values.Look(ref NewHarvestWoodFloorsToDropdowns, nameof(NewHarvestWoodFloorsToDropdowns), false);
            Scribe_Values.Look(ref BaseWoodFloorsToDropdowns, nameof(BaseWoodFloorsToDropdowns), false);
            Scribe_Values.Look(ref ModWoodFloorsToDropdowns, nameof(ModWoodFloorsToDropdowns), false);
            Scribe_Values.Look(ref NewHarvestNonWoodFloorsToDropdown, nameof(NewHarvestNonWoodFloorsToDropdown), false);
            Scribe_Values.Look(ref AddHayConversionRecipe, nameof(AddHayConversionRecipe), false);
            Scribe_Values.Look(ref AddWoodConversionRecipe, nameof(AddWoodConversionRecipe), false);
            Scribe_Values.Look(ref UseVanillaLogGraphic, nameof(UseVanillaLogGraphic), false);
            Scribe_Values.Look(ref AddWoodDryads, nameof(AddWoodDryads), false);
            Scribe_Values.Look(ref HayNeedsCooling, nameof(HayNeedsCooling), true);
            Scribe_Values.Look(ref GrainsProduceVCEFlourSecondary, nameof(GrainsProduceVCEFlourSecondary), false);
            Scribe_Values.Look(ref GrainFlourOutputPercent, nameof(GrainFlourOutputPercent), 0.5f);
            Scribe_Values.Look(ref GrainsProduceVCEFlourOnly, nameof(GrainsProduceVCEFlourOnly), false);
            Scribe_Values.Look(ref AddTruffleDiggingBehavior, nameof(AddTruffleDiggingBehavior), false);
            Scribe_Deep.Look(ref TruffleSettings, nameof(TruffleSettings));
            TruffleSettings ??= new TruffleDiggingSettings();
            Scribe_Values.Look(ref TreatFungusPoisoningLikeFoodPoisoning, nameof(TreatFungusPoisoningLikeFoodPoisoning), false);
            // Legacy name migration: setting was "GenesPreventFungusPoisoning" before the rename.
            // Will remove this in a later version.
            if (Scribe.mode is LoadSaveMode.LoadingVars && !TreatFungusPoisoningLikeFoodPoisoning)
            {
                bool legacyGenesPreventFungusPoisoning = false;
                Scribe_Values.Look(ref legacyGenesPreventFungusPoisoning, "GenesPreventFungusPoisoning", false);
                TreatFungusPoisoningLikeFoodPoisoning = legacyGenesPreventFungusPoisoning;
            }
            Scribe_Values.Look(ref AddNourishedHediffFromAnimalFood, nameof(AddNourishedHediffFromAnimalFood), false);
            Scribe_Values.Look(ref NourishedHediffSeverity, nameof(NourishedHediffSeverity), 0.05f);
            Scribe_Values.Look(ref NourishedHediffDuration, nameof(NourishedHediffDuration), new(15000, 60000));
            Scribe_Values.Look(ref ChangeAnimalFoodRecipes, nameof(ChangeAnimalFoodRecipes), false);
            Scribe_Values.Look(ref AddMapleSyrupChain, nameof(AddMapleSyrupChain), false);

            if (IsSettingAvailable(nameof(StuffCommonality)))
            {
                Scribe_Collections.Look(ref StuffCommonality, nameof(StuffCommonality), LookMode.Value, LookMode.Deep);
                StuffCommonality ??= [];
                if (Scribe.mode is LoadSaveMode.LoadingVars)
                    RemoveNullEntries(StuffCommonality);
            }

            if (IsSettingAvailable(nameof(MaterialColors)))
            {
                Scribe_Collections.Look(ref MaterialColors, nameof(MaterialColors), LookMode.Value, LookMode.Deep);
                MaterialColors ??= [];
                if (Scribe.mode is LoadSaveMode.LoadingVars)
                    RemoveNullEntries(MaterialColors);
            }

            if (IsSettingAvailable(nameof(FuelTypes)))
            {
                Scribe_Collections.Look(ref FuelTypes, nameof(FuelTypes), LookMode.Value, LookMode.Value);
                FuelTypes ??= [];
            }

            if (IsSettingAvailable(nameof(FallColorTrees)))
            {
                Scribe_Collections.Look(ref FallColorTrees, nameof(FallColorTrees), LookMode.Value, LookMode.Value);
                FallColorTrees ??= [];
            }

            Scribe_Collections.Look(ref CategoryData, nameof(CategoryData), LookMode.Deep);
            CategoryData ??= [];

            if (Scribe.mode is not LoadSaveMode.Saving)
            {
                if (Scribe.mode is LoadSaveMode.LoadingVars)
                    RemoveNullEntries(CategoryData);
                    
                CategoryAssignments.Invalidate(); // List instance replaced on load - index must rebuild.
                InvalidateEnabledSettings(); // Collections replaced on load - cached enabled-set must rebuild.
            }

            Scribe_Collections.Look(ref CategoryLabelCache, nameof(CategoryLabelCache), LookMode.Value, LookMode.Deep);
            CategoryLabelCache ??= [];
            if (Scribe.mode is LoadSaveMode.LoadingVars)
                RemoveNullEntries(CategoryLabelCache);
        }
    }

    /// <summary>
    /// Drops null entries a <see cref="LookMode.Deep"/> collection can come back from disk holding.
    /// <para>
    /// <c>ScribeExtractor.SaveableFromNode</c> returns null for an element whose node carries
    /// <c>IsNull="true"</c>, whose <c>Class</c> attribute names a type that no longer resolves (a class we
    /// renamed, or one an uninstalled mod owned), or whose own <c>ExposeData</c> threw and was caught.
    /// Neither Scribe path filters those out: the List/Deep loop does a bare <c>list.Add(...)</c>, and
    /// <c>Scribe_Collections.BuildDictionary</c> skips null KEYS but stores null VALUES verbatim.
    /// </para>
    /// <para>
    /// Every consumer of these collections dereferences the element straight after a <c>TryGetValue</c> or
    /// a foreach, so one null would NRE. In <c>InitializeCollections</c> that throw is swallowed by
    /// <c>ActionRunner</c> and silently aborts the remaining initializers, leaving the mod half-configured
    /// for the session; in a settings tab it would throw once per frame. Pruning here fixes it in one place
    /// instead of at every call site - a dropped entry just falls back to its default, which is what a
    /// corrupt entry is worth anyway.
    /// </para>
    /// <para>Load-time only: on save the collections are the live, non-null in-memory ones.</para>
    /// </summary>
    private static void RemoveNullEntries<T>(Dictionary<string, T> dict) where T : class
    {
        dict.RemoveAll(kvp => kvp.Value == null);
    }

    /// <inheritdoc cref="RemoveNullEntries{T}(Dictionary{string, T})"/>
    private static void RemoveNullEntries<T>(List<T> list) where T : class
    {
        list.RemoveAll(item => item == null);
    }
}