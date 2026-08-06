namespace NewHarvestPatches;

/// <summary>
/// Adds our dummy category to a third-party ThingFilter's &lt;categories&gt; list, but only when that
/// filter already accepts an equivalent category from some other mod. This is what keeps other mods'
/// benches and storage working after our defs move into the dummy category: a filter that used to reach
/// them through, say, "VCE_Fruit" is taught about our fruit category too, instead of silently losing
/// every def we relocated. A filter that never referenced a matching category is left alone - the point is
/// to preserve existing behaviour, not to widen filters that were deliberately narrow.
/// <para>
/// XML: xpath (must select &lt;categories&gt; nodes), categoryType. The xpath needs no
/// <c>[not(li="VV_NHCP_DummyCategory_…")]</c> predicate and should not carry one: the dummy defName is
/// derived from categoryType here (<see cref="PatchOperationPathedExtended.GetCategoryName"/>) and a filter
/// that already lists it is recognised as such below, so writing it in XML only duplicates C# knowledge.
/// That leaves the xpath free to be a single union over every filter location, matching the sibling
/// <see cref="AddOwnDefsToCategoryFilters"/> in the same block.
/// </para>
/// </summary>
internal class AddCategoryToFilter : PatchOperationPathedExtended
{
    private readonly string categoryType = null;

    /// <summary>
    /// The per-filter decision is <see cref="PatchOperationPathedExtended.EvaluateCategoriesNode"/>'s -
    /// shared with the operations that inject our own defs, so both act on the same idea of what a
    /// third-party category of this kind is. Returns true for an already-present category rather than
    /// skipping it, which is what lets the xpath drop the "does not already list our dummy" predicate.
    /// </summary>
    protected override bool ApplyWorker(XmlDocument xml)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(categoryType))
                return false;

            string categoryDefName = GetCategoryName(xml, categoryType);
            if (string.IsNullOrEmpty(categoryDefName))
                return false;

            if (!PreCheck(xpath, xml))
                return false;

            if (nodes[0].Name != "categories")
                return false;

            bool modified = false;

            foreach (XmlNode categoriesNode in nodes)
            {
                CategoryEvidence evidence = EvaluateCategoriesNode(xml, categoriesNode, categoryType, categoryDefName);

                if (evidence.IsEvidence && !evidence.AlreadyPresent)
                {
                    XmlNode newLiNode = categoriesNode.OwnerDocument.CreateElement("li");
                    newLiNode.InnerText = categoryDefName;
                    categoriesNode.AppendChild(newLiNode);

                    modified = true;

                    LogMessage(() => $"Added category [{categoryDefName}] to [{(Settings.Logging ? GetFullPathWithDefName(categoriesNode) : "")}].");
                }
                else if (evidence.AlreadyPresent)
                {
                    modified = true;
                }
            }

            return modified;
        }
        catch (Exception ex)
        {
            LogException(ex, ex.TargetSite, optMsg: $"{xpath}");
            return false;
        }
    }
}