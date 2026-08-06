namespace NewHarvestPatches;

/// <summary>
/// Tracks which of our dummy categories a single ThingDef belongs to.
/// "No category" is an empty string - there is no sentinel value. All state
/// transitions go through <see cref="AssignTo"/>, <see cref="Unassign"/> and
/// <see cref="ResetToOriginal"/> so the invariants live in one place.
/// </summary>
public class DefCategoryAssignment : IExposable
{
    public string ThingDefName = "";
    public string OriginalCategory = ""; // What XML patching originally assigned; empty = none.
    private string _currentCategory = "";
    private bool _userRemoved = false;   // Only meaningful while _currentCategory is empty.

    public string CurrentCategory => _currentCategory;
    public bool HasOriginal => !OriginalCategory.NullOrEmpty();
    public bool HasCategory => !_currentCategory.NullOrEmpty();
    public bool IsUserRemoved => !HasCategory && _userRemoved;

    /// <summary>True when this def currently belongs to the given category.</summary>
    public bool IsAssignedTo(string categoryDefName) => HasCategory && _currentCategory == categoryDefName;

    /// <summary>True when the given category's editor may toggle this def (it owns it, or nobody does).</summary>
    public bool CanBeEditedIn(string categoryDefName) => !HasCategory || IsAssignedTo(categoryDefName);

    /// <summary>
    /// Whether this entry carries any information worth persisting - no category, no user removal to
    /// remember, no default to restore. The user-removal term is NOT implied by the others:
    /// <see cref="CategoryAssignments.PruneStaleDefaults"/> and CategoryAssignments.TidyData both clear
    /// OriginalCategory while leaving the flag set, and dropping such an entry would resurrect a def the
    /// user removed by hand.
    /// </summary>
    public bool IsTransient => !HasCategory && !IsUserRemoved && !HasOriginal;

    /// <summary>Puts the def into the given category.</summary>
    public void AssignTo(string categoryDefName)
    {
        _currentCategory = categoryDefName ?? "";
        _userRemoved = false;
    }

    /// <summary>
    /// Removes the def from its category. User removals of a def with an original
    /// category are remembered so XML patching never auto re-assigns it.
    /// </summary>
    public void Unassign(bool byUser)
    {
        _currentCategory = "";
        _userRemoved = byUser && HasOriginal;
    }

    /// <summary>Resets to whatever XML patching originally assigned (possibly nothing).</summary>
    public void ResetToOriginal()
    {
        _currentCategory = OriginalCategory;
        _userRemoved = false;
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref ThingDefName, nameof(ThingDefName), "", false);
        Scribe_Values.Look(ref OriginalCategory, nameof(OriginalCategory), "", false);
        Scribe_Values.Look(ref _currentCategory, "CurrentCategory", "", false);
        Scribe_Values.Look(ref _userRemoved, "UserRemoved", false, false);

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            // Normalize hand-edited or stale data: no nulls, and the removed flag
            // only means something while no category is assigned.
            ThingDefName ??= "";
            OriginalCategory ??= "";
            _currentCategory ??= "";
            if (HasCategory)
                _userRemoved = false;
        }
    }
}

/// <summary>
/// Access point for Settings.CategoryData: O(1) lookups via a lazy index, plus the
/// on-demand session data used by the category editor dialog. The persisted list only
/// keeps meaningful entries - the exhaustive food-def scan is built when the editor
/// opens and pruned again when it closes.
/// </summary>
public static class CategoryAssignments
{
    private static Dictionary<string, DefCategoryAssignment> s_index;
    private static bool s_editorDataBuilt;

    private static List<DefCategoryAssignment> Data => Settings.CategoryData;

    private static Dictionary<string, DefCategoryAssignment> Index
    {
        get
        {
            if (s_index == null)
            {
                s_index = new Dictionary<string, DefCategoryAssignment>(Data.Count);
                foreach (var assignment in Data)
                {
                    if (!assignment.ThingDefName.NullOrEmpty())
                        s_index[assignment.ThingDefName] = assignment;
                }
            }
            return s_index;
        }
    }

    public static bool TryGet(string thingDefName, out DefCategoryAssignment assignment)
    {
        if (thingDefName.NullOrEmpty())
        {
            assignment = null;
            return false;
        }
        return Index.TryGetValue(thingDefName, out assignment);
    }

    /// <summary>Returns the existing entry or creates one assigned to its original category.</summary>
    public static DefCategoryAssignment GetOrAdd(string thingDefName, string originalCategory)
    {
        if (thingDefName.NullOrEmpty())
            return null;

        if (TryGet(thingDefName, out var assignment))
            return assignment;

        assignment = new DefCategoryAssignment
        {
            ThingDefName = thingDefName,
            OriginalCategory = originalCategory ?? "",
        };
        assignment.ResetToOriginal();

        Data.Add(assignment);
        Index[thingDefName] = assignment;
        LogMessage(() => $"\tCached assignment for ThingDef [{thingDefName}] with original category [{originalCategory}]");
        return assignment;
    }

    /// <summary>Drops the lookup index; rebuilt lazily on next access. Call after replacing or clearing the list.</summary>
    public static void Invalidate() => s_index = null;

    /// <summary>
    /// Resets every def belonging to (or originating from) the given category back to
    /// its original assignment.
    /// </summary>
    public static void ResetCategory(string categoryDefName)
    {
        if (categoryDefName.NullOrEmpty())
            return;

        foreach (var assignment in Data)
        {
            if (assignment.IsAssignedTo(categoryDefName) || assignment.OriginalCategory == categoryDefName)
                assignment.ResetToOriginal();
        }
    }

    /// <summary>
    /// Resets every tracked def back to its original assignment (all categories at once).
    /// Returns the resolved membership diffs for <see cref="CategoryApplier.TryApplyLive"/>.
    /// </summary>
    public static List<(ThingDef def, string newCategoryDefName)> ResetAllToOriginal()
    {
        List<(ThingDef def, string newCategoryDefName)> diffs = [];

        foreach (var assignment in Data)
        {
            string previousCategory = assignment.CurrentCategory;
            assignment.ResetToOriginal();

            if (assignment.CurrentCategory == previousCategory)
                continue;

            var def = DefDatabase<ThingDef>.GetNamedSilentFail(assignment.ThingDefName);
            if (def != null)
                diffs.Add((def, assignment.CurrentCategory));
        }

        // Don't prune while the editor's transient scan entries are alive under an open session.
        if (!s_editorDataBuilt)
        {
            Data.RemoveAll(assignment => assignment.IsTransient);
            Invalidate();
        }

        return diffs;
    }

    /// <summary>
    /// Builds the category editor's working data on demand: validates persisted entries, then scans the
    /// DefDatabase for every food def a category could contain. Cheap no-op after the first call;
    /// <see cref="FlushEditorData"/> undoes it.
    /// </summary>
    public static void EnsureEditorData()
    {
        if (s_editorDataBuilt)
            return;
        s_editorDataBuilt = true;

        TidyData();
        CacheFoodDefs();
        Invalidate();
    }

    /// <summary>Removes entries carrying no information and drops session state. Call when the editor closes.</summary>
    public static void FlushEditorData()
    {
        if (!s_editorDataBuilt)
            return;

        s_editorDataBuilt = false;

        Data.RemoveAll(assignment => assignment.IsTransient);
        Invalidate();
    }

    /// <summary>
    /// Clears defaults this build's classifier no longer produces. Complements <see cref="TidyData"/>, which
    /// only catches an OriginalCategory whose ThingCategoryDef is GONE - our own dummy categories always
    /// exist, so an entry filed under one by an older version's wider heuristics (the unpruned subcategory
    /// sweep that classed a mod's jams and corpses as raw produce) survives every other check. The
    /// auto-derived default is always dropped; the assignment goes with it exactly when it still matches
    /// that dead default, so a category the user picked themselves is never disturbed.
    /// <para>
    /// Boot only, and <see cref="CategoryClassifier.ClassifyAll"/> enforces that. The defaults map is built
    /// from the LIVE category tree, and CategoryApplier empties a third-party category's childThingDefs as
    /// it moves defs out of it, so every map built after the first apply pass is missing the defs we took
    /// over - running this against one of those would clear the assignments that pass just made.
    /// </para>
    /// </summary>
    /// <param name="defaults">defName -> dummy category, as built before any def was moved this session.</param>
    /// <returns>True when any entry changed, so the caller can persist the cleanup.</returns>
    internal static bool PruneStaleDefaults(Dictionary<string, string> defaults)
    {
        bool changed = false;

        foreach (var assignment in Data)
        {
            if (!assignment.HasOriginal || defaults.ContainsKey(assignment.ThingDefName))
                continue;

            // Both read before OriginalCategory is cleared - the log message is deferred.
            bool matchesDeadDefault = assignment.IsAssignedTo(assignment.OriginalCategory);
            string defName = assignment.ThingDefName;
            string deadDefault = assignment.OriginalCategory;

            assignment.OriginalCategory = "";
            if (matchesDeadDefault)
                assignment.Unassign(byUser: false);

            LogMessage(() => $"ThingDef [{defName}] no longer defaults to [{deadDefault}]; cleared it{(matchesDeadDefault ? " and its matching assignment" : "")}.", LogMessageType.Warning);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Drops invalid persisted entries. Does NOT touch entries for a category whose merge setting happens
    /// to be off right now - merge-off already produces correct runtime behaviour via
    /// CategoryApplier.ApplyTypeState/ResetTypeToNative, and this used to run on ANY category editor open
    /// (not just the affected category's), silently erasing hand-picked contents of every other merge-off
    /// category in the process.
    /// </summary>
    private static void TidyData()
    {
        HashSet<string> seenDefNames = [];
        Data.RemoveAll(assignment =>
        {
            if (assignment == null || assignment.ThingDefName.NullOrEmpty())
            {
                LogMessage(() => "Removing category assignment with empty ThingDefName.", LogMessageType.Error);
                return true;
            }

            if (!seenDefNames.Add(assignment.ThingDefName))
            {
                LogMessage(() => $"Removing duplicate category assignment for [{assignment.ThingDefName}].", LogMessageType.Error);
                return true;
            }

            if (DefDatabase<ThingDef>.GetNamedSilentFail(assignment.ThingDefName) == null)
            {
                LogMessage(() => $"Removing category assignment for missing ThingDef [{assignment.ThingDefName}].", LogMessageType.Warning);
                return true;
            }

            return false;
        });

        foreach (var assignment in Data)
        {
            // Categories can disappear when their source mod is removed.
            if (assignment.HasOriginal && DefDatabase<ThingCategoryDef>.GetNamedSilentFail(assignment.OriginalCategory) == null)
            {
                LogMessage(() => $"ThingDef [{assignment.ThingDefName}] has invalid original category [{assignment.OriginalCategory}], clearing it.", LogMessageType.Warning);
                assignment.OriginalCategory = "";
            }

            if (assignment.HasCategory && DefDatabase<ThingCategoryDef>.GetNamedSilentFail(assignment.CurrentCategory) == null)
            {
                LogMessage(() => $"ThingDef [{assignment.ThingDefName}] has invalid current category [{assignment.CurrentCategory}], unassigning.", LogMessageType.Warning);
                assignment.Unassign(byUser: false);
            }
        }
    }

    /// <summary>
    /// Adds a transient unassigned entry for every food-like def a category could contain, so the editor
    /// can list them. Pruned again by <see cref="FlushEditorData"/>. "Known mod food" reads
    /// CategoryApplier.ModAddedCategoriesByType - the same live snapshot CategoryApplier itself uses -
    /// rather than a separate hardcoded name list, so a def sitting in an unlisted mod's food category
    /// still reaches the editor even when it fails the isEdibleFood heuristic below.
    /// </summary>
    private static void CacheFoodDefs()
    {
        HashSet<string> cachedDefNames = [.. Data.Select(assignment => assignment.ThingDefName)];

        HashSet<string> knownModCategoryNames = [];
        foreach (var names in CategoryApplier.ModAddedCategoriesByType.Values)
        {
            knownModCategoryNames.UnionWith(names);
        }

        foreach (var def in DefDatabase<ThingDef>.AllDefs)
        {
            if (def?.defName is null || cachedDefNames.Contains(def.defName) || def.thingCategories.NullOrEmpty())
                continue;

            bool isKnownModFood = def.thingCategories.Any(cat => knownModCategoryNames.Contains(cat.defName));
            bool isEdibleFood =
                def.thingCategories.Any(CategoryClassifier.AllFoodCategories.Contains)
                && def.IsNutritionGivingIngestible
                && def.ingestible.foodType.IsAllowedFoodType()
                && def.ingestible.preferability != FoodPreferability.NeverForNutrition;

            if (!isKnownModFood && !isEdibleFood)
                continue;

            Data.Add(new DefCategoryAssignment { ThingDefName = def.defName });
        }
    }
}
