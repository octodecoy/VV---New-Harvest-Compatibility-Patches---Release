namespace NewHarvestPatches;

/// <summary>
/// Groups single-stuff wood floors into one architect-menu dropdown per wood type, so a dozen wood
/// mods cost a dozen menu slots instead of a dozen each. Which floors get folded in is user-selectable
/// (this mod's woods, vanilla WoodLog, other mods' woods) because grouping other mods' floors is a
/// visible change to their menus, not ours.
/// Boot-only: rewrites <c>designationCategory</c>/<c>designatorDropdown</c>/<c>uiOrder</c> on shared
/// TerrainDefs and re-resolves the affected categories, so the backing settings are marked
/// "requires restart".
/// </summary>
internal static class FloorDropdownAdder
{
    /// <param name="moveNewHarvestWoodFloors">Fold in floors costing a New Harvest wood.</param>
    /// <param name="moveBaseWoodFloors">Fold in floors costing vanilla WoodLog.</param>
    /// <param name="moveModWoodFloors">Fold in wood floors from any other source.</param>
    public static void TryAddFloorDropdowns(bool moveNewHarvestWoodFloors, bool moveBaseWoodFloors, bool moveModWoodFloors)
    {
        ActionRunner.Run(nameof(FloorDropdownAdder), nameof(TryAddFloorDropdowns), () => AddFloorDropdowns(moveNewHarvestWoodFloors, moveBaseWoodFloors, moveModWoodFloors));
    }

    /// <summary>
    /// The New Harvest wood ThingDefs, identified by defName rather than stuff category so a wood that
    /// another mod re-categorized still counts as ours.
    /// </summary>
    /// <returns>Defs whose name contains the parent prefix and ends in "Wood" or "Lumber"; empty if none.</returns>
    private static List<ThingDef> GetNewHarvestWoodDefs()
    {
        var woodList = DefUtility.ThingDefs.IndustrialResourceDefs;
        if (woodList.NullOrEmpty())
            return [];

        // isWood = contains (not just startswith, for cases where another prefix is used (MO logs)) "VV_" and endswith "Wood" or "Lumber"
        return [.. woodList
            .Where(kvp => kvp.Value.isWood)
            .Select(kvp => kvp.Key)];
    }

    /// <summary>
    /// Narrows an already wood-only floor list to the sources the user opted into. "Mod" is defined by
    /// exclusion - anything that is neither a New Harvest wood nor vanilla WoodLog - so floors from
    /// wood mods this code has never heard of are still classified correctly.
    /// </summary>
    /// <param name="floors">Candidate floors, expected to have passed <see cref="IsSingleStuffWoodFloor"/>.</param>
    /// <param name="moveNewHarvestWoodFloors">Keep floors costing a New Harvest wood.</param>
    /// <param name="moveBaseWoodFloors">Keep floors costing vanilla WoodLog.</param>
    /// <param name="moveModWoodFloors">Keep wood floors from any other source.</param>
    /// <returns>The kept floors; empty when the input is null/empty or nothing matches.</returns>
    private static List<TerrainDef> FilterFloorList(
        this IEnumerable<TerrainDef> floors,
        bool moveNewHarvestWoodFloors,
        bool moveBaseWoodFloors,
        bool moveModWoodFloors)
    {
        if (floors.EnumerableNullOrEmpty())
            return [];

        var newHarvestWoodDefs = new HashSet<ThingDef>(GetNewHarvestWoodDefs());
        var baseWoodDefs = new HashSet<ThingDef> { ThingDefOf.WoodLog };

        bool IsNewHarvestFloor(TerrainDef td) =>
            td.costList.Any(cost => newHarvestWoodDefs.Contains(cost.thingDef));

        bool IsBaseWoodFloor(TerrainDef td) =>
            td.costList.Any(cost => baseWoodDefs.Contains(cost.thingDef));

        // Both classifications scan the floor's costList, and the "mod" branch needs the negation of
        // each - so they are evaluated once per floor into locals instead of up to twice apiece.
        return [.. floors.Where(td =>
        {
            bool isNewHarvest = IsNewHarvestFloor(td);
            bool isBaseWood = IsBaseWoodFloor(td);

            return (moveNewHarvestWoodFloors && isNewHarvest)
                || (moveBaseWoodFloors && isBaseWood)
                || (moveModWoodFloors && !isNewHarvest && !isBaseWood);
        })];
    }

    /// <summary>
    /// A wood floor is a single-stuff, non-bridge, non-styled floor tile made from a stuff
    /// tagged as wood. Deliberately checks the stuff category's defName (untranslated), not
    /// any translated label, so this works regardless of the active game language.
    /// The single-cost requirement is what makes the later "group by costList[0]" grouping sound.
    /// </summary>
    private static bool IsSingleStuffWoodFloor(TerrainDef td)
    {
        if (td == null || td.bridge || !td.IsFloor || td.dominantStyleCategory != null)
            return false;

        if (td.costList == null || td.costList.Count != 1)
            return false;

        var stuffCategories = td.costList[0]?.thingDef?.stuffProps?.categories;
        return stuffCategories?.Any(cat => cat.defName != null && cat.defName.ContainsIgnoreCase("Wood")) == true;
    }

    /// <summary>
    /// Body of <see cref="TryAddFloorDropdowns"/>. Buckets the selected floors by their single cost
    /// def, creates one dropdown group per bucket, and moves the whole bucket into the Floors category
    /// under a shared <c>uiOrder</c> (the bucket's minimum) so the group occupies one slot at the
    /// position its earliest member used to hold. Buckets of one are skipped - a dropdown containing a
    /// single entry is strictly worse than the plain button. Every category a floor was moved out of is
    /// re-resolved alongside Floors, otherwise the old menu keeps a stale entry.
    /// </summary>
    private static void AddFloorDropdowns(bool moveNewHarvestWoodFloors, bool moveBaseWoodFloors, bool moveModWoodFloors)
    {
        if (!moveNewHarvestWoodFloors && !moveBaseWoodFloors && !moveModWoodFloors)
            return;

        var floorsCategory = DesignationCategoryDefOf.Floors;
        if (floorsCategory == null) // Probably don't need
            return;

        var floors = DefDatabase<TerrainDef>.AllDefs
            .Where(IsSingleStuffWoodFloor)
            .FilterFloorList(moveNewHarvestWoodFloors, moveBaseWoodFloors, moveModWoodFloors);

        if (floors.Count == 0)
        {
            LogMessage(() => "No floors found to add dropdowns to.", LogMessageType.Warning);
            return;
        }

        const string defPrefix = $"{ModName.Prefix.Compat}FloorDropdown_";

        var dictionary = new Dictionary<ThingDef, (DesignatorDropdownGroupDef dropdown, List<TerrainDef> floors)>();
        foreach (var floor in floors)
        {
            var costDef = floor.costList[0].thingDef;
            if (!dictionary.TryGetValue(costDef, out var entry))
            {
                var dropdown = new DesignatorDropdownGroupDef
                {
                    defName = defPrefix + costDef.defName
                };
                entry = (dropdown, new List<TerrainDef>());
                dictionary[costDef] = entry;
            }
            entry.floors.Add(floor);
        }

        if (dictionary.Count == 0)
            return;

        var designationCategories = new HashSet<DesignationCategoryDef>
        {
            floorsCategory
        };

        bool resolve = false;
        foreach (var kvp in dictionary)
        {
            if (kvp.Value.floors.Count <= 1)
                continue; // Try to avoid putting single items in a dropdown


            float order = kvp.Value.floors.Min(floor => floor.uiOrder);

            foreach (var floor in kvp.Value.floors)
            {
                var designationCategory = floor.designationCategory;
                bool changeCategory = designationCategory != floorsCategory;
                if (changeCategory)
                    floor.designationCategory = floorsCategory;

                var oldDesignatorDropdown = floor.designatorDropdown;
                floor.designatorDropdown = kvp.Value.dropdown;
                floor.uiOrder = order;

                if (designationCategory != null)
                    designationCategories.Add(designationCategory); // For resolving after

                resolve = true;

                var oldDropdownName = (oldDesignatorDropdown != null && !string.IsNullOrWhiteSpace(oldDesignatorDropdown.defName))
                    ? oldDesignatorDropdown.defName
                    : "none";

                LogMessage(() => $"Changed designatorDropdown on [{floor.defName}] from [{oldDropdownName}] to [{kvp.Value.dropdown.defName}].  designationCategory changed to {floorsCategory.defName} = {changeCategory}");
            }
        }

        if (resolve)
        {
            foreach (var category in designationCategories)
            {
                category.ResolveReferences();
            }
        }
    }
}