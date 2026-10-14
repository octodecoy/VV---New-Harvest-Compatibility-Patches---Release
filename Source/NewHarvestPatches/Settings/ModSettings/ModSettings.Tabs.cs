namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    private static List<TabCategoryDef> s_tabsInt;

    // Session cache: tab -> "has at least one visible setting". Nulled alongside _tabsInt in ClearMenuSessionCaches.
    private static Dictionary<TabCategoryDef, Func<bool>> s_tabHasContentInt;

    /// <summary>
    /// Tabs to actually show, filtered by <see cref="TabHasContent"/>. Built from the DefDatabase rather
    /// than a hardcoded list, so a tab can be added or reordered in XML alone; a tab whose every setting
    /// is gated off by missing modules is dropped entirely rather than shown empty.
    /// </summary>
    public static List<TabCategoryDef> Tabs
    {
        get
        {
            if (s_tabsInt == null)
                BuildTabs();

            return s_tabsInt;
        }
    }

    // Lazily built registry of per-tab "has visible content" predicates. A tab with no entry here is always
    // shown (General, Categories - nothing on them is mod-gated). NO PREDICATE MAY TEST MOD PRESENCE: each
    // one names the settings the tab draws and asks whether any of them is available, so the answer comes
    // from those settings' own [RequiresMod] (see ModSettings.Expose) and cannot drift out of step with the
    // rows themselves. Collection-backed tabs additionally require a populated collection, which is a
    // question about loaded defs rather than about mods.
    private static Dictionary<TabCategoryDef, Func<bool>> TabHasContent
    {
        get
        {
            return s_tabHasContentInt ??= BuildTabHasContent();
        }
    }

    private static Dictionary<TabCategoryDef, Func<bool>> BuildTabHasContent()
    {
        var map = new Dictionary<TabCategoryDef, Func<bool>>();
        Add(InternalTabCategoryDefOf.VV_NHCP_CraftingTab, () => AnySettingAvailable(
            nameof(Settings.AddHayConversionRecipe),
            nameof(Settings.ChangeAnimalFoodRecipes),
            nameof(Settings.AddWoodConversionRecipe),
            nameof(Settings.AddMapleSyrupChain)));

        Add(InternalTabCategoryDefOf.VV_NHCP_FloorsTab, () => AnySettingAvailable(
            nameof(Settings.AddMoreWoodFloors),
            nameof(Settings.NewHarvestWoodFloorsToDropdowns),
            nameof(Settings.BaseWoodFloorsToDropdowns),
            nameof(Settings.ModWoodFloorsToDropdowns),
            nameof(Settings.NewHarvestNonWoodFloorsToDropdown)));

        Add(InternalTabCategoryDefOf.VV_NHCP_FuelTab, () => IsSettingAvailable(nameof(Settings.FuelTypes))
            && !Settings.FuelTypes.NullOrEmpty());

        Add(InternalTabCategoryDefOf.VV_NHCP_VisualsTab, () => IsSettingAvailable(nameof(Settings.UseVanillaLogGraphic))
            || (IsSettingAvailable(nameof(Settings.FallColorTrees)) && !Settings.FallColorTrees.NullOrEmpty())
            || (IsSettingAvailable(nameof(Settings.MaterialColors)) && !Settings.MaterialColors.NullOrEmpty()));

        Add(InternalTabCategoryDefOf.VV_NHCP_BehaviorsTab, () => AnySettingAvailable(
            nameof(Settings.AddNourishedHediffFromAnimalFood),
            nameof(Settings.AddTruffleDiggingBehavior)));   
        
        Add(InternalTabCategoryDefOf.VV_NHCP_CommonalityTab, () => IsSettingAvailable(nameof(Settings.StuffCommonality))
            && !Settings.StuffCommonality.NullOrEmpty());   

        Add(InternalTabCategoryDefOf.VV_NHCP_MiscTab, () => AnySettingAvailable(
            nameof(Settings.HayNeedsCooling),
            nameof(Settings.AddWoodDryads),     
            nameof(Settings.AddAquaticReedsToBiomes),
            nameof(Settings.GrainsProduceVCEFlourSecondary),
            nameof(Settings.MoveDrinksToVBECategory),
            nameof(Settings.TreatFungusPoisoningLikeFoodPoisoning)));

        return map;

        // Maybe someone deleted a tab def for some stupid reason.
        void Add(TabCategoryDef tab, Func<bool> hasContent)
        {
            if (tab != null)
                map[tab] = hasContent;
        }
    }

    private static void BuildTabs()
    {
        s_tabsInt = [];

        foreach (TabCategoryDef tab in DefDatabase<TabCategoryDef>.AllDefs)
        {
            if (TabHasContent.TryGetValue(tab, out Func<bool> hasContent) && !hasContent())
                continue;

            s_tabsInt.Add(tab);
        }
        
        // Shouldn't need to distinct, but just for safety.
        s_tabsInt = [.. s_tabsInt.Distinct()]; 
    }
}
