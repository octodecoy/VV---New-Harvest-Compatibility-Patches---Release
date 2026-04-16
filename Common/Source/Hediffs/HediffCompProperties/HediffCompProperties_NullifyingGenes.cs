namespace NewHarvestPatches;

public class HediffCompProperties_NullifyingGenes : HediffCompProperties
{
    /// <summary>
    /// A list of genes that will cause the parent hediff to be removed if the pawn has any of them.
    /// </summary>
    public List<GeneDef> nullifyingGenes;

    /// <summary>
    /// If true, and the pawn has food poisoning that was added at the same time as the parent hediff, remove it or nullify its severity increase alongside removing the parent hediff.
    /// </summary>
    public bool removeLinkedFoodPoisoning = true;

    /// <summary>
    /// If true, the parent hediff will be removed in CompPostTick if the pawn has any of the nullifying genes.
    /// This way if the gene is added after the hediff is added, OR the hediff was already on the pawn with the gene at game load, it will still be removed.
    /// CompPostTick will be checked every ticksBetweenNullificationChecks (default: CompTickRare) ticks.
    /// </summary>
    public bool retroactiveNullification = true;

    /// <summary>
    /// If retroactiveNullification is true, this is the tick interval at which CompPostTick will check for nullifying genes.
    /// </summary>
    public int ticksBetweenNullificationChecks = GenTicks.TickRareInterval;   

    public HediffCompProperties_NullifyingGenes()
    {
        compClass = typeof(HediffComp_NullifyingGenes);
    }
}