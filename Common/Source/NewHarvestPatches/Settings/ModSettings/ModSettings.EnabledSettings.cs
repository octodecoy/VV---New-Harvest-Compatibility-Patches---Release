namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    // Public bool settings that participate in EnabledSettings, each paired with its inversion flag.
    // TryGetAttribute is uncached reflection (Attribute.IsDefined + an allocating GetCustomAttributes),
    // and attribute presence cannot change while the process lives - so [IgnoreEnabled] filtering and the
    // [DisabledIsEnabled] flag are resolved once here rather than twice per field on every rebuild, and
    // rebuilds happen on every MarkSettingChanged(). Deliberately NOT part of ClearMenuSessionCaches: this
    // holds assembly metadata (FieldInfo), not defs or translated strings, so it can never go stale, and it
    // is read outside the settings menu too - dropping it would only pay the reflection cost again.
    private static readonly (FieldInfo field, bool disabledIsEnabled)[] s_boolFields = BuildBoolFields();

    private static (FieldInfo field, bool disabledIsEnabled)[] BuildBoolFields()
    {
        List<(FieldInfo field, bool disabledIsEnabled)> fields = [];
        foreach (FieldInfo field in typeof(NewHarvestPatchesModSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType != typeof(bool))
                continue;

            if (field.TryGetAttribute<IgnoreAttribute>() != null || field.TryGetAttribute<IgnoreEnabledAttribute>() != null)
                continue;

            fields.Add((field, field.TryGetAttribute<DisabledIsEnabledAttribute>() != null));
        }
        return [.. fields];
    }

    // Setting name -> [RequiresMod] requirement, for gating reads when required mods are missing.
    private static readonly Dictionary<string, RequiresModAttribute> s_settingRequirements = BuildSettingRequirements();

    private static Dictionary<string, RequiresModAttribute> BuildSettingRequirements()
    {
        Dictionary<string, RequiresModAttribute> map = [];
        foreach (FieldInfo field in typeof(NewHarvestPatchesModSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            RequiresModAttribute requirement = field.TryGetAttribute<RequiresModAttribute>();
            if (requirement != null)
                map[field.Name] = requirement;
        }
        return map;
    }

    /// <summary>
    /// THE gate. True when the setting has no [RequiresMod] or that attribute is satisfied by the active mod
    /// list. The active-mod set is static per session, so no invalidation needed. Stored value stays
    /// untouched - only availability is gated, so removing a mod never destroys the user's choice.
    /// Everything that needs to know "may this setting show / apply" calls this, passing nameof(TheField);
    /// an unknown name is treated as available, which is what makes it safe for the dictionary-backed rows
    /// that pass a synthetic id instead of a field name.
    /// </summary>
    public static bool IsSettingAvailable(string settingName)
    {
        return !s_settingRequirements.TryGetValue(settingName, out RequiresModAttribute requirement) || IsSatisfied(requirement);
    }

    /// <summary>
    /// True when at least one of the named settings is available. Used for "does this tab have anything to
    /// show" so tab visibility is derived from the settings' own attributes instead of a second set of
    /// mod checks (see <see cref="TabHasContent"/>).
    /// </summary>
    public static bool AnySettingAvailable(params string[] settingNames)
    {
        foreach (string name in settingNames)
        {
            if (IsSettingAvailable(name))
                return true;
        }
        return false;
    }

    // Materialized once per session/toggle and invalidated on mutation - some settings can now apply
    // live, so this is queried far more often than a single boot pass.
    private HashSet<string> _enabledSettingsInt;

    /// <summary>
    /// Flat set of "setting names that are currently on", the gate every action checks instead of
    /// reading fields directly. A name in here does NOT mean its field is true - "enabled" means
    /// "this setting is asking for work to be done", which for the inverted groups is the false case
    /// (see <see cref="DisabledIsEnabledAttribute"/>, <see cref="GetEnabledFuelTypes"/>,
    /// <see cref="GetEnabledFallColorTrees"/>). Collection-backed groups contribute prefixed synthetic
    /// names (<see cref="Setting.Prefix"/> + defName) so a per-def choice looks like an ordinary setting.
    /// Settings whose [RequiresMod] is unsatisfied never appear, so an action can gate on membership
    /// alone without re-checking mod presence.
    /// </summary>
    public HashSet<string> EnabledSettings
    {
        get
        {
            if (_enabledSettingsInt != null)
                return _enabledSettingsInt;

            HashSet<string> set = [];
            foreach (Func<IEnumerable<string>> provider in EnabledSettingProviders)
            {
                IEnumerable<string> items = provider();
                if (items != null)
                    set.UnionWith(items);
            }
            return _enabledSettingsInt = set;
        }
    }

    /// <summary>Call after any mutation to the fields/collections read by the providers below.</summary>
    internal void InvalidateEnabledSettings() => _enabledSettingsInt = null;

    // Single registration point for enabled-setting sources. Adding a new collection-backed
    // group only requires writing one GetEnabledX provider and adding it here - no other
    // method needs touching.
    private List<Func<IEnumerable<string>>> _enabledProviders;
    private List<Func<IEnumerable<string>>> EnabledSettingProviders => _enabledProviders ??=
    [
        GetEnabledBoolFields,
        GetEnabledFuelTypes,
        GetEnabledCommonality,
        GetEnabledFallColorTrees,
    ];

    /// <summary>
    /// Every public bool field that is currently "on", by field name. [IgnoreEnabled] opts a field out
    /// entirely (it drives UI only) and [DisabledIsEnabled] inverts the test for settings whose default is
    /// true and whose work happens when the user turns them OFF (e.g. HayNeedsCooling) - both are read off
    /// <see cref="s_boolFields"/>, which resolved them once.
    /// </summary>
    private IEnumerable<string> GetEnabledBoolFields()
    {
        foreach (var (field, disabledIsEnabled) in s_boolFields)
        {
            // Required mod(s) missing - setting keeps its stored value but must not drive any logic.
            // Left as a per-rebuild dictionary lookup rather than folded into s_boolFields: the table
            // would then depend on s_settingRequirements already being initialized, and static field
            // init order across a partial class spread over many files is not worth betting on.
            if (!IsSettingAvailable(field.Name))
                continue;

            bool enabled = (bool)field.GetValue(this);
            if ((enabled && !disabledIsEnabled) || (!enabled && disabledIsEnabled))
            {
                yield return field.Name;
            }
        }
    }

    private IEnumerable<string> GetEnabledFuelTypes()
    {
        if (!IsSettingAvailable(nameof(FuelTypes)) || FuelTypes.NullOrEmpty())
            yield break;

        foreach (var kvp in FuelTypes)
        {
            if (!kvp.Value) // Enabled if the value is false - to disallow fuel type
                yield return Setting.Prefix.DisabledFuel_ + kvp.Key;
        }
    }

    private IEnumerable<string> GetEnabledCommonality()
    {
        if (!IsSettingAvailable(nameof(StuffCommonality)) || StuffCommonality.NullOrEmpty())
            yield break;

        foreach (var kvp in StuffCommonality)
        {
            var info = kvp.Value;
            if (info == null || !info.DiffersFromDefault)
                continue;

            yield return Setting.Prefix.SetCommonality_ + kvp.Key;
        }
    }

    private IEnumerable<string> GetEnabledFallColorTrees()
    {
        if (!IsSettingAvailable(nameof(FallColorTrees)) || FallColorTrees.NullOrEmpty())
            yield break;

        foreach (var kvp in FallColorTrees)
        {
            // Enabled if the value is false since all are on by default - to turn off fall colors
            if (!kvp.Value)
                yield return Setting.Prefix.NoFallColors_ + kvp.Key;
        }
    }
}
