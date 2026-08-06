namespace NewHarvestPatches;

/// <summary>
/// Whether a condition group's result is used as-is or inverted. Applied AFTER the quantifier, so Any +
/// Forbid reads "none of these" and All + Forbid reads "not all of these".
/// </summary>
public enum ComparePolarity
{
    Require,
    Forbid,
}