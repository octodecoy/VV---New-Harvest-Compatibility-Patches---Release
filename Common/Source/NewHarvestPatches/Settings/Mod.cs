namespace NewHarvestPatches;

/// <summary>
/// Verse.Mod entry point. Two jobs beyond hosting the settings window: it is the earliest code this
/// mod runs (constructed before defs load, so version detection happens here rather than in
/// <see cref="Bootstrap"/>), and its <see cref="WriteSettings"/> override is the single place live
/// settings get pushed into the def graph when the window closes.
/// </summary>
public class NewHarvestPatchesMod : Mod
{
    public static NewHarvestPatchesModSettings Settings;
    public static NewHarvestPatchesMod Instance { get; private set; }

    public ModMetaData MetaData => Content.ModMetaData;

    // Runs before defs are loaded
    public NewHarvestPatchesMod(ModContentPack content) : base(content)
    {
        Instance = this;
        Settings = GetSettings<NewHarvestPatchesModSettings>();
        ActionRunner.Run(nameof(NewHarvestPatchesMod), nameof(NewHarvestPatchesMod), TryInitialize);
    }

    /// <summary>
    /// Records this mod's version (comparing it against the stored one so an update can be detected,
    /// via <see cref="Utils.VersionChecker.Versions"/>) and logs which New Harvest modules are
    /// installed. Runs before defs exist, so it may only touch mod metadata - never the DefDatabase.
    /// </summary>
    public void TryInitialize()
    {
        Utils.VersionChecker.UpdateModVersion();

        foreach (Utils.VersionChecker.ModuleInfo module in Utils.VersionChecker.InstalledModules)
            LogMessage(() => $"Detected New Harvest module: [{module.Name} - v{module.Version}]");
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        base.DoSettingsWindowContents(inRect);
        DrawCenteredTitle(inRect);
        Settings.DoSettingsWindowContents(inRect);
    }

    internal static string s_modName = null;
    private static string ModName =>
        s_modName ??= "☀".Colorize(ColorLibrary.Peach)
            + " " + "General_ModName".TranslateKey()
            + " " + "☀".Colorize(ColorLibrary.Peach);

    // Vanilla draws SettingsCategory() top-left in Rect(0,0,w-167,35), then hands us a rect starting at
    // y=40 inside the SAME GUI group - so y=0 here reoccupies the title row it left blank.
    private static void DrawCenteredTitle(Rect inRect)
    {
        using (new UIState(GameFont.Medium, anchor: TextAnchor.MiddleCenter))
            Widgets.Label(new Rect(inRect.x, 0f, inRect.width, 35f), ModName);
    }

    // Set by Window.InnerWindowOnGUI for the duration of each window's draw. A Dialog_ModSettings only
    // ever asks its own mod for SettingsCategory, so seeing one here means it is ours - which an
    // IsOpen<Dialog_ModSettings> check can not tell (any mod's window satisfies it), and which also
    // excludes the Options mod list, whose rows call this every frame from Dialog_Options' own draw.
    private static bool IsOwnSettingsWindowDrawing => Find.WindowStack?.currentlyDrawnWindow is Dialog_ModSettings;

    // If the settings window is open, we write "" so that we can draw the mod name centered instead within DoSettingsWindowContents.
    public override string SettingsCategory()
        => IsOwnSettingsWindowDrawing ? "" : "General_ModName".TranslateKey();

    // Called by RimWorld when the settings window closes. Guarded entry point — a throw here would skip windows.Remove in WindowStack.TryRemove and leave the dialog stuck open.
    public override void WriteSettings()
    {
        ActionRunner.Run(nameof(NewHarvestPatchesMod), nameof(WriteSettings), DoWriteSettings);
        base.WriteSettings();
    }

    /// <summary>
    /// This is where every live-appliable change actually reaches the def graph. 
    /// Order matters and is not interchangeable: child windows are closed first so their PostClose 
    /// can still arm the changed flag, category toggles are applied before the generic live actions 
    /// (they move defs the later actions read), and the enabled-settings cache is dropped in between 
    /// so those actions see post-apply values. Both apply passes are gated on something having actually 
    /// changed. Every widget that writes a live-appliable setting arms the flag 
    /// (SettingCheckbox, the material picker's SetSelectedColor sites, the commonality grid, and TryConsumeTabReset for tab resets), 
    /// and the collections are otherwise only touched by TryInitializeCollections at boot - so an unarmed 
    /// close has nothing to apply and nothing to revert. The trade is that a close with no changes no longer 
    /// re-asserts this mod's values over anything a third party overwrote mid-session; that repair only ever happened
    /// incidentally, when the user opened and closed the window, and was never a guarantee.
    /// </summary>
    private void DoWriteSettings()
    {
        s_modName = null;

        bool settingChanged = s_settingChanged;
        s_settingChanged = false;

        // Allow PostClose actions that use s_settingChanged to run before finalizing the setting change.
        CloseOpenWindows();

        settingChanged |= s_settingChanged;   // a child closing may arm it again
        s_settingChanged = false;

        // Re-sync any Merge/ResourceReadout changes - only if something actually changed.
        if (settingChanged)
            CategoryApplier.TryApplyToggles();

        // Enabled-settings cache is read by live actions (e.g. FallColorDisabler). CategoryApplier
        // above mutates defs without going through MarkSettingChanged, so refresh here to guarantee
        // live actions see the just-applied values, not a stale set.
        Settings.InvalidateEnabledSettings();

        // Every other live-appliable action: each self-diffs against current Settings and
        // reverts what it previously applied for anything just toggled off. Gated like
        // CategoryApplier above - with nothing armed, every action would diff to a no-op anyway.
        if (settingChanged)
            LiveActions.TryApplyAll();

        ClearMenuSessionCaches();
    }

    /// <summary>
    /// Closes any <see cref="ISettingsMenuChildWindow"/> still open (e.g. the category editor) so its
    /// PostClose commits before this method decides whether anything changed.
    /// Probably not needed, but whatever.
    /// </summary>
    private static void CloseOpenWindows()
    {
        var openWindows = Find.WindowStack?.Windows ?? [];
        // Must ToList() or errors when iterating and removing windows from the stack.
        foreach (var window in openWindows.ToList()) 
        {
            if (window is ISettingsMenuChildWindow)
                Find.WindowStack.TryRemove(window);
        }
    }
}