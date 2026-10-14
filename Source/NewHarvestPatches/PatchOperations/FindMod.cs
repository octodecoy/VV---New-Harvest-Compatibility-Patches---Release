namespace NewHarvestPatches;

/// <summary>
/// Runs caseTrue or caseFalse depending on which other mods are installed. Richer than vanilla's
/// MayRequire: it combines a list under And/Or/Not/Xor, and supports TWO lists with their own internal
/// logic plus a third operator between them, which is what expresses "(A or B) and (C or D)" - a
/// compatibility patch needing one mod from each of two families.
/// XML: mods + logic, or modGroupOne/modGroupTwo + logicGroupOne/logicGroupTwo/logicBetweenGroups;
/// packageId switches matching from display name to package identifier.
/// </summary>
internal class FindMod : PatchOperationExtended
{
    private readonly List<string> mods = null;
    private readonly List<string> modGroupOne = null;
    private readonly List<string> modGroupTwo = null;
    private readonly bool packageId = false;
    private readonly CompareLogic logicGroupOne = CompareLogic.Or;
    private readonly CompareLogic logicGroupTwo = CompareLogic.Or;
    private readonly CompareLogic logicBetweenGroups = CompareLogic.Or;

    /// <summary>
    /// A plain <c>mods</c> list takes precedence; the two-group form applies only when neither is empty.
    /// Returns true when the test resolves but the matching branch is absent - "condition evaluated, nothing
    /// to do" is success, not failure, so an unused branch does not log a patch error.
    /// </summary>
    protected override bool ApplyWorker(XmlDocument xml)
    {
        bool hasModsList = !mods.NullOrEmpty();
        bool hasModGroupOne = !modGroupOne.NullOrEmpty();
        bool hasModGroupTwo = !modGroupTwo.NullOrEmpty();
        if (!hasModsList && !hasModGroupOne && !hasModGroupTwo)
            return false;

        bool flag;
        if (hasModsList)
        {
            flag = GetFlag(mods, logic);
        }
        else if (hasModGroupOne && hasModGroupTwo)
        {
            flag = GetGroupFlag();
        }
        else
        {
            LogMessage(() => "No mods supplied for FindMod patch operation.", LogMessageType.Error);
            return false;
        }

        if (flag)
        {
            if (caseTrue != null)
            {
                return caseTrue.Apply(xml);
            }
        }
        else if (caseFalse != null)
        {
            return caseFalse.Apply(xml);
        }

        return true;
    }

    private bool GetGroupFlag()
    {
        bool groupOneFlag = GetFlag(modGroupOne, logicGroupOne);
        return logicBetweenGroups switch
        {
            CompareLogic.Or => groupOneFlag || GetFlag(modGroupTwo, logicGroupTwo),
            CompareLogic.And => groupOneFlag && GetFlag(modGroupTwo, logicGroupTwo),
            // Just use <mods> and caseFalse for Not
            CompareLogic.Xor => groupOneFlag ^ GetFlag(modGroupTwo, logicGroupTwo),
            _ => false,
        };
    }

    /// <summary>
    /// Presence test for one list. packageId matching ignores the "_steam" suffix Steam appends, so a patch
    /// works for both Steam and manual installs; the name path is a case-insensitive display-name compare.
    /// </summary>
    private bool GetFlag(List<string> modsList, CompareLogic thisLogic)
    {
        bool IsInstalled(string mod) => packageId
            ? IsModActive(mod)   // ignorePostfix: true
            : LoadedModManager.RunningMods.Any(m => m.Name.ToLower() == mod.ToLower());

        return CompareUtility.MatchesLogic(thisLogic, modsList, IsInstalled);
    }
}