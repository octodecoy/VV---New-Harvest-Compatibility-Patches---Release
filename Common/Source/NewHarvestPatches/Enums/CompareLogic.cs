namespace NewHarvestPatches;

/// <summary>
/// How a predicate combines across a list of authored requirements. See <see cref="CompareUtility.MatchesLogic"/>
/// for the exact semantics of the two that do not read as they look.
/// </summary>
public enum CompareLogic
{
    Or,
    And,

    /// <summary>NONE match. For use in groups, no need for FindMod's mods since we can just use caseFalse.</summary>
    Not,

    /// <summary>EXACTLY ONE matches - not "an odd number match".</summary>
    Xor
}