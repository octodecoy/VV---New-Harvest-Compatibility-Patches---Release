namespace NewHarvestPatches;

/// <summary>
/// Adds our own raw produce to a third-party ThingFilter's &lt;thingDefs&gt; list, but only when that filter
/// already refers to an equivalent category from some other mod. The sibling of
/// <see cref="AddCategoryToFilter"/> and gated by the identical test
/// (<see cref="PatchOperationPathedExtended.EvaluateCategoriesNode"/>), so the two cannot disagree about
/// which foreign categories count - they used to, because the def-injection half was driven by a literal
/// list of category names in xpath that omitted the name heuristics in
/// <see cref="SharedConstants.Category.NameMatchesKind"/>.
/// <para>
/// Both halves are needed. <see cref="AddCategoryToFilter"/> only helps while the kind's merge setting is
/// ON - that is what puts our defs inside the dummy category it adds. With merge off the dummy is empty and
/// unparented, so the filter reaches nothing through it, and naming our defs outright is the only way a
/// third-party bench still accepts them. That is why an already-present dummy category does NOT suppress
/// this operation.
/// </para>
/// <para>
/// XML: xpath (must select &lt;categories&gt; nodes, as AddCategoryToFilter), categoryType, and an optional
/// value wrapper whose child &lt;li&gt; entries override the def list. Without value the list comes from
/// <see cref="SharedConstants.Category.s_ownDefNamesByKind"/> for that kind, which is the point of the
/// operation; override it only when a kind's full membership is wrong for the target filters - AnimalFoods
/// does, because its entry includes crafted feeds that must never become ingredients. Def-list resolution
/// is <see cref="PatchOperationPathedExtended.ResolveDefNames"/>, shared with
/// <see cref="AddDefsWhereDefPresent"/>.
/// </para>
/// </summary>
internal class AddOwnDefsToCategoryFilters : PatchOperationPathedExtended
{
    private readonly string categoryType = null;
    private readonly XmlContainer value = null;

    protected override bool ApplyWorker(XmlDocument xml)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(categoryType))
            {
                LogMessage(() => "AddOwnDefsToCategoryFilters requires a <categoryType>.", LogMessageType.Error);
                return false;
            }

            // Resolved before the xpath runs: on an install with none of the New Harvest modules there is
            // nothing to inject, and selecting every filter in the load order to discover that is wasteful.
            List<string> defNames = ResolveDefNames(xml, value, categoryType);
            if (defNames.Count == 0)
            {
                LogMessage(() => $"No installed defs to add for category type [{categoryType}].");
                return false;
            }

            if (!PreCheck(xpath, xml))
                return false;

            if (nodes[0].Name != "categories")
            {
                LogMessage(() => $"xpath [{xpath}] must select <categories> nodes, got <{nodes[0].Name}>.", LogMessageType.Error);
                return false;
            }

            bool modified = false;
            foreach (XmlNode categoriesNode in nodes)
            {
                if (!EvaluateCategoriesNode(xml, categoriesNode, categoryType, offeredDefName: null).IsEvidence)
                    continue;

                // The filter itself - <categories> and <thingDefs> are siblings under it.
                XmlNode filterNode = categoriesNode.ParentNode;
                if (filterNode == null)
                    continue;

                modified |= AddDefNamesToFilter(filterNode, defNames);
            }

            return modified;
        }
        catch (Exception ex)
        {
            LogException(ex, ex.TargetSite, optMsg: $"{xpath}");
            return false;
        }
    }

    /// <summary>
    /// Appends the missing defNames to the filter's &lt;thingDefs&gt;, creating that element only when there
    /// is something to put in it - a filter whose defs are all present already must come out of this
    /// byte-identical. Existing entries are matched on defName text, so this never duplicates one another
    /// mod (or an earlier pass of ours) already wrote.
    /// </summary>
    private bool AddDefNamesToFilter(XmlNode filterNode, List<string> defNames)
    {
        XmlNode thingDefsNode = filterNode["thingDefs"];

        HashSet<string> existing = [];
        if (thingDefsNode != null)
        {
            foreach (XmlNode liNode in thingDefsNode.ChildNodes)
            {
                existing.Add(liNode.InnerText);
            }
        }

        List<string> missing = [.. defNames.Where(defName => !existing.Contains(defName))];
        if (missing.Count == 0)
            return false;

        if (thingDefsNode == null)
        {
            thingDefsNode = filterNode.OwnerDocument.CreateElement("thingDefs");
            filterNode.AppendChild(thingDefsNode);
        }

        foreach (string defName in missing)
        {
            XmlNode newLiNode = thingDefsNode.OwnerDocument.CreateElement("li");
            newLiNode.InnerText = defName;
            thingDefsNode.AppendChild(newLiNode);
        }

        LogMessage(() => $"Added [{string.Join(", ", missing)}] to [{(Settings.Logging ? GetFullPathWithDefName(filterNode) : "")}].");
        return true;
    }
}
