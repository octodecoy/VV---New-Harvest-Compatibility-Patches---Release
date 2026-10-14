namespace NewHarvestPatches;

public static partial class Extensions
{
    public static string LabelCapSafe(this Def def)
    {
        if (def == null)
            return "null";

        string labelCap = def.LabelCap.RawText;
        return labelCap.NullOrEmpty() ? def.defName : labelCap;
    }
}