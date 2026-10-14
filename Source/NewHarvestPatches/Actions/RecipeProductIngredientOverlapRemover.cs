namespace NewHarvestPatches;

/// <summary>
/// Stops a recipe from consuming its own output. When a recipe's product also matches its ingredient
/// filter - which happens as soon as another mod files that product under a category the recipe accepts
/// - pawns will happily feed finished goods back into the bill, looping the item through the bench.  
/// This disallows each product on every one of the recipe's filters.
///
/// A removal is only applied once it is proven that no ingredient which can currently be satisfied would
/// become unsatisfiable, so a recipe deliberately built out of its own product is left alone. The whole
/// recipe is evaluated before anything is mutated, so there is never a partial edit to undo.
///
/// Runs at boot over every recipe, and again after any live category apply - <c>ThingFilter.SetAllow</c>
/// writes only <c>allowedDefs</c>, so a pass that recomputes allow state from the raw XML lists (as
/// <see cref="CategoryApplier"/> does) cannot see the boot removal and will undo it. Live callers pass a
/// product restriction to skip the recipes they could not have affected. Mutates shared ThingFilters on
/// RecipeDefs and keeps no baseline, so a removal is never reverted - only re-applied.
/// </summary>
internal static class RecipeProductIngredientOverlapRemover
{
    /// <summary>
    /// Product defs of the recipe currently being examined, plus the virtual defs
    /// <see cref="ThingFilter.SetAllow(ThingDef, bool)"/> would cascade into. Reused across recipes;
    /// safe only because boot actions run single-threaded and never re-enter.
    /// </summary>
    private static readonly HashSet<ThingDef> s_productClosure = [];

    /// <param name="recipes">Recipes to clean. Nulls and duplicates are dropped, so <c>GetNamedSilentFail</c> results can be passed straight in.</param>
    public static void TryRemoveOverlapFrom(List<RecipeDef> recipes)
    {
        ActionRunner.Run(nameof(RecipeProductIngredientOverlapRemover), nameof(TryRemoveOverlapFrom), () => RemoveOverlapFrom(recipes));
    }

    /// <param name="productRestriction">
    /// When non-null, only recipes producing one of these defs are examined. Live callers pass the set of
    /// defs they just moved between categories, which is exactly the set whose allow state they can have
    /// changed. Null sweeps every recipe, which is what boot needs.
    /// </param>
    public static void TryRemoveAllOverlap(HashSet<ThingDef> productRestriction = null)
    {
        ActionRunner.Run(nameof(RecipeProductIngredientOverlapRemover), nameof(TryRemoveAllOverlap), () => RemoveAllOverlap(productRestriction));
    }

    /// <summary>
    /// Body of <see cref="TryRemoveOverlapFrom"/>.
    /// </summary>
    private static void RemoveOverlapFrom(List<RecipeDef> recipes)
    {
        if (recipes.NullOrEmpty())
        {
            LogMessage(() => "recipes list is null or empty.", LogMessageType.Error);
            return;
        }

        foreach (RecipeDef recipe in recipes.Where(def => def != null).Distinct())
        {
            TryCleanRecipe(recipe, productRestriction: null);
        }
    }

    /// <summary>
    /// Body of <see cref="TryRemoveAllOverlap"/>. Every recipe in the load order is a candidate, so the
    /// per-recipe work is kept to a handful of hash lookups until an actual overlap is found.
    /// </summary>
    public static void RemoveAllOverlap(HashSet<ThingDef> productRestriction = null)
    {
        List<RecipeDef> allRecipes = DefDatabase<RecipeDef>.AllDefsListForReading;

        for (int i = 0; i < allRecipes.Count; i++)
        {
            TryCleanRecipe(allRecipes[i], productRestriction);
        }
    }

    /// <summary>
    /// Disallows every product of <paramref name="recipe"/> on its ingredient filters and its
    /// <c>fixedIngredientFilter</c>, unless doing so would leave an ingredient with nothing craftable.
    /// <c>defaultIngredientFilter</c> is left alone - it only seeds the filter of a newly placed bill and
    /// cannot make a recipe unusable.
    /// </summary>
    private static void TryCleanRecipe(RecipeDef recipe, HashSet<ThingDef> productRestriction)
    {
        if (recipe?.products.NullOrEmpty() != false || recipe.ingredients.NullOrEmpty())
            return;

        BuildProductClosure(recipe.products);
        if (s_productClosure.Count == 0)
            return;

        if (!ProducesRestrictedDef(productRestriction))
            return;

        List<IngredientCount> ingredients = recipe.ingredients;
        ThingFilter fixedFilter = recipe.fixedIngredientFilter;

        if (!AnyFilterAllowsProduct(ingredients, fixedFilter))
            return;

        if (!IsRemovalSafe(recipe, ingredients, fixedFilter))
            return;

        ApplyRemoval(recipe, ingredients, fixedFilter);
    }

    /// <summary>
    /// Fills <see cref="s_productClosure"/> with the recipe's product defs and everything
    /// <c>SetAllow</c> would drag along with them, so counting and simulation match what a real removal does.
    /// </summary>
    private static void BuildProductClosure(List<ThingDefCountClass> products)
    {
        s_productClosure.Clear();

        for (int i = 0; i < products.Count; i++)
        {
            ThingDef productDef = products[i]?.thingDef;
            if (productDef != null)
                AddWithVirtualDefs(productDef);
        }
    }

    private static void AddWithVirtualDefs(ThingDef def)
    {
        if (!s_productClosure.Add(def))
            return;

        List<ThingDef> virtualDefs = def.virtualDefs;
        if (virtualDefs == null)
            return;

        for (int i = 0; i < virtualDefs.Count; i++)
        {
            AddWithVirtualDefs(virtualDefs[i]);
        }
    }

    /// <summary>
    /// Whether the recipe under examination is one the caller asked for. Tested against the closure rather
    /// than the raw product list so a def reachable only as a product's virtual def still counts, and the
    /// closure is walked (not the restriction) because it holds a couple of entries at most.
    /// </summary>
    private static bool ProducesRestrictedDef(HashSet<ThingDef> productRestriction)
    {
        if (productRestriction == null)
            return true;

        foreach (ThingDef productDef in s_productClosure)
        {
            if (productRestriction.Contains(productDef))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Cheap rejection for the overwhelming majority of recipes: nothing to do unless some filter that
    /// actually selects ingredients allows one of the products.
    /// </summary>
    private static bool AnyFilterAllowsProduct(List<IngredientCount> ingredients, ThingFilter fixedFilter)
    {
        foreach (ThingDef productDef in s_productClosure)
        {
            if (fixedFilter != null && fixedFilter.Allows(productDef))
                return true;

            for (int i = 0; i < ingredients.Count; i++)
            {
                if (ingredients[i]?.filter?.Allows(productDef) == true)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Simulates the removal against every ingredient and reports whether all of them survive it.
    /// Nothing is mutated here, so a rejected recipe is left exactly as it was found.
    /// </summary>
    private static bool IsRemovalSafe(RecipeDef recipe, List<IngredientCount> ingredients, ThingFilter fixedFilter)
    {
        bool fixedFilterChanges = fixedFilter != null && CountAfterRemoval(fixedFilter) != fixedFilter.AllowedDefCount;

        for (int i = 0; i < ingredients.Count; i++)
        {
            ThingFilter filter = ingredients[i]?.filter;
            if (filter == null)
                continue;

            int countBefore = filter.AllowedDefCount;
            int countAfter = CountAfterRemoval(filter);

            // Untouched ingredient whose fixed-filter gate is also untouched cannot change state.
            if (countBefore == countAfter && !fixedFilterChanges)
                continue;

            if (!SurvivesRemoval(filter, fixedFilter, countBefore, countAfter))
            {
                int index = i;
                LogMessage(
                    () => $"Skipped recipe [{recipe.defName}] - disallowing its products would leave ingredient #{index} with nothing craftable.",
                    LogMessageType.Warning);

                return false;
            }

            if (countBefore > 1 && countAfter == 1)
            {
                int index = i;
                LogMessage(
                    () => $"Ingredient #{index} of recipe [{recipe.defName}] drops to a single allowed def and becomes a fixed ingredient, which bypasses fixedIngredientFilter and the bill's own filter.",
                    LogMessageType.Warning);
            }
        }

        return true;
    }

    /// <summary>
    /// Whether an ingredient that has at least one usable def today still has one after the removal.
    /// An ingredient that is already unusable is not our doing and never blocks the removal.
    /// </summary>
    private static bool SurvivesRemoval(ThingFilter filter, ThingFilter fixedFilter, int countBefore, int countAfter)
    {
        bool satisfiableBefore = false;
        bool satisfiableAfter = false;

        foreach (ThingDef def in filter.AllowedThingDefs)
        {
            bool survives = !s_productClosure.Contains(def);

            if (!satisfiableBefore && IsSatisfiable(fixedFilter, def, countBefore, afterRemoval: false))
                satisfiableBefore = true;

            if (survives && !satisfiableAfter && IsSatisfiable(fixedFilter, def, countAfter, afterRemoval: true))
                satisfiableAfter = true;

            if (satisfiableBefore && satisfiableAfter)
                break;
        }

        return satisfiableAfter || !satisfiableBefore;
    }

    /// <summary>
    /// Mirrors the runtime gate in <see cref="Bill.IsFixedOrAllowedIngredient(ThingDef)"/>: a def works if
    /// its ingredient is fixed (exactly one allowed def, which skips the fixed filter entirely) or the
    /// recipe's fixed filter lets it through. A null fixed filter would throw there, so only fixed
    /// ingredients count as usable on such a recipe.
    /// </summary>
    private static bool IsSatisfiable(ThingFilter fixedFilter, ThingDef def, int allowedDefCount, bool afterRemoval)
    {
        if (allowedDefCount == 1)
            return true;

        if (fixedFilter == null)
            return false;

        if (afterRemoval && s_productClosure.Contains(def))
            return false;

        return fixedFilter.Allows(def);
    }

    /// <summary>
    /// Allowed-def count <paramref name="filter"/> would report once every product def is disallowed.
    /// </summary>
    private static int CountAfterRemoval(ThingFilter filter)
    {
        if (filter == null)
            return 0;

        int count = filter.AllowedDefCount;

        foreach (ThingDef productDef in s_productClosure)
        {
            if (filter.Allows(productDef))
                count--;
        }

        return count;
    }

    private static void ApplyRemoval(RecipeDef recipe, List<IngredientCount> ingredients, ThingFilter fixedFilter)
    {
        foreach (ThingDef productDef in s_productClosure)
        {
            for (int i = 0; i < ingredients.Count; i++)
            {
                RemoveFromFilter(ingredients[i]?.filter, productDef, recipe, "ingredients filter");
            }

            RemoveFromFilter(fixedFilter, productDef, recipe, "fixedIngredientFilter");
        }
    }

    private static void RemoveFromFilter(ThingFilter filter, ThingDef productDef, RecipeDef recipe, string filterName)
    {
        if (filter?.Allows(productDef) != true)
            return;

        filter.SetAllow(productDef, false);

        LogMessage(() => $"Removed product [{productDef.defName}] from {filterName} on recipe [{recipe.defName}].", severity: LogMessageType.Warning);
    }
}
