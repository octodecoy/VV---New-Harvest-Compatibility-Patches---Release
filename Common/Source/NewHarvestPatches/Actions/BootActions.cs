namespace NewHarvestPatches;

/// <summary>
/// Boot-only counterpart to <see cref="LiveActions"/>, run once from <see cref="Bootstrap.Initialize"/>
/// after defs finish loading. Holds every action whose effect cannot be diffed and undone in place -
/// recipe list injection, designator dropdown surgery, dryad UI slots, harvest-yield rewrites - which
/// is why the settings backing them are marked "requires restart" in the settings window.
/// </summary>
internal static class BootActions
{
    /// <summary>
    /// Runs each boot action whose owning setting is enabled.
    /// </summary>
    /// <param name="enabledSettings">
    /// Currently enabled setting names - membership already accounts for each setting's [RequiresMod], so
    /// testing it is the ONLY gate needed here; no action re-checks mod presence.
    /// The two trailing actions (lacquered-wood overlap removal, forage recipe injection) sit outside
    /// this gate deliberately - they are module-gated only and always run.
    /// </param>
    public static void TryApplyAll(HashSet<string> enabledSettings)
    {
        bool hasEnabledSettings = enabledSettings.Count > 0;
        if (hasEnabledSettings)
        {
            CategoryLabelInfo.TrySetCategoryLabels();
        }

        // We use a LongEventHandler callback so the actions run after the game finishes its own boot work
        // and hopefully after other mods also do their own boot work, so we can see their defs and fold them in, or vice versa.
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            if (hasEnabledSettings)
            {
                if (enabledSettings.Contains(nameof(Settings.AddMoreWoodFloors)))
                {
                    BridgeDropdownAdder.TryAddBridgeDropdown();
                }

                if (enabledSettings.Any(s => s.EndsWith("ToDropdowns")))
                {
                    FloorDropdownAdder.TryAddFloorDropdowns(
                        moveNewHarvestWoodFloors: enabledSettings.Contains(nameof(Settings.NewHarvestWoodFloorsToDropdowns)),
                        moveBaseWoodFloors: enabledSettings.Contains(nameof(Settings.BaseWoodFloorsToDropdowns)),
                        moveModWoodFloors: enabledSettings.Contains(nameof(Settings.ModWoodFloorsToDropdowns)));
                } 

                if (enabledSettings.Any(s => s.StartsWith("Merge") && s.EndsWith("Category")))
                {
                    CategoryApplier.TryApplyAtBoot();
                }

                if (enabledSettings.Contains(nameof(Settings.AddWoodDryads)))
                {
                    DryadUISorter.TrySortDryads();
                }

                if (enabledSettings.Contains(nameof(Settings.GrainsProduceVCEFlourSecondary)))
                {
                    FlourOutputFixer.TryFixFlourOutput();
                }

                if (enabledSettings.Contains(nameof(Settings.AddMapleSyrupChain)))
                {
                    RecipePropagator.TryAddRecipes(
                        targetRecipe: DefDatabase<RecipeDef>.GetNamed("CookMealSimple"),
                        recipesToAdd:
                        [
                            DefDatabase<RecipeDef>.GetNamedSilentFail("VV_NHCP_MakeMapleSyrup"),
                            DefDatabase<RecipeDef>.GetNamedSilentFail("VV_NHCP_MakeMapleSyrupBulk"),
                        ]
                    );
                }
            }

            // Add our animal food recipes anywhere you can make kibble.
            if (HasForageModule)
            {
                RecipePropagator.TryAddRecipes(
                    targetRecipe: DefDatabase<RecipeDef>.GetNamed("Make_Kibble"),
                    recipesToAdd:
                    [
                        DefDatabase<RecipeDef>.GetNamedSilentFail("VV_MakeBeetPulp"),
                        DefDatabase<RecipeDef>.GetNamedSilentFail("VV_MakeBeetPulpBulk"),
                        DefDatabase<RecipeDef>.GetNamedSilentFail("VV_MakeFodderMix"),
                        DefDatabase<RecipeDef>.GetNamedSilentFail("VV_MakeFodderMixBulk"),
                        DefDatabase<RecipeDef>.GetNamedSilentFail("VV_MakeProteinKibble"),
                        DefDatabase<RecipeDef>.GetNamedSilentFail("VV_MakeProteinKibbleBulk")
                    ]
                );
            }

            // Overlap removal should be done last.
            // Not currently in use - needs more work (maybe a blacklist, or some more sophisticated way of checking if a recipe's product is actually meant to be a ingredient)
            // RecipeProductIngredientOverlapRemover.TryRemoveAllOverlap();
        });
    }
}