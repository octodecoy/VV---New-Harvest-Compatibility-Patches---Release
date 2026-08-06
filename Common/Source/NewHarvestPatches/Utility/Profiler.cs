using System.Text;

namespace NewHarvestPatches;

/// <summary>
/// Session-long timing built on disposable scopes: <c>using (Profiler.Measure(...)) { ... }</c> times
/// exactly the code inside the block, so a scope opened inside a deferred callback (e.g.
/// <c>LongEventHandler.ExecuteWhenFinished</c>) never counts the wait before the callback ran.
/// Nesting is tracked by a depth counter - depth 0 is an outermost scope, no flags needed. Scopes that
/// finish before <see cref="FlushBootReport"/> are collected and printed as one boot block; every scope
/// after that logs immediately on completion, so live re-runs (settings write-back, category apply) are
/// timed for the whole session. Compiled out entirely unless the PROFILE symbol is defined (Debug builds);
/// when compiled in, output is still gated on the Logging setting.
/// Single-threaded by design: RimWorld runs boot on the loading thread and everything after on the main
/// thread, and no current call path overlaps scopes across threads.
/// </summary>
public static class Profiler
{
#if PROFILE
    // Depth of currently open scopes; a scope created at depth 0 is outermost and its time counts
    // toward the boot total. Balanced by Scope.Dispose, which `using` guarantees even on throw.
    private static int s_depth;

    // Non-null while boot entries are being collected. FlushBootReport nulls it, flipping the
    // profiler to immediate per-scope logging for the rest of the session.
    private static List<(string Name, int Depth, double Seconds)> s_bootEntries = [];
#endif

    /// <summary>
    /// Opens a timing scope named <c>className.methodName</c>. Dispose it (via <c>using</c>) to record.
    /// Returns an inactive no-op scope when profiling is compiled out or the Logging setting is off.
    /// </summary>
    public static Scope Measure(string className, string methodName)
    {
#if PROFILE
        if (!Settings.Logging)
            return default;

        return new Scope($"{className}.{methodName}", s_depth++, Stopwatch.GetTimestamp());
#else
        return default;
#endif
    }

    /// <summary>
    /// Prints every scope completed so far as one block plus a total of the outermost (depth 0) entries,
    /// then switches the profiler to immediate per-scope logging permanently. Called once at the end of
    /// boot; scopes completing after this (late static ctors, live actions) log on their own.
    /// The call site itself is compiled out of non-PROFILE builds.
    /// </summary>

    public static void FlushBootReport()
    {
#if PROFILE
        var entries = s_bootEntries;
        s_bootEntries = null;

        if (!Settings.Logging || entries.NullOrEmpty())
            return;

        double total = 0d;
        var report = new StringBuilder("Startup timings:");
        foreach (var (name, depth, seconds) in entries)
        {
            if (depth == 0)
                total += seconds;
            report.Append('\n').Append(' ', depth * 2)
                  .Append(name).Append(" completed in: ").Append(FormatSeconds(seconds)).Append(" seconds.");
        }
        report.Append("\nFinished initializing in ").Append(FormatSeconds(total)).Append(" seconds.");
        LogMessage(report.ToString);
#endif
    }


    /// <summary>
    /// Queues <see cref="FlushBootReport"/> instead of calling it inline, so work our boot actions defer with
    /// <c>LongEventHandler.ExecuteWhenFinished</c> still lands in the boot block and counts toward the total.
    /// Called inline, the flush would null the entry list while those callbacks were merely queued, and each
    /// would then log on its own after the total. Vanilla drains the queue FIFO, so registering here - after
    /// every boot action has registered its own callback - puts the flush last.
    /// </summary>
    [Conditional("PROFILE")]
    public static void FlushBootReportWhenFinished()
    {
#if PROFILE
        LongEventHandler.ExecuteWhenFinished(FlushBootReport);
#endif
    }

#if PROFILE
    private static string FormatSeconds(double seconds, int decimals = 6)
    {
        double rounded = Math.Round(seconds, decimals);
        return rounded.ToString($"0.{new string('#', decimals)}", CultureInfo.InvariantCulture);
    }
#endif

    /// <summary>
    /// One open timing measurement. A <c>default</c> instance is inactive and disposes as a no-op,
    /// which is what every scope is in non-PROFILE builds.
    /// </summary>
    public readonly struct Scope : IDisposable
    {
#if PROFILE
        private readonly string _name;
        private readonly int _depth;
        private readonly long _startTimestamp;
        private readonly bool _active;

        internal Scope(string name, int depth, long startTimestamp)
        {
            _name = name;
            _depth = depth;
            _startTimestamp = startTimestamp;
            _active = true;
        }
#endif

        public void Dispose()
        {
#if PROFILE
            if (!_active)
                return;

            double seconds = (Stopwatch.GetTimestamp() - _startTimestamp) / (double)Stopwatch.Frequency;
            s_depth--;

            if (s_bootEntries != null)
            {
                s_bootEntries.Add((_name, _depth, seconds));
                return;
            }

            string line = $"{_name} completed in: {FormatSeconds(seconds)} seconds.";
            LogMessage(() => line);
#endif
        }
    }
}
