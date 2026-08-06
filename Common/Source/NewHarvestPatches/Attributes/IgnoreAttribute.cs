namespace NewHarvestPatches;

/// <summary>
/// Marks a bool field to be ignored so that GetEnabledSettings doesn't include them.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class IgnoreAttribute : Attribute
{
    
}