using System.Collections;
using System.Runtime.CompilerServices;

namespace NewHarvestPatches;

/// <summary>
/// Applies the category plan (Settings.CategoryData + the Merge/ResourceReadout
/// settings) to loaded defs. One code path serves game boot (static constructor) and
/// live changes (settings window / category editor), so nothing ever needs a restart.
/// Replaces the per-def and setting-conditional XML patch operations and the old
/// load-time CategorySyncer.
/// </summary>
internal static class CategoryApplier
{
    /// <summary>
    /// One row of the engine's static table: everything needed to apply, undo and re-target a single
    /// category type. The two settings are Funcs rather than values so the table can be built once and
    /// still read the user's current choice on every pass.
    /// </summary>
    private class CategoryTypeInfo
    {
        public string Type;
        public string DummyDefName;
        public string MergeParentDefName;
        public Func<bool> Merge;
        public Func<bool> Readout;

        private ThingCategoryDef _dummyInt;

        /// <summary>
        /// The dummy category this type owns. Read many times per apply pass (and once per type by
        /// EnsureClassified), so the resolved def is held rather than looked up each time - it cannot
        /// change once defs have loaded. Only a successful lookup is cached: a null means the def is
        /// missing (e.g. a broken install), and re-missing a dictionary is cheaper than reasoning about
        /// whether the table was ever built before the DefDatabase was ready. All six dummies live under
        /// the same LoadFolders gate (any NH module), so in practice they exist together or none do.
        /// </summary>
        public ThingCategoryDef Dummy => _dummyInt ??= DefDatabase<ThingCategoryDef>.GetNamedSilentFail(DummyDefName);
    }

    /// <summary>Everything one apply run touched, so caches get refreshed exactly once at the end.</summary>
    private class ApplyContext
    {
        public readonly HashSet<ThingCategoryDef> AffectedCategories = [];
        public readonly HashSet<ThingDef> MovedDefs = [];

        // Resolved pre-move categories per moved def, for the filter pass. Per-pass rather than static:
        // a language switch reloads every DefDatabase in place, so cached ThingCategoryDef references
        // would outlive the defs they point at.
        public readonly Dictionary<ThingDef, List<ThingCategoryDef>> NativeCategoryCache = [];

        public bool TreeChanged;
        public bool ReadoutChanged;

        public bool IsEmpty => AffectedCategories.Count == 0 && MovedDefs.Count == 0 && !TreeChanged && !ReadoutChanged;
    }

    /// <summary>
    /// Undo record for one trader stock generator this engine cloned. Only the clone has to be undone - the
    /// original generator is left exactly as found, so reverting is a plain remove.
    /// </summary>
    private class TraderCloneRecord
    {
        public TraderKindDef Trader;
        public StockGenerator Clone;
    }

    // TestCategory results copied out of the XML-phase cache so live code can use them after ClearAllCaches.
    internal static Dictionary<string, HashSet<string>> ModAddedCategoriesByType { get; private set; } = [];

    // defName -> the def's own (non-dummy) categories, captured the first time the engine mutates it.
    // Session-only: a fresh boot reloads native categories from XML anyway.
    private static readonly Dictionary<string, List<string>> s_nativeCategories = [];

    // Which defs this engine has claimed the allow state of, per filter. Ownership is what lets
    // CorrectFilterForMovedDefs leave a third party's SetAllow alone while still restoring the native
    // verdict on a merge toggled back off - see that method. Filled there and by AllowAll, which claims
    // every allowance the animal food ingredient pass writes. A ConditionalWeakTable rather than a
    // Dictionary: it keys on reference identity by design, and its weak keys stop per-save filters
    // (bills, stockpiles, policies) from being held alive after the save is unloaded.
    private static readonly ConditionalWeakTable<ThingFilter, HashSet<ThingDef>> s_ownFilterWrites = new();

    // Last state applied per category type, so live toggles know what to undo.
    private static readonly Dictionary<string, (bool merge, bool readout)> s_appliedState = [];

    private static readonly Dictionary<string, List<TraderCloneRecord>> s_traderClones = [];

    private static List<CategoryTypeInfo> s_types;

    // ThingFilter raw XML lists (same reflection precedent as the old CategorySyncer).
    private static readonly FieldInfo s_filterCategoriesField = typeof(ThingFilter).GetField("categories", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo s_filterDisallowedCategoriesField = typeof(ThingFilter).GetField("disallowedCategories", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo s_filterThingDefsField = typeof(ThingFilter).GetField("thingDefs", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo s_filterDisallowedThingDefsField = typeof(ThingFilter).GetField("disallowedThingDefs", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo s_resourceReadoutField = typeof(MapInterface).GetField("resourceReadout", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo s_stockCategoryDefField = typeof(StockGenerator_Category).GetField("categoryDef", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo s_stockCountRangeField = typeof(StockGenerator_Category).GetField("thingDefCountRange", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo s_stockExcludedThingDefsField = typeof(StockGenerator_Category).GetField("excludedThingDefs", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo s_stockExcludedCategoriesField = typeof(StockGenerator_Category).GetField("excludedCategories", BindingFlags.NonPublic | BindingFlags.Instance);

    // ThingFilter fields ComputeShouldAllow does NOT model (see HasUnmodelledFilterRules). Vanilla
    // ResolveReferences applies all of these; a filter using any of them alongside <categories> would get
    // a wrong allow verdict from our hand-rolled precedence, so such filters are skipped entirely instead.
    private static readonly FieldInfo[] s_exoticListFields =
    [
        typeof(ThingFilter).GetField("tradeTagsToAllow", BindingFlags.NonPublic | BindingFlags.Instance),
        typeof(ThingFilter).GetField("tradeTagsToDisallow", BindingFlags.NonPublic | BindingFlags.Instance),
        typeof(ThingFilter).GetField("thingSetMakerTagsToAllow", BindingFlags.NonPublic | BindingFlags.Instance),
        typeof(ThingFilter).GetField("thingSetMakerTagsToDisallow", BindingFlags.NonPublic | BindingFlags.Instance),

        // These two are not needed it seems - example: keeps our dummy category out of VGP's BrewSimpleSyrupVG.
        // typeof(ThingFilter).GetField("specialFiltersToAllow", BindingFlags.NonPublic | BindingFlags.Instance),
        // typeof(ThingFilter).GetField("specialFiltersToDisallow", BindingFlags.NonPublic | BindingFlags.Instance),
        typeof(ThingFilter).GetField("stuffCategoriesToAllow", BindingFlags.NonPublic | BindingFlags.Instance),
        typeof(ThingFilter).GetField("allowAllWhoCanMake", BindingFlags.NonPublic | BindingFlags.Instance),
    ];

    // Same idea for the scalar rules, paired with the value each one holds when the XML never set it -
    // ThingFilter declares them all without an initializer except disallowCheaperThan (= float.MinValue).
    private static readonly (FieldInfo Field, object Unset)[] s_exoticScalarFields =
    [
        (typeof(ThingFilter).GetField("disallowWorsePreferability", BindingFlags.NonPublic | BindingFlags.Instance), FoodPreferability.Undefined),
        (typeof(ThingFilter).GetField("disallowInedibleByHuman", BindingFlags.NonPublic | BindingFlags.Instance), false),
        (typeof(ThingFilter).GetField("disallowMedicalDrugs", BindingFlags.NonPublic | BindingFlags.Instance), false),
        (typeof(ThingFilter).GetField("disallowDoesntProduceMeat", BindingFlags.NonPublic | BindingFlags.Instance), false),
        (typeof(ThingFilter).GetField("disallowNotEverStorable", BindingFlags.NonPublic | BindingFlags.Instance), false),
        (typeof(ThingFilter).GetField("allowWithComp", BindingFlags.NonPublic | BindingFlags.Instance), null),
        (typeof(ThingFilter).GetField("disallowWithComp", BindingFlags.NonPublic | BindingFlags.Instance), null),
        (typeof(ThingFilter).GetField("disallowCheaperThan", BindingFlags.NonPublic | BindingFlags.Instance), float.MinValue),
    ];

    private static List<CategoryTypeInfo> Types => s_types ??=
    [
        new() { Type = Category.Kind.AnimalFoods, DummyDefName = "VV_NHCP_DummyCategory_AnimalFoods", MergeParentDefName = "Foods",
                Merge = () => Settings.MergeAnimalFoodsCategory, Readout = () => Settings.AnimalFoodsCategoryResourceReadout },
        new() { Type = Category.Kind.Fruit, DummyDefName = "VV_NHCP_DummyCategory_Fruit", MergeParentDefName = "PlantFoodRaw",
                Merge = () => Settings.MergeFruitCategory, Readout = () => Settings.FruitCategoryResourceReadout },
        new() { Type = Category.Kind.Grains, DummyDefName = "VV_NHCP_DummyCategory_Grains", MergeParentDefName = "PlantFoodRaw",
                Merge = () => Settings.MergeGrainsCategory, Readout = () => Settings.GrainsCategoryResourceReadout },
        new() { Type = Category.Kind.Nuts, DummyDefName = "VV_NHCP_DummyCategory_Nuts", MergeParentDefName = "PlantFoodRaw",
                Merge = () => Settings.MergeNutsCategory, Readout = () => Settings.NutsCategoryResourceReadout },
        new() { Type = Category.Kind.Vegetables, DummyDefName = "VV_NHCP_DummyCategory_Vegetables", MergeParentDefName = "PlantFoodRaw",
                Merge = () => Settings.MergeVegetablesCategory, Readout = () => Settings.VegetablesCategoryResourceReadout },
        new() { Type = Category.Kind.Fungus, DummyDefName = "VV_NHCP_DummyCategory_Fungus", MergeParentDefName = "PlantFoodRaw",
                Merge = () => Settings.MergeFungusCategory, Readout = () => Settings.FungusCategoryResourceReadout },
    ];

    /// <summary>Must run while the XML-phase cache is still alive (before ClearAllCaches).</summary>
    internal static void SnapshotModAddedCategories()
    {
        // Source already cleared (post-boot call): keep the snapshot we have.
        if (ModAddedCategoryDictionary.NullOrEmpty())
            return;

        ModAddedCategoriesByType = [];
        foreach (var kvp in ModAddedCategoryDictionary)
        {
            ModAddedCategoriesByType[kvp.Key] = [.. kvp.Value];
        }
    }

    public static void TryApplyAtBoot()
    {
        ActionRunner.Run(nameof(CategoryApplier), nameof(TryApplyAtBoot), ApplyAtBoot);
    }

    public static void TryApplyToggles()
    {
        ActionRunner.Run(nameof(CategoryApplier), nameof(TryApplyToggles), ApplyToggles);
    }

    public static void TryApplyLive(List<(ThingDef def, string newCategoryDefName)> diffs)
    {
        ActionRunner.Run(nameof(CategoryApplier), nameof(TryApplyLive), () => ApplyLive(diffs));
    }

    private static void ApplyAtBoot()
    {
        SnapshotModAddedCategories();

        // The only classifier pass whose defaults map is built from an untouched category tree, so the only
        // one that can tell a persisted default this build no longer produces from one we just moved away.
        bool classified = CategoryClassifier.ClassifyAll(pruneStaleDefaults: true);

        var context = new ApplyContext();
        foreach (var info in Types)
        {
            ApplyTypeState(info, context);
        }

        FinishApply(context, updateSaveObjects: false, removeRecipeOverlap: false);

        // Only write when something actually changed - otherwise every launch touches Mod.xml for nothing.
        if (classified || !context.IsEmpty)
            Settings.WriteSettingsToFile();
    }

    /// <summary>
    /// Re-runs the classifier so every enabled category's default contents are assigned to it.
    /// Safe to call repeatedly: ClassifyAll never overrides a persisted user assignment or a
    /// user removal, so defs already moved elsewhere stay put. Called whenever the category
    /// editor opens and whenever settings close, so turning a merge setting on immediately
    /// (re)populates that category - without this a category unassigned while its merge was
    /// off (by CategoryAssignments.TidyData) never gets its contents back when merge returns.
    /// Purely additive: the stale-default prune is <see cref="ApplyAtBoot"/>'s alone, since by the time
    /// this runs the tree the defaults are built from no longer lists the defs already moved.
    /// </summary>
    /// <returns>True when the classifier created or changed an assignment, so callers can force a re-apply.</returns>
    internal static bool EnsureClassified()
    {
        if (!Types.Any(info => info.Dummy != null && info.Merge()))
            return false;

        return CategoryClassifier.ClassifyAll();
    }

    /// <summary>Called when the settings window closes: re-applies every type whose toggles changed.</summary>
    private static void ApplyToggles()
    {
        // A new assignment (new food mod installed, or a category repopulated after TidyData pruned it)
        // changes what a type's merge SHOULD contain without changing the type's toggle state, and
        // ApplyTypeState's unchanged-state short-circuit would skip the only code that physically moves
        // defs - leaving the settings claiming a membership the def graph never got. Forcing the pass is
        // safe because reset-then-reapply is idempotent, and it costs nothing on the common path where the
        // classifier changed nothing.
        bool classified = EnsureClassified();

        var context = new ApplyContext();
        foreach (var info in Types)
        {
            ApplyTypeState(info, context, force: classified);
        }

        if (context.IsEmpty)
            return;

        FinishApply(context, updateSaveObjects: true);
    }

    /// <summary>
    /// Called when the category editor closes: moves individual defs between merged categories. A diff
    /// naming a category whose merge is not applied cannot be honoured now, but it is not a no-op either -
    /// see the deferred branch below.
    /// </summary>
    private static void ApplyLive(List<(ThingDef def, string newCategoryDefName)> diffs)
    {
        if (diffs.NullOrEmpty())
            return;

        var context = new ApplyContext();
        foreach (var (def, newCategoryDefName) in diffs)
        {
            if (def == null)
                continue;

            if (!string.IsNullOrEmpty(newCategoryDefName))
            {
                // Merge not applied yet (e.g. toggled on but settings window still open):
                // the assignment is persisted and the toggle pass will pick it up instead. The def
                // must still leave whatever dummy category it physically sits in right now - skipping
                // outright would strand it in its old category with the settings claiming otherwise,
                // and ApplyTypeState's unchanged-state short-circuit means nothing revisits it.
                if (!IsMergeApplied(newCategoryDefName))
                {
                    LogMessage(() => $"Live move deferred for [{def.defName}]: merge not applied for [{newCategoryDefName}]", LogMessageType.Warning);
                    if (IsInDummyCategory(def))
                        ResetNativeCategories(def, fallbackParentName: null, context);
                    continue;
                }

                var newCategory = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(newCategoryDefName);
                if (newCategory == null)
                {
                    LogMessage(() => $"Live move skipped for [{def.defName}]: category [{newCategoryDefName}] not found", LogMessageType.Error);
                    continue;
                }
                SetDefCategories(def, [newCategory], context);
            }
            else
            {
                ResetNativeCategories(def, fallbackParentName: null, context);
            }
        }

        FinishApply(context, updateSaveObjects: true);
    }

    /// <summary>Whether the def physically sits in one of our dummy categories right now.</summary>
    private static bool IsInDummyCategory(ThingDef def)
    {
        var categories = def.thingCategories;
        if (categories.NullOrEmpty())
            return false;

        foreach (var cat in categories)
        {
            if (cat != null && cat.defName.StartsWith(Category.Prefix.DummyCategory))
                return true;
        }
        return false;
    }

    /// <summary>Whether the engine has actually merged the given dummy category this session.</summary>
    private static bool IsMergeApplied(string dummyDefName)
    {
        if (dummyDefName.NullOrEmpty() || !dummyDefName.StartsWith(Category.Prefix.DummyCategory))
            return false;

        string type = dummyDefName.Substring(Category.Prefix.DummyCategory.Length);
        return s_appliedState.TryGetValue(type, out var state) && state.merge;
    }

    // ---------------------------------------------------------------- per-type state

    /// <summary>
    /// Brings one category type from its last applied state to what the settings now ask for. Always
    /// resets to native first and re-applies from scratch rather than computing a delta - readout implies
    /// merge, so there is no useful partial transition. Comparing against <see cref="s_appliedState"/>
    /// first means an unchanged type costs nothing and produces no log noise.
    /// </summary>
    /// <param name="force">
    /// Runs the reset-and-reapply even when the toggles are unchanged. Set by <see cref="ApplyToggles"/>
    /// when the classifier altered the assignments, since those change what a merge must contain without
    /// changing the state this method compares. Deliberately a parameter rather than a clear of
    /// <see cref="s_appliedState"/>: clearing would read as "nothing applied yet" and let a type that was
    /// merged and is now toggled off short-circuit out of its own undo.
    /// </param>
    private static void ApplyTypeState(CategoryTypeInfo info, ApplyContext context, bool force = false)
    {
        // Each setting Func is read exactly once - readout implies merge, so the chain short-circuits to
        // the same result the repeated calls produced.
        bool enabled = info.Dummy != null;
        bool wantMerge = enabled && info.Merge();
        bool wantReadout = wantMerge && info.Readout();
        (bool merge, bool readout) desired = (wantMerge, wantReadout);

        s_appliedState.TryGetValue(info.Type, out var current); // Missing key = (false, false): nothing applied yet.
        if (current == desired && !force)
            return;

        LogMessage(() => $"Applying [{info.Type}]: merge [{current.merge}->{desired.merge}], readout [{current.readout}->{desired.readout}]", LogMessageType.Warning);

        ResetTypeToNative(info, context);

        if (desired.merge)
            ApplyMerge(info, context);

        SetReadoutRoot(info, desired.readout, context);

        s_appliedState[info.Type] = desired;
    }

    /// <summary>Undoes everything a previous state did for this type: membership, parenting, readout, trader stock.</summary>
    private static void ResetTypeToNative(CategoryTypeInfo info, ApplyContext context)
    {
        var dummy = info.Dummy;
        if (dummy == null)
            return;

        foreach (var def in dummy.childThingDefs?.ToList() ?? [])
        {
            ResetNativeCategories(def, info.MergeParentDefName, context);
        }

        UnparentDummy(dummy, context);
        SetReadoutRoot(info, false, context);
        RemoveTraderClones(info);
    }

    /// <summary>
    /// Full merge: the dummy category becomes a real node under its parent and every def assigned to it
    /// moves in exclusively. Membership comes from the persisted assignments (not the classifier), so a
    /// def the user moved in the editor lands where they put it.
    /// </summary>
    private static void ApplyMerge(CategoryTypeInfo info, ApplyContext context)
    {
        var dummy = info.Dummy;
        ParentDummy(dummy, info.MergeParentDefName, context);

        foreach (var assignment in Settings.CategoryData)
        {
            if (!assignment.IsAssignedTo(info.DummyDefName))
                continue;

            var def = DefDatabase<ThingDef>.GetNamedSilentFail(assignment.ThingDefName);
            if (def == null)
                continue;

            SetDefCategories(def, [dummy], context);
        }

        ApplyTraderClones(info);
    }

    // ---------------------------------------------------------------- def membership

    /// <summary>Replaces a def's category membership, keeping both sides' child lists in sync.</summary>
    private static void SetDefCategories(ThingDef def, List<ThingCategoryDef> newCategories, ApplyContext context)
    {
        CaptureNativeCategories(def);

        def.thingCategories ??= [];
        foreach (var cat in def.thingCategories)
        {
            if (cat == null)
                continue;
            cat.childThingDefs?.Remove(def);
            context.AffectedCategories.Add(cat);
        }
        def.thingCategories.Clear();

        foreach (var cat in newCategories)
        {
            if (cat == null || def.thingCategories.Contains(cat))
                continue;
            def.thingCategories.Add(cat);
            cat.childThingDefs ??= [];
            if (!cat.childThingDefs.Contains(def))
                cat.childThingDefs.Add(def);
            context.AffectedCategories.Add(cat);
        }

        context.MovedDefs.Add(def);
        LogMessage(() => $"\tSet [{def.defName}] categories to [{string.Join(", ", def.thingCategories.Select(c => c.defName))}]");
    }

    /// <summary>
    /// Records a def's pre-engine categories the first time it is touched, so a later revert has
    /// something to restore. First-write-wins is the whole point: a second capture would record our own
    /// dummy category as "native" and make the original membership unrecoverable for the session.
    /// </summary>
    private static void CaptureNativeCategories(ThingDef def)
    {
        if (s_nativeCategories.ContainsKey(def.defName))
            return;

        s_nativeCategories[def.defName] = [.. (def.thingCategories ?? [])
            .Where(cat => cat != null && !cat.defName.StartsWith(Category.Prefix.DummyCategory))
            .Select(cat => cat.defName)];
    }

    /// <summary>
    /// A def's original categories, from the capture if it has one and from its current non-dummy
    /// categories otherwise. Guarantees a non-empty result: a def left with no category at all would
    /// vanish from every storage filter and stockpile UI, so it is placed under the type's parent (or
    /// Foods) instead.
    /// </summary>
    private static List<ThingCategoryDef> GetNativeCategoriesFor(ThingDef def, string fallbackParentName)
    {
        List<ThingCategoryDef> result;

        if (s_nativeCategories.TryGetValue(def.defName, out var names))
        {
            // Null when nothing resolved; SetDefCategories cannot take null, and an empty list falls
            // through to the fallback below anyway.
            result = ResolveCategories(names) ?? [];
        }
        else
        {
            // Never touched this session: current non-dummy categories are its native ones.
            result = [.. (def.thingCategories ?? []).Where(cat => cat != null && !cat.defName.StartsWith(Category.Prefix.DummyCategory))];
        }

        if (result.Count == 0)
        {
            var fallback = (fallbackParentName != null ? DefDatabase<ThingCategoryDef>.GetNamedSilentFail(fallbackParentName) : null)
                ?? ThingCategoryDefOf.Foods;
            if (fallback != null)
                result.Add(fallback);
        }

        return result;
    }

    private static void ResetNativeCategories(ThingDef def, string fallbackParentName, ApplyContext context)
    {
        SetDefCategories(def, GetNativeCategoriesFor(def, fallbackParentName), context);
    }

    // ---------------------------------------------------------------- tree parenting

    /// <summary>
    /// Splices the dummy category into the category tree under <paramref name="parentDefName"/>, keeping
    /// parent and child links plus the tree node's nest depth consistent - RimWorld's category UI indents
    /// by nestDepth, so a node grafted in without it renders at the wrong level.
    /// </summary>
    private static void ParentDummy(ThingCategoryDef dummy, string parentDefName, ApplyContext context)
    {
        var parent = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(parentDefName) ?? ThingCategoryDefOf.Foods;
        if (parent == null || dummy.parent == parent)
            return;

        UnparentDummy(dummy, context);

        dummy.parent = parent;
        parent.childCategories ??= [];
        if (!parent.childCategories.Contains(dummy))
            parent.childCategories.Add(dummy);

        SetNestLevelsFrom(parent);

        context.AffectedCategories.Add(dummy);
        context.AffectedCategories.Add(parent);
        context.TreeChanged = true;
        LogMessage(() => $"Parented [{dummy.defName}] under [{parent.defName}]", LogMessageType.Warning);
    }

    private static void UnparentDummy(ThingCategoryDef dummy, ApplyContext context)
    {
        if (dummy.parent == null)
            return;

        context.AffectedCategories.Add(dummy.parent);
        dummy.parent.childCategories?.Remove(dummy);
        dummy.parent = null;
        context.TreeChanged = true;
        LogMessage(() => $"Unparented [{dummy.defName}]", LogMessageType.Warning);
    }

    /// <summary>
    /// Replicates ThingCategoryNodeDatabase.SetNestLevelRecursive for a subtree. Assumes a NON-ROOT parent:
    /// vanilla seeds the root itself at 0, so its direct children are also 0, while this adds one to the
    /// parent's depth and would put them at 1. Unreachable in practice - the only caller is
    /// <see cref="ParentDummy"/>, whose parent is always Foods, FoodRaw or PlantFoodRaw (or the Foods
    /// fallback), never the tree root. Left as-is rather than special-cased so the arithmetic stays obvious.
    /// </summary>
    private static void SetNestLevelsFrom(ThingCategoryDef parent)
    {
        if (parent.treeNode == null || parent.childCategories.NullOrEmpty())
            return;

        foreach (var child in parent.childCategories)
        {
            if (child?.treeNode == null)
                continue;
            child.treeNode.nestDepth = parent.treeNode.nestDepth + 1;
            SetNestLevelsFrom(child);
        }
    }

    /// <summary>
    /// Re-walks the category tree into ThingCategoryNodeDatabase's flat node list. Vanilla builds that
    /// list once at load; parenting or unparenting a category afterwards leaves it stale, which shows up
    /// as a category missing from (or lingering in) storage-filter UI.
    /// </summary>
    private static void RebuildCategoryNodeList()
    {
        if (!ThingCategoryNodeDatabase.initialized || ThingCategoryNodeDatabase.RootNode == null)
            return;

        ThingCategoryNodeDatabase.allThingCategoryNodes = [.. ThingCategoryNodeDatabase.RootNode.ChildCategoryNodesAndThis];
        LogMessage(() => $"Rebuilt ThingCategoryNodeDatabase node list: [{ThingCategoryNodeDatabase.allThingCategoryNodes.Count}] nodes");
    }

    // ---------------------------------------------------------------- resource readout

    private static void SetReadoutRoot(CategoryTypeInfo info, bool on, ApplyContext context)
    {
        var dummy = info.Dummy;
        if (dummy == null || dummy.resourceReadoutRoot == on)
            return;

        dummy.resourceReadoutRoot = on;
        context.ReadoutChanged = true;
    }

    /// <summary>The readout snapshots its root categories in its constructor, so swap in a fresh instance.</summary>
    private static void SwapResourceReadout()
    {
        if (Current.ProgramState != ProgramState.Playing || Find.MapUI == null)
            return;

        if (s_resourceReadoutField == null)
        {
            LogMessage(() => "MapInterface.resourceReadout field not found; readout updates after reload instead", LogMessageType.Error);
            return;
        }

        s_resourceReadoutField.SetValue(Find.MapUI, new ResourceReadout());
        LogMessage(() => "Swapped ResourceReadout instance", LogMessageType.Warning);
    }

    // ---------------------------------------------------------------- trader stock

    /// <summary>
    /// Keeps traders stocking these goods after a merge. A trader's StockGenerator_Category points at a
    /// fixed category def; once our defs move into the dummy category that generator no longer reaches
    /// them, and the trader silently stops carrying them. So the generator is cloned onto the dummy
    /// category. One clone per trader is enough; the loop breaks after the first match.
    /// <para>
    /// The original generator is left untouched. It used to be zeroed to stop the two generators
    /// double-stocking the same defs, but they cannot overlap: <c>SetDefCategories</c> replaces a moved
    /// def's membership with the dummy alone, so the original's category no longer contains it, and the
    /// dummy can never be a descendant of that category either - <see cref="IsModAddedCategoryOfType"/>
    /// requires the matched category's parent to be Foods, FoodRaw or PlantFoodRaw, while the dummy parents
    /// to Foods or PlantFoodRaw directly, making them siblings at best. Zeroing therefore removed the only
    /// stock source for every def still left in the mod's category: ones the user pulled back out, ones the
    /// name heuristic never matched, and anything a later mod adds to it.
    /// </para>
    /// </summary>
    private static void ApplyTraderClones(CategoryTypeInfo info)
    {
        var dummy = info.Dummy;
        if (dummy == null || s_stockCategoryDefField == null || s_stockCountRangeField == null)
            return;

        List<TraderCloneRecord> records = [];

        foreach (var trader in DefDatabase<TraderKindDef>.AllDefs)
        {
            if (trader.stockGenerators.NullOrEmpty())
                continue;

            if (trader.stockGenerators.Any(g => g is StockGenerator_Category cg && (ThingCategoryDef)s_stockCategoryDefField.GetValue(cg) == dummy))
                continue;

            // Safe to enumerate the live list: the only mutation below is followed immediately by break,
            // and foreach version-checks in MoveNext, which break skips.
            foreach (var generator in trader.stockGenerators)
            {
                if (generator is not StockGenerator_Category categoryGenerator)
                    continue;

                var stockCategory = (ThingCategoryDef)s_stockCategoryDefField.GetValue(categoryGenerator);
                if (stockCategory == null || !IsModAddedCategoryOfType(stockCategory, info.Type))
                    continue;

                var range = (IntRange)s_stockCountRangeField.GetValue(categoryGenerator);
                if (range.TrueMax <= 0)
                    continue;

                var clone = new StockGenerator_Category
                {
                    countRange = categoryGenerator.countRange,
                    customCountRanges = categoryGenerator.customCountRanges,
                    totalPriceRange = categoryGenerator.totalPriceRange,
                    maxTechLevelGenerate = categoryGenerator.maxTechLevelGenerate,
                    maxTechLevelBuy = categoryGenerator.maxTechLevelBuy,
                    price = categoryGenerator.price,
                };
                s_stockCategoryDefField.SetValue(clone, dummy);
                s_stockCountRangeField.SetValue(clone, range);
                CopyExcludedList<ThingDef>(s_stockExcludedThingDefsField, categoryGenerator, clone);
                CopyExcludedList<ThingCategoryDef>(s_stockExcludedCategoriesField, categoryGenerator, clone);

                trader.stockGenerators.Add(clone);
                clone.ResolveReferences(trader);

                records.Add(new TraderCloneRecord { Trader = trader, Clone = clone });
                LogMessage(() => $"Cloned trader stock [{stockCategory.defName}] -> [{dummy.defName}] on [{trader.defName}]");
                break; // one clone per trader is all the dummy needs
            }
        }

        if (records.Count > 0)
            s_traderClones[info.Type] = records;
    }

    /// <summary>
    /// Copies one of StockGenerator_Category's private exclusion lists onto the clone. A fresh list, not the
    /// original's instance: sharing it would make anything that ever edits one generator's exclusions
    /// silently edit the other's.
    /// </summary>
    private static void CopyExcludedList<T>(FieldInfo field, StockGenerator source, StockGenerator target)
    {
        if (field == null)
            return;

        if (field.GetValue(source) is not List<T> list)
        {
            field.SetValue(target, null);
            return;
        }

        field.SetValue(target, new List<T>(list));
    }

    /// <summary>Undoes <see cref="ApplyTraderClones"/>: drops each clone. The original was never altered.</summary>
    private static void RemoveTraderClones(CategoryTypeInfo info)
    {
        if (!s_traderClones.TryGetValue(info.Type, out var records))
            return;

        foreach (var record in records)
        {
            record.Trader?.stockGenerators?.Remove(record.Clone);
        }

        s_traderClones.Remove(info.Type);
        LogMessage(() => $"Removed [{records.Count}] trader stock clones for [{info.Type}]", LogMessageType.Warning);
    }

    /// <summary>
    /// Post-load half of the patch-op matching: parent is a food root, the name is not excluded, the name
    /// matches the type. All three rules live on <see cref="Category"/> and are shared with
    /// the patch-phase call sites in <c>PatchOperationPathedExtended</c> - this used to be a hand-copied
    /// twin, and a rule changed on one side only made the two disagree with no error: defs get moved into
    /// our category by the patch phase while this method declines to clone the trader's stock generator, so
    /// the food quietly stops appearing at traders.
    /// <para>
    /// Sharing the rules does NOT make the two sides infallible. The patch phase tests the raw
    /// <c>&lt;parent&gt;</c> text on disk; this tests <c>cat.parent</c> after inheritance and after every
    /// other mod's patches ran. A category reparented by a third mod can still match on one side only.
    /// </para>
    /// </summary>
    private static bool IsModAddedCategoryOfType(ThingCategoryDef cat, string categoryType)
    {
        return Category.ParentMatchesKind(categoryType, cat.parent?.defName)
            && !Category.IsExcludedName(cat.defName)
            && Category.NameMatchesKind(cat.defName, categoryType);
    }

    // ---------------------------------------------------------------- finish: caches, filters, save objects

    /// <summary>
    /// Single tail for every apply path: refreshes exactly the caches the run's changes invalidated, in
    /// dependency order (category caches, then the node list, then filters, then the readout). Batching it
    /// here is why an apply touching six types still pays each refresh once.
    /// </summary>
    /// <param name="updateSaveObjects">
    /// False at boot - no game is loaded, so bills/stockpiles/policies do not exist yet. True for live
    /// applies, where those scribed filters must be corrected or the player sees stale allow states.
    /// </param>
    /// <param name="removeRecipeOverlap">
    /// False at boot: BootActions.TryApplyAll always runs an unrestricted
    /// RecipeProductIngredientOverlapRemover.TryRemoveAllOverlap() right after ApplyAtBoot, which would
    /// immediately redo this pass's restricted sweep. True for live applies, which have no such follow-up.
    /// </param>
    private static void FinishApply(ApplyContext context, bool updateSaveObjects, bool removeRecipeOverlap = true)
    {
        if (context.IsEmpty)
            return;

        RefreshCategoryCaches(context);

        if (context.TreeChanged)
        {
            // The classifier's Foods/PlantFoodRaw child-set snapshots come from ThisAndChildCategoryDefs, so
            // reparenting a category invalidates them exactly like the vanilla caches refreshed above - and
            // with them its cached defaults map, whose heuristics read those snapshots.
            CategoryClassifier.ClearCache();
            RebuildCategoryNodeList();
        }

        if (context.MovedDefs.Count > 0)
        {
            RefreshDefLevelFilters(context);

            // Strictly after the pass above, which recomputes each moved def's allow state from scratch.
            // This one only ever allows, so running it second means it layers on top of a correct baseline
            // instead of being undone by it - and when a def leaves the animal food category the baseline
            // is what removes it again, with no undo bookkeeping here.
            AllowAnimalFoodIngredients();

            // Both passes above can leave a recipe allowing its own product: the first recomputes allow
            // state from the raw XML lists, which SetAllow never writes, so it silently re-allows anything
            // the boot-time removal took out. Restricted to the moved defs because those are the only defs
            // either pass can have touched. Runs before the save objects are synced so bills copy the
            // corrected state rather than the overlapping one. Skipped at boot: BootActions.TryApplyAll
            // runs its own unrestricted sweep immediately after ApplyAtBoot returns.
            if (removeRecipeOverlap)
                RecipeProductIngredientOverlapRemover.TryRemoveAllOverlap(context.MovedDefs);

            if (updateSaveObjects && Current.ProgramState == ProgramState.Playing && Current.Game != null)
                UpdateSaveObjectFilters(context);
        }

        if (context.ReadoutChanged)
            SwapResourceReadout();

        LogMessage(() => $"Apply finished: [{context.MovedDefs.Count}] defs, [{context.AffectedCategories.Count}] categories, treeChanged [{context.TreeChanged}]", LogMessageType.Warning);
    }

    /// <summary>Rebuilds allChildThingDefsCached on every touched category and all of their ancestors.</summary>
    private static void RefreshCategoryCaches(ApplyContext context)
    {
        HashSet<ThingCategoryDef> toRefresh = [];
        foreach (var cat in context.AffectedCategories)
        {
            toRefresh.Add(cat);
            foreach (var parent in cat.Parents)
            {
                toRefresh.Add(parent);
            }
        }

        foreach (var cat in toRefresh)
        {
            cat.ClearCachedData();
            cat.ResolveReferences();
        }
    }

    /// <summary>
    /// Corrects allowedDefs on def-level filters (recipes, building storage presets, refuelables,
    /// ProcessorFramework processes) for each moved def. Never calls ThingFilter.ResolveReferences -
    /// it is additive-only and cannot remove defs that left a category.
    /// </summary>
    private static void RefreshDefLevelFilters(ApplyContext context)
    {
        if (s_filterCategoriesField == null)
        {
            LogMessage(() => "ThingFilter reflection unavailable; filters not refreshed", LogMessageType.Error);
            return;
        }

        int touched = 0;
        foreach (var filter in EnumerateDefLevelFilters())
        {
            touched += CorrectFilterForMovedDefs(filter, context);
        }

        LogMessage(() => $"Def-level filter pass corrected [{touched}] allow states", LogMessageType.Warning);
    }

    /// <summary>
    /// Every ThingFilter that lives on a Def (as opposed to the scribed ones in a save). Only filters need
    /// correcting: a mod that matches ingredients against a ThingCategoryDef directly - VEF's
    /// PipeSystem.ProcessDef.Ingredient.thingCategory, for one - resolves through
    /// ThingDef.IsWithinCategory / ThingCategoryDef.childThingDefs at runtime and therefore follows the
    /// category edits <see cref="SetDefCategories"/> makes with no work from us.
    /// </summary>
    private static IEnumerable<ThingFilter> EnumerateDefLevelFilters()
    {
        foreach (var recipe in DefDatabase<RecipeDef>.AllDefs)
        {
            if (recipe.fixedIngredientFilter != null)
                yield return recipe.fixedIngredientFilter;
            if (recipe.defaultIngredientFilter != null)
                yield return recipe.defaultIngredientFilter;
            if (!recipe.ingredients.NullOrEmpty())
            {
                foreach (var ingredient in recipe.ingredients)
                {
                    if (ingredient?.filter != null)
                        yield return ingredient.filter;
                }
            }
        }

        foreach (var thing in DefDatabase<ThingDef>.AllDefs)
        {
            if (thing.building?.fixedStorageSettings?.filter != null)
                yield return thing.building.fixedStorageSettings.filter;
            if (thing.building?.defaultStorageSettings?.filter != null)
                yield return thing.building.defaultStorageSettings.filter;

            var refuelable = thing.GetCompProperties<CompProperties_Refuelable>();
            if (refuelable?.fuelFilter != null)
                yield return refuelable.fuelFilter;
        }

        foreach (var filter in EnumerateProcessorFrameworkFilters())
        {
            yield return filter;
        }
    }

    private static Type s_processDefType;
    private static FieldInfo s_processIngredientFilterField;
    private static bool s_processorFrameworkChecked;

    /// <summary>ProcessorFramework ProcessDef.ingredientFilter, when that mod is loaded (soft reflection).</summary>
    private static IEnumerable<ThingFilter> EnumerateProcessorFrameworkFilters()
    {
        if (!s_processorFrameworkChecked)
        {
            s_processorFrameworkChecked = true;
            s_processDefType = GenTypes.GetTypeInAnyAssembly("ProcessorFramework.ProcessDef");
            s_processIngredientFilterField = s_processDefType?.GetField("ingredientFilter", BindingFlags.Public | BindingFlags.Instance);
        }

        if (s_processDefType == null || s_processIngredientFilterField == null)
            yield break;

        foreach (var def in GenDefDatabase.GetAllDefsInDatabaseForDef(s_processDefType))
        {
            if (s_processIngredientFilterField.GetValue(def) is ThingFilter filter)
                yield return filter;
        }
    }

    /// <summary>
    /// Recomputes one filter's allow state for each moved def, from the filter's RAW XML lists rather than
    /// its resolved allowedDefs - the raw lists still name categories, which is the only way to tell what
    /// the filter meant now that the def has changed category. The raw category NAMES are resolved to defs
    /// once up front (see <see cref="ResolveCategories"/>) because that result is the same for every moved
    /// def; the def's pre-move categories are resolved once per pass alongside them, since a category the
    /// def has LEFT still counts as reaching it (see <see cref="CategoriesReach"/>). Returns how many allow
    /// states changed.
    /// <para>
    /// A def is only written when the move actually CHANGED what the filter means for it - the post-move
    /// verdict is compared against the verdict the same rules give against the def's pre-move categories,
    /// and an unchanged pair is left alone. Without that comparison every recomputation overwrote allow
    /// states a third party had set with <c>SetAllow</c> at boot, which the raw XML lists never record.
    /// The exception is a pair this engine has already claimed (<see cref="s_ownFilterWrites"/>, filled
    /// here the first time a move re-verdicts the pair, and by <see cref="AllowAll"/> for the allowances the
    /// animal food ingredient pass writes): there the write is unconditional, because a merge toggled back
    /// off puts the def in its native categories, making both verdicts equal again - skipping that write
    /// would strand the filter on the merged-era value instead of restoring the native one.
    /// </para>
    /// </summary>
    private static int CorrectFilterForMovedDefs(ThingFilter filter, ApplyContext context)
    {
        var rawCategories = (List<string>)s_filterCategoriesField.GetValue(filter);
        var rawDisallowedCategories = (List<string>)s_filterDisallowedCategoriesField?.GetValue(filter);
        var rawThingDefs = (List<ThingDef>)s_filterThingDefsField?.GetValue(filter);
        var rawDisallowedThingDefs = (List<ThingDef>)s_filterDisallowedThingDefsField?.GetValue(filter);

        bool referencesCategories = !rawCategories.NullOrEmpty() || !rawDisallowedCategories.NullOrEmpty();

        // A filter mixing <categories> with a rule we don't model (trade tags, disallowWorsePreferability,
        // etc.) would get a wrong verdict from ComputeShouldAllow's precedence - leaving it untouched is
        // the same conservative outcome a filter with no categories reference already gets below.
        if (referencesCategories && HasUnmodelledFilterRules(filter))
            return 0;

        // Name -> def resolution is invariant across MovedDefs, so it happens once per filter here. The
        // previous shape re-resolved every raw category name for every moved def, for every filter in the
        // game - and EnumerateDefLevelFilters walks every recipe and every ThingDef.
        var rules = new FilterRules(
            ResolveCategories(rawCategories),
            ResolveCategories(rawDisallowedCategories),
            rawThingDefs,
            rawDisallowedThingDefs);

        s_ownFilterWrites.TryGetValue(filter, out HashSet<ThingDef> ownWrites);

        int changed = 0;
        foreach (var def in context.MovedDefs)
        {
            bool allowsNow = filter.Allows(def);

            // Only touch filters that could reference this def through categories or already allow it.
            if (!referencesCategories && !allowsNow)
                continue;

            List<ThingCategoryDef> nativeCategories = GetCapturedNativeCategories(def, context);

            bool shouldAllow = ComputeShouldAllow(def, rules, nativeCategories);

            if (ownWrites?.Contains(def) != true)
            {
                // What the same rules gave before the move. A filter naming no categories cannot disagree
                // between the two verdicts, so its second computation is skipped rather than run to a
                // foregone conclusion.
                bool verdictUnchanged = !referencesCategories
                    || ComputeShouldAllow(def, rules, nativeCategories, useCurrentTree: false) == shouldAllow;

                // Move changed nothing here, and we have never claimed this pair - so the current state is
                // somebody else's opinion of the def, not a stale consequence of our move. Leave it.
                if (verdictUnchanged)
                    continue;

                // Claimed on the verdict change rather than on the write below, which may not happen: a
                // filter that already agreed with the post-move verdict still needs the restoring write
                // when the merge goes off, and by then both verdicts are native again and the check above
                // would skip it.
                ownWrites ??= s_ownFilterWrites.GetOrCreateValue(filter);
                ownWrites.Add(def);
            }

            if (allowsNow != shouldAllow)
            {
                filter.SetAllow(def, shouldAllow);
                changed++;
            }
        }
        return changed;
    }

    /// <summary>
    /// Category names resolved to defs - a filter's raw XML list, or a def's captured native categories.
    /// Returns null for a null/empty input and for a list where nothing resolved, so callers that treat
    /// "no categories" as a shortcut can take it with one reference check. Does NOT de-duplicate: neither
    /// caller cares (<see cref="CategoriesReach"/> only ORs, <see cref="SetDefCategories"/> skips defs the
    /// list already holds), and the inputs cannot contain repeats to begin with.
    /// </summary>
    private static List<ThingCategoryDef> ResolveCategories(List<string> rawNames)
    {
        if (rawNames.NullOrEmpty())
            return null;

        List<ThingCategoryDef> categories = null;
        foreach (string name in rawNames)
        {
            var category = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(name);
            if (category == null)
                continue;

            categories ??= [];
            categories.Add(category);
        }
        return categories;
    }

    /// <summary>
    /// True when the filter uses a rule <see cref="ComputeShouldAllow"/> does not model (vanilla
    /// <c>ThingFilter.ResolveReferences</c> applies 16 more rules than the four we replicate). Checked only
    /// for filters that also reference categories - <see cref="CorrectFilterForMovedDefs"/> skips those
    /// filters entirely rather than risk a wrong verdict from our partial precedence.
    /// </summary>
    private static bool HasUnmodelledFilterRules(ThingFilter filter)
    {
        foreach (var field in s_exoticListFields)
        {
            if (field?.GetValue(filter) is ICollection { Count: > 0 })
                return true;
        }

        foreach (var (field, unset) in s_exoticScalarFields)
        {
            // Static Equals, not value.Equals: the two comp fields are Types and read back null.
            if (field != null && !Equals(field.GetValue(filter), unset))
                return true;
        }

        return false;
    }

    /// <summary>
    /// One filter's four raw XML lists, read once per filter and then reused for every moved def. Category
    /// lists arrive already resolved to defs (null when the filter names none), so <see cref="ComputeShouldAllow"/>
    /// does no DefDatabase work per def.
    /// </summary>
    private readonly struct FilterRules(
        List<ThingCategoryDef> allowedCategories,
        List<ThingCategoryDef> disallowedCategories,
        List<ThingDef> thingDefs,
        List<ThingDef> disallowedThingDefs)
    {
        public readonly List<ThingCategoryDef> AllowedCategories = allowedCategories;
        public readonly List<ThingCategoryDef> DisallowedCategories = disallowedCategories;
        public readonly List<ThingDef> ThingDefs = thingDefs;
        public readonly List<ThingDef> DisallowedThingDefs = disallowedThingDefs;
    }

    /// <summary>
    /// Replicates ThingFilter's own precedence for a single def, in the same order vanilla
    /// ResolveReferences applies its rules (thingDefs allow, then categories allow, then
    /// disallowedCategories disallow, then disallowedThingDefs disallow - each a straight overwrite, so
    /// the LAST matching rule wins). Default is false - a def no rule mentions is not allowed.
    /// </summary>
    /// <param name="useCurrentTree">
    /// Passed through to <see cref="CategoriesReach"/>. False asks for the verdict these rules gave BEFORE
    /// the engine moved the def, which <see cref="CorrectFilterForMovedDefs"/> compares against the current
    /// one to tell its own effect apart from a third party's allow state.
    /// </param>
    private static bool ComputeShouldAllow(
        ThingDef def,
        in FilterRules rules,
        List<ThingCategoryDef> nativeCategories,
        bool useCurrentTree = true)
    {
        bool allow = false;

        if (rules.ThingDefs?.Contains(def) == true)
            allow = true;

        if (CategoriesReach(def, rules.AllowedCategories, nativeCategories, useCurrentTree))
            allow = true;

        if (CategoriesReach(def, rules.DisallowedCategories, nativeCategories, useCurrentTree))
            allow = false;

        if (rules.DisallowedThingDefs?.Contains(def) == true)
            allow = false;

        return allow;
    }

    /// <summary>
    /// True when any listed category reaches the def - either through the tree as it stands now, or
    /// through the categories the def held before the engine moved it. That second test is what stops a
    /// move from silently narrowing a filter written against vanilla: a def relocated out of PlantFoodRaw
    /// is still exactly what "PlantFoodRaw" meant when the recipe was authored. It matters most for the
    /// AnimalFoods type, whose dummy parents under Foods and so leaves the FoodRaw subtree entirely,
    /// but it is applied to every type - for the others the direct test already passes and this is a no-op.
    /// Applied to the disallow list as well, or a filter that deliberately excluded a category would start
    /// letting moved defs back in.
    /// </summary>
    /// <param name="useCurrentTree">
    /// False drops the first test, leaving only the pre-move categories - the reachability this filter saw
    /// before the engine touched the def. <see cref="CorrectFilterForMovedDefs"/> uses that to compute the
    /// filter's earlier verdict; every other caller wants the default.
    /// </param>
    private static bool CategoriesReach(
        ThingDef def,
        List<ThingCategoryDef> categories,
        List<ThingCategoryDef> nativeCategories,
        bool useCurrentTree = true)
    {
        if (categories == null)
            return false;

        if (useCurrentTree)
        {
            foreach (var category in categories)
            {
                if (category.ContainedInThisOrDescendant(def))
                    return true;
            }
        }

        if (nativeCategories == null)
            return false;

        foreach (var category in categories)
        {
            foreach (var native in nativeCategories)
            {
                if (IsThisOrAncestorOf(category, native))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Walks the parent chain upward: true when <paramref name="category"/> is <paramref name="descendant"/>
    /// itself or one of its ancestors. Cheaper than ThisAndChildCategoryDefs, which enumerates a whole subtree.
    /// </summary>
    private static bool IsThisOrAncestorOf(ThingCategoryDef category, ThingCategoryDef descendant)
    {
        for (ThingCategoryDef node = descendant; node != null; node = node.parent)
        {
            if (node == category)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The def's captured pre-move categories, resolved to defs once per apply pass. Deliberately has no
    /// stand-in for an empty capture, unlike <see cref="GetNativeCategoriesFor"/>: that method needs a
    /// non-empty result so a def never ends up category-less, whereas fabricating one here would invent
    /// filter reachability the def never had.
    /// </summary>
    private static List<ThingCategoryDef> GetCapturedNativeCategories(ThingDef def, ApplyContext context)
    {
        if (context.NativeCategoryCache.TryGetValue(def, out var cached))
            return cached;

        // Null for a def with no capture and for one whose captured names no longer resolve - both mean
        // "no pre-move reachability to add", which is exactly what CategoriesReach reads null as.
        List<ThingCategoryDef> resolved = s_nativeCategories.TryGetValue(def.defName, out var names)
            ? ResolveCategories(names)
            : null;

        context.NativeCategoryCache[def] = resolved;
        return resolved;
    }

    // ---------------------------------------------------------------- animal food ingredients

    /// <summary>
    /// Lets raw plant matter sitting in the animal foods category be used as an ingredient by any recipe
    /// that makes animal food. Vanilla already does exactly this by hand for Hay, which lives in Foods
    /// rather than PlantFoodRaw (so it never reaches colonist meal recipes) and is then named explicitly in
    /// Make_Kibble's greens slot and fixedIngredientFilter. Our feed defs - and other mods' - are filed the
    /// same way but never got the second half, so a kibble recipe silently refuses the one thing most
    /// obviously meant for it. This generalises vanilla's whitelist instead of moving the defs, which would
    /// drag feed into every vegetarian meal.
    /// <para>
    /// Additive within a pass, and undone across passes by ownership rather than by undo state kept here:
    /// <see cref="AllowAll"/> claims every allowance it writes in <see cref="s_ownFilterWrites"/>, and
    /// <see cref="CorrectFilterForMovedDefs"/> rewrites a claimed pair from the filter's raw lists
    /// unconditionally, so the category toggled off takes the allowances with it. Ordering in
    /// <see cref="FinishApply"/> is what makes that work: the correcting pass runs first, so on the off pass
    /// it restores before this one could re-widen anything - and by then the dummy category is empty, so
    /// <see cref="BuildAnimalFoodIngredientCandidates"/> returns nothing and this pass exits early.
    /// </para>
    /// </summary>
    private static void AllowAnimalFoodIngredients()
    {
        if (InternalThingCategoryDefOf.VV_NHCP_DummyCategory_AnimalFoods is not { } dummy)
            return;

        List<ThingDef> candidates = BuildAnimalFoodIngredientCandidates(dummy);
        if (candidates.Count == 0)
            return;

        // Per-pass memo for SlotTakesPlantSourcedFood. The same handful of categories (PlantFoodRaw,
        // MeatRaw, the mod-added food categories) is named by slot after slot across every animal-food
        // recipe, and each miss walks that category's whole subtree through the UNCACHED DescendantThingDefs
        // iterator. Not static: the answer depends on category membership, which an apply pass is in the
        // middle of changing, and on def identity, which a language switch replaces wholesale.
        Dictionary<ThingCategoryDef, bool> plantSourcedCache = [];

        int touched = 0;
        foreach (var recipe in DefDatabase<RecipeDef>.AllDefs)
        {
            if (!ProducesAnimalFood(recipe))
                continue;

            touched += AllowIngredientsForRecipe(recipe, candidates, plantSourcedCache);
        }

        LogMessage(() => $"Animal food ingredient pass allowed [{touched}] entries from [{candidates.Count}] candidates", LogMessageType.Warning);
    }

    /// <summary>
    /// Current members of the animal foods category that are plant matter and not themselves crafted.
    /// Membership is read from the category rather than from the settings, so a def the user pulled out is
    /// dropped here on the next pass with no extra bookkeeping. The crafted test is a sweep over every
    /// recipe's products rather than a flag: it is what keeps a third-party feed that was tagged Plant
    /// instead of Kibble from becoming an ingredient of the very recipe that makes it, and it structurally
    /// rules out a recipe consuming its own output.
    /// </summary>
    private static List<ThingDef> BuildAnimalFoodIngredientCandidates(ThingCategoryDef dummy)
    {
        List<ThingDef> candidates = [];
        if (dummy.childThingDefs.NullOrEmpty())
            return candidates;

        HashSet<ThingDef> recipeProducts = BuildRecipeProductSet();

        foreach (ThingDef def in dummy.childThingDefs)
        {
            if (def == null || !def.IsPlantSourcedFood())
                continue;

            if (recipeProducts.Contains(def))
                continue;

            candidates.Add(def);
        }

        return candidates;
    }

    /// <summary>Every ThingDef any recipe produces, i.e. everything in the game that is crafted.</summary>
    private static HashSet<ThingDef> BuildRecipeProductSet()
    {
        HashSet<ThingDef> products = [];
        foreach (var recipe in DefDatabase<RecipeDef>.AllDefs)
        {
            if (recipe.products.NullOrEmpty())
                continue;

            foreach (var product in recipe.products)
            {
                if (product?.thingDef != null)
                    products.Add(product.thingDef);
            }
        }

        return products;
    }

    private static bool ProducesAnimalFood(RecipeDef recipe)
    {
        if (recipe.products.NullOrEmpty())
            return false;

        foreach (var product in recipe.products)
        {
            if (product?.thingDef?.IsAnimalFood() == true)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Ingredient slots come first and are widened only when they already take plant matter, which is what
    /// keeps hay out of Make_Kibble's meat slot while still reaching its greens slot. The fixed and default
    /// filters are then widened with what those slots actually ended up accepting, and with nothing at all
    /// when no slot qualified. The fixed filter is only the universe a bill's own filter may draw from and
    /// never a source of ingredients in its own right, so widening it past the slots grants nothing usable
    /// while listing every feed def in the bill dialog of a recipe whose slots are deliberately narrow -
    /// VV_MakeProteinKibble, whose slots are meat plus a single hard-named feed, is the case in point.
    /// </summary>
    private static int AllowIngredientsForRecipe(RecipeDef recipe, List<ThingDef> candidates, Dictionary<ThingCategoryDef, bool> plantSourcedCache)
    {
        if (recipe.ingredients.NullOrEmpty())
            return 0;

        List<ThingDef> granted = [];
        int changed = 0;

        foreach (var ingredient in recipe.ingredients)
        {
            if (ingredient?.filter is not { } filter)
                continue;

            if (!SlotTakesPlantSourcedFood(filter, plantSourcedCache))
                continue;

            changed += AllowAll(filter, candidates);
            CollectAllowed(filter, candidates, granted);
        }

        if (granted.Count == 0)
            return changed;

        changed += AllowAll(recipe.fixedIngredientFilter, granted)
            + AllowAll(recipe.defaultIngredientFilter, granted);

        return changed;
    }

    /// <summary>Adds every candidate the slot accepts to <paramref name="granted"/>, without duplicates.</summary>
    private static void CollectAllowed(ThingFilter filter, List<ThingDef> candidates, List<ThingDef> granted)
    {
        foreach (ThingDef def in candidates)
        {
            if (filter.Allows(def) && !granted.Contains(def))
                granted.Add(def);
        }
    }

    /// <summary>
    /// Whether a slot is one this pass should widen, judged from the CATEGORIES it names rather than from
    /// the defs it resolves to. A named category that reaches plant matter marks a slot as taking greens in
    /// general, which is the only thing that earns it our feeds. A slot naming a mod-added animal food
    /// category qualifies through the same test, our dummy having been merged into that category.
    /// <para>
    /// Explicit thingDefs entries deliberately do NOT qualify a slot. A slot that hard-names one feed means
    /// that feed and nothing else - VV_MakeProteinKibble's wheat slot is the case in point - and widening it
    /// would both list every feed in the bill dialog and cost the slot the single-def status that makes it a
    /// fixed ingredient. Reading the raw list rather than the resolved one also means our own previous
    /// SetAllow calls cannot qualify a slot on a later pass, since they never touch the raw lists.
    /// </para>
    /// </summary>
    /// <param name="plantSourcedCache">
    /// Per-pass memo of the category-level answer, owned by <see cref="AllowAnimalFoodIngredients"/>. The
    /// verdict for a given category is identical for every slot naming it, and computing it walks that
    /// category's whole subtree on a miss.
    /// </param>
    private static bool SlotTakesPlantSourcedFood(ThingFilter filter, Dictionary<ThingCategoryDef, bool> plantSourcedCache)
    {
        List<ThingCategoryDef> categories =
            ResolveCategories((List<string>)s_filterCategoriesField?.GetValue(filter));
        if (categories == null)
            return false;

        foreach (ThingCategoryDef category in categories)
        {
            if (ReachesPlantSourcedFood(category, plantSourcedCache))
                return true;
        }

        return false;
    }

    /// <summary>Whether any def in the category's subtree is plant-sourced food, memoised per apply pass.</summary>
    private static bool ReachesPlantSourcedFood(ThingCategoryDef category, Dictionary<ThingCategoryDef, bool> plantSourcedCache)
    {
        if (plantSourcedCache.TryGetValue(category, out bool cached))
            return cached;

        bool reaches = false;
        foreach (ThingDef def in category.DescendantThingDefs)
        {
            if (def.IsPlantSourcedFood())
            {
                reaches = true;
                break;
            }
        }

        plantSourcedCache[category] = reaches;
        return reaches;
    }

    /// <summary>
    /// Allows every candidate the filter does not already allow, claiming each write in
    /// <see cref="s_ownFilterWrites"/>. The claim is what makes these allowances undoable: a claimed pair is
    /// written unconditionally by the next pass's <see cref="CorrectFilterForMovedDefs"/>, so the animal
    /// foods category toggled back off restores the filter's raw-list verdict instead of stranding our
    /// allowance. Without it the pair stays unclaimed forever - the widened slots name categories that
    /// reach these defs neither before nor after the move, so the two verdicts always agree and the
    /// restoring write is skipped.
    /// <para>
    /// Only real writes are claimed. Claiming a def the filter already allowed would take ownership of
    /// somebody else's allow state and let a later pass overwrite it.
    /// </para>
    /// </summary>
    private static int AllowAll(ThingFilter filter, List<ThingDef> candidates)
    {
        if (filter == null)
            return 0;

        int changed = 0;
        HashSet<ThingDef> ownWrites = null;
        foreach (ThingDef def in candidates)
        {
            if (filter.Allows(def))
                continue;

            filter.SetAllow(def, true);

            ownWrites ??= s_ownFilterWrites.GetOrCreateValue(filter);
            ownWrites.Add(def);
            changed++;
        }

        return changed;
    }

    // ---------------------------------------------------------------- live save objects

    /// <summary>
    /// Scribed ThingFilters (bills, storage, food policies) are per-def snapshots with no
    /// category info left, so they are updated per moved def: bills follow their recipe's
    /// def-level filter; storage and food policies follow a sibling heuristic on the def's
    /// new category (allow when all other members are allowed, disallow when none are).
    /// </summary>
    private static void UpdateSaveObjectFilters(ApplyContext context)
    {
        int changed = 0;

        foreach (var bill in BillUtility.GlobalBills())
        {
            var recipe = bill?.recipe;
            var billFilter = bill?.ingredientFilter;
            if (recipe == null || billFilter == null)
                continue;

            foreach (var def in context.MovedDefs)
            {
                // Mirrors Bill.IsFixedOrAllowedIngredient: a fixed slot bypasses fixedIngredientFilter
                // entirely, everything else needs both its own slot filter AND the recipe's shared one.
                bool recipeAllows = !recipe.ingredients.NullOrEmpty()
                    && recipe.ingredients.Any(ingredient => ingredient?.filter?.Allows(def) == true
                        && (ingredient.IsFixedIngredient || recipe.fixedIngredientFilter?.Allows(def) == true));

                if (billFilter.Allows(def) != recipeAllows)
                {
                    billFilter.SetAllow(def, recipeAllows);
                    changed++;
                }
            }
        }

        foreach (var map in Find.Maps)
        {
            var groups = map?.haulDestinationManager?.AllGroupsListForReading;
            if (groups == null)
                continue;

            foreach (var group in groups)
            {
                var filter = group?.Settings?.filter;
                if (filter != null)
                    changed += ApplySiblingHeuristic(filter, context);
            }
        }

        var foodRestrictions = Current.Game.foodRestrictionDatabase?.AllFoodRestrictions;
        if (foodRestrictions != null)
        {
            foreach (var policy in foodRestrictions)
            {
                if (policy?.filter != null)
                    changed += ApplySiblingHeuristic(policy.filter, context);
            }
        }

        LogMessage(() => $"Save-object filter pass corrected [{changed}] allow states", LogMessageType.Warning);
    }

    /// <summary>
    /// Guesses a moved def's allow state in a scribed filter from how its new siblings are set. Only acts
    /// on a unanimous verdict (all siblings allowed, or none) - anything mixed is a deliberate per-def
    /// choice by the player and is left alone. Other moved defs are excluded from the vote so the outcome
    /// does not depend on iteration order.
    /// </summary>
    private static int ApplySiblingHeuristic(ThingFilter filter, ApplyContext context)
    {
        int changed = 0;
        foreach (var def in context.MovedDefs)
        {
            var newCategory = def.thingCategories?.FirstOrDefault();
            if (newCategory == null)
                continue;

            int allowedSiblings = 0;
            int totalSiblings = 0;
            foreach (var sibling in newCategory.childThingDefs)
            {
                if (sibling == def || context.MovedDefs.Contains(sibling))
                    continue;
                totalSiblings++;
                if (filter.Allows(sibling))
                    allowedSiblings++;
            }

            if (totalSiblings == 0)
                continue; // No signal - leave the user's per-def state alone.

            bool? want = null;
            if (allowedSiblings == totalSiblings)
                want = true;
            else if (allowedSiblings == 0)
                want = false;

            if (want.HasValue && filter.Allows(def) != want.Value)
            {
                filter.SetAllow(def, want.Value);
                changed++;
            }
        }
        return changed;
    }
}