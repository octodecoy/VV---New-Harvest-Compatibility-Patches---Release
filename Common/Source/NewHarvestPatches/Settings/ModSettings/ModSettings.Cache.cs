namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    public const float ChildIndent = 48f;
    private static string[] s_commonalityLabels;
    public static Dictionary<string, HashSet<string>> ModAddedCategoryDictionary = [];
    public static HashSet<string> ModAddedCategoryTypeCache = [];
    public static Dictionary<string, FieldInfo> SettingFieldCache = [];

    /// <summary>
    /// Block-scoped node cache filled by <see cref="CacheNodes"/> and read by
    /// <see cref="PatchOperationPathedExtended.cacheKey"/>. Entries are removed by
    /// CacheNodes itself once its block finishes - this null is only a boot-time backstop.
    /// </summary>
    public static Dictionary<string, List<XmlNode>> XmlNodeCache = [];

    /// <summary>
    /// Every ThingCategoryDef declared in the unified patch document, mapped to its raw &lt;parent&gt; text
    /// (empty string when it declares none). Built once on first use by
    /// <see cref="PatchOperationPathedExtended.GetCategoryParentIndex"/> - without it the category tests run
    /// a full-document SelectSingleNode per &lt;li&gt; of every filter they inspect.
    /// <para>
    /// Sound because RimWorld combines every mod's Defs into ONE XmlDocument before any patch runs
    /// (LoadedModManager.LoadModXML then CombineIntoUnifiedXML, both ahead of ApplyPatches), so the index
    /// sees every statically declared category. A category created BY a patch is the only thing it can
    /// miss, and one created by a later-loading mod is equally invisible to a live scan - the index is
    /// never worse than re-querying.
    /// </para>
    /// </summary>
    public static Dictionary<string, string> CategoryParentByDefName;

    /// <summary>
    /// defNames of every ThingDef in the unified patch document. Lets a patch operation tell whether one of
    /// our own defs is actually present before writing it into another mod's filter - the New Harvest
    /// modules are separate mods, so most of <see cref="SharedConstants.Category.s_ownDefNamesByKind"/> is
    /// absent on any given install, and an unresolvable defName in &lt;thingDefs&gt; costs one
    /// cross-reference error per occurrence. Same lifetime and same soundness argument as
    /// <see cref="CategoryParentByDefName"/>.
    /// </summary>
    public static HashSet<string> ThingDefNamesInDocument;

    /// <summary>
    /// Every ABSTRACT RecipeDef in the unified patch document, mapped by its <c>Name</c> attribute, so a
    /// patch operation can walk a recipe's <c>ParentName</c> chain and ask what the merged def will end up
    /// declaring. Needed because patches run BEFORE <see cref="XmlInheritance"/> resolves anything, so a
    /// node inherited from an abstract parent is simply not there yet to be found on the child.
    /// See <see cref="PatchOperationPathedExtended.DeclaresElementInAncestry"/>. Same lifetime and same
    /// soundness argument as <see cref="CategoryParentByDefName"/>.
    /// </summary>
    public static Dictionary<string, XmlNode> AbstractRecipeDefsByName;

    /// <summary>
    /// Drops every cache the settings layer owns. Called once from <see cref="Bootstrap.Initialize"/>
    /// after boot actions finish - by then the XML-phase data is dead and the menu has never opened, so
    /// all three tiers can go at once. Everything here rebuilds lazily; nothing is load-bearing.
    /// </summary>
    internal static void ClearAllCaches()
    {
        ClearTempForXmlCaches();
        ClearTempCaches();
        ClearMenuSessionCaches();
    }

    /// <summary>
    /// Stuff that is no longer needed after game is started (Xml data). The two document indexes are the
    /// load-bearing ones here: each holds a string per Def in the whole load order, and
    /// <see cref="XmlNodeCache"/> would pin the entire patched XmlDocument if a block ever leaked one.
    /// </summary>
    internal static void ClearTempForXmlCaches()
    {
        ModAddedCategoryDictionary = null;
        ModAddedCategoryTypeCache = null;
        SettingFieldCache = null;
        XmlNodeCache = null;
        CategoryParentByDefName = null;
        ThingDefNamesInDocument = null;
        AbstractRecipeDefsByName = null; // Holds live XmlNodes - would pin the patched document.
        CategoryAssignments.Invalidate(); // Free the lookup index built during XML patching.
    }

    /// <summary>
    /// Various temporary caches that can be cleared when no longer needed.
    /// </summary>
    internal static void ClearTempCaches()
    {
        CategoryClassifier.ClearCache();
    }

    /// <summary>
    /// Stuff that is no longer needed after menu is closed, and will be re-built if menu is opened again.
    /// Most of these hold translated strings, measured text widths and card heights, which depend on the
    /// active language and font metrics. Clearing per menu session is why none of them need language-change
    /// invalidation of their own: the language can only be switched from the main menu (Dialog_Options
    /// rejects it while a game is running), so any cache built by a settings session has already been
    /// dropped by the time a new language is picked. Nulling order matters at the end: the tab list is
    /// released last so the caches above it can still walk it while clearing.
    /// </summary>
    internal static void ClearMenuSessionCaches()
    {
        CategoryAssignments.FlushEditorData(); // Backstop - the editor dialog flushes on close itself.
        UITextureCache.ClearCache();
        UIBufferCache.ClearCache();
        DefUtility.ThingDefs.ClearCache();
        s_settingLabelCache.Clear(); // Translated-label cache.
        s_categoryCardStrings = null; // Per-card labels/tooltips/icon path (args make them uncacheable by name).
        s_fallColorTreeEntries = null;
        s_materialEntries = null;
        s_commonalityEntries = null;
        s_commonalityLabels = null;
        s_commonalityTooltips = null;
        s_commonalityDisabledTooltips = null;
        s_defaultColorLabel = null;
        s_currentColorLabel = null;
        s_flourTooltipPercent = -1f; // Sentinel - the tooltips also self-rebuild when the slider/toggle moves.
        s_commonalityLabelWidth = -1f;
        s_materialDropdownWidth = -1f;
        s_materialSwitchWidth = -1f;
        s_materialResetWidth = -1f;
        Settings?.ResetMaterialPickerSession(); // Picker session state (not a cache) - stale buffers would be committed over the real color on reopen.
        // Text.LineHeight-dependent card heights - session-scoped like the width caches above.
        s_nourishedCardHeight = -1f;
        s_truffleCardHeight = -1f;
        s_commonalityHeaderHeight = -1f; // Measured in GameFont.Medium; feeds s_commonalityCardHeight below.
        s_commonalityCardHeight = -1f;
        ClearTabLabelCaps();
        s_tabHasContentInt = null;
        s_tabsInt = null; // Must be nulled last so prior caches can utilize.
    }

    // Probably extraneous, but why not.
    internal static void ClearTabLabelCaps()
    {
        if (s_tabsInt == null)
            return;

        foreach (var tab in s_tabsInt)
        {
            tab.ClearCachedData();
        }
    }

    // internal static void MaintainSize<TKey, TValue>(IDictionary<TKey, TValue> cache, int maxSize = 200, int frameInterval = 10000, int intervalSize = 25)
    // {
    //     if (cache != null && (cache.Count > maxSize || (Time.frameCount % frameInterval == 0 && cache.Count > intervalSize)))
    //     {    
    //         cache.Clear();
    //     }
    // }

    // internal static void MaintainSize<T>(ICollection<T> cache, int maxSize = 200, int frameInterval = 10000, int intervalSize = 25)
    // {
    //     if (cache != null && (cache.Count > maxSize || (Time.frameCount % frameInterval == 0 && cache.Count > intervalSize)))
    //     {    
    //         cache.Clear();
    //     }
    // }
}