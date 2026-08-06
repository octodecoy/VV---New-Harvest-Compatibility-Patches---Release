namespace NewHarvestPatches;

public static class SharedFields
{
    /// <summary>
    /// Case-SENSITIVE comparer using the player's culture, for sorting translated labels in the settings UI
    /// so accented characters land where that language expects rather than after Z.
    /// </summary>
    public static readonly StringComparer CurrentCultureComparer = StringComparer.Create(CultureInfo.CurrentCulture, false);
}