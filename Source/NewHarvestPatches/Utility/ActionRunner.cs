namespace NewHarvestPatches;

/// <summary>
/// Uniform entry wrapper for every action this mod applies to the def graph. Gives each action the
/// same guarantees without repeating them: an exception never escapes into RimWorld's loading or
/// settings-window code, the failure is logged with the owning class/method name, and the action is
/// timed via <see cref="Profiler"/> (boot runs land in the boot report, later runs log immediately;
/// no-op in non-PROFILE builds).
/// </summary>
public static class ActionRunner
{
    /// <summary>
    /// Executes the action inside a <see cref="Profiler"/> scope and logs any exception it throws.
    /// The scope disposes after the catch, so a throwing action still records its elapsed time.
    /// </summary>
    /// <param name="className">The name of the class containing the method being executed.</param>
    /// <param name="methodName">The name of the method being executed.</param>
    /// <param name="action">The action to execute.</param>
    public static void Run(string className, string methodName, Action action)
    {
        using (Profiler.Measure(className, methodName))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogException(ex, ex.TargetSite, $"Exception while applying {className}.{methodName}.");
            }
        }
    }
}
