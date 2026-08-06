namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    /// <summary>
    /// Reconciles every def-backed settings collection with the defs that actually loaded this session.
    /// Must run after the DefDatabase is populated: scribed entries are keyed by defName, so a mod
    /// added or removed since the last save leaves stale keys and missing ones that only a def lookup
    /// can resolve. Each Initialize* method drops invalid keys, then fills in defaults for new defs.
    /// </summary>
    public void TryInitializeCollections()
    {
        ActionRunner.Run(nameof(NewHarvestPatchesModSettings), nameof(TryInitializeCollections), InitializeCollections);
    }

    /// <summary>Body of <see cref="TryInitializeCollections"/>.</summary>
    private void InitializeCollections()
    {
        InitializeFuels();
        InitializeFallColorTrees();
        InitializeColorInfo();
        InitializeCategoryLabelInfo();
        CommonalityInfo.BuildCommonalityStats(ref StuffCommonality);

        // PatchOperations (e.g. ConditionalSetting) run during the XML patch phase, before defs
        // exist, and can force-build the EnabledSettings cache early with DefaultCommonality still
        // at its field default (0f) - making any nonzero CoreCommonality look like a user change.
        // That stale cache then survives until something else invalidates it. Force a rebuild here,
        // now that real def-derived defaults exist, so the first real read is correct.
        InvalidateEnabledSettings();
    }

    /// <summary>
    /// Drops every entry whose key no longer resolves to a ThingDef, or whose def no longer qualifies -
    /// a mod uninstalled since the last save leaves keys that would otherwise be persisted forever and
    /// silently re-enable a setting if that mod ever returned.
    /// </summary>
    /// <param name="dict">Scribed defName-keyed settings collection, modified in place.</param>
    /// <param name="validator">Membership test for the def, e.g. "is still a fuel".</param>
    public static void RemoveInvalidThingDefs<T>(IDictionary<string, T> dict, Func<ThingDef, bool> validator)
    {
        foreach (var key in dict.Keys.ToList())
        {
            if (string.IsNullOrWhiteSpace(key)
                || DefDatabase<ThingDef>.GetNamedSilentFail(key) is not { } def
                || !validator(def))
            {
                dict.Remove(key);
            }
        }
    }

    /// <summary>
    /// Seeds a default for every def with no entry yet, leaving existing entries alone so a newly
    /// installed mod's defs appear at their defaults without resetting the user's stored choices.
    /// </summary>
    /// <param name="action">Writes the default for one def; it owns the dictionary write, not this method.</param>
    public static void PopulateWithDefaultThingDefs<T>(IDictionary<string, T> dict, IEnumerable<ThingDef> defs, Action<ThingDef> action)
    {
        foreach (var def in defs)
        {
            if (!dict.ContainsKey(def.defName))
                action(def);
        }
    }
    
    private void InitializeFuels()
    {
        if (!IsSettingAvailable(nameof(FuelTypes)))
            return;

        RemoveInvalidThingDefs(FuelTypes, DefUtility.ThingDefs.FuelThingDefs.Contains);
        PopulateWithDefaultThingDefs(FuelTypes, DefUtility.ThingDefs.FuelThingDefs, def => FuelTypes[def.defName] = def.HasWoodDefName());
    } 

    private void InitializeFallColorTrees()
    {
        if (!IsSettingAvailable(nameof(FallColorTrees)))
            return;

        RemoveInvalidThingDefs(FallColorTrees, DefUtility.ThingDefs.DeciduousTreeDefs.Contains);
        PopulateWithDefaultThingDefs(FallColorTrees, DefUtility.ThingDefs.DeciduousTreeDefs, def => FallColorTrees[def.defName] = true);
    }

    /// <summary>
    /// Re-reads every <see cref="ColorInfo"/>'s Default* pair from the def, so a def whose color changed in
    /// a mod update reverts to the NEW color rather than the one recorded when the entry was first made.
    /// The scribed Default* it replaces is first used to decide whether New* is a real user choice or just
    /// the seed copied from the def; an uncustomized New* is re-seeded so it follows the def forward
    /// instead of being replayed onto it as a customization.
    /// A def with no stuff color is dropped entirely - there is nothing to revert to.
    /// </summary>
    private void InitializeColorInfo()
    {
        if (!IsSettingAvailable(nameof(MaterialColors)))
            return;

        if (DefUtility.ThingDefs.IndustrialResourceDefs.NullOrEmpty())
        {   
            MaterialColors.Clear();
            return;
        }

        RemoveInvalidThingDefs(MaterialColors, DefUtility.ThingDefs.IndustrialResourceDefs.ContainsKey);

        foreach (var kvp in DefUtility.ThingDefs.IndustrialResourceDefs)
        {
            var def = kvp.Key;
            var defName = def.defName;

            if (def.stuffProps?.color is not Color stuffColor)
            {
                MaterialColors.Remove(defName);
                LogMessage(() => $"Removed {defName} because it has no stuff color.", LogMessageType.Error);
                continue;
            }

            var thingColor = def.graphicData?.color ?? white;

            if (MaterialColors.TryGetValue(defName, out var existingInfo))
            {
                // Settled against the SCRIBED defaults - the def colors this entry was seeded from - so it
                // must happen before those get refreshed below.
                bool stuffCustomized = !existingInfo.NewStuffColor.IndistinguishableFromFast(existingInfo.DefaultStuffColor);
                bool thingCustomized = !existingInfo.NewThingColor.IndistinguishableFromFast(existingInfo.DefaultThingColor);

                existingInfo.DefaultStuffColor = stuffColor;
                existingInfo.DefaultThingColor = thingColor;

                // An uncustomized New* is only ever a copy of the def color, so it has to follow the def
                // forward. Leaving it stale made a def that GAINED a color read as "user wants the old
                // one" and MaterialColorChanger wrote that stale color straight back over the new one.
                if (!stuffCustomized)
                    existingInfo.NewStuffColor = stuffColor;
                if (!thingCustomized)
                    existingInfo.NewThingColor = thingColor;
            }
            else
            {
                MaterialColors[defName] = new ColorInfo
                {
                    DefaultStuffColor = stuffColor,
                    DefaultThingColor = thingColor,
                    NewStuffColor = stuffColor,
                    NewThingColor = thingColor
                };
            }
        }
    }

    /// <summary>
    /// Same pattern as <see cref="InitializeColorInfo"/> for renamed categories: OriginalCategoryLabel is
    /// unscribed and re-captured from the def every session, so "revert this category's name" always
    /// restores the label the current def set carries, not one baked in by an older version.
    /// <para>
    /// CurrentCategoryLabel means "the user renamed this", and an empty string means they did not - it is
    /// NOT seeded from the def. Seeding it gave every category a non-default scribed value on first launch,
    /// which <c>CategoryLabelInfo.SetCategoryLabels</c> then wrote back onto the def every boot: a label
    /// corrected in a mod update, or translated by a DefInjected file for a newly selected language, was
    /// silently overwritten by the string captured on the user's very first launch. Runs before
    /// <c>BootActions</c> pushes any rename onto the defs, so <c>category.label</c> here is still the
    /// pristine XML/DefInjected value both branches below compare against.
    /// </para>
    /// </summary>
    private void InitializeCategoryLabelInfo()
    {
        CategoryLabelCache ??= [];
        foreach (var category in DefUtility.ThingCategoryDefs.InternalThingCategoryDefs)
        {
            if (!CategoryLabelCache.TryGetValue(category.defName, out var categoryLabelInfo))
            {
                categoryLabelInfo = new CategoryLabelInfo
                {
                    OriginalCategoryLabel = category.label
                };
                CategoryLabelCache[category.defName] = categoryLabelInfo;
            }
            else
            {
                // Update original label in case it changed due to mod updates
                categoryLabelInfo.OriginalCategoryLabel = category.label;

                // Migration off the seeded-rename shape: a stored rename identical to the label the def
                // already carries grants nothing and only serves to pin it, so drop it back to "no rename".
                // A user who typed the current label by hand loses nothing - the result is the same string.
                if (categoryLabelInfo.CurrentCategoryLabel == category.label)
                    categoryLabelInfo.CurrentCategoryLabel = "";
            }
        }
    }
}