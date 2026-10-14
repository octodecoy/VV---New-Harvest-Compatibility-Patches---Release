namespace NewHarvestPatches;

public static partial class Extensions
{
    /// <summary>
    /// The food types our category system is willing to handle - roughly "grown or processed plant matter".
    /// Meat, corpses and processed meals are excluded on purpose: this mod's categories exist to organise
    /// harvest produce, not every edible in the game.
    /// </summary>
    private static readonly FoodTypeFlags s_allowedFoodTypes =
        FoodTypeFlags.VegetableOrFruit |
        FoodTypeFlags.Seed |
        FoodTypeFlags.Plant |
        FoodTypeFlags.Fungus |
        FoodTypeFlags.Kibble;
        
    /// <summary>
    /// True if ANY allowed bit is set. FoodTypeFlags is a bitfield, so this is not the negation of
    /// <see cref="HasAnyDisallowedFoodType"/> - a def flagged both Plant and Meat returns true from both.
    /// </summary>
    public static bool IsAllowedFoodType(this FoodTypeFlags foodType)
        => (foodType & s_allowedFoodTypes) != 0;
}