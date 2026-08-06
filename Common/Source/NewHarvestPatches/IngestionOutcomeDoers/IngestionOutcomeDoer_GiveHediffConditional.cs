namespace NewHarvestPatches;

/// <summary>
/// Vanilla's GiveHediff outcome doer plus an XML-authored gate: the hediff is only applied to pawns
/// matching every configured condition. Exists so one food def can carry an effect that is right for some
/// eaters and wrong for others (a drug that should not hit animals, a treat only colonists react to)
/// without needing a separate def per audience. All fields are optional - an unset condition group is
/// simply not evaluated, so a doer with no conditions behaves exactly like the vanilla base class.
/// </summary>
/// <remarks>
/// Each of the gene/hediff/trait groups is configured the same way and reads:
/// "<c>Quantifier</c> of <c>Requirements</c> are present" then inverted if <c>Polarity</c> is Forbid.
/// So Any + Forbid means "none of these", All + Forbid means "not all of these". The
/// <c>allowIf...NotApplicable</c> flag only decides the outcome for pawns that CANNOT have that thing at
/// all (an animal has no traits) - it is not a fallback for pawns that merely lack the listed ones.
/// </remarks>
public class IngestionOutcomeDoer_GiveHediffConditional : IngestionOutcomeDoer_GiveHediff
{
    public enum PawnAffiliation
    {
        Any,
        ColonyMember,
        Colonist,
        Prisoner,
        Slave,
        Guest,
        Enemy
    }
    
    public enum PawnCharacteristic
    {
        Any,
        IsAnimal,

        /// <summary>An animal whose race can nuzzle at all - not one currently nuzzling.</summary>
        IsNuzzlingAnimal,
        IsHumanlike
    }
    public List<PawnAffiliation> pawnAffiliations = [PawnAffiliation.Any];
    public CompareLogic pawnAffiliationLogic = CompareLogic.Or;

    public List<PawnCharacteristic> pawnCharacteristics = [PawnCharacteristic.Any];
    public CompareLogic pawnCharacteristicLogic = CompareLogic.Or;

    public List<GeneDef> geneRequirements;
    public bool allowIfGeneNotApplicable = true;
    public CompareQuantifier geneQuantifier = CompareQuantifier.Any;
    public ComparePolarity genePolarity = ComparePolarity.Forbid;

    public List<HediffDef> hediffRequirements;
    public bool allowIfHediffNotApplicable = true;
    public CompareQuantifier hediffQuantifier = CompareQuantifier.Any;
    public ComparePolarity hediffPolarity = ComparePolarity.Forbid;

    public List<TraitDef> traitRequirements;
    public bool allowIfTraitNotApplicable = true;
    public CompareQuantifier traitQuantifier = CompareQuantifier.Any;
    public ComparePolarity traitPolarity = ComparePolarity.Forbid;

    public IntRange requiredHealthPercent = IntRange.Between(0, 100);
    public CompareOperator healthOperator = CompareOperator.None;

    protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
    {
        if (pawn == null || pawn.RaceProps == null || hediffDef == null || ingested == null)
            return;

        if (ValidatePawn(pawn))
        {
            base.DoIngestionOutcomeSpecial(pawn, ingested, ingestedCount);
        }
        else
        {
            LogMessage(() => $"Pawn [{pawn.Name}] ingested [{ingested.def.defName}] but does not match logic.  Not applying hediff [{hediffDef}].");
        }
    }

    /// <summary>
    /// Every condition group must pass - the groups are ANDed together, and the CompareLogic/Quantifier
    /// fields only control how members WITHIN a group combine. There is no way to OR two different groups.
    /// </summary>
    private bool ValidatePawn(Pawn pawn)
    {
        return HasRequiredHealthPercent(pawn)
            && EvaluateHediffs(pawn)
            && EvaluateGenes(pawn) 
            && EvaluateTraits(pawn)
            && MatchesAffiliations(pawn) 
            && MatchesCharacteristics(pawn);
    }

    /// <summary>
    /// Compares the pawn's summary health, as a whole percent, against <c>requiredHealthPercent</c>.
    /// The operator defaults to None, which skips the check - the range alone does nothing.
    /// </summary>
    private bool HasRequiredHealthPercent(Pawn pawn)
    {
        if (healthOperator is CompareOperator.None)
            return true;

        int healthPercent = (int)(pawn.health.summaryHealth.SummaryHealthPercent * 100f);

        return CompareUtility.MatchesComparisonOperator(healthOperator, requiredHealthPercent.Clamp(0, 100), healthPercent);
    }

    private bool EvaluateHediffs(Pawn pawn)
    {
        if (hediffRequirements.NullOrEmpty())
            return true;

        HediffSet hediffSet = pawn.health?.hediffSet;
        if (hediffSet == null)
            return allowIfHediffNotApplicable;

        bool result = CompareUtility.MatchesQuantifier(hediffQuantifier, hediffRequirements, h => hediffSet.HasHediff(h));

        if (hediffPolarity == ComparePolarity.Forbid)
        {    
            result = !result;
        }

        return result;
    }

    /// <summary>
    /// Gene check. Passes unconditionally without Biotech - the requirements are treated as unauthored
    /// rather than unmet, so a gene-gated food still works for players without the DLC.
    /// </summary>
    private bool EvaluateGenes(Pawn pawn)
    {
        if (geneRequirements.NullOrEmpty())
            return true;

        if (!ModsConfig.BiotechActive)
            return true;

        Pawn_GeneTracker geneTracker = pawn.genes;

        if (geneTracker == null)
            return allowIfGeneNotApplicable; // If geneTracker is null then pawn can't have genes.

        bool result = CompareUtility.MatchesQuantifier(geneQuantifier, geneRequirements, geneTracker.HasActiveGene);

        if (genePolarity == ComparePolarity.Forbid)
            result = !result;

        return result;
    }

    private bool EvaluateTraits(Pawn pawn)
    {
        if (traitRequirements.NullOrEmpty())
            return true;

        TraitSet traitSet = pawn.story?.traits;

        if (traitSet == null)
            return allowIfTraitNotApplicable; // If traitSet is null then pawn can't have traits.

        bool result = CompareUtility.MatchesQuantifier(traitQuantifier, traitRequirements, traitSet.HasTrait);

        if (traitPolarity == ComparePolarity.Forbid)
            result = !result;

        return result;
    }

    private bool MatchesAffiliations(Pawn pawn)
    {
        if (pawnAffiliations.NullOrEmpty() || pawnAffiliations.Contains(PawnAffiliation.Any))
            return true;


        return CompareUtility.MatchesLogic(pawnAffiliationLogic, pawnAffiliations, a => MatchesAffiliation(pawn, a));
    }

    /// <summary>
    /// Affiliations are not mutually exclusive - a prisoner is also a ColonyMember only if captured into
    /// the player faction, and Colonist excludes prisoners and slaves. Guest is spelled out because vanilla
    /// has no single property for it: hosted by the player, not OF the player, and not a prisoner, slave or
    /// hostile. <c>Any</c> is not handled here; the caller short-circuits it and reaching this switch with
    /// it returns false.
    /// </summary>
    private static bool MatchesAffiliation(Pawn pawn, PawnAffiliation affiliation)
    {
        switch (affiliation)
        {
            case PawnAffiliation.ColonyMember:
                return pawn.Faction == Faction.OfPlayer;

            case PawnAffiliation.Colonist:
                return pawn.IsColonist;

            case PawnAffiliation.Prisoner:
                return pawn.IsPrisoner;

            case PawnAffiliation.Slave:
                return pawn.IsSlave;

            case PawnAffiliation.Guest:
                return pawn.Faction != null 
                    && pawn.guest != null
                    && pawn.guest.GuestStatus == GuestStatus.Guest
                    && pawn.HostFaction.IsPlayerSafe() 
                    && !pawn.Faction.IsPlayerSafe() 
                    && !pawn.HostileTo(Faction.OfPlayer)
                    && !pawn.IsPrisoner
                    && !pawn.IsSlave;

            case PawnAffiliation.Enemy:
                return pawn.HostileTo(Faction.OfPlayer);

            default:
                return false;
        }
    }

    private bool MatchesCharacteristics(Pawn pawn)
    {
        if (pawnCharacteristics.NullOrEmpty() || pawnCharacteristics.Contains(PawnCharacteristic.Any))
            return true;

        return CompareUtility.MatchesLogic(pawnCharacteristicLogic, pawnCharacteristics, c => MatchesCharacteristic(pawn, c));
    }

    private static bool MatchesCharacteristic(Pawn pawn, PawnCharacteristic characteristic)
    {
        switch (characteristic)
        {
            case PawnCharacteristic.IsAnimal:
                return pawn.IsAnimal;

            case PawnCharacteristic.IsNuzzlingAnimal:
                return pawn.IsAnimal && pawn.RaceProps.nuzzleMtbHours > 0f;

            case PawnCharacteristic.IsHumanlike:
                return pawn.RaceProps.Humanlike;               

            default:
                return false;
        }
    }
}