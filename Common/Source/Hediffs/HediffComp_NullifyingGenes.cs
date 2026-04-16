namespace NewHarvestPatches;

// Shouldn't be any need to CompExposeData since shouldRemove should be checked every tick.
public class HediffComp_NullifyingGenes : HediffComp
{
    public HediffCompProperties_NullifyingGenes Props => (HediffCompProperties_NullifyingGenes)this.props;
    
    private bool shouldRemove = false;

    /// <summary>
    /// This is set to true in CompPostPostAdd if the pawn has any of the nullifying genes, 
    /// and is used by Pawn_HealthTracker to remove the hediff.
    /// </summary>
    public override bool CompShouldRemove => shouldRemove;

    public override void CompPostTick(ref float severityAdjustment)
    {
        base.CompPostTick(ref severityAdjustment);

        if (!Props.retroactiveNullification)
            return;

        if (Pawn?.IsHashIntervalTick(Props.ticksBetweenNullificationChecks) == false)
            return;

        RemoveParentHediff();
    }

    public override void CompPostPostAdd(DamageInfo? dinfo)
    {
        base.CompPostPostAdd(dinfo);

        RemoveParentHediff();
    }

    private void RemoveParentHediff()
    {
        if (Props.nullifyingGenes.NullOrEmpty())
            return;

        Pawn_GeneTracker genes = Pawn?.genes;
        if (genes == null)
            return;

        foreach (var nullifyingGene in Props.nullifyingGenes)
        {
            if (genes.HasActiveGene(nullifyingGene))
            {
                ToLog($"Removing hediff: [{Def.defName}] from: [{Pawn.Name}] due to having gene: [{nullifyingGene.defName}].");

                if (Props.removeLinkedFoodPoisoning)
                {
                    UndoFoodPoisoning();
                }

                shouldRemove = true;
                return;
            }
        }
    }

    private void UndoFoodPoisoning()
    {
        Pawn_HealthTracker health = Pawn?.health;
        if (health == null)
            return;

        HediffSet hediffSet = health.hediffSet;
        if (hediffSet == null)
            return;

        var foodPoisoningHediff = hediffSet.GetFirstHediffOfDef(HediffDefOf.FoodPoisoning);
        if (foodPoisoningHediff == null)
            return;

        // If the food poisoning was added when the parent was added, then foodPoisoningHediff.tickAdded == parent.tickAdded.
        // If the food poisoning was already on the pawn when the parent was added, then foodPoisoningHediff.ageTicks == parent.ageTicks.
        if (foodPoisoningHediff.tickAdded == parent.tickAdded || foodPoisoningHediff.ageTicks == parent.ageTicks)
        {
            ToLog($"Removing linked [{foodPoisoningHediff.def.defName}].");
            health.RemoveHediff(foodPoisoningHediff);
        }
    }
}