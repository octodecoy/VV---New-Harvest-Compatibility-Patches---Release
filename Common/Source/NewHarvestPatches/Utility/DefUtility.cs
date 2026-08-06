namespace NewHarvestPatches;

public static partial class DefUtility
{
    /// <summary>
    /// Resolves defNames to defs, silently dropping any that do not exist - so a caller can name defs from
    /// modules that may not be installed without guarding each one. The returned list is therefore often
    /// shorter than <paramref name="defNames"/>, and a missing def is not an error.
    /// </summary>
    /// <param name="order">Sort by label using the current culture, for anything user-facing.</param>
    public static List<T> GetDefsOfTypeByDefNames<T>(bool order = true, params string[] defNames) where T : Def
    {
        if (defNames.NullOrEmpty())
            return [];

        var defs = new List<T>();
        foreach (var defName in defNames)
        {
            var def = DefDatabase<T>.GetNamedSilentFail(defName);
            if (def != null)
                defs.Add(def);
        }
        return order ? [.. defs.OrderBy(td => td.label, CurrentCultureComparer).ToList()] : defs;
    }
}