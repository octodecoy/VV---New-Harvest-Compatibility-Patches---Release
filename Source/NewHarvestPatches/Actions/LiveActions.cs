namespace NewHarvestPatches;

/// <summary>
/// Single entry point for every setting that can apply without a restart. Each action called
/// here self-diffs against its own last-applied state (see each action's TryX method), so this
/// is safe to call both at game boot (nothing previously applied - pure forward apply) and on a
/// settings-window close that actually changed something (see NewHarvestPatchesMod.WriteSettings,
/// which gates this call on its changed flag) - toggling a setting off reverts exactly what that
/// action applied.
/// Category settings have their own equivalent live path: CategoryApplier.TryApplyAtBoot /
/// TryApplyToggles, called directly from BootActions and NewHarvestPatchesMod respectively.
/// Not included here: BridgeDropdownAdder / FloorDropdownAdder (designationCategory/dropdown
/// def-tree surgery - restart required, see the "requires restart" tooltip on their settings).
/// </summary>
internal static class LiveActions
{
    /// <summary>
    /// Runs every live-appliable action whose backing collection is available this session (gated by that
    /// collection's [RequiresMod] via IsSettingAvailable, so mod presence is never re-tested here).
    /// Deliberately unconditional on the individual VALUES: each action must be called even when its
    /// setting is off, because that call is what reverts the previously applied state. Availability is
    /// static for the session, so a group skipped here was never applied and has nothing to revert.
    /// </summary>
    public static void TryApplyAll()
    {
        if (IsSettingAvailable(nameof(Settings.MaterialColors)))
        {
            MaterialColorChanger.TryChangeMaterialColors();
        }

        if (IsSettingAvailable(nameof(Settings.FuelTypes)))
        {
            FuelTypeDisabler.TryDisallowFuels();
        }

        if (IsSettingAvailable(nameof(Settings.StuffCommonality)))
        {
            MaterialCommonalityChanger.TryChangeMaterialCommonality();
        }

        if (IsSettingAvailable(nameof(Settings.FallColorTrees)))
        {
            FallColorDisabler.TryDisableFallColors();
        }
    }
}
