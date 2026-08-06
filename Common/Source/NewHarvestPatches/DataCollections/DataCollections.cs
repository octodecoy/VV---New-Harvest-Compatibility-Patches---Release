#nullable enable

namespace NewHarvestPatches;

/// <summary>
/// Holds data flattened out of Defs at startup, in the MAIN assembly rather than beside the code that uses
/// it, so that the optional Harmony assembly can populate and read the same set without either assembly
/// depending on the other's types.
/// </summary>
public static class DataCollections
{
    public static HashSet<HediffDef> IngestionAffectedHediffDefSet = null!;

    /// <summary>
    /// Flattens a list-of-lists spread across several Defs into one set, so that any number of mods can each
    /// contribute a Def and the result is their union with duplicates dropped.
    /// </summary>
    /// <param name="selector">Pulls the contributed list off one Def; may return null for a Def that
    /// declares none.</param>
    /// <param name="targetSet">Added to, not replaced - an existing set keeps its contents.  Set gets created when null.</param>
    /// <param name="nameSelector">Used only for readable log lines; falls back to ToString.</param>
    /// <param name="cleanup">Optional per-Def callback once its items are taken, for freeing the Def's own
    /// list when the data is no longer needed there.</param>
    /// <param name="setType">Label for the log lines only.</param>
    public static void BuildCollection<TSource, TElement>(
        IEnumerable<TSource> defs,
        Func<TSource, IEnumerable<TElement>?> selector,
        ref HashSet<TElement> targetSet,
        Func<TElement, string>? nameSelector = null,
        Action<TSource>? cleanup = null,
        string setType = "")
    {
        var defList = defs?.ToList() ?? null!;

        if (defList.EnumerableNullOrEmpty())
            return;

        LogMessage(() => $"Building collection for [{setType}] with [{defList.Count}] items.");

        int i = 1;

        string defTypeName = null!;

        foreach (var def in defList)
        {
            var items = selector(def) ?? [];
            if (items.EnumerableNullOrEmpty())
            {
                i++;
                continue;
            }

            defTypeName ??= def?.GetType().Name ?? "Def";

            var itemList = items.ToList();

            if (itemList.Count > 1)
                LogMessage(() => $"Processing [{defTypeName}] #{i} - [{def?.ToString() ?? "null"}].");

            targetSet ??= [];

            foreach (var item in itemList)
            {
                if (item == null)
                    continue;

                if (targetSet.Add(item))
                {
                    int count = targetSet.Count;
                    LogMessage(() => $"Added [{nameSelector?.Invoke(item) ?? item?.ToString()}]. Count now: {count}.");
                }
            }

            cleanup?.Invoke(def);

            i++;
        }
    }
}