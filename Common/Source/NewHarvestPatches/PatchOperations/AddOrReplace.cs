namespace NewHarvestPatches;

/// <summary>
/// Adds each child of &lt;value&gt; to every matched node, replacing an equivalent existing child instead
/// of appending a duplicate. Vanilla forces a choice between PatchOperationAdd (duplicates if present)
/// and PatchOperationReplace (fails if absent); this handles both, so one patch works whether or not
/// another mod already wrote the element.
/// XML: xpath, value (wrapper whose CHILDREN are inserted, not the wrapper itself); equivalence is
/// governed by the inherited compare / checkAttributes fields. cacheKey selects from a
/// NewHarvestPatches.CacheNodes block instead of xpath (see PatchOperationPathedExtended).
/// With addOnly, an equivalent existing child is left alone instead of replaced - use this whenever
/// the matched node's element identity alone (the default compare=Name) could otherwise match and
/// overwrite content owned by another mod, e.g. inserting an empty &lt;thingDefs/&gt; placeholder.
/// </summary>
internal class AddOrReplace : PatchOperationPathedExtended
{
    private readonly XmlContainer value = null;
    private readonly bool addOnly = false;

    protected override bool ApplyWorker(XmlDocument xml)
    {
        try
        {
            if (value == null)
                return false;

            XmlNode node = value.node;
            if (node == null)
                return false;

            if (!PreCheck(xpath, xml))
                return false;

            bool modified = false;
            XmlNode foundNode = null;
            foreach (XmlNode xmlNode in nodes)
            {
                string path = "";
                if (Settings.Logging)
                {
                    path = GetFullPathWithDefName(xmlNode);
                }

                foreach (XmlNode addNode in node.ChildNodes)
                {
                    if (ContainsNode(xmlNode, addNode, ref foundNode))
                    {
                        if (addOnly)
                        {
                            LogMessage(() => $"Skipped node <{addNode.Name}> for <{path}> - equivalent already present and addOnly is set.");
                            continue;
                        }

                        // Replace
                        XmlNode importedNode = xmlNode.OwnerDocument.ImportNode(addNode, true);
                        xmlNode.ReplaceChild(importedNode, foundNode);
                        modified = true;
                        LogMessage(() => $"Replaced node <{foundNode.Name}> (old value: {foundNode.InnerXml}) with <{addNode.Name}> (new value: {addNode.InnerXml}) for <{path}>.");
                    }
                    else
                    {
                        // Add
                        xmlNode.AppendChild(xmlNode.OwnerDocument.ImportNode(addNode, true));
                        modified = true;
                        LogMessage(() => $"Added node <{addNode.Name}> (value: {addNode.InnerXml}) to {path}.");
                    }
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