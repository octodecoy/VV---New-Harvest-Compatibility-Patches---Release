using Verse;
using RimWorld;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using System;

namespace NewHarvestPatches_Harmony;

/// <summary>
/// Makes our fungus-poisoning hediff respect the same immunities as food poisoning. Vanilla's
/// <c>DoIngestionOutcome</c> is a flat <c>Rand.Value &lt; chance</c> roll with no pawn input, so a pawn with
/// the Iron Stomach trait or a poisoning-resistant gene is just as likely to be poisoned by our mushrooms as
/// anyone else. This prefix substitutes a roll against a chance scaled by those same modifiers.
/// </summary>
/// <remarks>
/// The patch is deliberately narrow: it only takes over when the doer is a GiveHediff doer whose hediffDef
/// was named by an <see cref="IngestionAffectedHediffsDef"/>, and returns true (run vanilla) for everything
/// else - so every other mod's ingestibles keep vanilla behaviour. Note that our own
/// <c>IngestionOutcomeDoer_GiveHediffConditional</c> derives from GiveHediff and is therefore also eligible;
/// its conditions still apply, because the reflective call below dispatches virtually to its override.
/// </remarks>
[HarmonyPatch(typeof(IngestionOutcomeDoer), nameof(IngestionOutcomeDoer.DoIngestionOutcome))]
public static class Patch_IngestionOutcomeDoer_DoIngestionOutcome
{
    private static MethodInfo s_doIngestionOutcomeSpecialMethod;

    /// <summary>
    /// Resolves the protected <c>DoIngestionOutcomeSpecial</c> once, before any patching happens, and vetoes
    /// the whole patch class if it is gone. Without it the prefix could suppress vanilla and then be unable
    /// to apply anything, silently deleting the hediff outcome - failing to patch is the safer outcome.
    /// </summary>
    /// <param name="original">Harmony calls Prepare once per class with null, then again per target method
    /// with that method. The null check confines the lookup to the single class-level call.</param>
    /// <returns>False to skip patching this entire class - see the Harmony auxiliary-patch docs
    /// (https://harmony.pardeike.net/articles/patching-auxiliary.html).</returns>
    public static bool Prepare(MethodInfo original)
    {
        if (s_doIngestionOutcomeSpecialMethod is null && original is null)
        {
            s_doIngestionOutcomeSpecialMethod = AccessTools.Method(typeof(IngestionOutcomeDoer_GiveHediff), "DoIngestionOutcomeSpecial");
            if (s_doIngestionOutcomeSpecialMethod is null)
            {
                LogMessage(() => $"Failed to find method DoIngestionOutcomeSpecial on {nameof(IngestionOutcomeDoer_GiveHediff)}!", LogMessageType.Error, ignoreSetting: true);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Replaces the vanilla body for our hediffs only: rolls against the modified chance and, on success,
    /// calls the doer's own <c>DoIngestionOutcomeSpecial</c> reflectively (it is protected, and going through
    /// the MethodInfo still dispatches to the runtime type's override).
    /// </summary>
    /// <remarks>
    /// Runs last so any other mod's prefix on this method gets to decide first; if one of them skips vanilla,
    /// this never runs and the hediff simply behaves as that mod intends.
    /// </remarks>
    /// <returns>False to skip vanilla - we have already done the roll ourselves. True to let vanilla run
    /// untouched, which is the path every non-matching ingestible takes.</returns>
    [HarmonyPriority(Priority.Last)]
    public static bool Prefix(IngestionOutcomeDoer __instance, Pawn pawn, Thing ingested, int ingestedCount)
    {
        if (ControlChance(__instance, pawn, ingested, out var hediffDoer))
        {
            if (DoIngestionOutcome(hediffDoer, pawn, ingested))
            {    
                s_doIngestionOutcomeSpecialMethod.Invoke(__instance,
                [
                    pawn,
                    ingested,
                    ingestedCount
                ]);
            }

            return false; // skip vanilla
        }

        return true; // run vanilla
    }

    /// <summary>
    /// Whether this ingestion is ours to take over. Every condition must hold, so anything unexpected falls
    /// back to vanilla rather than being handled half-way. A <c>chance</c> of 0 is excluded because vanilla
    /// would never fire it either and our modifiers only ever reduce.
    /// </summary>
    private static bool ControlChance(IngestionOutcomeDoer doer, Pawn pawn, Thing ingested, out IngestionOutcomeDoer_GiveHediff hediffDoer)
    {
        hediffDoer = doer as IngestionOutcomeDoer_GiveHediff;

        return hediffDoer is not null
            && pawn is not null
            && ingested is not null
            && doer.chance > 0f
            && IngestionAffectedHediffDefSet?.Contains(hediffDoer.hediffDef) is true;
    }

    private static bool DoIngestionOutcome(IngestionOutcomeDoer_GiveHediff hediffDoer, Pawn pawn, Thing ingested)
    {
        float newChance = CalculateChance(pawn, ingested.def, hediffDoer.chance);
        float rand = Rand.Value;
        bool apply = rand < newChance;
        LogMessage(() => $"{(apply ? "Applying" : "Skipping")} hediff [{hediffDoer.hediffDef?.defName}] for [{pawn.Name}] ingesting [{ingested.LabelCap + " x" + ingested.stackCount}]: rolled:[{rand}], baseChance=[{hediffDoer.chance}], newChance=[{newChance}]");
        return apply;
    }

    /// <summary>
    /// Reimplements <c>FoodUtility.GetFoodPoisonChanceFactor</c> and
    /// <c>TryGetFoodPoisoningChanceOverrideFromTraits</c>, but starting from the doer's own chance instead of
    /// <c>Find.Storyteller.difficulty.foodPoisonChanceFactor</c>. Calling vanilla and dividing the
    /// storyteller factor back out is not an option - that factor can legitimately be 0.
    /// </summary>
    /// <param name="baselineChance">The doer's authored chance, which the modifiers scale down.</param>
    /// <returns>The doer's chance after every applicable trait override, hediff and gene factor. Only
    /// factors below 1 are applied, so a pawn with no relevant traits gets the authored chance unchanged and
    /// nothing can make our hediff MORE likely than authored.</returns>
    private static float CalculateChance(Pawn pawn, ThingDef ingestedDef, float baselineChance = 1f)
    {
        if (ModsConfig.AnomalyActive && pawn.IsMutant && pawn.mutant.Def.preventIllnesses)
            return 0f;

        // poisonChanceOverride isn't actually used on any vanilla ingestibles, but check anyways, whatever.
        if (pawn.story?.traits is { } traits && traits.AnyTraitHasIngestibleOverrides)
        {
            var allTraits = traits.allTraits;

            for (int i = 0; i < allTraits.Count; i++)
            {
                var trait = allTraits[i];

                if (trait.Suppressed)
                    continue;

                var ingestibleModifiers = trait.CurrentData.ingestibleModifiers;

                if (ingestibleModifiers.NullOrEmpty())
                    continue;

                for (int j = 0; j < ingestibleModifiers.Count; j++)
                { 
                    var ingestibleModifier = ingestibleModifiers[j];
                    if (ingestibleModifier.ingestible is { } ingestibleDef && ingestibleDef == ingestedDef)
                    {
                        float overrideChance = Mathf.Clamp01(ingestibleModifier.poisonChanceOverride);
                        if (overrideChance < 1f)
                            return overrideChance;
                    }
                }
            }
        }      
  
        float num = baselineChance; // 1f instead of 'Find.Storyteller.difficulty.foodPoisonChanceFactor' - we want to calculate from baseline but could use something else if needed.

        var hediffs = pawn.health?.hediffSet?.hediffs;
        if (hediffs is not null)
        {
            for (int i = 0; i < hediffs.Count; i++)
            {
                var curStage = hediffs[i].CurStage;
                if (curStage is not null)
                {
                    float factor = curStage.foodPoisoningChanceFactor;
                    if (factor < 1f)
                    {
                        num *= factor;
                        if (num <= 0f)
                            return 0f;
                    }
                }
            }
        }

        if (ModsConfig.BiotechActive)
        {
            var genes = pawn.genes?.GenesListForReading;
            if (genes is not null)
            {
                for (int i = 0; i < genes.Count; i++)
                {
                    var gene = genes[i];
                    if (gene.Active)
                    {
                        float factor = gene.def.foodPoisoningChanceFactor;
                        if (factor < 1f)
                        {
                            num *= factor;
                            if (num <= 0f)
                                return 0f;
                        }
                    }
                }
            }
        }

        return num;
    }
}