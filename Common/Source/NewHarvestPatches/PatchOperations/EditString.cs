using System.Text.RegularExpressions;

namespace NewHarvestPatches;

/// <summary>
/// String surgery on the InnerText of matched nodes: prepend/append to the whole value, or find a
/// substring and prepend/append/replace at each hit. Exists because vanilla PatchOperationReplace can
/// only overwrite a value wholesale, which is unusable when another mod may have already edited the same
/// text - this lets a patch amend what is there rather than clobbering it.
/// XML: mode, value, target, targetOccurrences, ignoreTargetCase, treatValueAsRegex, treatTargetAsRegex.
/// </summary>
internal class EditString : PatchOperationPathedExtended
{
    /// <summary>
    /// Whole-value modes (Prepend/Append) ignore target; the *Substring modes require it.
    /// </summary>
    private enum StringEditMode
    {
        Prepend,
        Append,
        PrependSubstring,
        AppendSubstring,
        ReplaceSubstring,
    }

    private readonly StringEditMode mode = StringEditMode.Prepend;

    private readonly string value = null;
    private readonly string target = null;
    private readonly List<int> targetOccurrences = null;
    private readonly bool ignoreTargetCase = false;
    private readonly bool treatValueAsRegex = false;
    private readonly bool treatTargetAsRegex = false;

    /// <summary>
    /// Target matching always runs through a Regex - a literal target is escaped rather than handled by a
    /// separate code path, so occurrence counting and case-insensitivity work identically either way.
    /// targetOccurrences restricts the edit to the listed 1-based match positions; omit it to hit every match.
    /// treatValueAsRegex passes the replacement through Match.Result, enabling $1-style backreferences.
    /// </summary>
    protected override bool ApplyWorker(XmlDocument xml)
    {
        if (string.IsNullOrEmpty(value))
        {
            LogMessage(() => $"A 'value' is required for {mode}", LogMessageType.Error);
            return false;
        }

        if (!PreCheck(xpath, xml))
            return false;

        Regex regex = null;

        bool requiresTarget = mode is StringEditMode.PrependSubstring or StringEditMode.AppendSubstring or StringEditMode.ReplaceSubstring;
        if (requiresTarget)
        {
            if (string.IsNullOrEmpty(target))
            {
                LogMessage(() => $"A 'target' is required for {mode}", LogMessageType.Error);
                return false;
            }

            var options = ignoreTargetCase ? RegexOptions.IgnoreCase : RegexOptions.None;

            regex = new Regex(treatTargetAsRegex ? target : Regex.Escape(target), options);
        }

        var occurrenceSet = targetOccurrences?.ToHashSet();

        foreach (XmlNode node in nodes)
        {
            if (node.NodeType != XmlNodeType.Element)
                continue;

            int matchIndex = 0;

            switch (mode)
            {
                case StringEditMode.Append:
                    node.InnerText += value;
                    break;

                case StringEditMode.Prepend:
                default:
                    node.InnerText = value + node.InnerText;
                    break;

                case StringEditMode.ReplaceSubstring:
                    if (regex?.IsMatch(node.InnerText) == true)
                    {
                        node.InnerText = regex.Replace(node.InnerText, m =>
                        {
                            matchIndex++;

                            bool apply = occurrenceSet == null || occurrenceSet.Contains(matchIndex);

                            if (!apply)
                                return m.Value;

                            return treatValueAsRegex
                                ? m.Result(value)
                                : value;
                        });
                    }
                    break;

                case StringEditMode.PrependSubstring:
                case StringEditMode.AppendSubstring:
                {
                    if (regex?.IsMatch(node.InnerText) != true)
                        break;

                    bool isPrepend = mode == StringEditMode.PrependSubstring;

                    node.InnerText = regex.Replace(node.InnerText, m =>
                    {
                        matchIndex++;

                        bool apply = occurrenceSet == null || occurrenceSet.Contains(matchIndex);

                        if (!apply)
                            return m.Value;

                        string injected = treatValueAsRegex
                            ? m.Result(value)
                            : value;

                        return isPrepend
                            ? injected + m.Value
                            : m.Value + injected;
                    });

                    break;
                }
            }

            if (!string.IsNullOrEmpty(node.InnerText))
                node.InnerText = node.InnerText.TrimStart();
        }

        return true;
    }
}