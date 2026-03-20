using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NewHarvestPatches
{
    public static class Utils
    {
        public static class Logger
        {
            private static bool LoggingEnabled => Settings.Logging;
            public const string Prefix = "[NewHarvestPatches] - ";

            public static void ToLog(string message, int severity = 0, [CallerFilePath] string filePath = null, [CallerMemberName] string caller = null)
            {
                if (!LoggingEnabled)
                    return;

                string className = filePath != null ? System.IO.Path.GetFileNameWithoutExtension(filePath) : "UnknownClass";
                string m = $"{Prefix}Caller: [{className}.{caller}] - {message}";
                if (severity == 1)
                    Log.Warning(m);
                else if (severity == 2)
                    Log.Error(m);
                else
                    Log.Message(m);
            }

            public static void ExToLog(Exception exception, MethodBase method, string optMsg = null)
            {
                string omsg = optMsg != null ? $"(Additional info: {optMsg})\n" : "";
                string m = $"{Prefix}{omsg}Exception in {method.DeclaringType.FullName}.{method.Name}: {exception.Message}\n{exception.StackTrace}";
                Log.Error(m);
            }

            private static ConcurrentDictionary<string, Stopwatch> _stopwatches = new();
            public static double TimeElapsed;

            public static double StartStopwatch(string callerClass, string callerMethod)
            {
                if (!LoggingEnabled)
                    return 0;

                string key = $"{callerClass}.{callerMethod}";
                double previousElapsed = 0;

                // Stop and remove any existing stopwatch for this method
                if (_stopwatches.TryRemove(key, out var existing))
                {
                    existing.Stop();
                    previousElapsed = existing.Elapsed.TotalSeconds;
                    TimeElapsed += previousElapsed;
                    ToLog($"{key} (previous run) completed in: {previousElapsed:F3} seconds.", 0);
                }

                var sw = Stopwatch.StartNew();
                _stopwatches[key] = sw;
                return previousElapsed;
            }

            public static double LogStopwatch(string callerClass, string callerMethod)
            {
                if (!LoggingEnabled)
                    return 0;

                string key = $"{callerClass}.{callerMethod}";

                if (_stopwatches.TryRemove(key, out var sw))
                {
                    sw.Stop();
                    var elapsed = sw.Elapsed.TotalSeconds;
                    TimeElapsed += elapsed;
                    ToLog($"{key} completed in: {elapsed:F3} seconds.", 0);
                    return elapsed;
                }
                else
                {
                    ToLog($"{key} completed but stopwatch was not running.", 0);
                    return 0;
                }
            }

            public static void LogInitTime()
            {
                foreach (var sw in _stopwatches.Values)
                {
                    sw.Stop();
                }

                _stopwatches = null;

                ToLog($"Finished initializing in {TimeElapsed:F3} seconds.", 0);
                TimeElapsed = 0;
            }
        }

        public static class VersionChecker
        {
            public static bool HasCurrentGameVersion = ModsConfig.OdysseyActive || IsGameVersionAtLeast(new Version("1.6"));
            /// <summary>
            /// Version from the mod's About.xml.
            /// </summary>
            public static Version ModVersion = GetVersion();
            public static string[] PackageIDs
            {
                get
                {
                    var ids = new List<string>();
                    if (HasMainModule) ids.Add("vvenchov.vvnewharvest");
                    if (HasForageModule) ids.Add("vvenchov.vvnewharvestforagecrops");
                    if (HasGardenModule) ids.Add("vvenchov.vvnewharvestgardencrops");
                    if (HasIndustrialModule) ids.Add("vvenchov.vvnewharvestindustrialcrops");
                    if (HasMedicinalModule) ids.Add("vvenchov.vvnewharvestmedicinalplants");
                    if (HasTreesModule) ids.Add("vvenchov.vvnewharvesttrees");
                    if (HasFlowersModule) ids.Add("vvenchov.vvnewharvestflowers");
                    if (HasMushroomsModule) ids.Add("vvenchov.vvnewharvestmushrooms");
                    return [.. ids];
                }
            }
            /// <summary>
            /// Dictionary of active New Harvest modules and their versions from their About.xml.
            /// </summary>
            /// 
            public static Dictionary<string, (string version, string translationKey)> NewHarvestVersions => GetModVersions(PackageIDs);

            public static bool IsGameVersionAtLeast(Version version, bool checkBuild = false, bool checkRev = false)
            {
                if (version == null) 
                    return false;

                // Build a version to compare against, depending on which parts we care about
                int build = checkBuild ? VersionControl.CurrentBuild : 0;
                int revision = checkRev ? VersionControl.CurrentRevision : 0;

                Version current = new(VersionControl.CurrentMajor, VersionControl.CurrentMinor, build, revision);

                return current.CompareTo(version) >= 0;
            }

            public static Dictionary<string, (string version, string translationKey)> GetModVersions(params string[] packageIDs)
            {
                if (packageIDs.Length == 0)
                    return [];

                var moduleNames = StaticArrays.ModuleNames; // index 0 is "Full"

                var dictionary = new Dictionary<string, (string, string)>();
                foreach (var packageID in packageIDs)
                {
                    var mod = ModLister.GetActiveModWithIdentifier(packageID);
                    if (mod != null)
                    {
                        string translationKey = null;
                        foreach (var moduleName in moduleNames)
                        {
                            if (packageID.ContainsIgnoreCase(moduleName))
                            {
                                translationKey = moduleName;
                                break;
                            }
                        }

                        bool hasModVersion = !string.IsNullOrWhiteSpace(mod.ModVersion);
                        if (!hasModVersion)
                        {
                            ToLog($"Found mod [{mod.Name}] with unknown version.", 2);
                        }

                        string modVersion = hasModVersion ? mod.ModVersion : "UNKNOWN VERSION";

                        translationKey ??= HasMainModule ? moduleNames[0] : "UNKNOWN MODULE";

                        dictionary[mod.Name] = (modVersion, translationKey);
                    }
                }
                return dictionary;
            }

            private static Version GetVersion()
            {
                string versionString = NewHarvestPatchesMod.Instance?.MetaData?.ModVersion;
                return string.IsNullOrWhiteSpace(versionString) ? null : new Version(versionString);
            }


            internal static (Version oldVersion, Version newVersion) UpdateModVersion()
            {
                try
                {
                    Version currentVersion = ModVersion;
                    if (currentVersion == null)
                    {
                        ToLog("Could not get version.", 2);
                        return (null, null);
                    }

                    string savedVersionString = Settings.ModVersion;

                    Version savedVersion = null;
                    bool update = false;

                    if (string.IsNullOrWhiteSpace(savedVersionString))
                    {
                        string versionDisplay = currentVersion.Build >= 0
                            ? $"{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}"
                            : $"{currentVersion.Major}.{currentVersion.Minor}";
                        ToLog($"No saved version found, setting to version {versionDisplay}.", 0);
                        update = true;
                    }
                    else
                    {
                        savedVersion = new Version(savedVersionString);

                        // Use Version.CompareTo for proper version comparison
                        int comparison = currentVersion.CompareTo(savedVersion);

                        if (comparison != 0)
                        {
                            update = true;
                            string currentDisplay = currentVersion.Build >= 0
                                ? $"{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}"
                                : $"{currentVersion.Major}.{currentVersion.Minor}";
                            string savedDisplay = savedVersion.Build >= 0
                                ? $"{savedVersion.Major}.{savedVersion.Minor}.{savedVersion.Build}"
                                : $"{savedVersion.Major}.{savedVersion.Minor}";

                            if (comparison > 0)
                            {
                                ToLog($"Updated from version [{savedDisplay}] to version [{currentDisplay}].", 0);
                            }
                            else
                            {
                                ToLog($"Downgraded from version [{savedDisplay}] to version [{currentDisplay}]. This is unexpected.", 1);
                            }
                        }
                    }

                    if (update)
                    {
                        Settings.UpdateScribedVersion(currentVersion);
                    }

                    return (savedVersion, currentVersion);
                }
                catch (Exception ex)
                {
                    ExToLog(ex, MethodBase.GetCurrentMethod());
                    return (null, null);
                }
            }
        }

        public static class SettingChecker
        {
            public static NewHarvestPatchesModSettings Settings => NewHarvestPatchesMod.Settings;

            public static IEnumerable<string> EnabledSettings => Settings.EnabledSettings;

            public static List<string> ExtractNamesFromEnabledSettings(string substring)
            {
                return [.. EnabledSettings
                .Where(s => s.StartsWith(substring))
                .Select(s => s.Substring(substring.Length))];
            }
        }

        public static class ModChecker
        {
            public static bool HasMainModule = IsModActive("vvenchov.vvnewharvest");
            public static bool HasForageModule = HasMainModule || IsModActive("vvenchov.vvnewharvestforagecrops");
            public static bool HasGardenModule = HasMainModule || IsModActive("vvenchov.vvnewharvestgardencrops");
            public static bool HasIndustrialModule = HasMainModule || IsModActive("vvenchov.vvnewharvestindustrialcrops");
            public static bool HasMedicinalModule = HasMainModule || IsModActive("vvenchov.vvnewharvestmedicinalplants");
            public static bool HasTreesModule = HasMainModule || IsModActive("vvenchov.vvnewharvesttrees");
            public static bool HasFlowersModule = HasMainModule || IsModActive("vvenchov.vvnewharvestflowers");
            public static bool HasMushroomsModule = HasMainModule || IsModActive("vvenchov.vvnewharvestmushrooms");
            public static bool HasAnyModule = HasForageModule || HasGardenModule || HasIndustrialModule || HasMedicinalModule || HasTreesModule || HasFlowersModule || HasMushroomsModule;
            public static bool HasMedievalOverhaul = ModsConfig.IsActive("dankpyon.medieval.overhaul");
            public static bool HasVanillaCookingExpanded = ModsConfig.IsActive("vanillaexpanded.vcooke");
            public static bool HasVanillaPlantsExpandedMorePlants = ModsConfig.IsActive("vanillaexpanded.vplantsemore");

            /// <summary>
            /// If installed, show setting to move Medicinal module drinks to its non-alcoholic category.
            /// </summary>
            public static bool HasVanillaBrewingExpanded = ModsConfig.IsActive("vanillaexpanded.vbrewe");

            /// <summary>
            /// If Ferny's Floor Menu is active, we don't need to show our floor dropdown settings, as it makes dropdowns obsolete.
            /// </summary>
            public static bool HasFernyFloorMenu = ModsConfig.IsActive("ferny.floormenu");

            /// <summary>
            /// If Vanilla Expanded Framework is active, we can show commonality sliders utlizing its StuffExtension class instead of base game commonality stat.
            /// </summary>
            public static bool HasVanillaExpandedFramework = ModsConfig.IsActive("oskarpotocki.vanillafactionsexpanded.core");

            /// <summary>
            /// If Better Sliders is active, we don't need to place text fields next to our sliders.
            /// </summary>
            public static bool HasBetterSliders = ModsConfig.IsActive("sirrandoo.bettersliders");


            /// <summary>
            /// Indicates whether any tree modules are installed.
            /// </summary>
            /// <remarks>Returns <see langword="true"/>If HasIndustrialModule or HasMedicinalModule or HasTreesModule.</remarks>
            public static bool HasAnyTrees = HasIndustrialModule || HasMedicinalModule || HasTreesModule;

            /// <summary>
            /// Indicates whether any modules that provide fruit are installed.
            /// </summary>
            /// <remarks>Returns <see langword="true"/>If HasGardenModule or HasMedicinalModule or HasTreesModule.</remarks>
            public static bool HasAnyFruit = HasGardenModule || HasMedicinalModule || HasTreesModule;

            /// <summary>
            /// Indicates whether any modules that provide vegetables are installed.
            /// </summary>
            /// <remarks>Returns <see langword="true"/>If HasGardenModule or HasMedicinalModule.</remarks>
            public static bool HasAnyVegetables = HasGardenModule || HasMedicinalModule;

            /// <summary>
            /// Indicates whether the fuel settings should be displayed in menu.
            /// </summary>
            /// <remarks>If !HasIndustrialModule or HasMedievalOverhaul or LWM's Fuel Filter/[JPT] Burn It for Fuel 2 is active this returns <see langword="false"/>.
            /// Both mods add a fuel ITab, so user can just toggle fuels themselves.
            /// </remarks>
            public static bool ShowFuelSettings = HasIndustrialModule && !HasMedievalOverhaul && !ModsConfig.IsActive("zal.lwmfuelfilter") && !ModsConfig.IsActive("jpt.burnitforfuel");

            /// <summary>
            /// Indicates whether the Wood Conversion Recipe setting should be displayed in menu.
            /// </summary>
            /// <remarks>If Medieval Overhaul, Expanded Woodworking or Extended Woodworking are active this returns <see langword="false"/>.
            /// All 3 mods add recipes to convert wood types, and it's too much hassle to change our recipe's product just to fit.
            /// </remarks>
            public static bool ShowWoodConvertRecipe = !HasMedievalOverhaul && !IsAnyModActive(ignorePostfix: true, "teflonjim.extendedwoodworking", "zal.expandwoodwork");

            /// <summary>
            /// Indicates whether Vanilla Expanded Framework's commonality Stuff Extension sliders should be displayed in menu.
            /// </summary>
            /// <remarks>If !HasIndustrialModule or !HasVanillaExpandedFramework, returns <see langword="false"/>.</remarks>
            public static bool ShowVEFCommonalitySettings = HasIndustrialModule && HasVanillaExpandedFramework;

            /// <summary>
            /// Checks if a mod is active based on its PackageID.  Mainly used to check for New Harvest modules while ignoring steam_suffix postfix for Dev versions.
            /// </summary>
            /// <returns>Returns <see langword="false"/> if the passed PackageID is not active.</returns>
            public static bool IsModActive(string packageId, bool ignorePostfix = true)
            {
                return !string.IsNullOrWhiteSpace(packageId) && ModLister.GetActiveModWithIdentifier(packageId, ignorePostfix) != null;
            }

            public static bool IsAnyModActive(bool ignorePostfix = true, params string[] packageIds)
            {
                if (packageIds.NullOrEmpty())
                    return false;

                if (ignorePostfix)
                    return ModLister.AnyModActiveNoSuffix([.. packageIds]);
                else
                    return ModLister.AnyFromListActive([.. packageIds]);
            }
        }
    }
}