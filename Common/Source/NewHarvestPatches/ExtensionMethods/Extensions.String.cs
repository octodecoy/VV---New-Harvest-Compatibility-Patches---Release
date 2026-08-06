namespace NewHarvestPatches;

public static partial class Extensions
{
    public static bool ContainsIgnoreCase(this string source, string target) 
        => source.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;

    public static bool StartsAndEndsWith(
        this string str, 
        string start, 
        string end, 
        bool ignoreStartCase = false,
        bool ignoreEndCase = false)
    {
        return str.StartsWith(start, comparisonType: ignoreStartCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) 
            && str.EndsWith(end, comparisonType: ignoreEndCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}