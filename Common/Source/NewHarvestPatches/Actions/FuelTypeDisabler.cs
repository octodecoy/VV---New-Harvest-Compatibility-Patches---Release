namespace NewHarvestPatches;

/// <summary>
/// Disallow specified fuel types on all refuelable buildings.
/// Reflection after checking base CompProperties_Refuelable so we can hopefully get any mod added fuelFilter using comps too.
/// Live-appliable: re-diffs against the FuelTypes setting every call - only reverts pairs no
/// longer desired and only (re-)applies pairs not already disallowed by us, so an unchanged
/// setting produces no writes or logs. An unchanged SELECTION is also detected up front and
/// returns before any def lookup or pair walk at all - see <see cref="s_lastDesiredNames"/>.
/// </summary>
internal static class FuelTypeDisabler
{
    // Every (fuelFilter, fuelDef) pair currently disallowed by us, so a revert only re-allows
    // exactly what we touched - never something another mod or the base game disallowed itself.
    private static readonly HashSet<(ThingFilter Filter, ThingDef FuelDef)> s_disallowedPairs = [];

    // The fuel defNames the last completed call applied (empty when it applied nothing), or null
    // before the first call. Sole purpose is the change-aware early-out in DisallowFuels.
    private static HashSet<string> s_lastDesiredNames;

    // Every refuelable building's fuel filter plus a label for logging. The def graph (and each
    // def's comp list) is fixed once defs finish loading, so the DefDatabase sweep and the
    // per-comp reflection below run once instead of on every settings-window close.
    private static List<(ThingFilter Filter, string Label)> s_fuelFilters;

    /// <summary>Lazily built, then cached for the session - see <see cref="BuildFuelFilters"/>.</summary>
    private static List<(ThingFilter Filter, string Label)> FuelFilters
    {
        get
        {
            if (s_fuelFilters == null) 
                BuildFuelFilters();

            return s_fuelFilters;
        }
    }

    public static void TryDisallowFuels()
    {
        ActionRunner.Run(nameof(FuelTypeDisabler), nameof(TryDisallowFuels), DisallowFuels);
    }

    /// <summary>
    /// Body of <see cref="TryDisallowFuels"/>. Returns immediately when the selection matches the one
    /// already applied, otherwise reverts everything this mod applied that is no longer selected and
    /// applies what is missing. Any condition that means "nothing should be disallowed" - module
    /// absent, settings hidden, no selection - falls through to <see cref="ResetAll"/> rather than
    /// returning early, otherwise turning the feature off would strand the previously applied state.
    /// </summary>
    private static void DisallowFuels()
    {
        HashSet<string> desiredNames = IsSettingAvailable(nameof(Settings.FuelTypes))
            ? [.. ExtractNamesFromEnabledSettings(Setting.Prefix.DisabledFuel_)]
            : [];

        // Change-aware early-out. Everything else this method reads is fixed for the session - the
        // fuel filter list is built once from the def graph (see FuelFilters) and availability never
        // changes - so an identical selection can only ask for the state already applied. Without
        // this, every settings-window close paid for a def lookup plus a walk of every tracked pair
        // even when nothing was touched.
        if (s_lastDesiredNames != null && s_lastDesiredNames.SetEquals(desiredNames))
            return;

        s_lastDesiredNames = desiredNames;

        var fuelDefs = DefUtility.GetDefsOfTypeByDefNames<ThingDef>(defNames: [.. desiredNames]).ToHashSet();
        if (fuelDefs.Count == 0)
        {
            ResetAll();
            return;
        }

        // Reset pairs we previously disallowed that are no longer desired. Because the filter list is
        // session-constant, a tracked pair can only fall out of the desired set by its fuel def
        // leaving the selection - so testing fuel membership is equivalent to diffing against the
        // full (filter x fuel) product, without building it.
        foreach (var pair in s_disallowedPairs.Where(p => !fuelDefs.Contains(p.FuelDef)).ToList())
        {
            pair.Filter.SetAllow(pair.FuelDef, true);
            LogMessage(() => $"Re-allowed [{pair.FuelDef.defName}] on filter where it was previously disallowed.");
            s_disallowedPairs.Remove(pair);
        }

        // Apply pairs that are newly desired and not already disallowed by us. Don't gate on
        // AllowedThingDefs.Contains(fuelDef) as the desired test - our own prior SetAllow(false)
        // removes the def from the filter, which would make an already-disallowed pair look "not
        // desired" and get reverted next call.
        foreach (var (filter, defLabel) in FuelFilters)
        {
            foreach (var fuelDef in fuelDefs)
            {
                var pair = (filter, fuelDef);
                if (s_disallowedPairs.Contains(pair))
                    continue;

                if (!filter.Allows(fuelDef))
                    continue; // Already disallowed by another mod/base game - don't claim it as ours.

                // Vanilla CompRefuelable.EjectFuel calls fuelFilter.AllowedThingDefs.First() with no
                // empty check, so emptying a filter makes the eject gizmo throw. Always leave one def.
                if (filter.AllowedDefCount <= 1)
                {
                    LogMessage(() => $"Skipped disallowing [{fuelDef.defName}] on [{defLabel}] - it is the last fuel that filter allows.", LogMessageType.Warning);
                    continue;
                }

                filter.SetAllow(fuelDef, false);
                s_disallowedPairs.Add(pair);
                LogMessage(() => $"Disallowed [{fuelDef.defName}] on [{defLabel}]");
            }
        }
    }

    /// <summary>
    /// One-time sweep for every building comp exposing a 'fuelFilter', base CompProperties_Refuelable
    /// first then reflection over the remaining comps so mod-added refuelable comps are covered too.
    /// </summary>
    private static void BuildFuelFilters()
    {
        s_fuelFilters = [];

        foreach (var def in DefDatabase<ThingDef>.AllDefs)
        {
            if (def.building == null) // Might need to rethink
                continue;

            if (def.comps.NullOrEmpty())
                continue;

            var compPropertiesRefuelable = def.GetCompProperties<CompProperties_Refuelable>();
            if (compPropertiesRefuelable != null)
            {
                if (compPropertiesRefuelable.fuelFilter != null)
                    s_fuelFilters.Add((compPropertiesRefuelable.fuelFilter, def.defName));

                continue; // Continue to next def, no need to check comps further if we found the base refuelable comp, at least thats my thinking
            }

            // Check all comp properties for a 'fuelFilter' field/property
            foreach (var comp in def.comps)
            {
                var compType = comp.GetType();
                if (compType == null)
                    continue;

                var fuelFilterField = compType.GetField("fuelFilter");
                if (fuelFilterField == null)
                    continue;

                if (fuelFilterField.GetValue(comp) is not ThingFilter fuelFilter)
                    continue;

                s_fuelFilters.Add((fuelFilter, def.defName + " via reflection"));
            }
        }
    }

    /// <summary>
    /// Re-allows every pair this mod disallowed and drops the tracking set, returning all fuel filters
    /// to the state they had before this mod touched them.
    /// </summary>
    private static void ResetAll()
    {
        if (s_disallowedPairs.Count == 0)
            return;

        foreach (var (filter, fuelDef) in s_disallowedPairs)
        {
            filter.SetAllow(fuelDef, true);
            LogMessage(() => $"Re-allowed [{fuelDef.defName}] on filter previously disallowed by this mod.");
        }
        s_disallowedPairs.Clear();
    }
}
