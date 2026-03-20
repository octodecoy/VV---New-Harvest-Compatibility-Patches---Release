namespace NewHarvestPatches
{
    // Loosely based on XmlExtensions.
    internal abstract class PatchOperationPathedExtended : PatchOperationPathed
    {
        private readonly bool selectSingleNode = false;
        protected List<XmlNode> nodes;
        private readonly bool checkAttributes = false;
        private readonly Compare compare = Compare.Name;
        protected readonly PatchOperation caseTrue = null;
        protected readonly PatchOperation caseFalse = null;

        protected virtual bool PreCheck(string xpath, XmlDocument xml)
        {
            if (xml == null || string.IsNullOrWhiteSpace(xpath))
                return false;

            if (selectSingleNode)
                nodes = [SelectSingleNode(xpath, xml)];
            else
                nodes = [.. SelectNodes(xpath, xml).Cast<XmlNode>()];

            if (nodes.NullOrEmpty() || nodes[0] == null)
                return false;

            return true;
        }

        protected virtual bool ContainsNode(XmlNode parent, XmlNode node, ref XmlNode foundNode)
        {
            XmlAttributeCollection attrs = node.Attributes;
            foreach (XmlNode childNode in parent.ChildNodes)
            {
                if (!NodesMatch(childNode, node, compare))
                    continue;

                if (!checkAttributes)
                {
                    foundNode = childNode;
                    return true;
                }

                XmlAttributeCollection attrsChild = childNode.Attributes;
                if (attrs == null && attrsChild == null)
                {
                    foundNode = childNode;
                    return true;
                }

                if (attrs != null && attrsChild != null && attrs.Count == attrsChild.Count)
                {
                    bool b = true;
                    foreach (XmlAttribute attr in attrs)
                    {
                        XmlNode attrChild = attrsChild.GetNamedItem(attr.Name);
                        if (attrChild == null)
                        {
                            b = false;
                            break;
                        }
                        if (attrChild.Value != attr.Value)
                        {
                            b = false;
                            break;
                        }
                    }
                    if (b)
                    {
                        foundNode = childNode;
                        return true;
                    }
                }
            }
            foundNode = null;
            return false;
        }

        protected static bool NodesMatch(XmlNode childNode, XmlNode node, Compare compare)
        {
            return compare switch
            {
                Compare.Name => childNode.Name == node.Name,
                Compare.InnerText => childNode.InnerText == node.InnerText,
                Compare.Both => childNode.Name == node.Name && childNode.InnerText == node.InnerText,
                _ => false,
            };
        }

        protected static XmlNode SelectSingleNode(string path, XmlDocument xml)
        {
            return xml.SelectSingleNode(path);
        }

        protected static XmlNodeList SelectNodes(string path, XmlDocument xml)
        {
            return xml.SelectNodes(path);
        }

        protected static XmlNode FindNodeByName(XmlNode parent, string nodeName)
        {
            foreach (XmlNode child in parent.ChildNodes)
            {
                if (child.Name == nodeName)
                    return child;
            }
            return null;
        }

        protected static string ApplyOperation(string targetValue, string operation, string operand)
        {
            try
            {
                if (!float.TryParse(targetValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float targetFloat) ||
                    !float.TryParse(operand, NumberStyles.Float, CultureInfo.InvariantCulture, out float operandFloat))
                {
                    return null;
                }

                float result = operation switch
                {
                    "+" => targetFloat + operandFloat,
                    "-" => targetFloat - operandFloat,
                    "*" => targetFloat * operandFloat,
                    "/" when operandFloat != 0 => targetFloat / operandFloat,
                    "/" => float.NaN, // Division by zero
                    _ => float.NaN
                };

                if (float.IsNaN(result) || float.IsInfinity(result))
                    return null;

                return result.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        protected static bool TextMatchesForCategory(string defName, string categoryWaitingToAdd)
        {
            return categoryWaitingToAdd switch
            {
                nameof(Category.Type.AnimalFoods) => defName == "AnimalFeed" ||
                                                     defName == "Feed" ||
                                                     defName == "DavaiAnimalFood" ||
                                                     defName.ContainsIgnoreCase("Animal") ||
                                                     defName.ContainsIgnoreCase("Feed"),
                nameof(Category.Type.Fruit) => defName == "VCE_Fruit" ||
                                                defName == "FruitFoodRaw" ||
                                                defName == "RC2_FruitsRaw" ||
                                                defName.ContainsIgnoreCase(Category.Type.Fruit),
                nameof(Category.Type.Grains) => defName == "RC2_GrainsRaw" ||
                                                defName == "DankPyon_Cereal" ||
                                                defName.ContainsIgnoreCase("Grain") ||
                                                defName.ContainsIgnoreCase("Cereal"),
                nameof(Category.Type.Nuts) => defName.ContainsIgnoreCase(Category.Type.Nuts),
                nameof(Category.Type.Vegetables) => defName == "RC2_VegetablesRaw" ||
                                                    defName.ContainsIgnoreCase("Vegetable"),
                nameof (Category.Type.Fungus) => defName.ContainsIgnoreCase("Fungus") ||
                                                 defName.ContainsIgnoreCase("Mushroom"),
                _ => false,
            };
        }

        protected static bool IsExcludedCategory(string defName)
        {
            return defName.StartsWith(Category.Prefix.VV_NHCP_DummyCategory_) ||
                   defName.ContainsIgnoreCase("Product") || // AnimalProduct
                   defName.ContainsIgnoreCase("Process") || // RimCuisine2 "Processed" categories, etc
                   defName.ContainsIgnoreCase("Corpse"); // AnimalCorpse
        }

        protected static bool CategoryParentMatches(string categoryWaitingToAdd, string categoryParentDefName)
        {
            if (string.IsNullOrWhiteSpace(categoryParentDefName))
                return false;

            switch (categoryWaitingToAdd)
            {
                case nameof(Category.Type.AnimalFoods):
                case nameof(Category.Type.Fruit):
                case nameof(Category.Type.Grains):
                case nameof(Category.Type.Nuts):
                case nameof(Category.Type.Vegetables):
                case nameof(Category.Type.Fungus):
                    if (categoryParentDefName == "Foods" || categoryParentDefName == "FoodRaw" || categoryParentDefName == "PlantFoodRaw")
                        return true;
                    return false;
                default:
                    return false;
            }
        }

        protected static bool HasCategoryInSelfOrParent(XmlDocument xml, XmlNode thingDef, string categoryDefName)
        {
            XmlNode current = thingDef;

            while (current != null)
            {
                // Check if this node has the category in its own thingCategories
                XmlNode tcNode = FindNodeByName(current, "thingCategories");
                if (tcNode != null)
                {
                    foreach (XmlNode li in tcNode.ChildNodes)
                    {
                        if (li.InnerText.Trim() == categoryDefName)
                            return true;
                    }

                    // If thingCategories exists with Inherit="False", stop walking up
                    var inheritAttr = tcNode.Attributes?["Inherit"];
                    if (inheritAttr != null && inheritAttr.Value.Equals("False", StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // Walk up to the parent def via ParentName
                var parentNameAttr = current.Attributes?["ParentName"];
                if (parentNameAttr == null || string.IsNullOrWhiteSpace(parentNameAttr.Value))
                    break;

                string parentName = parentNameAttr.Value;
                current = xml.SelectSingleNode($"/Defs/ThingDef[@Name='{parentName}']");
            }

            return false;
        }

        protected static string GetFullXmlPath(XmlNode node)
        {
            if (node == null)
                return "??";

            var path = new List<string>();
            XmlNode current = node;
            while (current != null && current.NodeType != XmlNodeType.Document)
            {
                path.Add(current.Name);
                current = current.ParentNode;
            }
            path.Reverse();
            return "/" + string.Join("/", path);
        }

        protected static string GetParentDefDefName(XmlNode node)
        {
            if (node == null)
                return "(unknown)";

            XmlNode current = node;
            while (current != null)
            {
                if (current.Name.EndsWith("Def"))
                {
                    if (current.ParentNode != null && current.ParentNode.Name != "Defs")
                    {
                        current = current.ParentNode;
                        continue;
                    }
                }

                var defNameNode = current["defName"];
                if (defNameNode != null && !string.IsNullOrWhiteSpace(defNameNode.InnerText))
                {
                    return defNameNode.InnerText;
                }

                var nameAttr = current.Attributes?["Name"];
                if (nameAttr != null && !string.IsNullOrWhiteSpace(nameAttr.Value))
                {
                    return nameAttr.Value;
                }
                
                current = current.ParentNode;
            }
            return "(unknown)";
        }

        protected static string GetFullPathWithDefName(XmlNode node)
        {
            if (node == null)
                return "??";

            string path = GetFullXmlPath(node);
            string defName = GetParentDefDefName(node);
            return $"{path} | defName: {defName}";
        }

        public static bool IsCategoryValid(XmlDocument xml, string categoryDefName)
        {
            // Try both with and without the prefix
            var node = xml.SelectSingleNode($"/Defs/ThingCategoryDef[defName='{categoryDefName}']");
            if (node != null)
                return true;

            // Try with prefix
            if (!categoryDefName.StartsWith(Category.Prefix.VV_NHCP_DummyCategory_))
            {
                string prefixed = $"{Category.Prefix.VV_NHCP_DummyCategory_}{categoryDefName}";
                node = xml.SelectSingleNode($"/Defs/ThingCategoryDef[defName='{prefixed}']");
                if (node != null)
                    return true;
            }
            return false;
        }

        public static string GetCategoryName(XmlDocument xml, string category)
        {
            // Try with prefix first
            string prefixed = $"{Category.Prefix.VV_NHCP_DummyCategory_}{category}";
            if (IsCategoryValid(xml, prefixed))
                return prefixed;

            // Try without prefix
            if (IsCategoryValid(xml, category))
                return category;

            return "";
        }

        public static string GetThingDefName(XmlNode thingDef)
        {
            foreach (XmlNode child in thingDef.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element && child.Name == "defName")
                {
                    if (!string.IsNullOrWhiteSpace(child.InnerText))
                        return child.InnerText;
                }
            }

            var nameAttr = thingDef.Attributes?["Name"];
            if (nameAttr != null && !string.IsNullOrWhiteSpace(nameAttr.Value))
                return $"(parent def: {nameAttr.Value})";

            return "(unknown)";
        }

        public static bool ResolveCategory(XmlDocument xml, string thingDefName, string newCategoryDefName, out string resolvedCategory)
        {
            resolvedCategory = newCategoryDefName;

            if (string.IsNullOrWhiteSpace(thingDefName))
            {
                ToLog("ERROR: ThingDef has no defName. Skipping.", 2);
                resolvedCategory = "ERROR";
                return false;
            }

            if (!DefToCategoryInfo.TryGetDefToCategoryInfo(thingDefName, out DefToCategoryInfo defToCategoryInfo))
            {
                DefToCategoryInfo.CacheCategoryData(thingDefName, resolvedCategory, resolvedCategory);
                DefToCategoryInfo.TryGetDefToCategoryInfo(thingDefName, out defToCategoryInfo);
            }

            if (defToCategoryInfo != null)
            {
                defToCategoryInfo.OriginalCategoryName = resolvedCategory; // Update original here in case it was changed

                if (defToCategoryInfo.IsCurrentCategoryUserDisabled)
                {
                    resolvedCategory = defToCategoryInfo.CurrentCategoryName;
                    ToLog($"INFO: ThingDef [{thingDefName}] has a user-disabled category [{defToCategoryInfo.CurrentCategoryName}]. Skipping.", 1);
                    return false;
                }

                string tryCurrent = GetCategoryName(xml, defToCategoryInfo.CurrentCategoryName);

                if (!string.IsNullOrEmpty(tryCurrent))
                {
                    resolvedCategory = tryCurrent;
                }
                else
                {
                    string tryOriginal = GetCategoryName(xml, defToCategoryInfo.OriginalCategoryName);

                    if (!string.IsNullOrEmpty(tryOriginal))
                    {
                        resolvedCategory = tryOriginal;
                    }
                    else
                    {
                        ToLog($"ERROR: Could not resolve a valid category for ThingDef [{thingDefName}]. Skipping.", 2);
                        resolvedCategory = Category.Type.None_Base;
                        defToCategoryInfo.CurrentCategoryName = resolvedCategory;
                        return false;
                    }
                }

                defToCategoryInfo.CurrentCategoryName = resolvedCategory;
            }
            else
            {
                ToLog($"ERROR: Could not cache or find CategoryInfo for ThingDef [{thingDefName}]. Skipping.", 2);
                resolvedCategory = "ERROR";
                return false;
            }

            ToLog($"INFO: ThingDef [{thingDefName}] had category successfully resolved to [{resolvedCategory}].", 1);
            return true;
        }
    }
}