namespace NewHarvestPatches
{
    public class DefToCategoryInfo : IExposable
    {
        public string ThingDefName = "";
        public string OriginalCategoryName = Category.Type.None_Base;
        public string CurrentCategoryName = Category.Type.None_Base;
        public bool IsCurrentCategoryUserDisabled = false;
        public void ExposeData()
        {
            Scribe_Values.Look(ref ThingDefName, nameof(ThingDefName), "", true);
            Scribe_Values.Look(ref OriginalCategoryName, nameof(OriginalCategoryName), Category.Type.None_Base, true);
            Scribe_Values.Look(ref CurrentCategoryName, nameof(CurrentCategoryName), Category.Type.None_Base, true);
            Scribe_Values.Look(ref IsCurrentCategoryUserDisabled, nameof(IsCurrentCategoryUserDisabled), false, false);
        }

        internal static void CacheDefToCategoryInfo(ref List<DefToCategoryInfo> categoryData)
        {
            categoryData ??= [];

            if (!EnabledSettings.Any(s => s.StartsAndEndsWith("Merge", Setting.Suffix.Category)))
            {
                categoryData.Clear();
                return;
            }

            TidyCacheIfNeeded(categoryData);
            CacheAllFoodDefs(categoryData);
        }

        internal static bool TryGetDefToCategoryInfo(string thingDefName, out DefToCategoryInfo info)
        {
            info = Settings.CategoryData.FirstOrDefault(info => info.ThingDefName == thingDefName);
            if (info != null)
            {
                return true;
            }
            return false;
        }

        internal static void CacheCategoryData(string thingDefName, string originalCategoryDefName, string currentCategoryDefName, bool isUserDisabled = false)
        {
            if (string.IsNullOrWhiteSpace(thingDefName))
                return;

            if (TryGetDefToCategoryInfo(thingDefName, out DefToCategoryInfo info))
            {
                // Update existing entry
                info.OriginalCategoryName = originalCategoryDefName;
                info.CurrentCategoryName = currentCategoryDefName;
                info.IsCurrentCategoryUserDisabled = isUserDisabled;
                ToLog($"\tUpdated cache for ThingDef [{thingDefName}] to OriginalCategory [{originalCategoryDefName}], CurrentCategory [{currentCategoryDefName}], IsUserDisabled [{isUserDisabled}]");
            }
            else
            {
                // Create new entry
                info = new DefToCategoryInfo
                {
                    ThingDefName = thingDefName,
                    OriginalCategoryName = originalCategoryDefName,
                    CurrentCategoryName = currentCategoryDefName,
                    IsCurrentCategoryUserDisabled = isUserDisabled
                };
                Settings.CategoryData.Add(info);
                ToLog($"\tAdded to cache ThingDef [{thingDefName}] with OriginalCategory [{originalCategoryDefName}], CurrentCategory [{currentCategoryDefName}], IsUserDisabled [{isUserDisabled}]");
            }
        }

        internal static void RevertCategoryInCache(string categoryDefName)
        {
            if (string.IsNullOrEmpty(categoryDefName) || Settings.CategoryData.NullOrEmpty())
                return;

            foreach (var categoryInfo in Settings.CategoryData)
            {
                if (categoryInfo.CurrentCategoryName == categoryDefName)
                {
                    categoryInfo.CurrentCategoryName = Category.Type.None_Base;
                }
            }
        }

        private static void TidyCacheIfNeeded(List<DefToCategoryInfo> categoryData)
        {
            if (categoryData.Count == 0)
                return;

            List<DefToCategoryInfo> itemsToRemove = [];
            var uniqueThingDefNames = new HashSet<string>();
            foreach (var info in categoryData)
            {
                if (string.IsNullOrWhiteSpace(info.ThingDefName))
                {
                    itemsToRemove.Add(info);
                    ToLog($"ThingDefName is null or whitespace. Removing.", 2);
                    continue;
                }

                if (uniqueThingDefNames.Contains(info.ThingDefName))
                {
                    itemsToRemove.Add(info);
                    ToLog($"Duplicate ThingDefName [{info.ThingDefName}] found. Removing duplicate.", 2);
                    continue;
                }
                uniqueThingDefNames.Add(info.ThingDefName);

                if (DefDatabase<ThingDef>.GetNamedSilentFail(info.ThingDefName) == null)
                {
                    itemsToRemove.Add(info);
                    ToLog($"ThingDef [{info.ThingDefName}] is null. Removing.", 1);
                    continue;
                }

                if (string.IsNullOrEmpty(info.OriginalCategoryName) ||
                    (info.OriginalCategoryName != Category.Type.None_Base &&
                    DefDatabase<ThingCategoryDef>.GetNamedSilentFail(info.OriginalCategoryName) == null))
                {
                    itemsToRemove.Add(info);
                    ToLog($"ThingDef '{info.ThingDefName}' has invalid original category [{info.OriginalCategoryName}]. Removing.", 1);
                    continue;
                }

                if (string.IsNullOrEmpty(info.CurrentCategoryName) ||
                    (info.CurrentCategoryName != Category.Type.None_Base &&
                    DefDatabase<ThingCategoryDef>.GetNamedSilentFail(info.CurrentCategoryName) == null))
                {
                    itemsToRemove.Add(info);
                    ToLog($"ThingDef '{info.ThingDefName}' has invalid current category [{info.CurrentCategoryName}]. Removing.", 1);
                    continue;
                }

            }

            foreach (var item in itemsToRemove)
            {
                categoryData.Remove(item);
            }

            foreach (var item in categoryData)
            {
                if (!IsMergeSettingEnabledForCategory(item.OriginalCategoryName))
                {
                    item.OriginalCategoryName = Category.Type.None_Base;
                }

                if (item.CurrentCategoryName != Category.Type.None_Base && !IsMergeSettingEnabledForCategory(item.CurrentCategoryName))
                {
                    item.IsCurrentCategoryUserDisabled = false;
                    if (IsMergeSettingEnabledForCategory(item.OriginalCategoryName))
                    {
                        item.CurrentCategoryName = item.OriginalCategoryName;
                    }
                    else
                    {
                        item.CurrentCategoryName = Category.Type.None_Base;
                    }
                }
            }
        }

        private static bool IsMergeSettingEnabledForCategory(string categoryDefName)
        {
            // Check which category this is and return the corresponding merge setting
            return categoryDefName switch
            {
                var name when name.EndsWith("AnimalFoods") => Settings.MergeAnimalFoodsCategory,
                var name when name.EndsWith("Fruit") => Settings.MergeFruitCategory,
                var name when name.EndsWith("Grains") => Settings.MergeGrainsCategory,
                var name when name.EndsWith("Nuts") => Settings.MergeNutsCategory,
                var name when name.EndsWith("Vegetables") => Settings.MergeVegetablesCategory,
                var name when name.EndsWith("Fungus") => Settings.MergeFungusCategory,
                _ => false
            };
        }

        private static void CacheAllFoodDefs(List<DefToCategoryInfo> categoryData)
        {
            HashSet<string> cachedDefNames = [.. Settings.CategoryData.Select(info => info.ThingDefName)];

            var allowedFoodTypeFlags = new[]
            {
                  FoodTypeFlags.VegetableOrFruit,
                  FoodTypeFlags.Seed,
                  FoodTypeFlags.Kibble,
                  FoodTypeFlags.Fungus,
                  FoodTypeFlags.Plant,
                  FoodTypeFlags.VegetarianAnimal
            };

            var modCategoryNames = new[]
            {
                "AnimalFeed",
                "Feed",
                "DavaiAnimalFood",
                "VCE_Fruit",
                "FruitFoodRaw",
                "RC2_FruitsRaw",
                "RC2_GrainsRaw",
                "DankPyon_Cereal",
                "RC2_VegetablesRaw",
            };

            var allDesiredUncachedFoodDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(td =>
                    td != null &&
                    td.defName != null &&
                    !cachedDefNames.Contains(td.defName) &&
                    !td.thingCategories.NullOrEmpty() &&
                    (
                        td.thingCategories.Any(cat => modCategoryNames.Any(name => name == cat.defName)) ||
                        (
                            td.thingCategories.Any(cat =>
                                ThingCategoryDefOf.Foods?.ThisAndChildCategoryDefs?.Contains(cat) == true
                            ) &&
                            td.IsNutritionGivingIngestible &&
                            allowedFoodTypeFlags.Contains(td.ingestible.foodType) &&
                            td.ingestible.preferability != FoodPreferability.NeverForNutrition
                        )
                    )
                )
                .ToList();

            foreach (var def in allDesiredUncachedFoodDefs)
            {
                var newInfo = new DefToCategoryInfo
                {
                    ThingDefName = def.defName,
                    OriginalCategoryName = Category.Type.None_Base,
                    CurrentCategoryName = Category.Type.None_Base
                };
                categoryData.Add(newInfo);
            }
        }
    }
}

