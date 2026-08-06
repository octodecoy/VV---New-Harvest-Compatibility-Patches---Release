namespace NewHarvestPatches;

/// <summary>
/// Marker for a window opened from the settings menu. Carries no members: it exists so
/// <see cref="NewHarvestPatchesMod.WriteSettings"/> can find and close these windows before applying
/// settings, letting each one's PostClose commit its edits first.
/// </summary>
public interface ISettingsMenuChildWindow { }