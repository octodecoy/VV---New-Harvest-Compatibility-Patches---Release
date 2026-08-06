namespace NewHarvestPatches;

public static partial class Utils
{
    /// <summary>
    /// Which mods are installed, resolved ONCE into static readonly flags. The active mod list cannot
    /// change without a restart, so these never need invalidating and can be read from anywhere without a
    /// lookup. Initialization runs at first touch of this class, which is inside the Mod constructor -
    /// before defs load - so nothing here may reference a Def.
    ///
    /// Two tiers: raw presence (Has*) and aggregate convenience flags (HasAny*). This class is the ONLY
    /// place mod presence is tested - whether a setting is available is decided by its [RequiresMod]
    /// attribute and read through NewHarvestPatchesModSettings.IsSettingAvailable, never by a flag read at
    /// the point of use. The one deliberate exception is <see cref="ShowVEFCommonalitySettings"/>, which
    /// picks between two live modes of a setting rather than gating it.
    ///
    /// The main module contains every submodule, so each Has*Module is true when the main module is present.
    /// </summary>
    public static class ModChecker
    {
        public static readonly bool HasMainModule = IsModActive(ModName.PackageId.MainId);
        public static readonly bool HasForageModule = HasMainModule || IsModActive(ModName.PackageId.ForageId);
        public static readonly bool HasGardenModule = HasMainModule || IsModActive(ModName.PackageId.GardenId);
        public static readonly bool HasIndustrialModule = HasMainModule || IsModActive(ModName.PackageId.IndustrialId);
        public static readonly bool HasMedicinalModule = HasMainModule || IsModActive(ModName.PackageId.MedicinalId);
        public static readonly bool HasTreesModule = HasMainModule || IsModActive(ModName.PackageId.TreesId);
        public static readonly bool HasFlowersModule = HasMainModule || IsModActive(ModName.PackageId.FlowersId);
        public static readonly bool HasMushroomsModule = HasMainModule || IsModActive(ModName.PackageId.MushroomsId);
        public static readonly bool HasFruitModule = HasMainModule || IsModActive(ModName.PackageId.FruitId);
        public static readonly bool HasAnyModule = 
            HasForageModule || 
            HasGardenModule || 
            HasIndustrialModule || 
            HasMedicinalModule || 
            HasTreesModule || 
            HasFlowersModule || 
            HasMushroomsModule || 
            HasFruitModule;

        /// <summary>
        /// Indicates whether any tree modules are installed.
        /// </summary>
        /// <remarks>Returns <see langword="true"/> if HasIndustrialModule or HasMedicinalModule or HasTreesModule.</remarks>
        public static readonly bool HasAnyTrees = HasTreesModule || HasIndustrialModule || HasMedicinalModule;

        /// <summary>
        /// Indicates whether any modules that provide fruit are installed.
        /// </summary>
        /// <remarks>Returns <see langword="true"/> if HasFruitModule or HasMedicinalModule or HasTreesModule.</remarks>
        public static readonly bool HasAnyFruit = HasFruitModule || HasMedicinalModule || HasTreesModule;

        /// <summary>
        /// Indicates whether any modules that provide vegetables are installed.
        /// </summary>
        /// <remarks>Returns <see langword="true"/> if HasGardenModule or HasMedicinalModule.</remarks>
        public static readonly bool HasAnyVegetables = HasGardenModule || HasMedicinalModule;

        public static readonly bool HasHarmony = IsModActive("brrainz.harmony");
        public static readonly bool HasMedievalOverhaul = IsModActive("dankpyon.medieval.overhaul");
        public static readonly bool HasVanillaCookingExpanded = IsModActive("vanillaexpanded.vcooke");

        /// <summary>
        /// If installed, show setting to move Medicinal module drinks to its non-alcoholic category.
        /// </summary>
        public static readonly bool HasVanillaBrewingExpanded = IsModActive("vanillaexpanded.vbrewe");

        /// <summary>
        /// Ferny's Floor Menu / Better Architect Menu. Makes floor dropdowns obsolete, so the dropdown
        /// settings stand down via ModRequirement.NoFernyFloorMenu.
        /// </summary>
        public static readonly bool HasFernyFloorMenu = IsAnyModActive(ignorePostfix: true, "ferny.floormenu", "ferny.betterarchitect");

        /// <summary>
        /// If Vanilla Expanded Framework is active, we can show commonality sliders utlizing its StuffExtension class instead of base game commonality stat.
        /// </summary>
        public static readonly bool HasVanillaExpandedFramework = IsModActive("oskarpotocki.vanillafactionsexpanded.core");
        
        /// <summary>
        /// Extended/Expanded Woodworking. Both add their own wood-conversion recipes, so
        /// <c>AddWoodConversionRecipe</c> stands down rather than fighting them for the product.
        /// </summary>
        private static readonly bool HasWoodworkingMod = IsAnyModActive(ignorePostfix: true, "teflonjim.extendedwoodworking", "zal.expandwoodwork");

        /// <summary>
        /// LWM's Fuel Filter / [JPT] Burn It for Fuel 2. Both add a fuel ITab, so the user can toggle
        /// fuels themselves and our fuel settings stand down.
        /// </summary>
        private static readonly bool HasFuelFilterMod = IsAnyModActive(ignorePostfix: true, "zal.lwmfuelfilter", "jpt.burnitforfuel");

        /// <summary>
        /// Indicates whether Vanilla Expanded Framework's commonality Stuff Extension sliders should be displayed in menu.
        /// </summary>
        /// <remarks>If !HasIndustrialModule or !HasVanillaExpandedFramework, returns <see langword="false"/>.</remarks>
        public static readonly bool ShowVEFCommonalitySettings = HasIndustrialModule && HasVanillaExpandedFramework;

        /// <summary>
        /// Evaluates a <see cref="RequiresModAttribute"/>: every positive requirement must be met, or any one
        /// of them if AnyOf. Exclusions (the No* members) are ALWAYS mandatory and sit outside AnyOf - "any
        /// one of these modules, and none of those mods" is the shape every real gate needs, and an exclusion
        /// folded into an AnyOf list would satisfy the whole attribute on its own.
        /// An attribute with no requirements is satisfied - it constrains nothing.
        /// </summary>
        public static bool IsSatisfied(RequiresModAttribute requiresMod)
        {
            ModRequirement[] requirements = requiresMod.Requirements;
            if (requirements.NullOrEmpty())
                return true;

            bool sawPositive = false;
            bool anyPositiveSatisfied = false;

            foreach (ModRequirement requirement in requirements)
            {
                bool satisfied = IsSatisfied(requirement);

                if (IsExclusion(requirement))
                {
                    if (!satisfied)
                        return false;
                    continue;
                }

                sawPositive = true;
                if (requiresMod.AnyOf)
                {
                    if (satisfied)
                        anyPositiveSatisfied = true;
                }
                else if (!satisfied)
                {
                    return false;
                }
            }

            // AnyOf over an exclusion-only list has nothing to choose from - the exclusions already passed.
            return !requiresMod.AnyOf || !sawPositive || anyPositiveSatisfied;
        }

        /// <summary>
        /// True for the No* members - requirements met by a mod's ABSENCE. Kept as an explicit switch rather
        /// than an enum-order or name check so adding a member cannot silently change how existing ones evaluate.
        /// </summary>
        private static bool IsExclusion(ModRequirement requirement)
        {
            return requirement is ModRequirement.NoMedievalOverhaul
                or ModRequirement.NoFernyFloorMenu
                or ModRequirement.NoWoodworkingMod
                or ModRequirement.NoFuelFilterMod;
        }

        /// <summary>
        /// Maps a <see cref="ModRequirement"/> to its active-mod flag. Valid in the Mod constructor (ModLister/ModsConfig based).
        /// </summary>
        public static bool IsSatisfied(ModRequirement requirement)
        {
            return requirement switch
            {
                ModRequirement.Forage => HasForageModule,
                ModRequirement.Garden => HasGardenModule,
                ModRequirement.Industrial => HasIndustrialModule,
                ModRequirement.Medicinal => HasMedicinalModule,
                ModRequirement.Trees => HasTreesModule,
                ModRequirement.Mushrooms => HasMushroomsModule,
                ModRequirement.Fruit => HasFruitModule,
                ModRequirement.Flowers => HasFlowersModule,
                ModRequirement.AnyTrees => HasAnyTrees,
                ModRequirement.AnyFruit => HasAnyFruit,
                ModRequirement.AnyVegetables => HasAnyVegetables,
                ModRequirement.Harmony => HasHarmony,
                ModRequirement.VanillaExpandedFramework => HasVanillaExpandedFramework,
                ModRequirement.VanillaCookingExpanded => HasVanillaCookingExpanded,
                ModRequirement.VanillaBrewingExpanded => HasVanillaBrewingExpanded,
                ModRequirement.MedievalOverhaul => HasMedievalOverhaul,
                ModRequirement.Ideology => ModsConfig.IdeologyActive,
                ModRequirement.Odyssey => ModsConfig.OdysseyActive,
                // Exclusions - satisfied while the mod is absent. Keep IsExclusion in sync.
                ModRequirement.NoMedievalOverhaul => !HasMedievalOverhaul,
                ModRequirement.NoFernyFloorMenu => !HasFernyFloorMenu,
                ModRequirement.NoWoodworkingMod => !HasWoodworkingMod,
                ModRequirement.NoFuelFilterMod => !HasFuelFilterMod,
                _ => false
            };
        }

        /// <summary>
        /// Checks if a mod is active based on its PackageID.  Mainly used to check for New Harvest modules while ignoring steam_suffix postfix for Dev versions.
        /// </summary>
        /// <returns>Returns <see langword="false"/> if the passed PackageID is not active.</returns>
        public static bool IsModActive(string packageId, bool ignorePostfix = true)
        {
            return !string.IsNullOrWhiteSpace(packageId) && ModLister.GetActiveModWithIdentifier(packageId, ignorePostfix) != null;
        }

        /// <summary>Overload also returning the mod's metadata (version, name) when it is active.</summary>
        public static bool IsModActive(string packageId, out ModMetaData metaData, bool ignorePostfix = true)
        {
            metaData = null;
            if (string.IsNullOrWhiteSpace(packageId))
                return false;

            metaData = ModLister.GetActiveModWithIdentifier(packageId, ignorePostfix);
            
            return metaData != null;
        }

        /// <summary>
        /// True if any listed packageId is active. Used where several different mods provide the same
        /// feature and any one of them means we should stand down.
        /// </summary>
        public static bool IsAnyModActive(bool ignorePostfix = true, params string[] packageIds)
        {
            if (packageIds.NullOrEmpty())
                return false;

            if (ignorePostfix)
                return ModLister.AnyModActiveNoSuffix([.. packageIds]);
            else
                return ModLister.AnyFromListActive([.. packageIds]);
        }
    }
}