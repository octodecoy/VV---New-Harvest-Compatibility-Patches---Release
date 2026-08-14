namespace NewHarvestPatches;

/// <summary>
/// Shared base for this mod's xpath-targeted PatchOperations, holding the node selection, XML tree
/// walking, value parsing and category-matching helpers the individual operations reuse.
///
/// Two things govern everything in here. First, these run during the XML PATCH PHASE: no DefDatabase
/// exists yet, so every "does this category exist / what is its parent" question has to be answered by
/// querying the raw XmlDocument, which is why so many helpers take an <c>XmlDocument</c> and re-select
/// nodes by xpath. The three document indexes (<see cref="GetCategoryParentIndex"/>,
/// <see cref="GetThingDefNameIndex"/>, <see cref="GetAbstractRecipeDefIndex"/>) exist so the hot ones are
/// answered from a dictionary instead. Inheritance has not been resolved at this point either, so a def's
/// inherited elements have to be found by walking <c>ParentName</c> yourself -
/// <see cref="DeclaresElementInAncestry"/>.
/// Second, every field below is populated by RimWorld's XML loader through reflection -
/// they look permanently stuck at their initializers to a C# reader, but each is really an optional
/// element on the patch's XML node.
///
/// Operations that act on another mod's raw-food categories share one test,
/// <see cref="EvaluateCategoriesNode"/>, rather than each re-implementing the name/exclusion/parent rules.
///
/// A failed operation must return false rather than throw: an exception escaping the patch phase aborts
/// loading for every mod, not just this one.
///
/// A subclass instance can source its nodes from a <see cref="NewHarvestPatches.CacheNodes"/> block
/// instead of its own xpath: set <see cref="cacheKey"/> and <see cref="xpath"/> is then evaluated
/// RELATIVE to each cached node (blank = the cached set itself). See <see cref="PreCheckCached"/>.
///
/// <see cref="ResolveDefNames"/> is the shared defName-list resolver for every op that injects this
/// mod's own produce into a third-party node (<see cref="AddOwnDefsToCategoryFilters"/>,
/// <see cref="AddDefsWhereDefPresent"/>).
/// </summary>
// Loosely based on XmlExtensions.
internal abstract class PatchOperationPathedExtended : PatchOperationPathed
{
    private readonly bool selectSingleNode = false;
    protected List<XmlNode> nodes;
    private readonly bool checkAttributes = false;
    private readonly CompareText compare = CompareText.Name;
    protected readonly PatchOperation caseTrue = null;
    protected readonly PatchOperation caseFalse = null;
    /// <summary>When set, node selection reads from the <see cref="NewHarvestPatches.CacheNodes"/> slot
    /// of this name instead of running <see cref="PatchOperationPathed.xpath"/> against the whole
    /// document. See <see cref="PreCheckCached"/>.</summary>
    protected readonly string cacheKey = null;

    /// <summary>Which part of a node <see cref="NodesMatch"/> compares when looking for an existing node.</summary>
    public enum CompareText
    {
        Name,
        InnerText,
        Both
    }

    /// <summary>
    /// Resolves <paramref name="xpath"/> and stores the hits in <see cref="nodes"/> for the caller to
    /// iterate - the selection is a SIDE EFFECT, not a return value. Returns false when nothing matched,
    /// which is the normal case for a patch targeting a mod that is not installed, hence a warning rather
    /// than an error. Every ApplyWorker must call this before touching <see cref="nodes"/>.
    ///
    /// When <see cref="cacheKey"/> is set, selection is delegated to <see cref="PreCheckCached"/> and
    /// <paramref name="xpath"/> is relative to each cached node instead of the whole document.
    /// </summary>
    protected virtual bool PreCheck(string xpath, XmlDocument xml)
    {
        if (xml == null)
            return false;

        if (!string.IsNullOrWhiteSpace(cacheKey))
            return PreCheckCached(xpath);

        if (string.IsNullOrWhiteSpace(xpath))
            return false;

        if (selectSingleNode)
            nodes = [xml.SelectSingleNode(xpath)];
        else
            nodes = [.. xml.SelectNodes(xpath)?.Cast<XmlNode>() ?? []];

        if (nodes.NullOrEmpty() || nodes[0] == null)
        {
            LogMessage(() => $"No nodes found for xpath [{xpath}].", LogMessageType.Warning);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Node selection for a <see cref="cacheKey"/>-bearing operation: reads the node list a
    /// <see cref="NewHarvestPatches.CacheNodes"/> block left in <see cref="NewHarvestPatchesModSettings.XmlNodeCache"/>
    /// instead of walking the document. <paramref name="relativeXpath"/> is evaluated against EACH cached
    /// node (blank = use the cached set as-is); a leading "/" is rejected because it would escape back to
    /// document root and silently defeat the cache. Detached cached nodes (an earlier op in the same
    /// block replaced their parent) are skipped rather than dereferenced.
    /// </summary>
    private bool PreCheckCached(string relativeXpath)
    {
        if (XmlNodeCache == null || !XmlNodeCache.TryGetValue(cacheKey, out List<XmlNode> cached) || cached.NullOrEmpty())
        {
            LogMessage(() => $"No cached nodes for key [{cacheKey}].", LogMessageType.Warning);
            return false;
        }

        if (relativeXpath != null && relativeXpath.StartsWith("/"))
        {
            LogMessage(() => $"cacheKey [{cacheKey}] xpath [{relativeXpath}] must be relative (no leading '/').", LogMessageType.Error);
            return false;
        }

        if (string.IsNullOrWhiteSpace(relativeXpath))
        {
            nodes = [.. cached.Where(n => n.ParentNode != null)];
        }
        else
        {
            HashSet<XmlNode> seen = [];
            List<XmlNode> matched = [];
            foreach (XmlNode cachedNode in cached)
            {
                if (cachedNode.ParentNode == null)
                    continue;

                foreach (XmlNode hit in cachedNode.SelectNodes(relativeXpath)?.Cast<XmlNode>() ?? [])
                {
                    if (seen.Add(hit))
                        matched.Add(hit);
                }
            }
            nodes = matched;
        }

        if (selectSingleNode && nodes.Count > 1)
            nodes = [nodes[0]];

        if (nodes.NullOrEmpty())
        {
            LogMessage(() => $"No nodes found under cacheKey [{cacheKey}] for relative xpath [{relativeXpath}].", LogMessageType.Warning);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Looks for a child of <paramref name="parent"/> equivalent to <paramref name="node"/>, so a caller
    /// can replace it instead of appending a duplicate. With checkAttributes set, equivalence also requires
    /// an identical attribute set - needed for list entries that are distinguished only by attributes
    /// (Class, MayRequire) and would otherwise all look like the same node.
    /// </summary>
    /// <param name="foundNode">The matched child, or null. Only meaningful when this returns true.</param>
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

    protected static bool NodesMatch(XmlNode childNode, XmlNode node, CompareText compare)
    {
        return compare switch
        {
            CompareText.Name => childNode.Name == node.Name,
            CompareText.InnerText => childNode.InnerText == node.InnerText,
            CompareText.Both => childNode.Name == node.Name && childNode.InnerText == node.InnerText,
            _ => false,
        };
    }

    /// <summary>
    /// Applies a "+"/"-"/"*"/"/" operation to two numeric strings, round-tripping through invariant culture
    /// so a comma-decimal locale cannot corrupt the written XML.
    /// </summary>
    /// <returns>The result as a string, or null if either side is not a number or the result is NaN /
    /// infinite (including divide-by-zero) - callers treat null as "skip this node", never as a value.</returns>
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

    /// <summary>
    /// Renders a settings field's boxed value as the XML text for it. The declared
    /// <paramref name="type"/> must match the object's real type - a mismatch returns false instead of
    /// coercing, so an XML author who declares the wrong valueType gets a logged error rather than a
    /// silently mangled def.
    /// </summary>
    protected static bool ConvertToValueFromObject(object obj, CompareType type, out string result)
    {
        result = null;

        switch (type)
        {
            case CompareType.Bool:
                if (obj is not bool b)
                    return false;
                result = b.ToString();
                return true;

            case CompareType.Int:
                if (obj is not int i)
                    return false;
                result = i.ToString(CultureInfo.InvariantCulture);
                return true;

            case CompareType.Float:
                if (obj is not float f)
                    return false;
                result = f.ToString(CultureInfo.InvariantCulture);
                return true;

            case CompareType.String:
                if (obj is not string s)
                    return false;
                result = s;
                return true;

            case CompareType.IntRange:
                if (obj is not IntRange ir)
                    return false;
                result = ir.ToString();
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Inverse of <see cref="ConvertToValueFromObject"/>: parses existing node text as
    /// <paramref name="type"/> and re-renders it canonically. Round-tripping rather than comparing raw
    /// strings is what lets a caller tell "already set" from "differently formatted" ("1.50" vs "1.5").
    /// </summary>
    protected static bool TryParseToString(string input, CompareType type, out string result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(input))
            return false; // Rethink?  May want to replace a empty value with a good value.

        try
        {
            switch (type)
            {
                case CompareType.Bool:
                    if (!bool.TryParse(input, out var b)) 
                        return false;
                    result = b.ToString();
                    return true;

                case CompareType.Int:
                    if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                        return false;
                    result = i.ToString(CultureInfo.InvariantCulture);
                    return true;

                case CompareType.Float:
                    if (!float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                        return false;
                    result = f.ToString(CultureInfo.InvariantCulture);
                    return true;

                case CompareType.String:
                    result = input;
                    return true;

                case CompareType.IntRange:
                    if (!TryParseIntRange(input, out var ir)) 
                        return false;
                    result = ir.ToString();
                    return true;       

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseIntRange(string input, out IntRange? range)
    {
        range = null;

        try
        {
            range = IntRange.FromString(input);
            return range is IntRange;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reflected settings field by name, memoized for the patch phase. A miss is cached as null too, so a
    /// misspelled setting in XML costs one failed lookup rather than one per patch that names it.
    /// The cache is released with the rest of the XML-phase state once loading finishes.
    /// </summary>
    public static FieldInfo GetCachedSettingFieldInfo(string name)
    {
        SettingFieldCache ??= [];
        
        if (!SettingFieldCache.TryGetValue(name, out var field))
        {
            field = Settings.GetType()?.GetField(name, BindingFlags.Instance | BindingFlags.Public);

            SettingFieldCache[name] = field;
        }

        return field;
    }

    /// <summary>
    /// Patch-phase call site for the category name test. The rule itself lives on
    /// <see cref="SharedConstants.Category.NameMatchesKind"/> so the post-load pass
    /// (<c>CategoryApplier.IsModAddedCategoryOfType</c>) applies the identical test instead of a hand-copied
    /// twin. Deliberately permissive - pair it with <see cref="IsExcludedCategory"/> and
    /// <see cref="CategoryParentMatches"/> at every call site.
    /// </summary>
    protected static bool TextMatchesForCategory(string defName, string categoryWaitingToAdd)
        => Category.NameMatchesKind(defName, categoryWaitingToAdd);

    /// <summary>
    /// Categories that would pass the name test but must never be treated as raw-food categories - our own
    /// dummy categories, plus product/processed/corpse variants that carry food-like words. The rule itself
    /// lives on <see cref="SharedConstants.Category.IsExcludedName"/> so the post-load side can apply the
    /// identical test; this stays as the patch-phase call site.
    /// </summary>
    protected static bool IsExcludedCategory(string defName) => Category.IsExcludedName(defName);

    /// <summary>
    /// Patch-phase call site for the structural half of the category test. The rule lives on
    /// <see cref="SharedConstants.Category.ParentMatchesKind"/> for the same reason as
    /// <see cref="TextMatchesForCategory"/>. Note the patch phase feeds it the raw <c>&lt;parent&gt;</c> text
    /// as written on disk while the post-load pass feeds it the resolved <c>cat.parent</c>; sharing the rule
    /// does not make those two sources of evidence agree.
    /// </summary>
    protected static bool CategoryParentMatches(string categoryWaitingToAdd, string categoryParentDefName)
        => Category.ParentMatchesKind(categoryWaitingToAdd, categoryParentDefName);

    /// <summary>
    /// What one filter's &lt;categories&gt; list says about a category kind. Three separate flags rather
    /// than one verdict because the two consumers disagree about <see cref="AlreadyPresent"/>:
    /// <c>AddCategoryToFilter</c> must not append a duplicate, while <c>AddOwnDefsToCategoryFilters</c> has
    /// to inject regardless - a filter that already carries our dummy category still reaches nothing through
    /// it while that kind's merge setting is off.
    /// </summary>
    protected readonly struct CategoryEvidence(bool anyMatch, bool anyExcluded, bool alreadyPresent)
    {
        /// <summary>At least one listed category is a genuine third-party category of this kind.</summary>
        public readonly bool AnyMatch = anyMatch;

        /// <summary>At least one listed category matched the kind but is on the exclusion list.</summary>
        public readonly bool AnyExcluded = anyExcluded;

        /// <summary>The category being offered is already in the list.</summary>
        public readonly bool AlreadyPresent = alreadyPresent;

        /// <summary>
        /// Whether this filter counts as referring to the kind. A single excluded entry vetoes the whole
        /// filter rather than just itself: one mixing raw and processed categories is not one we can reason
        /// about, so the safe move is to leave it alone entirely.
        /// </summary>
        public bool IsEvidence => AnyMatch && !AnyExcluded;
    }

    /// <summary>
    /// Runs the three-rule category test over every entry of one &lt;categories&gt; node. Shared by every
    /// operation that acts on "this filter refers to somebody else's fruit/grain/... category", so the rules
    /// - name match, exclusion, food-root parent - cannot drift between them. Each entry must clear ALL
    /// three before it counts as evidence.
    /// </summary>
    /// <param name="offeredDefName">
    /// The category the caller intends to add, reported back through
    /// <see cref="CategoryEvidence.AlreadyPresent"/>. Pass null when the caller adds no category.
    /// </param>
    protected static CategoryEvidence EvaluateCategoriesNode(
        XmlDocument xml, XmlNode categoriesNode, string categoryType, string offeredDefName)
    {
        Dictionary<string, string> parentIndex = GetCategoryParentIndex(xml);

        bool anyMatch = false;
        bool anyExcluded = false;
        bool alreadyPresent = false;

        foreach (XmlNode liNode in categoriesNode.ChildNodes)
        {
            string defNameInLiNode = liNode.InnerText;

            if (offeredDefName != null && defNameInLiNode == offeredDefName)
            {
                alreadyPresent = true;
                continue;
            }

            // Absent from the index means no ThingCategoryDef declares this name; an empty value means it
            // declares no <parent>, which CategoryParentMatches rejects on its own.
            if (!parentIndex.TryGetValue(defNameInLiNode, out string parentDefName))
                continue;

            if (!CategoryParentMatches(categoryType, parentDefName))
                continue;

            if (!TextMatchesForCategory(defNameInLiNode, categoryType))
                continue;

            anyMatch = true;

            if (IsExcludedCategory(defNameInLiNode))
                anyExcluded = true;
        }

        return new CategoryEvidence(anyMatch, anyExcluded, alreadyPresent);
    }

    /// <summary>
    /// defName -&gt; raw &lt;parent&gt; text for every ThingCategoryDef in the document, built once per patch
    /// run. See <see cref="NewHarvestPatchesModSettings.CategoryParentByDefName"/> for why caching it is
    /// sound. First declaration wins, matching the SelectSingleNode this replaced.
    /// </summary>
    protected static Dictionary<string, string> GetCategoryParentIndex(XmlDocument xml)
    {
        if (CategoryParentByDefName != null)
            return CategoryParentByDefName;

        Dictionary<string, string> index = [];
        foreach (XmlNode categoryDefNode in xml.SelectNodes("/Defs/ThingCategoryDef")?.Cast<XmlNode>() ?? [])
        {
            string defName = categoryDefNode.SelectSingleNode("defName")?.InnerText;
            if (string.IsNullOrWhiteSpace(defName) || index.ContainsKey(defName))
                continue;

            index[defName] = categoryDefNode.SelectSingleNode("parent")?.InnerText ?? "";
        }

        LogMessage(() => $"Indexed [{index.Count}] ThingCategoryDefs for the patch phase.");
        return CategoryParentByDefName = index;
    }

    /// <summary>
    /// Every ThingDef defName in the document, built once per patch run. Abstract defs carry a Name
    /// attribute instead of a defName and are therefore absent, which is correct - nothing can reference one
    /// from a filter. See <see cref="NewHarvestPatchesModSettings.ThingDefNamesInDocument"/>.
    /// </summary>
    protected static HashSet<string> GetThingDefNameIndex(XmlDocument xml)
    {
        if (ThingDefNamesInDocument != null)
            return ThingDefNamesInDocument;

        HashSet<string> names = [];
        foreach (XmlNode defNameNode in xml.SelectNodes("/Defs/ThingDef/defName")?.Cast<XmlNode>() ?? [])
        {
            if (!string.IsNullOrWhiteSpace(defNameNode.InnerText))
                names.Add(defNameNode.InnerText);
        }

        LogMessage(() => $"Indexed [{names.Count}] ThingDef names for the patch phase.");
        return ThingDefNamesInDocument = names;
    }

    /// <summary>
    /// Abstract RecipeDefs by their <c>Name</c> attribute, built once per patch run. First declaration wins;
    /// RimWorld's own <c>XmlInheritance.GetBestParentFor</c> resolves a duplicated Name by load order
    /// instead, so a name declared twice can in principle index the wrong one - it only ever costs this
    /// index a wrong answer about which optional element an ancestor declares, never a written node.
    /// See <see cref="NewHarvestPatchesModSettings.AbstractRecipeDefsByName"/>.
    /// </summary>
    protected static Dictionary<string, XmlNode> GetAbstractRecipeDefIndex(XmlDocument xml)
    {
        if (AbstractRecipeDefsByName != null)
            return AbstractRecipeDefsByName;

        Dictionary<string, XmlNode> index = [];
        foreach (XmlNode recipeDefNode in xml.SelectNodes("/Defs/RecipeDef[@Name]")?.Cast<XmlNode>() ?? [])
        {
            string name = recipeDefNode.Attributes?["Name"]?.Value;
            if (string.IsNullOrWhiteSpace(name) || index.ContainsKey(name))
                continue;

            index[name] = recipeDefNode;
        }

        LogMessage(() => $"Indexed [{index.Count}] abstract RecipeDefs for the patch phase.");
        return AbstractRecipeDefsByName = index;
    }

    /// <summary>
    /// Whether <paramref name="defNode"/> or anything in its <c>ParentName</c> chain declares a
    /// &lt;<paramref name="elementName"/>&gt; child. The question only has to be asked of elements whose
    /// ABSENCE means something to the game - <c>RecipeDef.defaultIngredientFilter</c> is the case this
    /// exists for, since <c>RecipeDef.ResolveReferences</c> substitutes a copy of the fixedIngredientFilter
    /// only while the field is still null, and writing the node at all takes that fallback away.
    /// <para>
    /// The depth guard is for cyclic inheritance: RimWorld reports and drops a cycle later, but this runs
    /// first and must not spin on one.
    /// </para>
    /// </summary>
    protected static bool DeclaresElementInAncestry(XmlDocument xml, XmlNode defNode, string elementName)
    {
        if (defNode == null)
            return false;

        Dictionary<string, XmlNode> abstractDefs = GetAbstractRecipeDefIndex(xml);

        XmlNode current = defNode;
        for (int depth = 0; current != null && depth < MaxInheritanceDepth; depth++)
        {
            if (current[elementName] != null)
                return true;

            string parentName = current.Attributes?["ParentName"]?.Value;
            if (string.IsNullOrWhiteSpace(parentName) || !abstractDefs.TryGetValue(parentName, out XmlNode parentNode))
                return false;

            current = parentNode;
        }

        return false;
    }

    /// <summary>Cycle backstop for <see cref="DeclaresElementInAncestry"/>; no real def chain is this deep.</summary>
    private const int MaxInheritanceDepth = 16;

    // --- Diagnostics. Only for log text; never branch on these. Callers guard the calls behind
    // Settings.Logging because walking the tree per node is not free.

    /// <summary>Slash-joined ancestor path of a node, for log messages.</summary>
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

    /// <summary>
    /// Walks up from a node to the defName of the def containing it, so a log line about some deeply
    /// nested element can name which def it belongs to. Falls back to the Name attribute for abstract
    /// defs, which have no defName, and to "(unknown)" rather than throwing.
    /// </summary>
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

    /// <summary>
    /// The defNames to inject: the explicit <paramref name="value"/> list when given, otherwise
    /// <see cref="Category.s_ownDefNamesByKind"/>'s entry for <paramref name="categoryType"/>. Filtered
    /// down to defs that actually exist in the document - most of any kind's list is absent on a given
    /// install, since the New Harvest modules are separate mods, and writing an unresolvable defName into
    /// a filter costs one cross-reference error per occurrence. Shared by every op that injects a defName
    /// list this way (<see cref="AddOwnDefsToCategoryFilters"/>, <see cref="AddDefsWhereDefPresent"/>).
    /// </summary>
    protected static List<string> ResolveDefNames(XmlDocument xml, XmlContainer value, string categoryType)
    {
        List<string> resolved = [];

        IEnumerable<string> source;
        if (value?.node != null)
        {
            source = value.node.ChildNodes.Cast<XmlNode>().Select(child => child.InnerText);
        }
        else if (Category.s_ownDefNamesByKind.TryGetValue(categoryType, out string[] seeded))
        {
            source = seeded;
        }
        else
        {
            LogMessage(() => $"No seed map entry for category type [{categoryType}] and no <value> given.", LogMessageType.Error);
            return resolved;
        }

        HashSet<string> presentInDocument = GetThingDefNameIndex(xml);
        foreach (string defName in source)
        {
            if (!string.IsNullOrWhiteSpace(defName) && presentInDocument.Contains(defName) && !resolved.Contains(defName))
                resolved.Add(defName);
        }

        return resolved;
    }

    /// <summary>
    /// Whether a ThingCategoryDef with this defName is being defined anywhere in the loaded XML. Tries the
    /// bare name and the dummy-category prefixed form, so callers may pass either. Existence has to be
    /// asked of the document because no DefDatabase exists yet.
    /// </summary>
    public static bool IsCategoryValid(XmlDocument xml, string categoryDefName)
    {
        // Try both with and without the prefix
        var node = xml.SelectSingleNode($"/Defs/ThingCategoryDef[defName='{categoryDefName}']");
        if (node != null)
            return true;

        // Try with prefix
        if (!categoryDefName.StartsWith(Category.Prefix.DummyCategory))
        {
            string prefixed = $"{Category.Prefix.DummyCategory}{categoryDefName}";
            node = xml.SelectSingleNode($"/Defs/ThingCategoryDef[defName='{prefixed}']");
            if (node != null)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves a category type or defName to the real defName that exists in the XML, preferring our
    /// prefixed dummy category over a bare match. Lets XML authors write the short type name
    /// ("Fruit") and get the dummy category back.
    /// </summary>
    /// <returns>The resolved defName, or "" when neither form exists.</returns>
    public static string GetCategoryName(XmlDocument xml, string category)
    {
        // Try with prefix first
        string prefixed = $"{Category.Prefix.DummyCategory}{category}";
        if (IsCategoryValid(xml, prefixed))
            return prefixed;

        // Try without prefix
        if (IsCategoryValid(xml, category))
            return category;

        return "";
    }
}