using System.Runtime.CompilerServices;

namespace NewHarvestPatches;

public static partial class Utils
{
    /// <summary>
    /// Logging, gated on the Logging setting so a release install stays silent. Messages are passed as
    /// a Func and the caller's file/method/line are filled in by the compiler, so a disabled log costs
    /// one bool check - no string is built and no call site needs to name itself.
    /// Timing lives in <see cref="Profiler"/>.
    /// </summary>
    public static class Logger
    {
        // Could be called before Settings is filled.
        private static bool LoggingEnabled => Settings?.Logging == true;
        public static string Prefix = $"[{nameof(NewHarvestPatches)}]".Colorize(cyan);

        /// <summary>
        /// Writes one prefixed, caller-tagged line. The message is only materialized once the gate passes,
        /// which is the entire reason for the Func - interpolated arguments at the call site would be built
        /// whether or not logging is on.
        /// </summary>
        /// <param name="ignoreSetting">Log even with the Logging setting off. For failures a user must see.</param>
        /// <param name="doOnceKey">Non-negative routes to Log.WarningOnce/ErrorOnce with this key, for
        /// messages reachable from per-frame or per-def code that would otherwise flood the log.</param>
        public static void LogMessage(
            Func<string> messageFactory,
            LogMessageType severity = LogMessageType.Message,
            bool ignoreSetting = false,
            int doOnceKey = -1,
            [CallerFilePath] string filePath = null,
            [CallerMemberName] string memberName = null,
            [CallerLineNumber] int lineNumber = 0)
        {
            if (!LoggingEnabled && !ignoreSetting)
                return;

            string pathToFile = filePath != null
                ? System.IO.Path.GetFileNameWithoutExtension(filePath)
                : "UNKNOWN FILE";

            string methodName = memberName ?? "UNKNOWN METHOD";

            string message = $"{Prefix} - Caller: [{pathToFile}.{methodName}] @line number: [{lineNumber}] - {messageFactory()}";

            switch (severity)
            {
                case LogMessageType.Warning:
                    if (doOnceKey != -1)
                    {
                        Log.WarningOnce(message, doOnceKey);
                    }
                    else
                    {
                        Log.Warning(message);
                    }
                    break;
                case LogMessageType.Error:
                    if (doOnceKey != -1)
                    {
                        Log.ErrorOnce(message, doOnceKey);
                    }
                    else
                    {
                        Log.Error(message);
                    }
                    break;
                default:
                    Log.Message(message);
                    break;
            }
        }

        /// <summary>
        /// Logs an exception with the throwing method's name. Never gated on the Logging setting - a
        /// swallowed exception the user cannot see is worse than log noise. Callers pass
        /// <c>ex.TargetSite</c> for <paramref name="method"/>.
        /// </summary>
        public static void LogException(Exception exception, MethodBase method, string optMsg = null)
        {
            string omsg = optMsg != null ? $"(Additional info: {optMsg})\n" : "";
            string m = $"{Prefix}{omsg}Exception in {method?.DeclaringType?.FullName ?? "UNKNOWN"}.{method?.Name ?? "UNKNOWN"}: {exception}";
            Log.Error(m);
        }
    }
}