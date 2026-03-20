namespace NewHarvestPatches;

/// <summary>
/// Marks a field to be ignored when true for use on settings fields that are true by default, so that 
/// GetEnabledSettings doesn't include them when in their default state.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class IgnoreEnabledAttribute : Attribute
{
    
}