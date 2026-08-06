namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    internal static bool s_settingChanged = false;

    /// <summary>
    /// Universal mutation hook: arms the debounced settings write and invalidates the
    /// EnabledSettings cache.
    /// </summary>
    public static void MarkSettingChanged()
    {
        s_settingChanged = true;
        Settings?.InvalidateEnabledSettings(); // Universal mutation hook - cache must not go stale mid-session.
    }

    internal void WriteSettingsToFile()
    {
        Write();
        s_settingChanged = false;
        InvalidateEnabledSettings(); // Belt-and-suspenders: guarantee a fresh set for any immediate re-apply after write.
    }

    internal void WriteSettingsKeepFlag()
    {
        Write();
        InvalidateEnabledSettings(); // Belt-and-suspenders: guarantee a fresh set for any immediate re-apply after write.
    }

    internal void UpdateScribedVersion(Version v)
    {
        ModVersion = Utils.VersionChecker.VersionToDisplayString(v);
        WriteSettingsToFile();
    }
}