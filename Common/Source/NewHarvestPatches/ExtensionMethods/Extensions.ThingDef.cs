namespace NewHarvestPatches;

public static partial class Extensions
{
    private const string Wood = "Wood";
    private const string Lumber = "Lumber";
    private const string Tree = "Tree";

    /// <summary>
    /// Identifies one of our trees by defName convention (parent prefix + "Tree" suffix) rather than by a
    /// def flag, because nothing on ThingDef marks a plant as ours. Consequently a third-party def that
    /// happens to follow the same naming would be picked up, and one of ours renamed would not.
    /// </summary>
    public static bool IsNewHarvestTree(this ThingDef def)
    {
        return def != null 
            && def.plant != null 
            && def.IsPlant
            && def.defName.StartsAndEndsWith(start: ModName.Prefix.Parent, end: Tree);
    }

    /// <summary>
    /// Naming test for "this material is a wood". Deliberately separate from
    /// <see cref="HasWoodStuffCategory"/> - the two do not imply each other, and callers pick the one that
    /// matches what they mean. Used for default-on choices (fuel defaults, wood-first sort order) where a
    /// wrong guess only costs the user a toggle.
    /// </summary>
    public static bool HasWoodDefName(this ThingDef def)
    {
        return def != null
            && (def.defName.EndsWith(Wood) || def.defName.EndsWith(Lumber));
    }

    /// <summary>
    /// Stuff-category test for "this material burns like wood", matched by substring so modded wood
    /// categories count too. A material can pass this and fail <see cref="HasWoodDefName"/>, or the reverse.
    /// </summary>
    // If we wanted to be really sure we could also compare against items in the category.
    public static bool HasWoodStuffCategory(this ThingDef def)
    {
        return def?.stuffProps?.categories?.Any(cat => cat.defName.ContainsIgnoreCase(Wood)) is true;
    }

    /// <summary>
    /// Whether a def is food meant for ANIMALS rather than food an animal merely can eat. There is no such
    /// flag in vanilla, so this approximates it: the def must give animals a feeding bonus, be a food type
    /// animals accept, be unappealing enough that colonists will not choose it (kibble-tier preferability),
    /// and sit in a food category. All four must hold, which keeps ordinary meals and raw produce out.
    /// </summary>
    /// <remarks>
    /// Heuristic over other mods' defs, so it can misjudge an unusual one. Only used to seed a default
    /// category assignment the user can override, so a wrong answer is a cosmetic default, not a break.
    /// </remarks>
    public static bool IsAnimalFood(this ThingDef def)
    {
        if (def?.ingestible is not { } ingestible)
            return false;

        if (ingestible.optimalityOffsetFeedingAnimals <= 0)
            return false;

        if ((ingestible.foodType & (FoodTypeFlags.Kibble | FoodTypeFlags.Plant | FoodTypeFlags.Meat)) == 0)
            return false;

        if (ingestible.preferability is not (FoodPreferability.DesperateOnlyForHumanlikes or FoodPreferability.RawBad))
            return false;

        return def.thingCategories?.Any(CategoryClassifier.AllFoodCategories.Contains) == true;
    }

    /// <summary>
    /// The food types that count as "grown, not crafted". Fungus is in the list because vanilla files raw
    /// mushrooms under PlantFoodRaw alongside plant produce, so any filter that accepts one accepts the other.
    /// </summary>
    private static readonly FoodTypeFlags s_plantSourcedFoodTypes =
        FoodTypeFlags.VegetableOrFruit |
        FoodTypeFlags.Seed |
        FoodTypeFlags.Plant |
        FoodTypeFlags.Fungus;

    /// <summary>
    /// Whether a def is plant matter in its own right rather than something cooked out of plant matter.
    /// The Kibble flag is an explicit veto instead of a mere absence: a def is disqualified by being kibble
    /// even if it also carries a plant flag, which is how vanilla Kibble and our own processed feeds
    /// (VV_BeetPulp, VV_FodderMix, VV_ProteinKibble) stay out while the raw feeds (VV_AlfalfaHay,
    /// VV_FeedCorn, ...) stay in.
    /// </summary>
    public static bool IsPlantSourcedFood(this ThingDef def)
    {
        if (def?.ingestible is not { } ingestible)
            return false;

        if ((ingestible.foodType & FoodTypeFlags.Kibble) != 0)
            return false;

        return (ingestible.foodType & s_plantSourcedFoodTypes) != 0;
    }
}