namespace NewHarvestPatches;

/// <summary>
/// Adds or replaces a node next to each xpath-matched node, using that node's InnerText as the value.
/// Optionally add/subtract/multiply/divide the copied value first.
/// Created to add plants to biomes with specific plants, and with that plant's commonality, so the
/// injected value tracks whatever the host mod chose rather than a hardcoded number that would be wrong
/// for every biome. XML: xpath (selects the SOURCE node), node (sibling name to write), operation, value.
/// </summary>
internal class AddOrReplaceSiblingWithCopiedFloat : PatchOperationPathedExtended
{
    private readonly string node = null;
    private readonly string operation = null;
    private readonly string value = null;

    protected override bool ApplyWorker(XmlDocument xml)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(node))
                return false;

            if (!PreCheck(xpath, xml))
                return false;

            bool modified = false;
            foreach (XmlNode targetNode in nodes)
            {
                // Get the target node's InnerText (the one we're copying from)
                string targetValue = targetNode.InnerText;
                if (string.IsNullOrWhiteSpace(targetValue))
                    continue;

                // Apply operation if specified
                if (!string.IsNullOrWhiteSpace(operation) && !string.IsNullOrWhiteSpace(value))
                {
                    targetValue = ApplyOperation(targetValue, operation, value);
                    if (targetValue == null)
                        continue; // Skip if operation failed
                }

                // Get the parent node where we'll add/replace the sibling
                XmlNode parentNode = targetNode.ParentNode;
                if (parentNode == null)
                    continue;

                XmlNode existingNode = parentNode[node];
                if (existingNode != null)
                {
                    // Replace existing node's InnerText
                    existingNode.InnerText = targetValue;
                    modified = true;
                }
                else
                {
                    // Add new node as sibling
                    XmlNode newNode = xml.CreateElement(node);
                    newNode.InnerText = targetValue;
                    parentNode.AppendChild(newNode);
                    modified = true;
                }

                LogMessage(() => $"xpath='{xpath}', node='{node}', value='{targetValue}'");
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