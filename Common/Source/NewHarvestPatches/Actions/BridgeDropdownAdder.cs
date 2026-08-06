namespace NewHarvestPatches;

/// <summary>
/// Collapses this mod's wood bridge variants into the same architect-menu dropdown as vanilla's
/// Bridge, so adding wood types costs one menu slot instead of one per type.
/// Boot-only: rewrites <c>designationCategory</c>/<c>designatorDropdown</c> on shared TerrainDefs and
/// re-resolves the affected categories, which cannot be undone in place - the backing setting is
/// therefore marked "requires restart".
/// </summary>
internal static class BridgeDropdownAdder
{
    public static void TryAddBridgeDropdown()
    {
        ActionRunner.Run(nameof(BridgeDropdownAdder), nameof(TryAddBridgeDropdown), AddDropdown);
    }

    /// <summary>
    /// Moves every <c>VV_*Bridge</c> TerrainDef into vanilla Bridge's designation category and
    /// dropdown group, creating that group if vanilla has none. Vanilla's Bridge is the anchor rather
    /// than a mod-defined group so the bridges keep whatever category another mod may have moved
    /// vanilla's into. Every designation category that lost or gained a def is re-resolved afterwards,
    /// including the ones the bridges came from - skipping those leaves stale entries in the old menu.
    /// </summary>
    private static void AddDropdown()
    {
        var baseBridge = DefDatabase<TerrainDef>.GetNamedSilentFail("Bridge");
        if (baseBridge == null)
            return;

        var designationCategoryToUse = baseBridge.designationCategory;
        if (designationCategoryToUse == null)
            return;

        var modBridges = DefDatabase<TerrainDef>.AllDefs
            .Where(td => td?.defName.StartsAndEndsWith(start: "VV_", end: "Bridge") == true && td.bridge)
            .ToList();

        if (modBridges.Count == 0)
            return;

        var bridgeDropdownToUse = baseBridge.designatorDropdown ??= new DesignatorDropdownGroupDef
        {
            defName = $"{ModName.Prefix.Compat}BridgeDropdown"
        };

        var designationCategories = new HashSet<DesignationCategoryDef>
        {
            designationCategoryToUse
        };

        foreach (var bridge in modBridges)
        {
            var modBridgeDesignationCategory = bridge.designationCategory;
            if (modBridgeDesignationCategory == null || modBridgeDesignationCategory != designationCategoryToUse)
                bridge.designationCategory = designationCategoryToUse;

            if (bridge.designatorDropdown == null || bridge.designatorDropdown != bridgeDropdownToUse)
                bridge.designatorDropdown = bridgeDropdownToUse;

            if (modBridgeDesignationCategory != null)
                designationCategories.Add(modBridgeDesignationCategory);
        }

        foreach (var category in designationCategories)
        {
            category.ResolveReferences();
        }
    }
}