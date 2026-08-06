namespace NewHarvestPatches;

public static partial class Utils
{
    /// <summary>
    /// Short static accessors for the settings instance and its enabled-setting set. Exists so the whole
    /// codebase can write <c>Settings</c> / <c>EnabledSettings</c> after a global using static, rather than
    /// reaching through <see cref="NewHarvestPatchesMod"/> at every site.
    /// </summary>
    public static class SettingChecker
    {
        public static NewHarvestPatchesModSettings Settings => NewHarvestPatchesMod.Settings;

        /// <summary>
        /// Null ONLY when the settings object itself is missing - the instance property behind it always
        /// returns a set. That happens when the Mod constructor threw: vanilla's <c>CreateModClasses</c>
        /// catches and continues, so the rest of boot still runs with a null Settings. Guarding here rather
        /// than letting it throw keeps that one failure reportable instead of burying it under an NRE from
        /// every gate that runs afterwards (<see cref="ConditionalSetting"/> already logs on this null).
        /// </summary>
        public static HashSet<string> EnabledSettings => Settings?.EnabledSettings;

        /// <summary>
        /// The gate every action uses: "is this setting asking for work right now". True only when the
        /// setting is on AND its [RequiresMod] is satisfied, because unavailable settings never enter
        /// EnabledSettings - so no caller needs its own mod check.
        /// Pass nameof(Settings.TheField). For the inverted groups "on" means the field is false
        /// (see <see cref="DisabledIsEnabledAttribute"/>).
        /// </summary>
        public static bool IsSettingEnabled(string settingName) => EnabledSettings?.Contains(settingName) == true;

        /// <summary>
        /// Recovers the defNames behind a group of prefixed synthetic setting names - given
        /// <c>Setting.Prefix.DisabledFuel_</c>, returns the defName of every fuel currently disabled.
        /// The inverse of how the collection-backed providers build those names.
        /// </summary>
        public static List<string> ExtractNamesFromEnabledSettings(string substring)
        {
            if (string.IsNullOrWhiteSpace(substring))
                return [];

            if (EnabledSettings is not { } enabled)
                return [];

            return [.. enabled
                .Where(s => s.StartsWith(substring))
                .Select(s => s.Substring(substring.Length))];
        }
    }
}