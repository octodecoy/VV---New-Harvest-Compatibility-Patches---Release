namespace NewHarvestPatches;

public static partial class Extensions
{
    public static bool Includes(this IntRange range, int val) => val >= range.min && val <= range.max;

    /// <summary>
    /// Clamps both ends into the given bounds and swaps them if they end up crossed. The swap matters
    /// because these ranges come from XML another mod may have authored backwards, and a min above max
    /// would make every comparison against the range fail silently.
    /// </summary>
    public static IntRange Clamp(this IntRange range, int minVal, int maxVal)
    {
        int min = Mathf.Clamp(range.min, minVal, maxVal);
        int max = Mathf.Clamp(range.max, minVal, maxVal);

        if (min > max) (min, max) = (max, min);

        return new(min, max);
    }
}