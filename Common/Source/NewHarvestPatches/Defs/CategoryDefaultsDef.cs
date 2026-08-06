namespace NewHarvestPatches;

/// <summary>
/// User-extensible default category assignments. Each instance maps a list of ThingDef
/// names to one of our dummy categories. Names are plain strings so entries for absent
/// mods resolve silently instead of producing cross-reference errors, and are marked
/// NoTranslate so they stay out of def-injection translation templates. All instances are
/// merged by <see cref="CategoryClassifier"/> at boot; persisted user choices always win.
/// </summary>
public class CategoryDefaultsDef : Def
{
    [NoTranslate]
    public List<string> defNames = [];
    public ThingCategoryDef targetDefaultCategory;

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors())
            yield return error;

        if (defNames.NullOrEmpty())
            yield return "defNames is null or empty";
        else if (targetDefaultCategory == null)
            yield return "targetDefaultCategory is null";
        else if (!targetDefaultCategory.defName.StartsWith(Category.Prefix.DummyCategory))
            yield return $"targetDefaultCategory [{targetDefaultCategory.defName}] is not one of our categories";
    }
}
