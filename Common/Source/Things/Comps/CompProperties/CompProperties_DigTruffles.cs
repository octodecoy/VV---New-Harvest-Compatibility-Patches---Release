namespace NewHarvestPatches;

public class CompProperties_DigTruffles : CompProperties
{
    public CompProperties_DigTruffles()
    {
        compClass = typeof(CompDigTruffles);    
    }

    public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
    {
        foreach (string error in base.ConfigErrors(parentDef))
        {
            yield return error;
        }
    
        if (parentDef.race?.Animal != true)
            yield return $"{Prefix}CompDigTruffles can only be added to animals.";
    }
}