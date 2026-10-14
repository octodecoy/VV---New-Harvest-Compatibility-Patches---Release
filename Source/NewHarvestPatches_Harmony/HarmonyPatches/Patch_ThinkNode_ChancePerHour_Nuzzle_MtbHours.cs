using Verse;
using Verse.AI;
using HarmonyLib;
using UnityEngine;

namespace NewHarvestPatches_Harmony;

/// <summary>
/// Scales an animal's nuzzle interval by the severity of our NourishedPet hediff, so an animal fed our
/// animal food nuzzles more often. MtbHours is a MEAN TIME BETWEEN, so the multiplier is inverted from
/// intuition: severity below 1 shortens the interval (more nuzzling), severity above 1 lengthens it. The
/// NourishedHediffSeverity setting defaults to 0.05, roughly a twentyfold increase in nuzzle frequency.
/// Animals without the hediff are untouched, so this cannot affect a colony that does not use our food.
/// </summary>
[HarmonyPatch(typeof(ThinkNode_ChancePerHour_Nuzzle), "MtbHours")]
public class Patch_ThinkNode_ChancePerHour_Nuzzle_MtbHours
{
    public static void Postfix(Pawn pawn, ref float __result)
    {
        var hediff = pawn?.health?.hediffSet?.GetFirstHediffOfDef(InternalHediffDefOf.VV_NHCP_NourishedPet);
        if (hediff != null)
        {
            // Floor at the slider's own minimum so a severity of 0 (from another mod, or a rounding
            // artifact) cannot collapse MtbHours to 0 and make the animal nuzzle every tick.
            float mult = Mathf.Max(hediff.Severity, Settings.NourishedHediffSeverityRange.min);
            __result *= mult;
        }
    }
}