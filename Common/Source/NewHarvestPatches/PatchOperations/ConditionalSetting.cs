namespace NewHarvestPatches;

/// <summary>
/// Run patches based on the value of a setting or settings.
/// Tests membership of <c>EnabledSettings</c> rather than reading fields, so the prefixed synthetic names
/// for per-def choices (fuel types, fall colors, commonality) are usable from XML exactly like plain bool
/// settings - and a setting whose required mods are missing simply never matches.
/// Note this runs during the XML patch phase, BEFORE defs load: it forces the enabled-settings cache to
/// build early, which is why <c>InitializeCollections</c> invalidates that cache once real def-derived
/// defaults exist.
/// XML: setting, or settings + logic; matchDisabled inverts the test.
/// </summary>
internal class ConditionalSetting : PatchOperationExtended
{
    private readonly string setting = null;
    private readonly List<string> settings = null;
    private readonly bool matchDisabled = false;

    protected override bool ApplyWorker(XmlDocument xml)
    {
        if (GetFlag())
        {
            if (caseTrue != null)
                return caseTrue.Apply(xml);
        }
        else if (caseFalse != null)
        {
            return caseFalse.Apply(xml);
        }
        return true;
    }

    private bool Matches(string setting, IEnumerable<string> enabledSettings)
    {
        bool enabled = enabledSettings.Contains(setting);
        return matchDisabled ? !enabled : enabled;
    }

    private bool GetFlag()
    {
        var enabledSettings = EnabledSettings;
        if (enabledSettings == null)
        {
            LogMessage(() => $"Settings failed to initialize in {nameof(ConditionalSetting)} patch operation for setting [{(!string.IsNullOrWhiteSpace(setting) ? setting : (settings != null ? string.Join(", ", settings) : "null"))}].", LogMessageType.Error);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(setting))
        {
            return Matches(setting, enabledSettings);
        }
        else if (!settings.NullOrEmpty())
        {
            return CompareUtility.MatchesLogic(logic, settings, s => Matches(s, enabledSettings));
        }
        else 
        {
            LogMessage(() => $"No settings supplied for {nameof(ConditionalSetting)} patch operation.", LogMessageType.Warning);
        }
        return false;
    }
}