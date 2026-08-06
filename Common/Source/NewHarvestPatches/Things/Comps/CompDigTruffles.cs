namespace NewHarvestPatches;

/// <summary>
/// Lets a tamed animal periodically dig up truffles from the soil it is standing on. Attached to animals by
/// an XML patch rather than by us defining the animals, so it must tolerate being on any race the user's
/// other mods provide. Every path is gated on <c>Settings.AddTruffleDiggingBehavior</c>, so turning the
/// setting off makes the comp inert without needing to be removed from the def.
/// </summary>
public class CompDigTruffles : ThingComp
{
    /// <summary>
    /// Per-animal digging toggle, scribed. Public because <see cref="CountAnimalsThatCanDig"/> reads it off
    /// OTHER pawns' comps to scale the spawn chance.
    /// </summary>
    public bool DiggingEnabled = true;
    // private Effecter effecter;
    private int _ticksSinceLastDigAttempt;
    private static readonly List<TrainableDef> s_obedienceDefs = BuildObedienceDefs();

    /// <summary>
    /// The training defs that count as "this animal takes orders" for the gizmo. Obedience is vanilla; the
    /// digging-flavoured ones only exist with Odyssey, and the VEF one only with that framework installed -
    /// so the list length varies by install and may be just Obedience.
    /// </summary>
    private static List<TrainableDef> BuildObedienceDefs()
    {
        var defs = new List<TrainableDef>();

        // Null-guard every DefOf: a stripped/renamed def leaves the field null, and a null
        // reaching the DefMap indexer in HasLearnedAny throws, not the Add.
        if (TrainableDefOf.Obedience != null)
            defs.Add(TrainableDefOf.Obedience);

        if (ModsConfig.OdysseyActive)
        {
            if (TrainableDefOf.Forage != null)
                defs.Add(TrainableDefOf.Forage);

            if (TrainableDefOf.Dig != null)
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

    /// <summary>
    /// Adds the digging on/off toggle. Absence of the gizmo does NOT mean the animal cannot dig - an
    /// untrained tame animal still digs, it just cannot be told to stop. The gizmo is only offered where the
    /// toggle would actually be honoured.
    /// </summary>
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
        string state = DiggingEnabled ? "Disable" : "Enable";
        string iconState = DiggingEnabled ? "Enabled" : "Disabled";

        yield return new Command_Action
        {
            action = () => DiggingEnabled = !DiggingEnabled,
            defaultLabel = $"{TKey.KeyPrefix}{state}Digging".Translate(),
            defaultDesc = $"{TKey.KeyPrefix}{state}DiggingDesc".Translate(),
            icon = ContentFinder<Texture2D>.Get($"{ModPrefix}/UI/Icons/TruffleIcon_{iconState}", true),
            activateSound = DiggingEnabled ? SoundDefOf.ClickReject : SoundDefOf.Click,
        };
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref DiggingEnabled, "DiggingOn", true, false); // DiggingOn renamed to DiggingEnabled - look for DiggingOn.
        Scribe_Values.Look(ref _ticksSinceLastDigAttempt, nameof(_ticksSinceLastDigAttempt), 0, false);
    }

    /// <summary>
    /// Runs the dig attempt. Rate is controlled by the settings interval rather than the rare-tick period,
    /// so <see cref="_ticksSinceLastDigAttempt"/> accumulates and the body below it only runs on the
    /// interval. Chance is rolled per attempt, so a failed roll costs a whole interval.
    /// </summary>
    public override void CompTickRare()
    {
        base.CompTickRare();

        if (!Settings.AddTruffleDiggingBehavior)
            return;

        if (parent is not Pawn pawn)
            return;

        // If digging was toggled off but the animal lost obedience (gizmo gone),
        // re-enable so it's not stuck in limbo
        if (!DiggingEnabled)
        {
            if (IsTamed(pawn) && !IsObedient(pawn.training))
                DiggingEnabled = true;

            return;
        }

        _ticksSinceLastDigAttempt += GenTicks.TickRareInterval;
        if (_ticksSinceLastDigAttempt < Settings.TruffleSettings.TicksBetweenDigAttempts)
            return;
            
        _ticksSinceLastDigAttempt = 0;

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
        ThingDef truffleDef = InternalThingDefOf.VV_BlackTruffles;
        if (truffleDef == null)
            return;

        Thing truffle = ThingMaker.MakeThing(truffleDef);
        if (truffle == null)
            return;

        int count = Settings.TruffleSettings.TrufflesPerDigRange.RandomInRange;

        truffle.stackCount = count;

        bool placed = GenPlace.TryPlaceThing(truffle, pawn.Position, map, ThingPlaceMode.Near);
        if (!placed)
            return;

        if (Settings.TruffleSettings.SpawnsForbidden)
            truffle.SetForbidden(true, false);

        // effecter ??= EffecterDefOf.ImpactSmallDustCloud.Spawn();
        // effecter.Trigger(pawn, truffle);

        Effecter e = EffecterDefOf.ImpactSmallDustCloud.Spawn();
        e.Trigger(pawn, truffle);
        e.Cleanup();

        MoteMaker.ThrowText(pawn.DrawPos, map, $"{TKey.KeyPrefix}FoundTruffle".Translate(pawn.Named("PAWN"), truffle.Named("TRUFFLE")));
    }

    private static bool IsTamed(Pawn pawn) => pawn.Faction == Faction.OfPlayer;

    /// <summary>
    /// True if the animal has learned any of <see cref="s_obedienceDefs"/>. Uses the public
    /// <c>Pawn_TrainingTracker.HasLearned</c>, which also returns false when the pawn's trainability is
    /// below the def's requiredTrainability. Fails closed: a null tracker returns false, so the worst
    /// case is the toggle gizmo not appearing, never an exception on the tick path.
    /// </summary>
    private static bool HasLearnedAny(Pawn_TrainingTracker training)
    {
        if (training == null)
            return false;

        if (s_obedienceDefs == null)
            return false;

        for (int i = 0; i < s_obedienceDefs.Count; i++)
        {
            if (training.HasLearned(s_obedienceDefs[i]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the animal counts as taking orders. With GizmoRequiresTraining off this is unconditionally
    /// true - untrained animals then get the toggle too.
    /// </summary>
    private static bool IsObedient(Pawn_TrainingTracker training)
    {
        return !Settings.TruffleSettings.GizmoRequiresTraining || HasLearnedAny(training);
    }

    /// <summary>
    /// Truffles only come out of natural diggable ground - soil or sand. Excludes floors, rock and anything
    /// a mod marks non-natural, so an animal standing indoors produces nothing.
    /// </summary>
    private static bool IsDiggable(TerrainDef terrain)
    {
        return terrain.natural && terrain.affordances.Contains(InternalTerrainAffordanceDefOf.Diggable) && (terrain.IsSoil || terrain.categoryType == TerrainDef.TerrainCategoryType.Sand);
    }

    /// <summary>
    /// Isolates the one direct VEF reference. The type must stay inside a method BODY - a VEF type in a
    /// field or a method signature is resolved at class load and throws TypeLoadException when VEF is
    /// absent. The return type is deliberately the base-game <see cref="TrainableDef"/>.
    /// Only call behind a VEF-installed check.
    /// </summary>
    private static TrainableDef GetVEFDiggingDef()
    {
        return VEF.AnimalBehaviours.InternalDefOf.VEF_DiggingDiscipline;
    }

    /// <summary>
    /// How many colony animals on the map are currently set to dig, including this one. Feeds the
    /// diminishing-returns curve in <see cref="GetDiggingChance"/>.
    /// </summary>
    private int CountAnimalsThatCanDig(Map map)
    {
        int count = 0;
        var spawnedColonyAnimals = map.mapPawns.SpawnedColonyAnimals;
        for (int i = 0; i < spawnedColonyAnimals.Count; i++)
        {
            if (spawnedColonyAnimals[i].TryGetComp<CompDigTruffles>() is CompDigTruffles comp && comp.DiggingEnabled)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Per-attempt spawn chance, reduced as more digging animals are kept so a large herd cannot farm
    /// truffles at a linear rate. Starts at the range max for a single animal and steps down by
    /// DiggingChanceReduction per extra animal, floored at the range min - the floor is what stops the herd
    /// penalty from reaching zero.
    /// </summary>
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