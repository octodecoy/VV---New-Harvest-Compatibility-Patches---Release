using System.Text;

namespace NewHarvestPatches;

public static partial class Utils
{
    /// <summary>
    /// Reads mod versions from About.xml metadata and detects when this mod's version changed since the
    /// last run. Metadata-only, so it is usable from the Mod constructor before defs exist.
    /// </summary>
    public static class VersionChecker
    {
        private const string UnknownVersion = "UNKNOWN VERSION";

        /// <summary>
        /// Version from the mod's About.xml. Reads <see cref="NewHarvestPatchesMod.Instance"/>, which is
        /// assigned one line before this class is first touched (<see cref="NewHarvestPatchesMod"/>
        /// constructor) - touching this class any earlier leaves it permanently null.
        /// </summary>
        public static readonly Version ModVersion = GetVersion();

        /// <summary>(version stored in settings from the last run, version of this build).</summary>
        public static (Version oldVersion, Version newVersion) Versions { get; private set; }

        /// <summary>No usable version pair - treat every version test below as "do not run".</summary>
        public static bool VersionUnknown => Versions.newVersion == null;

        /// <summary>First run on this profile (no stored version, or an unparseable one).</summary>
        public static bool IsFirstInstall => !VersionUnknown && Versions.oldVersion == new Version("0.0");

        /// <summary>
        /// True when this run crossed <paramref name="version"/> going up: it is newer than what was stored
        /// and no newer than this build. The primitive for a one-time migration - a migration gated on this
        /// runs exactly once, on the launch that performs the upgrade past that version.
        /// </summary>
        public static bool UpgradedPast(Version version) =>
            !VersionUnknown
            && Versions.oldVersion != null
            && Versions.oldVersion < version
            && Versions.newVersion >= version;

        /// <summary>An installed New Harvest module, as its own About.xml describes it.</summary>
        public readonly struct ModuleInfo(string name, string version)
        {
            public readonly string Name = name;
            public readonly string Version = version;
        }

        /// <summary>
        /// Every New Harvest module packageId, in the order modules are listed to the user. Names and
        /// versions are read from each module's own About.xml - this is the only thing we declare.
        /// </summary>
        private static readonly string[] s_modulePackageIds =
        [
            ModName.PackageId.MainId,
            ModName.PackageId.ForageId,
            ModName.PackageId.GardenId,
            ModName.PackageId.IndustrialId,
            ModName.PackageId.MedicinalId,
            ModName.PackageId.TreesId,
            ModName.PackageId.FlowersId,
            ModName.PackageId.MushroomsId,
            ModName.PackageId.FruitId,
        ];

        /// <summary>
        /// Installed New Harvest modules, in <see cref="s_modulePackageIds"/> order. Resolved ONCE - the
        /// active mod list cannot change without a restart. Empty when no module is installed.
        /// </summary>
        public static readonly List<ModuleInfo> InstalledModules = ResolveInstalledModules();

        private static List<ModuleInfo> ResolveInstalledModules()
        {
            List<ModuleInfo> modules = [];

            foreach (string packageId in s_modulePackageIds)
            {
                if (!IsModActive(packageId, out ModMetaData modData))
                    continue;

                bool hasVersion = !string.IsNullOrWhiteSpace(modData.ModVersion);
                if (!hasVersion)
                    LogMessage(() => $"Found mod [{modData.Name}] with unknown version.", LogMessageType.Error);

                modules.Add(new ModuleInfo(modData.Name, hasVersion ? modData.ModVersion : UnknownVersion));
            }

            return modules;
        }

        private static Version GetVersion()
        {
            string versionString = NewHarvestPatchesMod.Instance?.MetaData?.ModVersion;
            return Version.TryParse(versionString, out Version version) ? version : null;
        }

        /// <summary>
        /// Compares this build's version against the one stored in settings and, on any difference, writes
        /// the new one back immediately. Missing or unparseable stored versions are treated as 0.0 so a
        /// first install or a corrupted value still yields a usable "upgraded from" pair rather than null.
        /// A downgrade is logged as unexpected but allowed.
        /// </summary>
        /// <returns>(stored version, current version). Both null if the current version could not be read,
        /// which callers treat as "version unknown", not "unchanged".</returns>
        internal static (Version oldVersion, Version newVersion) UpdateModVersion()
        {
            try
            {
                Version currentVersion = ModVersion;
                if (currentVersion == null)
                {
                    LogMessage(() => "Could not get version.", LogMessageType.Error);
                    return (null, null);
                }

                string savedVersionString = Settings.ModVersion;

                Version savedVersion = null;
                bool update = false;

                string currentDisplay = VersionToDisplayString(currentVersion);

                if (string.IsNullOrWhiteSpace(savedVersionString))
                {
                    LogMessage(() => $"No saved version found, setting to version {currentDisplay}.");

                    savedVersion = new Version("0.0");

                    update = true;
                }
                else
                {
                    if (!Version.TryParse(savedVersionString, out savedVersion)) 
                    { 
                        savedVersion = new Version("0.0"); 
                        update = true; 
                    }

                    int comparison = currentVersion.CompareTo(savedVersion);

                    if (comparison != 0)
                    {
                        update = true;

                        string savedDisplay = VersionToDisplayString(savedVersion);

                        if (comparison > 0)
                            LogMessage(() => $"Updated from version [{savedDisplay}] to version [{currentDisplay}].", LogMessageType.Warning);
                        else
                            LogMessage(() => $"Downgraded from version [{savedDisplay}] to version [{currentDisplay}]. This is unexpected.", LogMessageType.Warning);
                    }
                    else
                    {
                        LogMessage(() => $"Version is [{currentDisplay}].");
                    }
                }

                if (update)
                {
                    Settings.UpdateScribedVersion(currentVersion);
                }

                Versions = (savedVersion, currentVersion);
                return Versions;
            }
            catch (Exception ex)
            {
                LogException(ex, ex.TargetSite);
                Versions = (null, null);
                return Versions;
            }
        }

        /// <summary>
        /// Formats a Version with only the components it actually has, so a two-part version does not
        /// display as "1.2.-1.-1" (Version reports absent components as -1, and ToString(n) throws if n
        /// exceeds what was parsed).
        /// </summary>
        public static string VersionToDisplayString(Version v)
        {
            if (v == null)
                return UnknownVersion;

            if (v.Revision >= 0)
                return v.ToString(4);

            if (v.Build >= 0)
                return v.ToString(3);

            return v.ToString(2);
        }

        /// <summary>One "Name: version" line per installed New Harvest module. Null when none installed.</summary>
        public static string InstalledModulesReport()
        {
            if (InstalledModules.NullOrEmpty())
                return null;

            StringBuilder builder = new();
            foreach (ModuleInfo module in InstalledModules)
            {
                string version = "v" + module.Version;
                builder.AppendLine($"    {module.Name.Colorize(ColorLibrary.PastelGreen)}: {version.Colorize(cyan)}");
            }
            return builder.ToString().TrimEndNewlines();
        }
    }
}