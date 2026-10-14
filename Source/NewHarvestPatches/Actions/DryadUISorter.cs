namespace NewHarvestPatches;

/// <summary>
/// Sorts dryads in the UI so our added dryads fit, instead of hoping for the best via XML ordering.
/// <c>GauranlenTreeModeDef.drawPosition</c> is an absolute slot coordinate, so any two mods that pick
/// positions independently can overlap; recomputing the whole layout from the final def list is the
/// only assignment that stays correct no matter how many other mods added modes.
/// Boot-only: writes shared defs and holds no baseline, so it cannot be reverted in place.
/// </summary>
internal static class DryadUISorter
{
    public static void TrySortDryads() => ActionRunner.Run(nameof(DryadUISorter), nameof(TrySortDryads), SortDryads);

    /// <summary>
    /// Reassigns every dryad mode's <c>drawPosition</c> into a column-major grid: seven rows per
    /// column, the last row pinned to 1.0 so it sits flush against the panel's bottom edge rather than
    /// at the arithmetic step.
    /// </summary>
    private static void SortDryads()
    {
        // Easier to just sort all than to find first empty spots
        var defs = DefDatabase<GauranlenTreeModeDef>.AllDefsListForReading;
        if (defs.NullOrEmpty())
        {
            LogMessage(() => "No dryads found to sort.", LogMessageType.Error);
            return;
        }

        const float spacing = 0.1665f; // step
        const int rowsPerColumn = 7;   // 0-6 then bottom at 1.0

        for (int i = 0; i < defs.Count; i++)
        {
            int col = i / rowsPerColumn;
            int row = i % rowsPerColumn;

            float y = row == rowsPerColumn - 1 ? 1f : row * spacing;
            float x = col;

            defs[i].drawPosition = new Vector2(x, y);

            LogMessage(() => $"Assigned dryad [{defs[i].defName}] -> drawPosition ({x}, {y:0.###})");
        }
    }
}