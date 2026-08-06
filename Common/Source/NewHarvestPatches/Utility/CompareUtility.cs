namespace NewHarvestPatches;

/// <summary>
/// Turns the XML-authored Compare* enums into actual boolean tests. Exists so that def classes only declare
/// WHAT to compare and leave HOW to combine the results here, keeping the combining semantics identical
/// everywhere they are offered. Every method fails closed on an unhandled enum value, so an option added to
/// an enum but not to a switch here silently blocks rather than silently passes.
/// </summary>
public class CompareUtility
{
    /// <summary>
    /// Combines a predicate across a list. Note the two non-obvious members: <c>Not</c> means NONE match
    /// (not "not all"), and <c>Xor</c> means EXACTLY ONE matches - for lists longer than two that is not the
    /// chained-XOR "an odd number match" a reader might expect.
    /// </summary>
    public static bool MatchesLogic<T>(CompareLogic logic, IEnumerable<T> source, Func<T, bool> predicate)
    {
        return logic switch
        {
            CompareLogic.Or => source.Any(predicate),
            CompareLogic.And => source.All(predicate),
            CompareLogic.Not => !source.Any(predicate),
            CompareLogic.Xor => source.Count(predicate) == 1,
            _ => false
        };
    }

    /// <summary>
    /// The Any/All half of <see cref="MatchesLogic{T}"/>, kept separate because the condition groups that
    /// use it pair the quantifier with a <see cref="ComparePolarity"/> instead, and offering Not/Xor
    /// alongside a Forbid flag would give two overlapping ways to spell the same negation.
    /// </summary>
    public static bool MatchesQuantifier<T>(CompareQuantifier quantifier, IEnumerable<T> source, Func<T, bool> predicate)
    {
        return quantifier switch
        {
            CompareQuantifier.Any => source.Any(predicate),
            CompareQuantifier.All => source.All(predicate),
            _ => false
        };
    }

    /// <summary>
    /// Compares a value against an authored range. The scalar operators use only ONE end of the range, so
    /// the range doubles as a plain single-value holder: GreaterThan and Equal read its low end, LessThan
    /// its high end, and the other end is ignored entirely.
    /// </summary>
    /// <remarks>
    /// Reads through TrueMin/TrueMax rather than min/max, so a range authored backwards in XML still
    /// evaluates as intended. The guard only rejects vanilla's Invalid sentinel - a crossed range is not
    /// "invalid" by that test and is handled by the True* accessors instead.
    /// </remarks>
    /// <returns>False for an Invalid range or an unhandled operator - a misconfigured condition denies
    /// rather than admits.</returns>
    public static bool MatchesComparisonOperator(CompareOperator comparisonOperator, IntRange range, int value)
    {
        if (range.IsInvalid)
            return false;

        return comparisonOperator switch
        {
            CompareOperator.InRange => range.Includes(value),
            CompareOperator.OutOfRange => !range.Includes(value),
            CompareOperator.GreaterThan => value > range.TrueMin,
            CompareOperator.LessThan => value < range.TrueMax,
            CompareOperator.Equal => value == range.TrueMin,
            CompareOperator.EqualOrGreaterThan => value >= range.TrueMin, 
            CompareOperator.EqualOrLessThan => value <= range.TrueMax, 
            _ => false
        };
    } 
}