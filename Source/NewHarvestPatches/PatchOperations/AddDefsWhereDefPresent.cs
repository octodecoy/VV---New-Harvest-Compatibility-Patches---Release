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
/// WIDENING A RECIPE INGREDIENT SLOT IS NOT A NEUTRAL ACT, which is what most of this class is about.
/// <see cref="RimWorld.IngredientCount.IsFixedIngredient"/> is <c>filter.AllowedDefCount == 1</c>, so adding
/// our defs to an <c>ingredients/li/filter/thingDefs</c> that held one def flips that slot from fixed to
/// non-fixed. A fixed slot bypasses the recipe's <c>fixedIngredientFilter</c> entirely - which is why most
/// recipes never bother declaring a meaningful one - and the moment the slot stops being fixed that filter
/// starts being consulted, rejecting the very anchor def that used to satisfy the slot on its own. Nothing
/// errors; the recipe just quietly stops being craftable.
/// </para>
/// <para>
/// The compensation for that is not optional and is therefore no longer spelled by an XML flag: whenever
/// this operation widens an ingredient slot's &lt;thingDefs&gt;, it also makes the owning recipe's
/// <c>fixedIngredientFilter</c> allow the anchor plus everything just added, creating that node when the
/// recipe has none. Creating it is always safe - <c>RecipeDef.fixedIngredientFilter</c> is field-initialized
/// to an empty <see cref="Verse.ThingFilter"/> with no fallback behaviour attached to its absence, so the
/// node can only ever widen what is allowed - and appending is always safe, because RimWorld's inheritance
/// APPENDS a child def's &lt;li&gt; entries into the parent's list rather than replacing it
/// (<c>XmlInheritance.RecursiveNodeCopyOverwriteElements</c>).
/// </para>
/// <para>
/// <c>defaultIngredientFilter</c> is the exception, and the one node this operation will not create.
/// <c>RecipeDef.ResolveReferences</c> fills a null <c>defaultIngredientFilter</c> with a COPY OF THE WHOLE
/// <c>fixedIngredientFilter</c>; <see cref="RimWorld.Bill"/>'s constructor then copies that into the bill's
/// own ingredient filter. Writing the node suppresses that substitution, so a
/// <c>defaultIngredientFilter</c> created holding only our defs strips every other ingredient the recipe
/// needs out of the bill - a peg-leg surgery loses the medicine half of its recipe and reports a medicine
/// restriction it does not have. So the mirror only ever backfills a <c>defaultIngredientFilter</c> that the
/// recipe or one of its ancestors already declares (<see cref="DeclaresElementInAncestry"/>); where none is
/// declared, vanilla's copy of the already-compensated <c>fixedIngredientFilter</c> is both correct and
/// better than anything written here.
/// </para>
/// <para>
/// <c>mirrorFixedIngredientFilter</c>/<c>mirrorDefaultIngredientFilter</c> survive as statements of INTENT -
/// "propagate these defs into that filter as well", which is what a <c>disallowedThingDefs</c> widening
/// wants - and no longer carry any safety responsibility. Both remain subject to the two rules above, and to
/// the refusal to write the anchor into any disallow list. A recipe this operation does not widen must come
/// out byte-identical.
/// </para>
/// </summary>
internal class AddDefsWhereDefPresent : PatchOperationPathedExtended
{
    private readonly string anchor = null;
    private readonly string categoryType = null;
    private readonly XmlContainer value = null;
    private readonly bool mirrorFixedIngredientFilter = false;
    private readonly bool mirrorDefaultIngredientFilter = false;

    private const string FixedFilterName = "fixedIngredientFilter";
    private const string DefaultFilterName = "defaultIngredientFilter";
    private const string AllowListName = "thingDefs";
    private const string DisallowListName = "disallowedThingDefs";

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

                List<string> addable = WithoutSelfDisallowedAnchor(listNode, defNames);
                if (addable.Count == 0)
                    continue;

                bool widensIngredientSlot = IsIngredientSlotAllowList(listNode);
                bool needsRecipe = widensIngredientSlot || mirrorFixedIngredientFilter || mirrorDefaultIngredientFilter;
                XmlNode recipeDefNode = needsRecipe ? FindAncestor(listNode, "RecipeDef") : null;

                // Atomic per recipe: a slot that cannot be compensated is left alone rather than widened
                // into an unsatisfiable state. Only reachable if an xpath points this at an ingredients
                // list outside a RecipeDef, which nothing can currently do.
                if (widensIngredientSlot && recipeDefNode == null)
                {
                    LogMessage(
                        () => $"Skipped widening [{GetFullPathWithDefName(listNode)}] - no owning RecipeDef to compensate.",
                        LogMessageType.Warning);
                    continue;
                }

                // Compensation runs even when this pass adds nothing new: the entries may have been written
                // by an earlier operation carrying the same value list, which leaves the recipe just as
                // uncompensated as if we had written them ourselves.
                modified |= AddMissingDefNames(listNode, addable);

                if (recipeDefNode == null)
                    continue;

                if (widensIngredientSlot)
                    modified |= MirrorToFilter(xml, recipeDefNode, FixedFilterName, AllowListName, addable, backfillAnchor: true);

                modified |= MirrorByIntent(xml, recipeDefNode, listNode.Name, addable, alreadyMirroredFixedAllowList: widensIngredientSlot);
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
    /// Whether <paramref name="listNode"/> is the &lt;thingDefs&gt; of a recipe ingredient slot - the one
    /// shape whose widening can flip <see cref="RimWorld.IngredientCount.IsFixedIngredient"/> and so demands
    /// the fixedIngredientFilter compensation. Anything else (a storage filter, a disallow list, a filter
    /// that is already the recipe's fixed/default one) either cannot flip a slot or cannot be reached from
    /// a slot at all.
    /// </summary>
    private static bool IsIngredientSlotAllowList(XmlNode listNode)
    {
        return listNode.Name == AllowListName
            && listNode.ParentNode?.Name == "filter"
            && listNode.ParentNode.ParentNode?.Name == "li"
            && listNode.ParentNode.ParentNode.ParentNode?.Name == "ingredients";
    }

    /// <summary>
    /// Drops the anchor from the defNames destined for a DISALLOW list. Writing it there would forbid the
    /// very def whose presence qualified the list, which can only ever take an ingredient slot away.
    /// An XML author asking for that has made a mistake worth reporting, not honouring.
    /// </summary>
    private List<string> WithoutSelfDisallowedAnchor(XmlNode listNode, List<string> defNames)
    {
        if (listNode.Name != DisallowListName || !defNames.Contains(anchor))
            return defNames;

        LogMessage(
            () => $"Refused to disallow anchor [{anchor}] in [{GetFullPathWithDefName(listNode)}] - it is what qualified the list.",
            LogMessageType.Error);

        return [.. defNames.Where(defName => defName != anchor)];
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
    /// The opt-in half of the mirroring: propagates a widened list into whichever of the recipe's
    /// fixed/default ingredient filters the XML asked for. Purely about intent - the satisfiability
    /// compensation has already run by the time this is called, and
    /// <paramref name="alreadyMirroredFixedAllowList"/> just avoids repeating it.
    /// Returns whether anything was written.
    /// </summary>
    private bool MirrorByIntent(
        XmlDocument xml, XmlNode recipeDefNode, string listName, List<string> defNames, bool alreadyMirroredFixedAllowList)
    {
        // An anchor backfilled into a disallow list would disallow it - the opposite of the point.
        bool backfillAnchor = listName == AllowListName;
        bool modified = false;

        if (mirrorFixedIngredientFilter && !(alreadyMirroredFixedAllowList && listName == AllowListName))
            modified |= MirrorToFilter(xml, recipeDefNode, FixedFilterName, listName, defNames, backfillAnchor);

        if (mirrorDefaultIngredientFilter)
            modified |= MirrorToFilter(xml, recipeDefNode, DefaultFilterName, listName, defNames, backfillAnchor);

        return modified;
    }

    /// <summary>
    /// Writes <paramref name="defNames"/> (plus the anchor, when <paramref name="backfillAnchor"/>) into
    /// <paramref name="filterName"/>'s &lt;<paramref name="listName"/>&gt; on the owning recipe, creating
    /// the nodes only when there is something new to put in them so an already-covered recipe comes out
    /// byte-identical. Returns whether the document was written to - the caller must fold that into its own
    /// result, because a pass whose only change was this mirroring still counts as a successful patch, and a
    /// <see cref="Verse.PatchOperationSequence"/> abandons every remaining operation after one reports failure.
    /// <para>
    /// <c>defaultIngredientFilter</c> is never CREATED here, only backfilled: see the class doc for why its
    /// absence is load-bearing. Everything else may be created freely.
    /// </para>
    /// </summary>
    private bool MirrorToFilter(
        XmlDocument xml, XmlNode recipeDefNode, string filterName, string listName, List<string> defNames, bool backfillAnchor)
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
            return false;

        if (filterNode == null)
        {
            if (!MayCreateFilter(xml, recipeDefNode, filterName))
                return false;

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
        return true;
    }

    /// <summary>
    /// Whether a filter node absent from this recipe may be created. Only
    /// <c>defaultIngredientFilter</c> ever answers no, and only when nothing in the recipe's ancestry
    /// declares one - creating it there would suppress the copy-of-fixedIngredientFilter that
    /// <c>RecipeDef.ResolveReferences</c> substitutes for a null field, which is strictly better than what
    /// this operation could write.
    /// </summary>
    private static bool MayCreateFilter(XmlDocument xml, XmlNode recipeDefNode, string filterName)
    {
        if (filterName != DefaultFilterName)
            return true;

        if (DeclaresElementInAncestry(xml, recipeDefNode, DefaultFilterName))
            return true;

        LogMessage(() => $"Left <{DefaultFilterName}> uncreated for [{(Settings.Logging ? GetFullPathWithDefName(recipeDefNode) : "")}] - vanilla copies the fixedIngredientFilter into it.");
        return false;
    }
}
