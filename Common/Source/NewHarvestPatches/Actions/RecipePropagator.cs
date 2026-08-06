namespace NewHarvestPatches;

/// <summary>
/// Adds recipes to every building that already has a given target recipe, using an existing vanilla
/// recipe as a stand-in for "the kind of workbench this belongs on". This avoids having to name every
/// workbench in XML, so recipes land on mod-added benches this mod has never heard of - e.g. maple
/// syrup follows CookMealSimple onto any stove, vanilla or modded.
/// Boot-only: mutates shared ThingDefs and cannot be reverted in place.
/// </summary>
internal static class RecipePropagator
{
    private static readonly FieldInfo s_allRecipesCached = typeof(ThingDef).GetField("allRecipesCached", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// Adds <paramref name="recipesToAdd"/> to every building that can already run <paramref name="targetRecipe"/>.
    /// </summary>
    /// <param name="targetRecipe">
    /// The stand-in recipe used to locate target buildings. Both directions of the vanilla
    /// recipe/building link are followed: buildings listing it in <c>ThingDef.recipes</c>, and
    /// buildings the recipe itself names in <c>RecipeDef.recipeUsers</c>.
    /// </param>
    /// <param name="recipesToAdd">Recipes to inject. Nulls and duplicates are dropped, so callers may pass <c>GetNamedSilentFail</c> results directly.</param>
    /// <param name="blacklistedBuildingDefNames">Optional defNames to exclude from the matched building set.</param>
    public static void TryAddRecipes(RecipeDef targetRecipe, List<RecipeDef> recipesToAdd, List<string> blacklistedBuildingDefNames = null)
    {
        ActionRunner.Run(nameof(RecipePropagator), nameof(TryAddRecipes), () => AddRecipes(targetRecipe, recipesToAdd, blacklistedBuildingDefNames));
    }

    /// <summary>
    /// Body of <see cref="TryAddRecipes"/>. Writes to <c>ThingDef.recipes</c> rather than the recipe's
    /// <c>recipeUsers</c> so nothing another mod owns is mutated, then clears the building's private
    /// <c>allRecipesCached</c> list - vanilla builds that cache lazily from both sides of the link and
    /// never rechecks it, so an addition made after it was populated would otherwise stay invisible.
    /// </summary>
    private static void AddRecipes(RecipeDef targetRecipe, List<RecipeDef> recipesToAdd, List<string> blacklistedBuildingDefNames)
    {
        if (targetRecipe == null)
        {
            LogMessage(() => "targetRecipe is null.", LogMessageType.Error);
            return;
        }

        if (recipesToAdd.NullOrEmpty())
        {
            LogMessage(() => $"recipesToAdd for recipe [{targetRecipe.defName}] is null or empty.", LogMessageType.Error);
            return;
        }

        recipesToAdd = [.. recipesToAdd.Where(def => def != null).Distinct()];

        if (recipesToAdd.Count == 0)
        {
            LogMessage(() => $"recipesToAdd for target recipe [{targetRecipe.defName}] contains only nulls.", LogMessageType.Error);
            return;
        }

        HashSet<ThingDef> buildingsWithRecipe = [.. DefDatabase<ThingDef>.AllDefs.Where(def => def.building != null && def.recipes?.Contains(targetRecipe) == true)];

        if (targetRecipe.recipeUsers != null)
        {

            buildingsWithRecipe.AddRange(targetRecipe.recipeUsers.Where(user => user?.building != null));
        }

        if (blacklistedBuildingDefNames.HasData())
        {
            buildingsWithRecipe.RemoveWhere(b => blacklistedBuildingDefNames.Contains(b.defName));
        }

        if (buildingsWithRecipe.NullOrEmpty())
        {
            LogMessage(() => $"buildingsWithRecipe for recipe [{targetRecipe.defName}] is null or empty.", LogMessageType.Warning);
            return;
        }

        foreach (var building in buildingsWithRecipe)
        {
            bool added = false;
            foreach (var recipe in recipesToAdd)
            {
                if (building.recipes?.Contains(recipe) == true || recipe.recipeUsers?.Contains(building) == true)
                    continue;

                building.recipes ??= [];
                building.recipes.Add(recipe);
                added = true;
                LogMessage(() => $"Added recipe [{recipe.defName}] to building [{building.defName}].");
            }

            // Only reset the cached recipes if we actually added any new recipes.
            if (added)
                s_allRecipesCached?.SetValue(building, null);
        }
    }
}