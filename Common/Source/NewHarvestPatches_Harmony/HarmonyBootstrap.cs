global using NewHarvestPatches;
global using static NewHarvestPatches.Utils.Logger;
global using static NewHarvestPatches.DataCollections;
global using static NewHarvestPatches.Utils.SettingChecker;
using System;
using System.Linq;
using Verse;
using HarmonyLib;


namespace NewHarvestPatches_Harmony;

/// <summary>
/// Entry point of the separate Harmony assembly. This assembly exists solely so that every UNGATED
/// reference to HarmonyLib lives behind a mod dependency the user may not have installed - the main
/// <c>NewHarvestPatches</c> assembly must load and run with no Harmony present, and a Harmony type in a
/// field or method signature there would fail to resolve at class load.
/// </summary>
/// <remarks>
/// Patching is decided once at startup from module presence and settings, never re-evaluated, which is why
/// the corresponding settings are flagged restart-required in the UI. Turning one off at runtime leaves the
/// patch applied; the patch bodies do not re-check their setting.
/// </remarks>
[StaticConstructorOnStartup]
public static class HarmonyBootstrap
{
    static HarmonyBootstrap() => TryDoHarmonyPatches();

    private static void TryDoHarmonyPatches()
    {
        ActionRunner.Run(nameof(HarmonyBootstrap), nameof(TryDoHarmonyPatches), DoHarmonyPatches);
    }

    /// <summary>
    /// Applies only the patches whose setting is enabled - membership of EnabledSettings already means
    /// "on AND its [RequiresMod] satisfied", so the module checks live in those attributes rather than here
    /// and a user running one module never pays for the other's patch. The ingestion patch additionally requires at least one
    /// <see cref="IngestionAffectedHediffsDef"/> to have named a hediff - with an empty set the prefix could
    /// only ever fall through to vanilla, so it is not applied at all.
    /// </summary>
    private static void DoHarmonyPatches()
    {
        var harmonyInstance = new Harmony("com.octodecoy.vvnewharvest.compatpatches");

        if (IsSettingEnabled(nameof(Settings.AddNourishedHediffFromAnimalFood)))
        {
            TryPatch(harmonyInstance, typeof(Patch_ThinkNode_ChancePerHour_Nuzzle_MtbHours));
        }

        if (IsSettingEnabled(nameof(Settings.TreatFungusPoisoningLikeFoodPoisoning)))
        {
            BuildCollection(
                defs: DefDatabase<IngestionAffectedHediffsDef>.AllDefs.Where(d => !d.hediffDefs.NullOrEmpty()),
                selector: d => d.hediffDefs,
                targetSet: ref IngestionAffectedHediffDefSet,
                nameSelector: d => d.defName,
                cleanup: null, // Could wipe the def's list, but whatever, hold it.
                setType: nameof(IngestionAffectedHediffsDef)
            );

            if (!IngestionAffectedHediffDefSet.NullOrEmpty())
            {
                TryPatch(harmonyInstance, typeof(Patch_IngestionOutcomeDoer_DoIngestionOutcome));
            }
        }
    }

    /// <summary>
    /// Applies one annotated patch class through a class processor rather than <c>PatchAll</c>, so that each
    /// class is opted in explicitly by the gates above instead of every patch class in the assembly being
    /// applied unconditionally. A failure is logged and swallowed: one broken patch must not abort the rest
    /// or throw out of a static constructor.
    /// </summary>
    private static void TryPatch(Harmony instance, Type type)
    {
        try
        {
            var processor = instance.CreateClassProcessor(type);
            if (processor == null)
            {
                LogMessage(() => $"Failed to create Harmony class processor for type {type.Name}", LogMessageType.Error);
                return;
            }

            var methods = processor.Patch();
            if (methods.NullOrEmpty())
            {
                LogMessage(() => $"Failed to patch any methods for class {type.Name}", LogMessageType.Error);
                return;
            }

            LogMessage(() => $"Successfully applied patches for class [{type.Name}] in category [{processor.Category ?? "<none>"}].");
        }
        catch (Exception ex)
        {
            LogException(ex, ex.TargetSite, $"Exception while patching class {type.Name}");
        }
    }
}