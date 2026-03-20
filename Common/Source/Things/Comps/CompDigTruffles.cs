using System.Linq.Expressions;

namespace NewHarvestPatches;

public class CompDigTruffles : ThingComp
{
    public bool diggingOn = true;
    private Effecter effecter;
    private int ticksSinceLastDigAttempt;
    public CompProperties_DigTruffles Props => (CompProperties_DigTruffles)this.props;
    private static readonly List<TrainableDef> ObedienceDefs = BuildObedienceDefs();
    private static readonly Func<Pawn_TrainingTracker, DefMap<TrainableDef, bool>> GetLearned = CreateGetter();

    private static Func<Pawn_TrainingTracker, DefMap<TrainableDef, bool>> CreateGetter()
    {
        if (!Settings.AddTruffleDiggingBehavior)
            return null;

        var field = typeof(Pawn_TrainingTracker).GetField("learned", BindingFlags.NonPublic | BindingFlags.Instance);

        var instance = Expression.Parameter(typeof(Pawn_TrainingTracker), "instance");
        var fieldAccess = Expression.Field(instance, field);

        var lambda = Expression.Lambda<Func<Pawn_TrainingTracker, DefMap<TrainableDef, bool>>>(fieldAccess, instance);

        return lambda.Compile();
    }

    private static List<TrainableDef> BuildObedienceDefs()
    {
        if (!Settings.AddTruffleDiggingBehavior)
            return null;

        var defs = new List<TrainableDef> { TrainableDefOf.Obedience };
        if (ModsConfig.OdysseyActive)
        {
            defs.Add(TrainableDefOf.Forage);
            defs.Add(TrainableDefOf.Dig);

            if (HasVanillaExpandedFramework)
            {
                var vefDiggingDisciplineDef = GetVEFDiggingDef();
                if (vefDiggingDisciplineDef != null)
                    defs.Add(vefDiggingDisciplineDef);
            }

        }

        return defs;
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        foreach (Gizmo gizmo in base.CompGetGizmosExtra())
        {
            yield return gizmo;
        }

        if (!Settings.AddTruffleDiggingBehavior)
            yield break;

        if (parent is not Pawn pawn || !pawn.Spawned || pawn.Dead)
            yield break;

        // Not tamed, no gizmo (can't toggle)
        if (!IsTamed(pawn))
            yield break;

        // Not obedient — still digs, but no gizmo (can't toggle)
        if (!IsObedient(pawn.training))
            yield break;

        // Obedient — show the toggle gizmo (can toggle)
        string state = diggingOn ? "Disable" : "Enable";
        string iconState = diggingOn ? "Enabled" : "Disabled";

        yield return new Command_Action
        {
            action = () => diggingOn = !diggingOn,
            defaultLabel = $"{Translator.KeyPrefix}{state}Digging".Translate(),
            defaultDesc = $"{Translator.KeyPrefix}{state}DiggingDesc".Translate(),
            icon = ContentFinder<Texture2D>.Get($"NHCP/UI/Icons/TruffleIcon_{iconState}", true),
            activateSound = diggingOn ? SoundDefOf.ClickReject : SoundDefOf.Click,
        };
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref diggingOn, nameof(diggingOn), true, false);
        Scribe_Values.Look(ref ticksSinceLastDigAttempt, nameof(ticksSinceLastDigAttempt), 0, false);
    }

    public override void CompTickRare()
    {
        base.CompTickRare();

        if (!Settings.AddTruffleDiggingBehavior)
            return;

        if (parent is not Pawn pawn)
            return;

        // If digging was toggled off but the animal lost obedience (gizmo gone),
        // re-enable so it's not stuck in limbo
        if (!diggingOn)
        {
            if (IsTamed(pawn) && !IsObedient(pawn.training))
                diggingOn = true;

            return;
        }

        ticksSinceLastDigAttempt += GenTicks.TickRareInterval;
        if (ticksSinceLastDigAttempt < Settings.TruffleSettings.TicksBetweenDigAttempts)
            return;
            
        ticksSinceLastDigAttempt = 0;

        if (!CanDigNow(pawn))
            return;

        Map map = pawn.Map;
        if (map == null)
            return;

        TerrainDef terrain = pawn.Position.GetTerrain(map);
        if (!IsDiggable(terrain))
            return;

        float chance = GetDiggingChance(map);
        if (!Rand.Chance(chance))
            return;

        SpawnTruffles(pawn, map);
    }

    private bool CanDigNow(Pawn pawn)
    {
        return pawn.Spawned
            && !pawn.Dead
            && !pawn.Downed
            && pawn.Awake()
            && IsTamed(pawn);
    }

    private void SpawnTruffles(Pawn pawn, Map map)
    {
        ThingDef truffleDef = NHCP_ThingDefOf.VV_BlackTruffles;
        if (truffleDef == null)
            return;

        Thing truffle = GenSpawn.Spawn(truffleDef, pawn.Position, map, WipeMode.Vanish);
        if (truffle == null)
            return;

        int count = Settings.TruffleSettings.AmountRange.RandomInRange;
        truffle.stackCount = count;

        if (Settings.TruffleSettings.SpawnsForbidden)
            truffle.SetForbidden(true, false);

        effecter ??= EffecterDefOf.ImpactSmallDustCloud.Spawn();
        effecter.Trigger(pawn, truffle);

        MoteMaker.ThrowText(pawn.DrawPos, map, $"{Translator.KeyPrefix}FoundTruffle".Translate(pawn.Named("PAWN"), truffle.Named("TRUFFLE")));
    }

    private bool IsTamed(Pawn pawn)
    {   
        return pawn.Faction == Faction.OfPlayer;
    }

    private static bool HasLearnedAny(Pawn_TrainingTracker training)
    {
        if (training == null)
            return false;

        var learned = GetLearned(training);
        if (learned == null)
            return false;

        for (int i = 0; i < ObedienceDefs.Count; i++)
        {
            if (learned[ObedienceDefs[i]])
                return true;
        }

        return false;
    }

    private static bool IsObedient(Pawn_TrainingTracker training)
    {
        return !Settings.TruffleSettings.GizmoRequiresTraining || HasLearnedAny(training);
    }

    private static bool IsDiggable(TerrainDef terrain)
    {
        return terrain.natural && terrain.affordances.Contains(BaseGameDefOf.Diggable) && (terrain.IsSoil || terrain.categoryType == TerrainDef.TerrainCategoryType.Sand);
    }

    // Keep direct VEF usage in a separate method to avoid issueswhen not installed
    private static TrainableDef GetVEFDiggingDef()
    {
        return VEF.AnimalBehaviours.InternalDefOf.VEF_DiggingDiscipline;
    }

    private int CountAnimalsThatCanDig(Map map)
    {
        int count = 0;
        var spawnedColonyAnimals = map.mapPawns.SpawnedColonyAnimals;
        for (int i = 0; i < spawnedColonyAnimals.Count; i++)
        {
            if (IsTamed(spawnedColonyAnimals[i]) && spawnedColonyAnimals[i].TryGetComp<CompDigTruffles>() is CompDigTruffles comp && comp.diggingOn)
                count++;
        }
        return count;
    }

    private float GetDiggingChance(Map map)
    {
        int count = CountAnimalsThatCanDig(map);

        // Why not be thourough, even though we wouldn't be here if the count was 0
        if (count <= 0)
            return 0f;

        float chance = Mathf.Max(Settings.TruffleSettings.DiggingChanceRange.min, Settings.TruffleSettings.DiggingChanceRange.max - (count - 1) * Settings.TruffleSettings.DiggingChanceReduction);

        // Don't NEED to clamp, since Rand.Chance will work fine with <0/>1 values
        return Mathf.Clamp01(chance);
    }
}