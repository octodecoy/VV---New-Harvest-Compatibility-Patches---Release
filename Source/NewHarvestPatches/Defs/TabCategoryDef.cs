namespace NewHarvestPatches;

/// <summary>
/// One tab in the settings window, declared in XML so tabs can be added or reordered without a code change.
/// The Def's own label is the tab caption and is translated normally; only the icon path is not.
/// </summary>
public class TabCategoryDef : Def
{
    [NoTranslate]
    public string texPath;
}