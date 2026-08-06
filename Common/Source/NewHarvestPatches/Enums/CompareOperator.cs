namespace NewHarvestPatches;

/// <summary>
/// How a value is tested against an authored IntRange. Only InRange and OutOfRange use both ends; the rest
/// treat the range as a single threshold - see <see cref="CompareUtility.MatchesComparisonOperator"/>.
/// </summary>
public enum CompareOperator
{
    /// <summary>Skips the check entirely. This is the default, so authoring a range without also setting an
    /// operator does nothing.</summary>
    None,
    InRange,
    OutOfRange ,
    GreaterThan,
    LessThan,
    Equal,
    EqualOrGreaterThan,
    EqualOrLessThan
}