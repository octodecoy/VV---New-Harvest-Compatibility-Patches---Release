namespace NewHarvestPatches
{
    internal static class CategorySyncer
    {
        private static HashSet<ThingCategoryDef> _categoriesToResolve = [];
        private static HashSet<RecipeDef> _recipesToResolve = [];
        private static HashSet<ThingDef> _buildingsToResolve = [];

        private static void TrySyncDefsToCategory(List<ThingDef> modDefs, string sourceCategoryName, string categoryType, string targetDefToSyncWith = null)
        {

            if (string.IsNullOrWhiteSpace(categoryType))
            {
                ToLog($"categoryType was null/whitespace, aborting sync", 1);
                return;
            }

            if (string.IsNullOrWhiteSpace(sourceCategoryName))
            {
                ToLog($"sourceCategoryName was null/whitespace for type [{categoryType}], aborting sync", 1);
                return;
            }

            if (modDefs.NullOrEmpty())
            {
                ToLog($"modDefs is null/empty for type [{categoryType}] and sourceCategory [{sourceCategoryName}], aborting sync.  All belong to another category already?", 1);
                return;
            }

            StartStopwatch(nameof(CategorySyncer), nameof(TrySyncDefsToCategory));
            try
            {
                SyncDefsToCategory(modDefs, sourceCategoryName, categoryType, targetDefToSyncWith);
            }
            catch (Exception ex)
            {
                ExToLog(ex, MethodBase.GetCurrentMethod(), optMsg: sourceCategoryName);
            }
            finally
            {
                LogStopwatch(nameof(CategorySyncer), nameof(TrySyncDefsToCategory));
            }
        }

        private static void TryMergeToCategory()
        {
            StartStopwatch(nameof(CategorySyncer), nameof(TryMergeToCategory));
            try
            {
                MergeToCategory();
            }
            catch (Exception ex)
            {
                ExToLog(ex, MethodBase.GetCurrentMethod());
            }
            finally
            {
                LogStopwatch(nameof(CategorySyncer), nameof(TryMergeToCategory));
            }
        }


        public static void SyncAllFoods()
        {
            StartStopwatch(nameof(CategorySyncer), nameof(SyncAllFoods));
            try
            {
                if (EnabledSettings.Any(s => s.StartsAndEndsWith("Merge", Setting.Suffix.Category)))
                {
                    TryMergeToCategory();
                }

                if (EnabledSettings.Any(s => s.StartsAndEndsWith("AddTo", Setting.Suffix.Category)))
                {
                    List<ThingDef> thingDefList;
                    ThingCategoryDef sourceCategory;
                    if (HasForageModule && Settings.AddToAnimalFoodsCategory && !Settings.MergeAnimalFoodsCategory)
                    {
                        sourceCategory = NHCP_ThingCategoryDefOf.VV_NHCP_DummyCategory_AnimalFoods;
                        thingDefList = ThingDefUtility.GetChildDefsOfCategory(sourceCategory, newHarvestOnly: true);
                        TrySyncDefsToCategory(thingDefList, sourceCategory?.defName, Category.Type.AnimalFoods, "Hay");
                    }

                    if (HasAnyFruit && Settings.AddToFruitCategory && !Settings.MergeFruitCategory)
                    {
                        sourceCategory = NHCP_ThingCategoryDefOf.VV_NHCP_DummyCategory_Fruit;
                        thingDefList = ThingDefUtility.GetChildDefsOfCategory(sourceCategory, newHarvestOnly: true);
                        TrySyncDefsToCategory(thingDefList, sourceCategory?.defName, Category.Type.Fruit, "RawBerries");
                    }

                    if (HasGardenModule && Settings.AddToGrainsCategory && !Settings.MergeGrainsCategory)
                    {
                        sourceCategory = NHCP_ThingCategoryDefOf.VV_NHCP_DummyCategory_Grains;
                        thingDefList = ThingDefUtility.GetChildDefsOfCategory(sourceCategory, newHarvestOnly: true);
                        TrySyncDefsToCategory(thingDefList, sourceCategory?.defName, Category.Type.Grains, "RawCorn");
                    }

                    if (HasTreesModule && Settings.AddToNutsCategory && !Settings.MergeNutsCategory)
                    {
                        sourceCategory = NHCP_ThingCategoryDefOf.VV_NHCP_DummyCategory_Nuts;
                        thingDefList = ThingDefUtility.GetChildDefsOfCategory(sourceCategory, newHarvestOnly: true);
                        TrySyncDefsToCategory(thingDefList, sourceCategory?.defName, Category.Type.Nuts);
                    }

                    if (HasAnyVegetables && Settings.AddToVegetablesCategory && !Settings.MergeVegetablesCategory)
                    {
                        sourceCategory = NHCP_ThingCategoryDefOf.VV_NHCP_DummyCategory_Vegetables;
                        thingDefList = ThingDefUtility.GetChildDefsOfCategory(sourceCategory, newHarvestOnly: true);
                        TrySyncDefsToCategory(thingDefList, sourceCategory?.defName, Category.Type.Vegetables, "RawPotatoes");
                    }

                    if (HasMushroomsModule && Settings.AddToFungusCategory && !Settings.MergeFungusCategory)
                    {
                        sourceCategory = NHCP_ThingCategoryDefOf.VV_NHCP_DummyCategory_Fungus;
                        thingDefList = ThingDefUtility.GetChildDefsOfCategory(sourceCategory, newHarvestOnly: true);
                        TrySyncDefsToCategory(thingDefList, sourceCategory?.defName, Category.Type.Fungus, "RawMushroom");
                    }
                }

                ResolveAllReferences();
                Settings.WriteSettingsToFile();
            }
            finally
            {
                LogStopwatch(nameof(CategorySyncer), nameof(SyncAllFoods));
            }
        }

        private static List<ThingDef> GetAllThingDefsFromCache()
        {
            if (Settings.CategoryData.NullOrEmpty())
                return [];

            return [.. Settings.CategoryData
                .Where(info => !info.IsCurrentCategoryUserDisabled && info.CurrentCategoryName != Category.Type.None_Base)
                .Select(info => DefDatabase<ThingDef>.GetNamedSilentFail(info.ThingDefName))
                .Where(def => def != null)];
        }

        private static List<ThingDef> GetAllUserDisabledThingDefsFromCache()
        {
            if (Settings.CategoryData.NullOrEmpty())
                return [];

            return [.. Settings.CategoryData
                .Where(info => info.IsCurrentCategoryUserDisabled)
                .Select(info => DefDatabase<ThingDef>.GetNamedSilentFail(info.ThingDefName))
                .Where(def => def != null)];
        }

        private static List<ThingCategoryDef> GetThingCategoryDefsFromCache()
        {
            if (Settings.CategoryData.NullOrEmpty())
                return [];

            var uniqueNames = new HashSet<string>(
                Settings.CategoryData
                    .Select(info => info.CurrentCategoryName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
            );

            return [.. uniqueNames
                .Select(name => DefDatabase<ThingCategoryDef>.GetNamedSilentFail(name))
                .Where(def => def != null)];
        }

        private static List<ThingCategoryDef> GetCategorySet(string categoryType)
        {
            if (ModAddedCategoryDictionary.NullOrEmpty())
                return [];

            if (ModAddedCategoryDictionary.TryGetValue(categoryType, out var cacheSet))
            {
                return [.. cacheSet
                    .Select(defName => DefDatabase<ThingCategoryDef>.GetNamedSilentFail(defName))
                    .Where(def => def != null)];
            }
            return [];
        }

        private static void TrySetCategoryForUserDisabledThingDefs()
        {
            var userDisabledThingDefs = GetAllUserDisabledThingDefsFromCache();
            if (userDisabledThingDefs.NullOrEmpty())
                return;

            HashSet<ThingCategoryDef> categoriesToResolve = [];
            List<ThingCategoryDef> removedDummyCategories = [];
            foreach (var def in userDisabledThingDefs)
            {
                var thingCategories = def.thingCategories;
                if (thingCategories.NullOrEmpty())
                {
                    ToLog($"\tSkipping [{def.defName}] as it has no categories", 2);
                    continue;
                }

                bool hasNonDummyCategory = thingCategories.Any(cat => !cat.defName?.StartsWith(Category.Prefix.VV_NHCP_DummyCategory_) == true);
                if (!hasNonDummyCategory)
                {
                    ToLog($"\tSkipping [{def.defName ?? "??"}] as it has no non-dummy categories", 2);
                    continue;
                }

                if (thingCategories.Count == 1)
                {
                    var onlyCategory = thingCategories[0];
                    if (onlyCategory?.defName?.StartsWith(Category.Prefix.VV_NHCP_DummyCategory_) == true)
                    {
                        var parentCategory = onlyCategory.parent;
                        if (parentCategory == null)
                        {
                            parentCategory = ThingCategoryDefOf.Foods; // Just fallback to Foods
                            ToLog($"\tNo parent category found for [{onlyCategory.defName}], defaulting to Foods", 1);
                        }
                        categoriesToResolve.AddRange(thingCategories.Concat(parentCategory));
                        removedDummyCategories.AddUnique(onlyCategory);
                        thingCategories.Clear();
                        thingCategories.Add(parentCategory);
                        if (!parentCategory.childThingDefs.Contains(def))
                        {
                            parentCategory.childThingDefs.Add(def);
                        }
                        ToLog($"\tSet [{def.defName}] to parent category [{parentCategory.defName}] instead of user disabled [{onlyCategory.defName}]", 1);
                        continue;
                    }
                    else
                    {
                        continue;
                    }
                }

                categoriesToResolve.AddRange(thingCategories);
                removedDummyCategories.AddRangeWhereFast(thingCategories, cat => cat?.defName?.StartsWith(Category.Prefix.VV_NHCP_DummyCategory_) == true);

                int removedCount = def.thingCategories.RemoveAll(cat => cat?.defName?.StartsWith(Category.Prefix.VV_NHCP_DummyCategory_) == true);
                ToLog($"\tRemoved [{removedCount}] dummy categories from [{def.defName}] due to user disabling", 1);
            }

            foreach (var cat in removedDummyCategories.Distinct())
            {
                if (cat.childThingDefs.NullOrEmpty())
                    continue;
                int removedCount = cat.childThingDefs.RemoveAll(userDisabledThingDefs.Contains);
                if (removedCount > 0)
                {
                    ToLog($"\tRemoved [{removedCount}] defs from category [{cat.defName ?? "??"}] due to user disabling", 1);
                }
            }

            CacheReferencesForResolving(categoriesToResolve);
        }

        private static void MergeToCategory()
        {
            var thingDefList = GetAllThingDefsFromCache();
            if (thingDefList.NullOrEmpty())
            {
                ToLog($"thingDefList is null/empty, aborting", 2);
                return;
            }

            var cachedCategoryDefs = GetThingCategoryDefsFromCache();
            if (cachedCategoryDefs.NullOrEmpty())
            {
                ToLog($"No cached categories found, aborting", 2);
                return;
            }

            TrySetCategoryForUserDisabledThingDefs();

            for (int i = thingDefList.Count - 1; i >= 0; i--)
            {
                var thingDef = thingDefList[i];
                if (!DefToCategoryInfo.TryGetDefToCategoryInfo(thingDef.defName, out var defToCategoryInfo))
                {
                    ToLog($"\tRemoving [{thingDef.defName}] from merge list as no cached info found", 2);
                    thingDefList.RemoveAt(i);
                    continue;
                }
                // Redundant
                if (defToCategoryInfo.IsCurrentCategoryUserDisabled || defToCategoryInfo.CurrentCategoryName == Category.Type.None_Base)
                {
                    ToLog($"\tRemoving [{thingDef.defName}] from merge list as it is set to none or user disabled", 1);
                    thingDefList.RemoveAt(i);
                    continue;
                }

                if (!defToCategoryInfo.CurrentCategoryName.StartsWith(Category.Prefix.VV_NHCP_DummyCategory_))
                {
                    ToLog($"\tRemoving [{thingDef.defName}] from merge list as its assigned category [{defToCategoryInfo.CurrentCategoryName}] is not one of our categories", 2);
                    thingDefList.RemoveAt(i);
                    continue;
                }

                if (thingDef.thingCategories?.Count == 1 && thingDef.thingCategories[0]?.defName == defToCategoryInfo.CurrentCategoryName)
                {
                    ToLog($"\tRemoving [{thingDef.defName}] from merge list as it already belongs only to [{defToCategoryInfo.CurrentCategoryName}]", 1);
                    thingDefList.RemoveAt(i);
                }
            }

            if (thingDefList.Count == 0)
            {
                ToLog($"All defs already belong to their assigned categories or are set to None, returning", 1);
                return;
            }

            ToLog($"Attempting to merge [{thingDefList.Count}] defs", 1);

            Dictionary<ThingDef, List<string>> initialCategories = [];
            if (Settings.Logging)
            {
                foreach (var def in thingDefList)
                {
                    initialCategories[def] = def.thingCategories?.Select(cat => cat.defName)?.ToList() ?? [];
                    ToLog($"Initial categories for {def.defName}: {string.Join(", ", initialCategories[def])}");
                }
            }

            HashSet<ThingCategoryDef> modCategories = [.. thingDefList
                .Where(def => !def.thingCategories.NullOrEmpty())
                .SelectMany(def => def.thingCategories)];

            HashSet<ThingCategoryDef> categoriesToResolve = modCategories;

            foreach (var cat in modCategories)
            {
                int removedCount = cat.childThingDefs.RemoveAll(thingDefList.Contains);
                if (removedCount > 0)
                {
                    ToLog($"\tRemoved {removedCount} defs from category {cat.defName}");
                }
            }

            foreach (var def in thingDefList)
            {
                // Can't be null, but whatever
                if (!DefToCategoryInfo.TryGetDefToCategoryInfo(def.defName, out var defToCategoryInfo))
                {
                    ToLog($"\tNo cached info found for def {def.defName}, skipping", 2);
                    continue;
                }

                ThingCategoryDef categoryDefToUse = cachedCategoryDefs.FirstOrDefault(cat => cat.defName == defToCategoryInfo.CurrentCategoryName);

                // Can't be null, but whatever
                if (categoryDefToUse == null)
                {
                    ToLog($"\tNo cached category def found for name {defToCategoryInfo.CurrentCategoryName}, skipping def {def.defName}", 2);
                    continue;
                }

                categoriesToResolve.AddRange(def.thingCategories.Concat(categoryDefToUse));

                def.thingCategories.RemoveWhere(cat => cat != categoryDefToUse);

                if (!def.thingCategories.Contains(categoryDefToUse))
                {
                    def.thingCategories.Add(categoryDefToUse);
                    ToLog($"\tAdded category {defToCategoryInfo.CurrentCategoryName} to def {def.defName}");
                }
                if (!categoryDefToUse.childThingDefs.Contains(def))
                {
                    categoryDefToUse.childThingDefs.Add(def);
                    ToLog($"\tAdded def {def.defName} to category {categoryDefToUse.defName}");
                }
            }

            if (Settings.Logging)
            {
                PrintFinalCategories(thingDefList, initialCategories);
            }

            CacheReferencesForResolving(categoriesToResolve);
        }


        private static void SyncDefsToCategory(List<ThingDef> thingDefList, string sourceCategoryName, string categoryType, string targetDefToSyncWith = null)
        {
            var categoriesToTrySyncWith = GetCategorySet(categoryType);
            if (categoriesToTrySyncWith.NullOrEmpty())
            {
                ToLog($"No categories to sync with found for sourceCategory [{sourceCategoryName}], aborting sync", 1);
                return;
            }

            if (EnabledSettings.Any(s => s.StartsAndEndsWith("Merge", Setting.Suffix.Category)))
            {
                var categoryData = Settings.CategoryData;
                if (!categoryData.NullOrEmpty())
                {
                    for (int i = thingDefList.Count - 1; i >= 0; i--)
                    {
                        var thingDef = thingDefList[i];
                        if (!DefToCategoryInfo.TryGetDefToCategoryInfo(thingDef.defName, out var defToCategoryInfo))
                            continue;

                        if (defToCategoryInfo.CurrentCategoryName != Category.Type.None_Base)
                        {
                            thingDefList.RemoveAt(i);
                            ToLog($"\tRemoving [{thingDef.defName}] from sync list as it is set to [{defToCategoryInfo.CurrentCategoryName}]", 1);
                        }
                    }
                    if (thingDefList.Count == 0)
                    {
                        ToLog($"All defs in thingDefList are assigned to categories other than [{sourceCategoryName}], aborting sync", 1);
                        return;
                    }
                }
            }

            // Log initial categories for each def
            ToLog($"Beginning category sync for [{thingDefList.Count}] defs from [{sourceCategoryName}], found [{categoriesToTrySyncWith.Count}] potential target categories: [{string.Join(", ", categoriesToTrySyncWith.Select(c => c.defName))}]", 1);

            Dictionary<ThingDef, List<string>> initialCategories = [];
            foreach (var def in thingDefList)
            {
                initialCategories[def] = def.thingCategories?.Select(c => c.defName)?.ToList() ?? [];
                ToLog($"\tInitial categories for [{def.defName}]: [{string.Join(", ", initialCategories[def])}]");
            }

            ThingCategoryDef targetCategory = null;
            if (categoriesToTrySyncWith.Count == 1)
            {
                targetCategory = categoriesToTrySyncWith[0];
                ToLog($"Selected target category [{targetCategory.defName}] based on it being the only one", 1);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(targetDefToSyncWith))
                {
                    targetCategory = categoriesToTrySyncWith
                        .FirstOrDefault(cat => cat.childThingDefs.Any(def => def.defName == targetDefToSyncWith));
                }

                if (targetCategory == null)
                {
                    targetCategory = categoriesToTrySyncWith
                        .OrderByDescending(cat => cat.childThingDefs.Count)
                        .FirstOrDefault();

                    ToLog($"Selected target category [{targetCategory.defName}] based on child def count of [{targetCategory.childThingDefs.Count}]");
                }
                else
                {
                    ToLog($"Selected target category [{targetCategory.defName}] based on sync target [{targetDefToSyncWith}]", 1);
                }
            }

            HashSet<ThingCategoryDef> categoriesToResolve =
            [
                targetCategory,
            ];

            HashSet<ThingCategoryDef> modCategories = [.. thingDefList
                .Where(def => !def.thingCategories.NullOrEmpty())
                .SelectMany(def => def.thingCategories)
                .Where(cat => !cat.defName?.StartsWith(Category.Prefix.VV_NHCP_DummyCategory_) ?? false)];

            categoriesToResolve.AddRange(modCategories);

            foreach (var cat in modCategories)
            {
                int removedCount = cat.childThingDefs.RemoveAll(thingDefList.Contains);
                if (removedCount > 0)
                {
                    ToLog($"\tRemoved [{removedCount}] child thingdefs from category [{cat.defName}]");
                }
            }

            foreach (var def in thingDefList)
            {
                categoriesToResolve.AddRange(def.thingCategories);
                int removedCount = def.thingCategories.RemoveAll(modCategories.Contains); // Remove all but our dummy category
                if (removedCount > 0)
                {
                    ToLog($"\tRemoved [{removedCount}] categories from thingdef [{def.defName}]");
                }

                if (!def.thingCategories.Contains(targetCategory))
                {
                    def.thingCategories.Add(targetCategory);
                    ToLog($"\tAdded category [{targetCategory.defName}] to thingdef [{def.defName}]");
                }

                if (!targetCategory.childThingDefs.Contains(def))
                {
                    targetCategory.childThingDefs.Add(def);
                    ToLog($"\tAdded def [{def.defName}] to category [{targetCategory.defName}]");
                }
            }

            if (Settings.Logging)
            {
                PrintFinalCategories(thingDefList, initialCategories);
            }

            CacheReferencesForResolving(categoriesToResolve);
        }

        private static void PrintFinalCategories(List<ThingDef> thingDefList, Dictionary<ThingDef, List<string>> initialCategories)
        {
            foreach (var def in thingDefList)
            {
                List<string> finalCategories = def.thingCategories?.Select(c => c.defName)?.ToList() ?? [];
                ToLog($"\t\tFinal categories for {def.defName}: {string.Join(", ", finalCategories)}");

                // Show what changed
                var added = finalCategories.Except(initialCategories[def]).ToList();
                var removed = initialCategories[def].Except(finalCategories).ToList();

                if (added.Any() || removed.Any())
                {
                    ToLog($"\t\tChanges for {def.defName}: --- " + (added.Any() ? $"Added: {string.Join(", ", added)} --- " : "") + (removed.Any() ? $"Removed: {string.Join(", ", removed)}" : ""));
                }
            }
        }

        private static void CacheReferencesForResolving(HashSet<ThingCategoryDef> categoriesToResolve)
        {
            if (categoriesToResolve.NullOrEmpty())
                return;

            _categoriesToResolve.AddRange(categoriesToResolve);

            var categoriesField = typeof(ThingFilter).GetField("categories", BindingFlags.NonPublic | BindingFlags.Instance);
            if (categoriesField != null)
            {
                foreach (var recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
                {
                    var ingredients = recipe?.ingredients;
                    if (ingredients.NullOrEmpty())
                        continue;

                    foreach (var ingredient in ingredients)
                    {
                        var filter = ingredient?.filter;
                        if (filter == null)
                            continue;

                        var ingCategories = (List<string>)categoriesField.GetValue(filter);
                        if (ingCategories.NullOrEmpty())
                            continue;

                        if (categoriesToResolve.Any(cat => ingCategories.Contains(cat.defName)))
                        {
                            _recipesToResolve.Add(recipe);
                            break;
                        }
                    }
                }

                foreach (var thing in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    var building = thing?.building;
                    if (building == null)
                        continue;

                    var fixedFilter = building.fixedStorageSettings?.filter;
                    if (fixedFilter == null)
                        continue;

                    var ingCategories = (List<string>)categoriesField.GetValue(fixedFilter);
                    if (ingCategories.NullOrEmpty())
                        continue;

                    if (categoriesToResolve.Any(cat => ingCategories.Contains(cat.defName)))
                    {
                        _buildingsToResolve.Add(thing);
                        break;
                    }
                }
            }
            else
            {
                ToLog($"ThingFilter field 'categories' was null. How?", 2);
            }
        }

        private static void ResolveAllReferences()
        {
            ToLog("Beginning resolving references...");

            foreach (var cat in _categoriesToResolve)
            {
                cat.ClearCachedData();
                cat.ResolveReferences();
                ToLog($"\tResolved [category]: {cat.defName ?? cat.label ?? "?"}");
            }

            foreach (var recipe in _recipesToResolve)
            {
                recipe.ResolveReferences();
                ToLog($"\tResolved [recipe]: {recipe.defName ?? recipe.label ?? "?"}");
            }

            foreach (var building in _buildingsToResolve)
            {
                building.ResolveReferences();
                ToLog($"\tResolved [building]: {building.defName ?? building.label ?? "?"}");
            }

            ToLog($"Completed resolving [{_categoriesToResolve.Count}] categories, [{_recipesToResolve.Count}] recipes and [{_buildingsToResolve.Count}] buildings", 1);

            _categoriesToResolve = null;
            _recipesToResolve = null;
            _buildingsToResolve = null;
        }
    }
}