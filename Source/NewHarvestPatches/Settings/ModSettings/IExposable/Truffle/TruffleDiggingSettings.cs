namespace NewHarvestPatches;

/// <summary>
/// Tuning for <see cref="CompDigTruffles"/>, grouped into its own IExposable so the truffle knobs scribe and
/// reset as a unit rather than as loose fields on the settings object.
/// </summary>
/// <remarks>
/// Every default lives once, in <see cref="Defaults"/>, and is referenced from the field initializers, the
/// Scribe calls and the settings UI. Keeping them in one place is not just tidiness: a value equal to its
/// Scribe default is never written to the config file, so a default that disagreed between the field
/// initializer and the Scribe call would silently change what existing configs load as.
/// </remarks>
public class TruffleDiggingSettings : IExposable
{
    /// <summary>
    /// The single source of truth for truffle defaults. The settings UI reads these for its own
    /// defaultValue arguments, so a change here reaches the field, the save format and the reset button.
    /// </summary>
    public static class Defaults
    {
        public const int TicksBetweenDigAttempts = GenDate.TicksPerDay;
        public const float DiggingChanceReduction = 0.05f;
        public const bool SpawnsForbidden = false;
        public const bool GizmoRequiresTraining = true;
        public static readonly FloatRange DiggingChanceRange = new(0.05f, 0.5f);
        public static readonly IntRange TrufflesPerDigRange = IntRange.One;
    }

    public int TicksBetweenDigAttempts = Defaults.TicksBetweenDigAttempts;
    public FloatRange DiggingChanceRange = Defaults.DiggingChanceRange;
    public float DiggingChanceReduction = Defaults.DiggingChanceReduction;
    public IntRange TrufflesPerDigRange = Defaults.TrufflesPerDigRange;
    public bool SpawnsForbidden = Defaults.SpawnsForbidden;
    public bool GizmoRequiresTraining = Defaults.GizmoRequiresTraining;

    public void ExposeData()
    {
        Scribe_Values.Look(ref TicksBetweenDigAttempts, nameof(TicksBetweenDigAttempts), Defaults.TicksBetweenDigAttempts, false);
        Scribe_Values.Look(ref DiggingChanceRange, nameof(DiggingChanceRange), Defaults.DiggingChanceRange, false);
        Scribe_Values.Look(ref DiggingChanceReduction, nameof(DiggingChanceReduction), Defaults.DiggingChanceReduction, false);
        Scribe_Values.Look(ref TrufflesPerDigRange, "AmountRange", Defaults.TrufflesPerDigRange, false); // TrufflesPerDigRange was previously saved as "AmountRange"
        Scribe_Values.Look(ref SpawnsForbidden, nameof(SpawnsForbidden), Defaults.SpawnsForbidden, false);
        Scribe_Values.Look(ref GizmoRequiresTraining, nameof(GizmoRequiresTraining), Defaults.GizmoRequiresTraining, false);
    }
}
