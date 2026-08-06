namespace NewHarvestPatches;

/// <summary>
/// THE single source of truth for whether a setting is available this session. Marks a settings field as
/// requiring the given mod(s)/module(s) to be active - and, via the <c>No*</c> members of
/// <see cref="ModRequirement"/>, as requiring some other mod to be ABSENT.
/// While a requirement is unmet the setting is excluded from EnabledSettings (so every action and
/// setting-driven XML patch operation skips it) and its row is not drawn in the settings window - the
/// stored value is never modified, so the user's choice survives mod churn.
/// All requirements must be met unless <see cref="AnyOf"/> is true; exclusions are mandatory either way.
/// Nothing else may test mod presence to decide a setting: read
/// <see cref="IsSettingAvailable"/> instead.
/// </summary>
/// <example>
/// [RequiresMod(ModRequirement.Industrial, ModRequirement.NoFernyFloorMenu)]  // Industrial AND no Ferny
/// [RequiresMod(ModRequirement.Forage, ModRequirement.Industrial, AnyOf = true)]  // either module
/// </example>
[AttributeUsage(AttributeTargets.Field)]
public class RequiresModAttribute(params ModRequirement[] requirements) : Attribute
{
    public ModRequirement[] Requirements { get; } = requirements;

    /// <summary>True = any one requirement suffices; false (default) = all required.</summary>
    public bool AnyOf { get; set; }
}
