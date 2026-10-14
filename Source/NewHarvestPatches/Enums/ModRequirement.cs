namespace NewHarvestPatches;

/// <summary>
/// Mod/module requirements usable on settings fields via <see cref="RequiresModAttribute"/>.
/// Each member maps to a Utils.ModChecker flag in Utils.ModChecker.IsSatisfied.
/// The No* members are EXCLUSIONS - satisfied only while that mod is ABSENT, for settings whose
/// job another mod already does better. They are always mandatory, even under AnyOf
/// (see Utils.ModChecker.IsSatisfied(RequiresModAttribute)).
/// </summary>
public enum ModRequirement
{
    // New Harvest modules
    Forage,
    Garden,
    Industrial,
    Medicinal,
    Trees,
    Mushrooms,
    Fruit,
    Flowers,
    // New Harvest composites
    AnyTrees,
    AnyFruit,
    AnyVegetables,
    // DLC
    Ideology,
    Odyssey,
    // Third-party mods
    Harmony,
    VanillaExpandedFramework,
    VanillaCookingExpanded,
    VanillaBrewingExpanded,
    MedievalOverhaul,
    // Exclusions - "no other mod already does this". Add the matching case to
    // Utils.ModChecker.IsExclusion when adding a member here, or it is treated as a positive requirement.
    NoMedievalOverhaul,
    NoFernyFloorMenu,
    NoWoodworkingMod,
    NoFuelFilterMod
}
