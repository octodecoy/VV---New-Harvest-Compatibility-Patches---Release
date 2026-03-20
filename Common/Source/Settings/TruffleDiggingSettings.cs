namespace NewHarvestPatches;

public class TruffleDiggingSettings : IExposable
{
    public int TicksBetweenDigAttempts = GenDate.TicksPerDay;
    public FloatRange DiggingChanceRange = new(0.05f, 0.5f);
    public float DiggingChanceReduction = 0.05f;
    public IntRange AmountRange = IntRange.One;
    public bool SpawnsForbidden = false;
    public bool GizmoRequiresTraining = true;

    public void ExposeData()
    {
        Scribe_Values.Look(ref TicksBetweenDigAttempts, nameof(TicksBetweenDigAttempts), GenDate.TicksPerDay, false);
        Scribe_Values.Look(ref DiggingChanceRange, nameof(DiggingChanceRange), new FloatRange(0.05f, 0.5f), false);
        Scribe_Values.Look(ref DiggingChanceReduction, nameof(DiggingChanceReduction), 0.05f, false);
        Scribe_Values.Look(ref AmountRange, nameof(AmountRange), IntRange.One, false);
        Scribe_Values.Look(ref SpawnsForbidden, nameof(SpawnsForbidden), false, false);
        Scribe_Values.Look(ref GizmoRequiresTraining, nameof(GizmoRequiresTraining), true, false);
    }

    public void ResetToDefaults()
    {
        TicksBetweenDigAttempts = GenDate.TicksPerDay;
        DiggingChanceRange = new FloatRange(0.05f, 0.5f);
        DiggingChanceReduction = 0.05f;
        AmountRange = IntRange.One;
        SpawnsForbidden = false;
        GizmoRequiresTraining = true;
    }
}
