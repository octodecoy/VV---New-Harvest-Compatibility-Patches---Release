using Verse.Sound;

namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    // Null to start - the default tab is assigned automatically on first draw and used if none is selected.
    // Setter is private so every tab change goes through SelectTab and cannot skip the scroll reset.
    public static TabCategoryDef SelectedTab { get; private set; }
    private static TabCategoryDef DefaultTab => InternalTabCategoryDefOf.VV_NHCP_GeneralTab; // Cannot static assign because DefOfs will be null at that point.

    /// <summary>
    /// Only way to change the selected tab. The options scroll offset belongs to the previous tab's
    /// content, so it is dropped here - carrying it onto different content lands the view mid-nowhere.
    /// </summary>
    private static void SelectTab(TabCategoryDef tab)
    {
        SelectedTab = tab;
        s_optionsScrollPosition = Vector2.zero;
    }
    private static float s_tabsViewRectHeight;
    private static float s_optionsViewRectHeight;
    private static Vector2 s_tabsScrollPosition = Vector2.zero;
    private static Vector2 s_optionsScrollPosition = Vector2.zero;
    private const float TabWidth = 160f;
    private const float ScrollbarWidth = GenUI.ScrollBarWidth + 10f;
    private const float TabRowStep = 50f;
    private const float TabRowHeight = TabRowStep - 2f;
    private const float TabIconSize = 20f;
    private const float TabIconOffset = 10f;
    private const float TabTextOffset = TabIconSize + TabIconOffset;
    private static readonly Vector2 s_infoButtonSize = new(20f, 20f);
    // Color the info button breathes toward/from white. Change this to change the breathe hue.
    private static readonly Color s_infoButtonPulseColor = cyan;
    private static readonly float s_closeButtonHeight = Window.CloseButSize.y;
    public static readonly Color MenuSectionBGBorderColor = new ColorInt(97, 108, 122).ToColor;
    private const float SectionLineThickness = 2f;
    private const float HeaderHeight = 24f;

    // While true, every setting-input helper applies its default value instead of drawing/reading
    // input. Used by ResetTab to replay a tab's own draw method as a reset pass, so a setting's
    // reset always matches whichever tab currently draws it - move the setting, the reset follows.
    private static bool s_isResettingTab;

    // Breath the info button color until it has been clicked this game session.
    private static bool s_infoButtonClicked = false;

    /// <summary>
    /// Root of the settings window: pulsing info button, the tab column on the left, the selected tab's
    /// options on the right. Each region runs inside its own <see cref="UIState"/> scope because
    /// Text.Anchor is global mutable state - leaking one region's anchor into the next is how RimWorld
    /// UI silently mis-aligns.
    /// </summary>
    public void DoSettingsWindowContents(Rect inRect)
    {
        using (new UIState(anchor: TextAnchor.MiddleCenter))
        {
            Color infoButtonColor = !s_infoButtonClicked ? s_infoButtonPulseColor * Pulser.PulseBrightness(0.75f, 0.5f) : white;
            Rect infoButtonRect = new(inRect.x + TabWidth / 2 - (GenUI.GapTiny * 2), inRect.y + (HeaderHeight - s_infoButtonSize.y) / 2f - GenUI.GapTiny, s_infoButtonSize.x, s_infoButtonSize.y);

            if (Widgets.ButtonImage(
                infoButtonRect,
                TexButton.Info ?? BaseContent.BadTex,
                baseColor: infoButtonColor,
                mouseoverColor: s_infoButtonClicked ? s_infoButtonPulseColor : white))
            {
                s_infoButtonClicked = true;
                string modules = Utils.VersionChecker.InstalledModulesReport();

                string messageBoxText =
                    "General_Version".TranslateKey(keepTags: true, args: Utils.VersionChecker.VersionToDisplayString(Utils.VersionChecker.ModVersion).Colorize(cyan))
                    + (modules.NullOrEmpty() ? "" : "\n\n" + "General_InstalledModules".TranslateKey() + "\n" + modules)
                    + "\n\n"
                    + "General_RightClickToReset".TranslateKey()
                    + "\n\n"
                    + "General_RestartRequired".TranslateKey(keepTags: true, args: TranslateUtility.RestartMarker);

                Find.WindowStack.Add(new Dialog_MessageBox(messageBoxText, title: "General_Information".TranslateKey()));
            }

            TooltipHandler.TipRegion(infoButtonRect, "General_Information".TranslateKey());
        }
        
        using (new UIState(anchor: TextAnchor.MiddleLeft))
        {
            DoTabs(new(inRect.x, inRect.y + HeaderHeight, TabWidth, inRect.height - HeaderHeight));
        }

        using (new UIState(anchor: TextAnchor.UpperLeft))
        {
            DoOptions(
                SelectedTab,
                new(
                    inRect.x + TabWidth + GenUI.Gap,
                    inRect.y + HeaderHeight + GenUI.GapTiny,
                    inRect.width - TabWidth - GenUI.Gap,
                    inRect.height - s_closeButtonHeight - GenUI.GapTiny));
        }
    }

    private void DoTabs(Rect inRect)
    {
        if (!Tabs.Contains(SelectedTab))
            SelectTab(DefaultTab);

        bool needsScrollbar = s_tabsViewRectHeight > inRect.height;

        Rect outRect = new(inRect);
        Rect viewRect = new(outRect.x, outRect.y, outRect.width - (needsScrollbar ? ScrollbarWidth : 0f), s_tabsViewRectHeight);

        Widgets.BeginScrollView(outRect, ref s_tabsScrollPosition, viewRect);

        int tabIndex = 0;
        foreach (TabCategoryDef tab in Tabs)
        {
            Rect rect = new(viewRect.x, viewRect.y + tabIndex * TabRowStep, viewRect.width, TabRowHeight);
            DoTabRow(rect.ContractedBy(GenUI.GapTiny), tab);
            tabIndex++;
        }

        s_tabsViewRectHeight = tabIndex * TabRowStep;

        Widgets.EndScrollView();
    }

    private void DoTabRow(Rect r, TabCategoryDef tab)
    {
        DoResetFloatMenu(r, tab);

        Widgets.DrawOptionBackground(r, tab == SelectedTab);

        string controlName = tab.defName;
        GUI.SetNextControlName(controlName);

        if (Widgets.ButtonInvisible(r))
        {
            if (SelectedTab != tab)
            {
                SelectTab(tab);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            // Keep keyboard focus on the clicked row.
            GUI.FocusControl(controlName);
        }

        Widgets.DrawHighlightIfMouseover(r);

        float textX = r.x + TabIconOffset;

        Rect iconRect = new(textX, r.y + (r.height - TabIconSize) / 2f, TabIconSize, TabIconSize);
        if (UITextureCache.MenuTabIcons.TryGetValue(tab, out Texture2D icon))
            GUI.DrawTexture(iconRect, icon);

        textX += TabTextOffset;
        Rect labelRect = new(textX, r.y, r.width - textX, r.height);
        Widgets.Label(labelRect, tab.LabelCapSafe().Truncate(labelRect.width));
    }

    /// <summary>
    /// Right-click-to-reset for a single setting: pops a one-option float menu running
    /// <paramref name="resetAction"/>. Consumes the event, so a row may only register one of these.
    /// Called by nearly every draw helper - a helper that omits it has no right-click reset.
    /// </summary>
    private void DoResetFloatMenu(Rect rect, Action resetAction)
    {
        if (resetAction == null)
            return;

        if (!rect.IsBeingRightClicked())
            return;

        string label = "General_Reset".TranslateKey(ColorLibrary.Gold, args: "General_Setting".TranslateKey());

        Find.WindowStack.Add(new FloatMenu(
        [
            new(label, resetAction),
        ]));

        Event.current.Use();
    }

    /// <summary>Whole-tab variant of <see cref="DoResetFloatMenu(Rect, Action)"/>, raised from a tab row.</summary>
    private void DoResetFloatMenu(Rect rect, TabCategoryDef tab)
    {
        if (tab == null)
            return;

        if (!rect.IsBeingRightClicked())
            return;

        string label = "General_Reset".TranslateKey(ColorLibrary.Peach, args: tab.LabelCapSafe());

        Find.WindowStack.Add(new FloatMenu(
        [
            new(label, () => ResetTab(tab)),
        ]));

        Event.current.Use();
    }

    /// <summary>
    /// Right-clicking a tab row asks for confirmation, then replays that tab's own draw method
    /// in reset mode (see <see cref="ApplyTabReset"/>) so every setting it currently owns reverts to default.
    /// </summary>
    private void ResetTab(TabCategoryDef tab)
    {
        if (tab == null)
            return;

        string text = "General_ResetTabConfirm".TranslateKey(args: tab.LabelCapSafe());
        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, () => ApplyTabReset(tab), destructive: true));
    }

    /// <summary>
    /// Runs <see cref="DrawTabBody"/> into an off-screen listing with s_isResettingTab set, so every input
    /// helper takes its reset-mode early-return instead of drawing. Same dispatch <see cref="DoOptions"/>
    /// uses, so a setting's reset always follows wherever it's currently drawn - no separate reset table
    /// to maintain. The off-screen rect is deliberately enormous and far above the viewport: the pass must
    /// visit every row (so each one resets) while producing no visible output.
    /// </summary>
    private void ApplyTabReset(TabCategoryDef tab)
    {
        s_isResettingTab = true;
        try
        {
            Listing_Standard listing = new();
            listing.Begin(new(0f, -100000f, TabWidth * 4f, 100000f));
            DrawTabBody(tab, listing);
            listing.End();
        }
        finally
        {
            s_isResettingTab = false;
        }

        UIBufferCache.ClearCache();
        GUI.FocusControl(null);
        MarkSettingChanged();
        SoundDefOf.Click.PlayOneShotOnCamera();
    }

    /// <summary>
    /// Single dispatch point mapping a tab to the settings it draws - shared by normal drawing
    /// (<see cref="DoOptions"/>) and tab reset (<see cref="ApplyTabReset"/>). This is the only place
    /// tab-to-setting ownership lives; moving a setting to another tab moves its reset with it.
    /// </summary>
    private void DrawTabBody(TabCategoryDef tab, Listing_Standard listing)
    {
        if (tab == InternalTabCategoryDefOf.VV_NHCP_GeneralTab)
        {
            DoGeneralTab(listing);
        }
        else if (tab == InternalTabCategoryDefOf.VV_NHCP_CategoriesTab)
        {
            DoCategoriesTab(listing);
        }
        else if (tab == InternalTabCategoryDefOf.VV_NHCP_CommonalityTab)
        {
            DoCommonalityTab(listing);
        }
        else if (tab == InternalTabCategoryDefOf.VV_NHCP_CraftingTab)
        {
            DoCraftingTab(listing);
        }
        else if (tab == InternalTabCategoryDefOf.VV_NHCP_FloorsTab)
        {
            DoFloorsTab(listing);
        }
        else if (tab == InternalTabCategoryDefOf.VV_NHCP_FuelTab)
        {
            DoFuelTab(listing);
        }
        else if (tab == InternalTabCategoryDefOf.VV_NHCP_VisualsTab)
        {
            DoVisualsTab(listing);
        }
        else if (tab == InternalTabCategoryDefOf.VV_NHCP_BehaviorsTab)
        {
            DoBehaviorsTab(listing);
        }
        else if (tab == InternalTabCategoryDefOf.VV_NHCP_MiscTab)
        {
            DoMiscTab(listing);
        }
    }

    private void DoOptions(TabCategoryDef tab, Rect inRect)
    {
        bool needsScrollbar = s_optionsViewRectHeight > inRect.height;
        Rect outRect = new(inRect);
        UITextureCache.DrawBackgroundLogo(outRect);
        Rect viewRect = new(outRect.x, outRect.y, outRect.width - (needsScrollbar ? ScrollbarWidth : 0f), s_optionsViewRectHeight);
        Widgets.BeginScrollView(outRect, ref s_optionsScrollPosition, viewRect);
        Listing_Standard listing = new();
        Rect rect = new(viewRect.x, viewRect.y, viewRect.width, 999999f);
        listing.Begin(rect);
        listing.verticalSpacing = 5f;

        DrawTabBody(tab, listing);

        s_optionsViewRectHeight = listing.CurHeight;
        listing.End();
        Widgets.EndScrollView();
    }
}