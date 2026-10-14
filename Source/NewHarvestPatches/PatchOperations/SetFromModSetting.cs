namespace NewHarvestPatches;

/// <summary>
/// Writes a mod setting's value straight into matched def nodes, letting a numeric/text setting drive def
/// data (hediff severity, durations) without any C# action to apply it. The counterpart to
/// <see cref="ConditionalSetting"/>, which branches on a setting rather than writing it.
/// XML: xpath, setting (settings field name), valueType.
/// </summary>
internal class SetFromModSetting : PatchOperationPathedExtended
{
    private readonly string setting = null;
    private readonly CompareType valueType = CompareType.String;

    /// <summary>
    /// A setting whose [RequiresMod] is unsatisfied returns true WITHOUT writing - the stored value is kept
    /// for when that mod comes back, but must not reach defs meanwhile. Existing node text is parsed and
    /// re-rendered before comparing so a formatting-only difference is not treated as a change.
    /// </summary>
    protected override bool ApplyWorker(XmlDocument xml)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(setting))
            {
                LogMessage(() => $"'setting' is null or whitespace.", LogMessageType.Error);
                return false;
            }

            FieldInfo field = GetCachedSettingFieldInfo(setting);
            if (field == null)
            {
                LogMessage(() => $"Setting '{setting}' not found.", LogMessageType.Error);
                return false;
            }

            // Required mod(s) missing - keep the stored value but never write it into defs.
            if (!IsSettingAvailable(setting))
            {
                LogMessage(() => $"Skipping setting [{setting}] - required mod(s) not active.");
                return true;
            }

            object fieldValue = field.GetValue(Settings);

            if (!ConvertToValueFromObject(fieldValue, valueType, out string newValue) || newValue == null)
            {
                LogMessage(() => $"Type [{valueType}] for setting [{setting}] is incorrect.", LogMessageType.Error);
                return false;
            }

            if (!PreCheck(xpath, xml))
                return false;

            foreach (XmlNode node in nodes)
            {
                if (node.NodeType != XmlNodeType.Element)
                    continue;

                if (!TryParseToString(node.InnerText, valueType, out var currentValue))
                {
                    LogMessage(() => $"Node value for setting [{setting}] is not valid for type [{valueType}].", LogMessageType.Error);
                    return false;
                }

                if (newValue == currentValue)
                    continue;

                node.InnerText = newValue;

                var parentNode = node.ParentNode;
                string fullPath = Settings.Logging ? GetFullPathWithDefName(parentNode) : "";
                LogMessage(() => $"Set value to [{newValue}] from [{currentValue}] for [{fullPath}].");
            }

            return true;
        }
        catch (Exception ex)
        {
            LogException(ex, ex.TargetSite, optMsg: $"{xpath}");
            return false;
        }
    }
}