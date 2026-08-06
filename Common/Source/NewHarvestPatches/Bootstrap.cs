global using System;
global using System.Collections.Generic;
global using System.Diagnostics;
global using System.Globalization;
global using System.Linq;
global using System.Reflection;
global using System.Xml;
global using RimWorld;
global using UnityEngine;
global using Verse;
global using static NewHarvestPatches.NewHarvestPatchesModSettings;
global using static NewHarvestPatches.Utils.Logger;
global using static NewHarvestPatches.Utils.ModChecker;
global using static NewHarvestPatches.Utils.SettingChecker;
global using static NewHarvestPatches.SharedConstants;
global using static NewHarvestPatches.SharedFields;
global using static UnityEngine.Color;

namespace NewHarvestPatches;

/// <summary>
/// Startup entry point of the main assembly. Runs after defs are loaded, which is the whole reason the work
/// lives here rather than in the Mod constructor: the actions below read and rewrite loaded defs.
/// </summary>
/// <remarks>
/// Also carries the assembly's global usings, so the static imports every file relies on
/// (<c>Settings</c>, logging, mod checks) are declared in one place.
/// </remarks>
[StaticConstructorOnStartup]
public static class Bootstrap
{
    static Bootstrap() => TryInitialize();

    /// <summary>
    /// Runs the whole boot pass through <c>ActionRunner</c> so an exception is logged rather than escaping a
    /// static constructor - which would leave the type unusable for the rest of the session.
    /// </summary>
    private static void TryInitialize()
    {
        ActionRunner.Run(nameof(Bootstrap), nameof(TryInitialize), Initialize);
        Profiler.FlushBootReportWhenFinished();
    }

    /// <summary>
    /// Populates the settings collections first, since everything below reads them, then applies whatever the
    /// enabled settings call for, and drops the startup caches in a finally block so a failure part-way
    /// through still releases the XML-phase data rather than holding it for the session.
    /// </summary>
    private static void Initialize()
    {
        try
        {
            // Null only when the Mod constructor threw: vanilla's CreateModClasses catches and carries on, so
            // boot still reaches here with nothing to configure from. Reported and abandoned rather than left
            // to NRE on the first dereference, which would bury the real failure. Sits inside the try so the
            // finally below still releases the XML-phase caches on this path.
            if (Settings == null)
            {
                LogMessage(() => "Settings failed to initialize - skipping all boot actions.", LogMessageType.Error, ignoreSetting: true);
                return;
            }

            Settings.TryInitializeCollections();

            HashSet<string> enabledSettingsSet = [.. EnabledSettings];
            bool hasEnabledSettings = enabledSettingsSet.Count > 0;
            
            // Must run before ClearAllCaches nulls the XML-phase caches, and regardless of
            // enabled settings: live toggles later in the session need this data too.
            CategoryApplier.SnapshotModAddedCategories();

            if (hasEnabledSettings && Settings.Logging)
            {
                foreach (var setting in enabledSettingsSet)
                {
                    LogMessage(() => $"Enabled setting: [{setting}].");
                }
            }

            // Live-appliable actions: each self-diffs against current Settings, so this single
            // call serves both this boot pass (nothing previously applied - pure forward apply)
            // and every later settings-window close (see NewHarvestPatchesMod.WriteSettings).
            LiveActions.TryApplyAll();

            BootActions.TryApplyAll(enabledSettingsSet);
        }
        finally
        {
            ClearAllCaches();
        }
    }
}