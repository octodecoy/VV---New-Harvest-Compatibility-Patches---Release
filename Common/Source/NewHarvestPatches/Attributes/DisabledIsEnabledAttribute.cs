namespace NewHarvestPatches;

/// <summary>
/// Marks a bool field to be counted as enabled when false for use on settings fields that are false by default, so that 
/// GetEnabledSettings includes them.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class DisabledIsEnabledAttribute : Attribute
{
    
}