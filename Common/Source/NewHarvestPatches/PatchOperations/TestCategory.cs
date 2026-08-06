namespace NewHarvestPatches;

/// <summary>
/// Detects whether any loaded mod defines a ThingCategoryDef of the given type, branching to
/// caseTrue/caseFalse. Doubles as the DISCOVERY pass: every match is recorded into
/// ModAddedCategoryDictionary, which <c>CategoryApplier.SnapshotModAddedCategories</c> copies out before
/// the XML-phase caches are dropped, and which the classifier and sync-target selection both consume
/// after load. So this operation feeds post-load C# as much as it drives XML branching - the patch phase
/// is the only point where the raw document can be scanned for categories that no def references yet.
/// XML: xpath (must select ThingCategoryDef nodes), categoryType.
/// </summary>
internal class TestCategory : PatchOperationPathedExtended
{
    private readonly string categoryType = null;

    /// <summary>
    /// Records every match before branching - the cache is populated for its own sake, not only when the
    /// branch is taken. Uses the same name/exclusion tests as the rest of the category machinery.
    /// </summary>
    protected override bool ApplyWorker(XmlDocument xml)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(categoryType))
                return false;

            if (!PreCheck(xpath, xml))
                return false;

            if (nodes[0].Name != "ThingCategoryDef")
                return false;

            bool flag = false;
            foreach (XmlNode thingCategoryDefNode in nodes)
            {
                string thingCategoryDefName = thingCategoryDefNode.SelectSingleNode("defName")?.InnerText;
                if (string.IsNullOrWhiteSpace(thingCategoryDefName))
                    continue;

                bool match = TextMatchesForCategory(thingCategoryDefName, categoryType);
                bool excluded = match && IsExcludedCategory(thingCategoryDefName);

                if (match && !excluded)
                {
                    ModAddedCategoryTypeCache ??= [];
                    ModAddedCategoryDictionary ??= [];

                    flag = true;
                    ModAddedCategoryTypeCache.Add(categoryType);
                    if (!ModAddedCategoryDictionary.TryGetValue(categoryType, out var cacheSet))
                    {
                        cacheSet = [];
                        ModAddedCategoryDictionary[categoryType] = cacheSet;
                    }
                    if (cacheSet.Add(thingCategoryDefName))
                    {
                        LogMessage(() => $"Matched ThingCategoryDef [{thingCategoryDefName}] for type [{categoryType}]");
                    }
                }
            }

            if (flag)
            {
                if (caseTrue != null)
                    return caseTrue.Apply(xml);
            }
            else if (caseFalse != null)
                return caseFalse.Apply(xml);
            return true;

        }
        catch (Exception ex)
        {
            LogException(ex, ex.TargetSite, optMsg: $"{xpath}");
            return false;
        }
    }
}