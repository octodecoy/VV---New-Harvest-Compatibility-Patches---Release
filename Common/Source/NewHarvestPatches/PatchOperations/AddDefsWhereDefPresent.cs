namespace NewHarvestPatches;

/// <summary>
/// Adds our own defNames to any matched list node (&lt;thingDefs&gt;, &lt;disallowedThingDefs&gt;, ...) that
/// already contains a given anchor defName - "wherever Hay is listed, also list our hays" - without the
/// per-location sentinel dedupe (<c>[not(li="OneOfOurDefs")]</c>) vanilla xpath needs to approximate the same
/// thing, and without silently duplicating an entry a third-party mod already wrote.
/// <para>
/// XML: xpath (selects the list nodes to scan), anchor (required - the defName that must already be present
/// for a list to qualify), and either value (child &lt;li&gt; entries) or categoryType (pulls this kind's
/// seed list) to supply the defNames to add. Def-list resolution is
/// <see cref="PatchOperationPathedExtended.ResolveDefNames"/>, shared with
/// <see cref="AddOwnDefsToCategoryFilters"/>.
/// </para>
/// <para>
/// <c>mirrorFixedIngredientFilter</c>/<c>mirrorDefaultIngredientFilter</c> exist because widening a RecipeDef ingredient slot from one allowed def to
/// many has a trap: <see cref="RimWorld.IngredientCount.IsFixedIngredient"/> is
/// <c>filter.AllowedDefCount == 1</c>, so adding our defs to
/// <c>ingredients/li/filter/thingDefs</c> flips that slot from fixed to non-fixed, and the slot's own
/// <c>fixedIngredientFilter</c> - never null (field-initialized), but bypassed entirely while the slot is
/// fixed, so most recipes never bother declaring one - starts being CONSULTED, and an implicit empty one
/// rejects the anchor def that used to satisfy the slot on its own. <c>defaultIngredientFilter</c> has the
/// same shape of trap: it CAN be null, in which case a bill falls back to whatever the ingredient's own
/// filter allows (anchor included); once this operation creates one, that fallback stops applying and an
/// empty list would default the anchor to unchecked in the bill UI. The recipe silently loses the anchor as
/// an option, or as a default selection; nothing errors. Setting <c>mirrorFixedIngredientFilter</c> and/or
/// <c>mirrorDefaultIngredientFilter</c> makes a widened list also create/populate the matching list on the
/// owning RecipeDef's <c>fixedIngredientFilter</c> and/or <c>defaultIngredientFilter</c> respectively, AND
/// (only for a &lt;thingDefs&gt; list, never &lt;disallowedThingDefs&gt;) re-adds the anchor itself to
/// whichever filter(s) are mirrored, closing the trap there. Each is independently optional - mirror only
/// the filter(s) a recipe actually declares meaningful use of. Only recipes actually widened by this
/// operation are touched - one left alone by the anchor match must come out byte-identical.
/// </para>
/// </summary>
internal class AddDefsWhereDefPresent : PatchOperationPathedExtended
{
    private readonly string anchor = null;
    private readonly string categoryType = null;
    private readonly XmlContainer value = null;
    private readonly bool mirrorFixedIngredientFilter = false;
    private readonly bool mirrorDefaultIngredientFilter = false;

    protected override bool ApplyWorker(XmlDocument xml)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(anchor))
            {
                LogMessage(() => "AddDefsWhereDefPresent requires an <anchor>.", LogMessageType.Error);
                return false;
            }

            // Resolved before the xpath runs: on an install with none of the New Harvest modules there is
            // nothing to inject, and selecting every list in the load order to discover that is wasteful.
            List<string> defNames = ResolveDefNames(xml, value, categoryType);
            if (defNames.Count == 0)
            {
                LogMessage(() => $"No installed defs to add for anchor [{anchor}].");
                return false;
            }

            if (!PreCheck(xpath, xml))
                return false;

            bool modified = false;
            foreach (XmlNode listNode in nodes)
            {
                if (!ContainsAnchor(listNode, anchor))
                    continue;

                if (!AddMissingDefNames(listNode, defNames))
                    continue;

                modified = true;

                if (!mirrorFixedIngredientFilter && !mirrorDefaultIngredientFilter)
                    continue;

                XmlNode recipeDefNode = FindAncestor(listNode, "RecipeDef");
                if (recipeDefNode != null)
                    MirrorToRecipeFilters(recipeDefNode, listNode.Name, defNames);
            }

            return modified;
        }
        catch (Exception ex)
        {
            LogException(ex, ex.TargetSite, optMsg: $"{xpath}");
            return false;
        }
    }

    /// <summary>Whether any child &lt;li&gt; of <paramref name="listNode"/> already names <paramref name="anchor"/>.</summary>
    private static bool ContainsAnchor(XmlNode listNode, string anchor)
    {
        foreach (XmlNode liNode in listNode.ChildNodes)
        {
            if (liNode.InnerText == anchor)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Appends the missing defNames as &lt;li&gt; children of an EXISTING list node. Unlike
    /// <see cref="AddOwnDefsToCategoryFilters"/>'s equivalent, the node itself is never created here - it
    /// was matched by xpath, so it already exists.
    /// </summary>
    private bool AddMissingDefNames(XmlNode listNode, List<string> defNames)
    {
        HashSet<string> existing = [];
        foreach (XmlNode liNode in listNode.ChildNodes)
        {
            existing.Add(liNode.InnerText);
        }

        List<string> missing = [.. defNames.Where(defName => !existing.Contains(defName))];
        if (missing.Count == 0)
            return false;

        foreach (string defName in missing)
        {
            XmlNode newLiNode = listNode.OwnerDocument.CreateElement("li");
            newLiNode.InnerText = defName;
            listNode.AppendChild(newLiNode);
        }

        LogMessage(() => $"Added [{string.Join(", ", missing)}] to [{(Settings.Logging ? GetFullPathWithDefName(listNode) : "")}].");
        return true;
    }

    /// <summary>Walks up from <paramref name="node"/> to the nearest ancestor element named <paramref name="name"/>, or null.</summary>
    private static XmlNode FindAncestor(XmlNode node, string name)
    {
        XmlNode current = node.ParentNode;
        while (current != null && current.Name != name)
        {
            current = current.ParentNode;
        }
        return current;
    }

    /// <summary>
    /// Propagates a widened &lt;<paramref name="listName"/>&gt; into whichever of the owning recipe's
    /// fixedIngredientFilter/defaultIngredientFilter are enabled, so a slot that was satisfiable (and
    /// selected) by the anchor alone stays that way. See the class doc for why an enabled filter needs the
    /// anchor backfilled, and why only a &lt;thingDefs&gt; list gets it - backfilling the anchor into a
    /// disallow list would mean disallowing it, the opposite of the fix.
    /// </summary>
    private void MirrorToRecipeFilters(XmlNode recipeDefNode, string listName, List<string> defNames)
    {
        bool backfillAnchor = listName == "thingDefs";
        if (mirrorFixedIngredientFilter)
            MirrorToFilter(recipeDefNode, "fixedIngredientFilter", listName, defNames, backfillAnchor);
        if (mirrorDefaultIngredientFilter)
            MirrorToFilter(recipeDefNode, "defaultIngredientFilter", listName, defNames, backfillAnchor);
    }

    /// <summary>
    /// Creates <paramref name="filterName"/> and its &lt;<paramref name="listName"/>&gt; child only when
    /// there is something new to write into them, so a recipe already fully covered comes out
    /// byte-identical.
    /// </summary>
    private void MirrorToFilter(XmlNode recipeDefNode, string filterName, string listName, List<string> defNames, bool backfillAnchor)
    {
        List<string> toAdd = [.. defNames];
        if (backfillAnchor && !toAdd.Contains(anchor))
            toAdd.Add(anchor);

        XmlNode filterNode = recipeDefNode[filterName];
        XmlNode listNode = filterNode?[listName];

        HashSet<string> existing = [];
        if (listNode != null)
        {
            foreach (XmlNode liNode in listNode.ChildNodes)
            {
                existing.Add(liNode.InnerText);
            }
        }

        List<string> missing = [.. toAdd.Where(defName => !existing.Contains(defName))];
        if (missing.Count == 0)
            return;

        if (filterNode == null)
        {
            filterNode = recipeDefNode.OwnerDocument.CreateElement(filterName);
            recipeDefNode.AppendChild(filterNode);
        }

        if (listNode == null)
        {
            listNode = filterNode.OwnerDocument.CreateElement(listName);
            filterNode.AppendChild(listNode);
        }

        foreach (string defName in missing)
        {
            XmlNode newLiNode = listNode.OwnerDocument.CreateElement("li");
            newLiNode.InnerText = defName;
            listNode.AppendChild(newLiNode);
        }

        LogMessage(() => $"Mirrored [{string.Join(", ", missing)}] into <{filterName}/{listName}> for [{(Settings.Logging ? GetFullPathWithDefName(recipeDefNode) : "")}].");
    }
}
