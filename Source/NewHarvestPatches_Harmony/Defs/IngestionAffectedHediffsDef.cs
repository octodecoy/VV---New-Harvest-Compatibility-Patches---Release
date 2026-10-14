using Verse;
using System.Collections.Generic;

namespace NewHarvestPatches_Harmony;

/// <summary>
/// XML-authored list of hediffs whose ingestion chance should go through our food-poisoning-style
/// calculation instead of the doer's flat <c>chance</c>. Defined as a Def rather than a settings field so
/// other mods can contribute hediffs without touching our code; all instances are merged into one set at
/// startup.
/// </summary>
public class IngestionAffectedHediffsDef : Def
{
    public List<HediffDef> hediffDefs;
}