namespace NewHarvestPatches;

public static class SharedConstants
{
    public const string ModPrefix = "NHCP";
    public static class ModName
    {
        public static class Prefix
        {
            public const string Parent = "VV_";
            public const string Compat = Parent + ModPrefix + "_";
        }
        public static class PackageId
        {
            public const string MainId = "vvenchov.vvnewharvest";
            public const string ForageId = $"{MainId}foragecrops";
            public const string GardenId = $"{MainId}gardencrops";
            public const string IndustrialId = $"{MainId}industrialcrops";
            public const string MedicinalId = $"{MainId}medicinalplants";
            public const string TreesId = $"{MainId}trees";
            public const string FlowersId = $"{MainId}flowers";
            public const string MushroomsId = $"{MainId}mushrooms";
            public const string FruitId = $"{MainId}fruitplants";
        }
    }
    public static class Category
    {
        public static class Prefix
        {
            /// <summary>
            /// Marks the ThingCategoryDefs this mod creates purely as sorting targets - they exist to hang
            /// other mods' produce off, never to appear as real categories. Used both to validate that a Def
            /// points at one of ours and to exclude them when scanning for genuine categories to match
            /// against, so tests on this prefix must stay in sync with the XML defNames.
            /// </summary>
            public const string DummyCategory = ModName.Prefix.Compat + "DummyCategory_";
        }

        /// <summary>
        /// The category kinds we sort produce into. These are matched by name against other mods' category
        /// defNames, so the member names themselves are data, not just identifiers - renaming one changes
        /// which foreign categories match.
        /// </summary>
        public static class Kind
        {
            public const string AnimalFoods = nameof(AnimalFoods);
            public const string Grains = nameof(Grains);
            public const string Fruit = nameof(Fruit);
            public const string Nuts = nameof(Nuts);
            public const string Vegetables = nameof(Vegetables);
            public const string Fungus = nameof(Fungus);
        }

        /// <summary>
        /// Our own raw produce per category kind - which defs this mod considers to BE fruit, grain, and so
        /// on. Read by both halves of the mod, which is why it lives here rather than on either consumer:
        /// the post-load classifier seeds its defaults map from it
        /// (<c>CategoryClassifier.AddSeedMapDefaults</c>), and the XML patch phase injects these defNames
        /// into third-party filters that reference a matching foreign category
        /// (<c>AddOwnDefsToCategoryFilters</c>). A plain string table with no DefDatabase dependency, so the
        /// patch phase - which runs long before any Def exists - can read it directly.
        /// <para>
        /// Names are NOT filtered by which New Harvest module is installed. Every consumer must check that a
        /// def actually exists before using it: the classifier skips a missing def via
        /// <c>DefDatabase.GetNamedSilentFail</c>, and the patch operation drops names absent from
        /// <c>NewHarvestPatchesModSettings.ThingDefNamesInDocument</c>. Writing an unresolvable defName into
        /// a filter's thingDefs produces one cross-reference error per occurrence.
        /// </para>
        /// </summary>
        internal static readonly Dictionary<string, string[]> s_ownDefNamesByKind = new()
        {
            [Kind.AnimalFoods] =
            [
                "VV_AlfalfaHay", "VV_CloverHay", "VV_TimothyHay", "VV_FeedCorn", "VV_FeedWheat",
                "VV_FeedBeet", "VV_BeetPulp", "VV_FodderMix", "VV_ProteinKibble",
            ],
            [Kind.Fruit] =
            [
                "VV_Blackberries", "VV_Blackcurrants", "VV_DragonFruits", "VV_Rosehips", "VV_Cranberries",
                "VV_AppleCactusFruits", "VV_HawthornBerries", "VV_Apricots", "VV_Durians", "VV_Grapefruits",
                "VV_JabuticabaBerries", "VV_Limes", "VV_Medlars", "VV_Mulberries", "VV_Papayas",
                "VV_Persimmons", "VV_Pomegranates", "VV_Quinces", "VV_SourCherrys", "VV_WhiteMulberries",
                "VV_CherryPlums",
            ],
            [Kind.Grains] =
            [
                "VV_AmaranthGrain", "VV_MilletGrain", "VV_QuinoaSeeds", "VV_TeffGrain",
            ],
            [Kind.Nuts] =
            [
                "VV_CashewNuts", "VV_Chestnuts", "VV_Hazelnuts", "VV_PineNuts", "VV_Pecans", "VV_Walnuts",
            ],
            [Kind.Vegetables] =
            [
                "VV_Artichoke", "VV_Asparagus", "VV_BlackBeans", "VV_BroccoliFlorets", "VV_Leeks",
                "VV_Parsnips", "VV_RedCabbages", "VV_SpinachLeaves", "VV_WhiteTurnips", "VV_Tomatillos",
                "VV_NettleLeaves",
            ],
            [Kind.Fungus] =
            [
                "VV_BlackTruffles", "VV_KingBoletes", "VV_Chanterelles", "VV_Morels", "VV_ParasolMushrooms",
                "VV_OysterMushrooms", "VV_ShiitakeMushrooms", "VV_FlyAgarics", "VV_FoolsFunnels",
                "VV_JackOLanternMushrooms",
            ],
        };

        /// <summary>
        /// Name-based guess at whether a third-party ThingCategoryDef represents the given kind. There is no
        /// metadata to go on, so this pairs hardcoded defNames for known mods with loose substring matches
        /// for unknown ones. Deliberately permissive - <see cref="IsExcludedName"/> and
        /// <see cref="ParentMatchesKind"/> are what keep the false positives out, so all three belong
        /// together at every call site.
        /// <para>
        /// Single source for both halves of the mod: the XML patch phase
        /// (<c>PatchOperationPathedExtended.TextMatchesForCategory</c>) and the post-load pass
        /// (<c>CategoryApplier.IsModAddedCategoryOfType</c>), which used to hand-copy these rules and could
        /// drift apart silently - a drift that shows up as defs moved into our category while the matching
        /// trader stock clone is never made.
        /// </para>
        /// </summary>
        public static bool NameMatchesKind(string defName, string kind)
        {
            if (string.IsNullOrEmpty(defName))
                return false;

            return kind switch
            {
                Kind.AnimalFoods => defName == "AnimalFeed" ||
                                    defName == "Feed" ||
                                    defName == "DavaiAnimalFood" ||
                                    defName.ContainsIgnoreCase("Animal") ||
                                    defName.ContainsIgnoreCase("Feed"),
                Kind.Fruit => defName == "VCE_Fruit" ||
                              defName == "FruitFoodRaw" ||
                              defName == "RC2_FruitsRaw" ||
                              defName.ContainsIgnoreCase(Kind.Fruit),
                Kind.Grains => defName == "RC2_GrainsRaw" ||
                               defName == "DankPyon_Cereal" ||
                               defName.ContainsIgnoreCase("Grain") ||
                               defName.ContainsIgnoreCase("Cereal"),
                Kind.Nuts => defName.ContainsIgnoreCase(Kind.Nuts),
                Kind.Vegetables => defName == "RC2_VegetablesRaw" ||
                                   defName.ContainsIgnoreCase("Vegetable"),
                Kind.Fungus => defName.ContainsIgnoreCase("Fungus") ||
                               defName.ContainsIgnoreCase("Mushroom"),
                _ => false,
            };
        }

        /// <summary>
        /// Structural half of the category test: a genuine raw-food category sits under Foods, FoodRaw or
        /// PlantFoodRaw, and the kind being asked about has to be one we actually sort. Rules out a
        /// similarly-named category living somewhere unrelated in the tree. Shared for the same reason as
        /// <see cref="NameMatchesKind"/>.
        /// </summary>
        public static bool ParentMatchesKind(string kind, string parentDefName)
        {
            if (string.IsNullOrWhiteSpace(parentDefName))
                return false;

            return (kind is Kind.AnimalFoods or Kind.Fruit or Kind.Grains or Kind.Nuts or Kind.Vegetables or Kind.Fungus)
                && (parentDefName == "Foods" || parentDefName == "FoodRaw" || parentDefName == "PlantFoodRaw");
        }

        /// <summary>
        /// Names that would pass a raw-food name test but must never be treated as a raw-food category -
        /// our own dummy categories, plus product/processed/corpse variants that carry food-like words.
        /// Lives here rather than on the patch-operation base class because the post-load side needs it
        /// too: <c>CategoryClassifier.AddCategoryChildren</c> prunes on exactly this test while walking a
        /// matched category's subtree, and it must stay the same test the patch phase used to match the
        /// category in the first place.
        /// </summary>
        public static bool IsExcludedName(string defName)
        {
            return !string.IsNullOrEmpty(defName)
                && (defName.StartsWith(Prefix.DummyCategory) ||
                    defName.ContainsIgnoreCase("Product") || // AnimalProduct
                    defName.ContainsIgnoreCase("Process") || // RimCuisine2 "Processed" categories, etc
                    defName.ContainsIgnoreCase("Corpse"));   // AnimalCorpse
        }
    }

    public static class Setting
    {
        public static class Prefix
        {
            public const string DisabledFuel_ = nameof(DisabledFuel_); // Disabled fuel types
            public const string NoFallColors_ = nameof(NoFallColors_); // Disabled fall colors on trees
            public const string SetCommonality_ = nameof(SetCommonality_); // Changed commonality
        }
    }

    public static class TKey
    {
        public const string KeyPrefix = ModPrefix + ".";
    }

    /// <summary>
    /// Hover tints for toggleable settings rows, drawn behind the row's own widget. They preview the RESULT
    /// of clicking, not the current state - a row that is currently on highlights red, because clicking will
    /// turn it off. Low alpha because the label draws on top of them.
    /// </summary>
    public static class Colors
    {
        public static readonly Color GreenHighlightColor = new(0f, 1f, 0f, 0.2f);
        public static readonly Color RedHighlightColor = new(1f, 0f, 0f, 0.2f);
    }
}