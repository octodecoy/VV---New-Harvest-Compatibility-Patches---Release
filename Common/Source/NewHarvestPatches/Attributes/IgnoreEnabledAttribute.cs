namespace NewHarvestPatches;

/// <summary>
/// Marks a bool field to be ignored when true for use on settings that don't matter for logic, so that 
/// GetEnabledSettings doesn't include them.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class IgnoreEnabledAttribute : Attribute
{
    
}