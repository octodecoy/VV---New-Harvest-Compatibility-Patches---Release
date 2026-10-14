namespace NewHarvestPatches;

/// <summary>
/// Boot-time replacement for the old per-def XML patch lists: decides which dummy
/// category every food def belongs to by default. Precedence per def (first wins):
/// persisted user state in Settings.CategoryData, CategoryDefaultsDef instances,
/// the shared seed map of our own defs (SharedConstants.Category.OwnDefNamesByKind), then
/// heuristics (mod-added category children and the fungus foodType scan). Only writes
/// through CategoryAssignments.
/// </summary>
internal static class CategoryClassifier
{
    private static HashSet<ThingCategoryDef> s_allFoodCategoriesInt;
    private static HashSet<ThingCategoryDef> s_allPlantFoodRawCategoriesInt;

    public static HashSet<ThingCategoryDef> AllFoodCategories => GetSubtree(ref s_allFoodCategoriesInt, ThingCategoryDefOf.Foods);
    public static HashSet<ThingCategoryDef> AllPlantFoodRawCategories => GetSubtree(ref s_allPlantFoodRawCategoriesInt, ThingCategoryDefOf.PlantFoodRaw);

    /// <summary>
    /// A category subtree snapshotted into the given cache field on first use. A missing root yields an
    /// empty set that is deliberately NOT cached, so a call made before the DefOf is populated cannot
    /// freeze the snapshot empty for the session. Invalidated by <see cref="ClearCache"/>.
    /// </summary>
    private static HashSet<ThingCategoryDef> GetSubtree(ref HashSet<ThingCategoryDef> cache, ThingCategoryDef root)
    {
        if (cache != null)
            return cache;

        if (root == null)
            return [];

        return cache = [.. root.ThisAndChildCategoryDefs];
    }

    // Result of the last BuildDefaults, held for the session - see Defaults.
    private static Dictionary<string, string> s_defaults;

    /// <summary>
    /// The defaults map, built once and reused. Rebuilding it means a full DefDatabase sweep
    /// (<see cref="AddHeuristicDefaults"/>) plus a pruned subtree walk per mod-added category
    /// (<see cref="AddCategoryChildren"/>), and
    /// <see cref="ClassifyAll"/> runs on every settings-window close that changed anything at all - the
    /// change need not be category related. Every input is fixed for the session except the two category
    /// tree snapshots above, so the same invalidation covers this: <see cref="ClearCache"/>.
    /// </summary>
    private static Dictionary<string, string> Defaults => s_defaults ??= BuildDefaults();

    /// <summary>
    /// Drops every cache derived from the category tree. Called by CategoryApplier whenever an apply
    /// pass reparented a category, which is the only thing that can change what these snapshots - and
    /// therefore the defaults map built from them - would contain.
    /// </summary>
    public static void ClearCache()
    {
        s_allFoodCategoriesInt = null;
        s_allPlantFoodRawCategoriesInt = null;
        s_defaults = null;
    }

    /// <summary>
    /// Ensures every def with a known default has a CategoryData entry whose
    /// OriginalCategory is that default. Persisted user choices (assignments and
    /// user-removals) are never overridden. Cheap to call repeatedly - the defaults it walks are
    /// cached (<see cref="Defaults"/>), leaving only a lookup and a couple of writes per entry.
    /// </summary>
    /// <param name="pruneStaleDefaults">
    /// Also clear persisted defaults this build no longer produces (see
    /// <see cref="CategoryAssignments.PruneStaleDefaults"/>). Boot only: <see cref="Defaults"/> is rebuilt
    /// from the live tree, and after the first apply pass that tree no longer lists the defs we moved, so a
    /// later prune would erase our own work.
    /// </param>
    /// <returns>True if any CategoryData entry was created or changed, so callers can skip a settings write when nothing moved.</returns>
    internal static bool ClassifyAll(bool pruneStaleDefaults = false)
    {
        using (Profiler.Measure(nameof(CategoryClassifier), nameof(ClassifyAll)))
        {
            Dictionary<string, string> defaults = Defaults;
            bool changed = false;

            if (pruneStaleDefaults)
                changed = CategoryAssignments.PruneStaleDefaults(defaults);

            foreach (var kvp in defaults)
            {
                string defName = kvp.Key;
                string dummyCategoryName = kvp.Value;

                if (DefDatabase<ThingDef>.GetNamedSilentFail(defName) == null)
                    continue;

                bool isNew = !CategoryAssignments.TryGet(defName, out _);
                DefCategoryAssignment assignment = CategoryAssignments.GetOrAdd(defName, dummyCategoryName);
                if (assignment == null)
                    continue;

                // Refresh the default; a def's classification can change between versions/mod lists.
                if (assignment.OriginalCategory != dummyCategoryName)
                {
                    assignment.OriginalCategory = dummyCategoryName;
                    changed = true;
                }

                if (isNew)
                    changed = true;

                if (assignment.IsUserRemoved || assignment.HasCategory)
                    continue; // User decision or persisted assignment wins.

                // Past that guard the entry holds no category, so this always assigns one.
                assignment.AssignTo(dummyCategoryName);
                changed = true;
            }

            LogMessage(() => $"Classifier processed [{defaults.Count}] default assignments");
            return changed;
        }
    }

    /// <summary>
    /// defName -> dummy category defName. Later writes win, so fill lowest precedence first.
    /// Expensive; go through <see cref="Defaults"/> rather than calling this directly.
    /// </summary>
    private static Dictionary<string, string> BuildDefaults()
    {
        Dictionary<string, string> defaults = [];

        AddHeuristicDefaults(defaults);
        AddModAddedCategoryChildren(defaults);
        AddSeedMapDefaults(defaults);
        AddCategoryDefaultsDefEntries(defaults);

        return defaults;
    }

    /// <summary>
    /// Lowest-precedence layer for our own produce, read from
    /// <see cref="SharedConstants.Category.s_ownDefNamesByKind"/> - the same table the XML patch phase injects
    /// from, so a def cannot be classified as one kind here and injected as another there.
    /// </summary>
    private static void AddSeedMapDefaults(Dictionary<string, string> defaults)
    {
        foreach (var kvp in Category.s_ownDefNamesByKind)
        {
            string dummyCategoryName = Category.Prefix.DummyCategory + kvp.Key;
            foreach (string defName in kvp.Value)
            {
                defaults[defName] = dummyCategoryName;
            }
        }
    }

    private static void AddCategoryDefaultsDefEntries(Dictionary<string, string> defaults)
    {
        foreach (var defaultsDef in DefDatabase<CategoryDefaultsDef>.AllDefs)
        {
            if (defaultsDef.targetDefaultCategory == null || defaultsDef.defNames.NullOrEmpty())
                continue;

            foreach (string defName in defaultsDef.defNames.Distinct())
            {
                if (!string.IsNullOrWhiteSpace(defName))
                    defaults[defName] = defaultsDef.targetDefaultCategory.defName;
            }
        }
    }

    /// <summary>
    /// Children of third-party categories matched during XML patching (TestCategory)
    /// default into our category of the same type. Replaces the old
    /// TryReplaceCategoryChildrenWithCategoryOfType patch operation.
    /// </summary>
    private static void AddModAddedCategoryChildren(Dictionary<string, string> defaults)
    {
        var modAddedCategories = CategoryApplier.ModAddedCategoriesByType;
        if (modAddedCategories.NullOrEmpty())
            return;

        foreach (var kvp in modAddedCategories)
        {
            string dummyCategoryName = Category.Prefix.DummyCategory + kvp.Key;

            foreach (string categoryDefName in kvp.Value)
            {
                var categoryDef = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(categoryDefName);
                if (categoryDef == null)
                    continue;

                AddCategoryChildren(categoryDef, dummyCategoryName, defaults);
            }
        }
    }

    /// <summary>
    /// ThingCategoryDef.DescendantThingDefs equivalent that prunes excluded subcategories rather than
    /// taking the whole subtree. A matched raw-food category routinely parents product/processed/corpse
    /// subcategories - the very names <see cref="SharedConstants.Category.IsExcludedName"/> rejects when
    /// the category itself is tested - so an unpruned walk filed a mod's jams, dried fruit and corpses as
    /// raw produce, moved them into our category and dragged them through every filter correction with it.
    /// Same test as the patch phase used to match the parent category, so both halves agree on what counts
    /// as raw food.
    /// </summary>
    private static void AddCategoryChildren(ThingCategoryDef categoryDef, string dummyCategoryName, Dictionary<string, string> defaults)
    {
        if (!categoryDef.childThingDefs.NullOrEmpty())
        {
            foreach (ThingDef def in categoryDef.childThingDefs)
            {
                if (def != null && !def.defName.NullOrEmpty())
                    defaults[def.defName] = dummyCategoryName;
            }
        }

        if (categoryDef.childCategories.NullOrEmpty())
            return;

        foreach (ThingCategoryDef child in categoryDef.childCategories)
        {
            if (child == null || Category.IsExcludedName(child.defName))
                continue;

            AddCategoryChildren(child, dummyCategoryName, defaults);
        }
    }

    /// <summary>
    /// Fungus and Animal Food heuristic property scan.
    /// </summary>
    private static void AddHeuristicDefaults(Dictionary<string, string> defaults)
    {
        if (ThingCategoryDefOf.Foods is not { } foodsCategory)
            return;

        if (ThingCategoryDefOf.PlantFoodRaw is not { } plantFoodRawCategory)
            return;

        if (InternalThingCategoryDefOf.VV_NHCP_DummyCategory_Fungus is not { } dummyFungusCategory)
            return;

        if (InternalThingCategoryDefOf.VV_NHCP_DummyCategory_AnimalFoods is not { } dummyAnimalFoodsCategory)
            return;

        foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
        {
            if (def.ingestible is not { } ingestible)
                continue;

            var thingCategories = def.thingCategories;
            if (thingCategories.NullOrEmpty())
                continue;

            bool isFungus = ingestible.foodType.HasFlag(FoodTypeFlags.Fungus);
            bool isAnimalFood = !isFungus && def.IsAnimalFood();
            if (!isFungus && !isAnimalFood)
                continue;

            bool excluded = thingCategories.Any(cat =>
                cat != null &&
                (cat.defName.ContainsIgnoreCase("meat") ||
                cat.defName == "Textiles"));

            if (excluded)
                continue;

            if (isFungus)
            {
                if (thingCategories.Any(AllPlantFoodRawCategories.Contains))
                    defaults[def.defName] = dummyFungusCategory.defName;
            }
            else
            {
                defaults[def.defName] = dummyAnimalFoodsCategory.defName;
            }
        }
    }
}
