using System.Text.RegularExpressions;

namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    private const string CommonalityFloatFormat = "F4";
    private const int CommonalityMaxInputLength = 8; // "100.0000"
    // HALF the least significant digit an F4 field can show, not a whole one: both compares below are
    // strict >, so a full-step tolerance swallows single-digit edits - typing 0.0001 into a field sitting
    // at 0 gives a difference of exactly one step, which is not greater than one step, so the value never
    // commits and the row silently keeps its old number.
    private const float FloatTolerance4Decimals = 0.00005f;
    private const float CommonalityTextFieldWidth = 70f;
    private const float CommonalityIconSize = 32f;
    private const float CommonalityRowGap = 40f;
    // Up to 3 digits before decimal point, up to 4 digits after decimal point
    private static readonly Regex s_commonalityInputRegex = new(@"^\d{0,3}(\.\d{0,4})?$", RegexOptions.Compiled);
    private static float s_commonalityCardHeight = -1f;
    private static float s_commonalityHeaderHeight = -1f;
    private static float s_commonalityLabelWidth = -1f;
    // Parallel to s_commonalityLabels - same indices, same VEF-mode-vs-standard-mode shape. Nulled
    // together in ClearMenuSessionCaches() since both are built by EnsureCommonalityLabelsCached().
    private static string[] s_commonalityTooltips;
    // Same indices again: each category row's tooltip with the "Base is 0" note already prefixed. Built
    // up front rather than concatenated per row per frame, since a disabled row redraws every frame.
    private static string[] s_commonalityDisabledTooltips;

    /// <summary>
    /// Stacked icon-over-label header, distinct from the shared icon-left-of-label CardHeaderHeight. The
    /// material name is drawn at GameFont.Medium, so its line has to be measured in THAT font: the ambient
    /// Text.LineHeight during a tab draw reports Small and left the name clipped at the bottom. Cached
    /// because the measurement costs a font switch and cannot change while the menu is open - wiped in
    /// ClearMenuSessionCaches() with the other font-metric caches.
    /// </summary>
    private static float CommonalityHeaderHeight
    {
        get
        {
            if (s_commonalityHeaderHeight >= 0f)
                return s_commonalityHeaderHeight;

            float labelHeight;
            using (new UIState(font: GameFont.Medium))
            {
                labelHeight = Text.LineHeight;
            }

            s_commonalityHeaderHeight = CommonalityIconSize + GenUI.GapTiny + labelHeight;
            return s_commonalityHeaderHeight;
        }
    }

    // StuffCommonality entries snapshotted once per menu session (keys are stable while the menu
    // is open); wiped in ClearMenuSessionCaches().
    private static List<CommonalityRow> s_commonalityEntries;

    /// <summary>
    /// One card's worth of pre-resolved, session-stable header data. The def lookup, the capitalized
    /// label and the "Default: X" tooltip text are all fixed for as long as the menu is open (DefLabel and
    /// DefaultCommonality are only refreshed by <see cref="CommonalityInfo.BuildCommonalityStats"/> at
    /// init), so they are resolved once here instead of per card per frame.
    /// </summary>
    private sealed class CommonalityRow
    {
        public string Key;
        public CommonalityInfo Info;
        public ThingDef Def;
        public string LabelCap;
        public string DefaultTag;
    }

    private List<CommonalityRow> BuildCommonalityRows()
    {
        List<CommonalityRow> rows = [];
        foreach (var kvp in StuffCommonality)
        {
            rows.Add(new CommonalityRow
            {
                Key = kvp.Key,
                Info = kvp.Value,
                Def = DefDatabase<ThingDef>.GetNamedSilentFail(kvp.Key),
                LabelCap = kvp.Value.DefLabel.CapitalizeFirst(),
                DefaultTag = "General_Default".TranslateKey(args: kvp.Value.DefaultCommonality.ToString(CommonalityFloatFormat)),
            });
        }
        return rows;
    }

    /// <summary>
    /// Card-per-material layout via DrawTwoColumnGrid. Each card is icon + name centered above one
    /// (standard) or four (VEF categories installed: Base, Structure, Weapon, Apparel) rows of
    /// "commonality = [text field]" - matching whichever mode <see cref="CommonalityInfo"/> has live. The
    /// material's default commonality is a tooltip over the header rather than a drawn tag, so the card
    /// stays compact; each row has its own tooltip explaining what it governs (see
    /// <see cref="EnsureCommonalityLabelsCached"/>). In VEF mode the three category rows gray out while
    /// Base is 0 - see <see cref="DrawCommonalityCard"/>.
    /// </summary>
    private void DoCommonalityTab(Listing_Standard listing)
    {
        if (StuffCommonality.NullOrEmpty())
            return;

        EnsureCommonalityLabelsCached();

        DrawCustomSubLabel(listing, GetSettingLabel(SettingLabelKind.Raw, "TabSubLabel_CommonalityDescription"), color: white);

        listing.Gap(GenUI.GapWide);

        bool showAllRows = ShowVEFCommonalitySettings;
        float cardHeight = GetCommonalityCardHeight();

        s_commonalityEntries ??= BuildCommonalityRows();

        bool settingChanged = DrawTwoColumnGrid(
            listing,
            s_commonalityEntries,
            cellHeight: cardHeight,
            rowGap: CommonalityRowGap,
            contextHash: StuffCommonality.GetHashCode(),
            drawCell: (rect, row) => DrawCommonalityCard(rect, showAllRows, row),
            rowSeparators: true);

        if (settingChanged)
            MarkSettingChanged(); // Commonality applies live via MaterialCommonalityChanger.
    }

    private bool DrawCommonalityCard(Rect cardRect, bool showAllRows, CommonalityRow row)
    {
        string key = row.Key;
        CommonalityInfo info = row.Info;

        Rect headerRect = new(cardRect.x, cardRect.y, cardRect.width, CommonalityHeaderHeight);

        DrawCommonalityHeader(headerRect, row);

        // Header-only region, NOT the whole card: row tooltips below sit inside cardRect too, and
        // TooltipHandler.TipRegion calls stack rather than override each other - a card-wide region here
        // would draw this "Default: X" box underneath every row's own tooltip. Registered before the rows
        // so the row widgets still get their own input; tooltips do not consume clicks.
        DoTooltip(headerRect, row.DefaultTag, isDisabled: false);

        float curY = headerRect.yMax + GenUI.GapTiny;

        bool changed = false;

        if (showAllRows)
        {
            float labelWidth = GetCommonalityLabelWidth();

            // The three category rows are factors relative to Base, so a Base of 0 leaves them nothing to
            // scale - MaterialCommonalityChanger.ChangeVEF skips computing them entirely, and VEF multiplies
            // by a base of 0 regardless. Read live so the rows gray out the same frame Base reaches 0.
            bool factorsInert = info.CoreCommonality <= 0f;

            Rect baseRow = new(cardRect.x, curY, cardRect.width, CardRowHeight);
            changed |= DrawCommonalityAdjuster(baseRow, key + "_Base", s_commonalityLabels[0], labelWidth, info.CoreCommonality, info.DefaultCommonality, v => info.CoreCommonality = v, s_commonalityTooltips[0]);
            curY += CardRowHeight + CardRowGap;

            Rect structureRow = new(cardRect.x, curY, cardRect.width, CardRowHeight);
            changed |= DrawCommonalityAdjuster(structureRow, key + "_Structure", s_commonalityLabels[1], labelWidth, info.StructureOffset, info.DefaultCommonality, v => info.StructureOffset = v, GetCommonalityTooltip(1, factorsInert), factorsInert);
            curY += CardRowHeight + CardRowGap;

            Rect weaponRow = new(cardRect.x, curY, cardRect.width, CardRowHeight);
            changed |= DrawCommonalityAdjuster(weaponRow, key + "_Weapon", s_commonalityLabels[2], labelWidth, info.WeaponOffset, info.DefaultCommonality, v => info.WeaponOffset = v, GetCommonalityTooltip(2, factorsInert), factorsInert);
            curY += CardRowHeight + CardRowGap;

            Rect apparelRow = new(cardRect.x, curY, cardRect.width, CardRowHeight);
            changed |= DrawCommonalityAdjuster(apparelRow, key + "_Apparel", s_commonalityLabels[3], labelWidth, info.ApparelOffset, info.DefaultCommonality, v => info.ApparelOffset = v, GetCommonalityTooltip(3, factorsInert), factorsInert);
        }
        else
        {
            Rect coreRow = new(cardRect.x, curY, cardRect.width, CardRowHeight);
            changed |= DrawCommonalityAdjuster(coreRow, key + "_Core", s_commonalityLabels[0], s_commonalityLabels[0].GetWidthCached(), info.CoreCommonality, info.DefaultCommonality, v => info.CoreCommonality = v, s_commonalityTooltips[0]);
        }

        return changed;
    }

    /// <summary>Icon above material name, both centered. The default commonality is not drawn here - it
    /// is the card's tooltip (see <see cref="DrawCommonalityCard"/>).</summary>
    private void DrawCommonalityHeader(Rect headerRect, CommonalityRow row)
    {
        Rect iconRect = new(headerRect.center.x - (CommonalityIconSize / 2f), headerRect.y, CommonalityIconSize, CommonalityIconSize);
        if (row.Def != null)
            Widgets.DefIcon(iconRect, row.Def, drawPlaceholder: true);
        else
            Widgets.DrawTextureFitted(iconRect, UITextureCache.PlaceholderIcon, 1f);

        Rect labelRect = new(headerRect.x, iconRect.yMax + GenUI.GapTiny, headerRect.width, headerRect.yMax - iconRect.yMax - GenUI.GapTiny);
        using (new UIState(font: GameFont.Medium, anchor: TextAnchor.MiddleCenter))
        {
            Widgets.Label(labelRect, row.LabelCap);
        }
    }

    /// <summary>
    /// Draws "label [text field]" row, label left of a fixed-width 4-decimal text field, both
    /// centered as a unit. Right clicking the row resets the value to the material's default.
    /// </summary>
    /// <param name="bufferKey">Unique id for this row's text buffer AND its reset identity - must stay
    /// stable across frames, hence the "{defName}_{channel}" form the caller builds.</param>
    /// <param name="tooltip">What this row's field actually controls, shown on hover over the whole row.</param>
    /// <param name="disabled">
    /// Grays the row and blocks both the text field and the right-click reset - the three category rows
    /// pass this while Base is 0, where nothing they hold can reach the game (see
    /// <see cref="DrawCommonalityCard"/>). The stored value is deliberately left alone rather than zeroed:
    /// it goes live again the moment Base leaves 0. The TAB reset still runs while disabled, for the same
    /// reason - skipping it would leave a stale number hidden behind the gray.
    /// </param>
    /// <returns>True if the value changed this frame; the caller aggregates these to mark settings dirty once.</returns>
    private bool DrawCommonalityAdjuster(Rect rowRect, string bufferKey, string label, float labelWidth, float value, float defaultValue, Action<float> setter, string tooltip, bool disabled = false)
    {
        DoTooltip(rowRect, tooltip, disabled);

        if (TryConsumeTabReset(bufferKey, defaultValue, setter, alsoOnReset: () => ClearCommonalityBuffer(bufferKey)))
            return true;

        float oldValue = value;

        float totalWidth = Math.Min(rowRect.width, labelWidth + GenUI.GapSmall + CommonalityTextFieldWidth);
        Rect centeredRect = rowRect.MiddlePartPixels(totalWidth, rowRect.height);

        Rect labelRect = new(centeredRect.x, centeredRect.y, labelWidth, centeredRect.height);
        Rect textRect = new(labelRect.xMax + GenUI.GapSmall, centeredRect.y, CommonalityTextFieldWidth, centeredRect.height);

        if (!UIBufferCache.TryGetTextFieldBuffer(UIBufferCache.s_commonalityBuffers, bufferKey, out string buffer))
            buffer = value.ToString(CommonalityFloatFormat);

        // GUI.enabled is what actually stops the input: a disabled TextField hands back the string it was
        // given, so the parse and change check below are inert without needing their own guard.
        using (new UIState(color: disabled ? gray : white, enabled: !disabled))
        {
            using (new UIState(anchor: TextAnchor.MiddleRight))
            {
                Widgets.Label(labelRect, label);
            }

            if (!disabled)
            {
                DoResetFloatMenu(rowRect, () =>
                {
                    setter(defaultValue);
                    ClearCommonalityBuffer(bufferKey);
                    MarkSettingChanged(); // Commonality applies live via MaterialCommonalityChanger.
                });
            }

            // Hand-rolled instead of Widgets.TextFieldNumeric: Verse's version reformats the buffer to a
            // minimal string (e.g. "0.0" -> "0") the instant the typed text parses as a complete number,
            // which eats zeros mid-decimal-entry (typing "0.05" collapses to "0" after the second char).
            // Parsing the raw typed text ourselves avoids that reformat-while-typing behavior entirely.
            string typed = Widgets.TextField(textRect, buffer, CommonalityMaxInputLength, s_commonalityInputRegex);
            if (typed != buffer)
            {
                buffer = typed;
                if (float.TryParse(buffer, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                {
                    value = ClampToCommonalityRange(parsed);

                    // Input regex allows up to 999.9999, so out-of-range text must be snapped back or the
                    // field keeps showing the typed number while the setting holds the clamped one.
                    if (Math.Abs(parsed - value) > FloatTolerance4Decimals)
                        buffer = value.ToString(CommonalityFloatFormat);
                }
            }
        }

        UIBufferCache.SetTextFieldBuffer(ref UIBufferCache.s_commonalityBuffers, bufferKey, buffer);

        bool changed = Math.Abs(value - oldValue) > FloatTolerance4Decimals;
        if (changed)
            setter(value);

        return changed;
    }

    private static float ClampToCommonalityRange(float value) => Mathf.Clamp(value, CommonalityInfo.Min, CommonalityInfo.Max);

    /// <summary>
    /// Drops the stored text so the field falls back to the (just reset) value on the next frame, and
    /// releases focus - a focused TextField keeps drawing its own editing state and ignores the new string.
    /// </summary>
    private static void ClearCommonalityBuffer(string bufferKey)
    {
        UIBufferCache.SetTextFieldBuffer(ref UIBufferCache.s_commonalityBuffers, bufferKey, null);
        GUI.FocusControl(null);
    }

    /// <summary>
    /// Labels and tooltips share index order: [0]=Base in VEF mode ([0]=the single Commonality row in
    /// standard mode), [1]=Structure, [2]=Weapon, [3]=Apparel. VEF mode's Base row reuses the standard
    /// tooltip text (same field, same meaning - it IS the value written to stuffProps.commonality in
    /// both modes) with the addition that it also anchors the three factors below.
    /// The disabled variants keep the same indices so <see cref="GetCommonalityTooltip"/> can index either
    /// array with one number; [0] is the plain text in both, since the Base row is never disabled.
    /// </summary>
    private static void EnsureCommonalityLabelsCached()
    {
        if (s_commonalityLabels != null)
            return;

        if (ShowVEFCommonalitySettings)
        {
            s_commonalityLabels =
            [
                "SliderLabel_BaseCommonality".TranslateKey() + " = ",
                "SliderLabel_StructureCommonality".TranslateKey() + " = ",
                "SliderLabel_WeaponCommonality".TranslateKey() + " = ",
                "SliderLabel_ApparelCommonality".TranslateKey() + " = "
            ];
            s_commonalityTooltips =
            [
                "SliderTooltip_BaseCommonality".TranslateKey(),
                "SliderTooltip_StructureCommonality".TranslateKey(),
                "SliderTooltip_WeaponCommonality".TranslateKey(),
                "SliderTooltip_ApparelCommonality".TranslateKey()
            ];

            string zeroBaseNote = "SliderTooltip_CommonalityDisabledByZeroBase".TranslateKey();
            s_commonalityDisabledTooltips =
            [
                s_commonalityTooltips[0],
                zeroBaseNote + "\n\n" + s_commonalityTooltips[1],
                zeroBaseNote + "\n\n" + s_commonalityTooltips[2],
                zeroBaseNote + "\n\n" + s_commonalityTooltips[3]
            ];
        }
        else
        {
            s_commonalityLabels = ["SliderLabel_Commonality".TranslateKey() + " = "];
            s_commonalityTooltips = ["SliderTooltip_Commonality".TranslateKey()];

            // Standard mode draws one row that nothing can disable, so the disabled array is only present
            // to keep GetCommonalityTooltip total rather than because any caller reaches it.
            s_commonalityDisabledTooltips = s_commonalityTooltips;
        }
    }

    /// <summary>Row tooltip for the given index, with the "Base is 0" note prefixed while disabled.</summary>
    private static string GetCommonalityTooltip(int index, bool disabled) =>
        disabled ? s_commonalityDisabledTooltips[index] : s_commonalityTooltips[index];

    /// <summary>
    /// Widest of the active mode's row labels, so every row's text field aligns vertically. Loops the
    /// whole array rather than indexing fixed slots, so it stays correct regardless of how many rows
    /// the active mode draws.
    /// </summary>
    private static float GetCommonalityLabelWidth()
    {
        if (s_commonalityLabelWidth >= 0f)
            return s_commonalityLabelWidth;

        float widest = 0f;
        foreach (string label in s_commonalityLabels)
            widest = Mathf.Max(widest, label.GetWidthCached());

        s_commonalityLabelWidth = widest;
        return s_commonalityLabelWidth;
    }

    /// <summary>
    /// Card height for the active commonality mode: header plus the adjuster row stack. Reads
    /// ShowVEFCommonalitySettings directly rather than taking it as an argument - the value cannot change
    /// while the menu is open, and a parameter that the cached result ignored promised a per-call answer
    /// this never gave. The total must stay exactly the drawn content's height - DrawTwoColumnGrid uses
    /// this as the cell height, and every row tooltip is registered against its own row rect within it.
    /// </summary>
    private static float GetCommonalityCardHeight()
    {
        if (s_commonalityCardHeight >= 0f)
            return s_commonalityCardHeight;

        int rowCount = ShowVEFCommonalitySettings ? 4 : 1;
        float rowStackHeight = (CardRowHeight * rowCount) + (CardRowGap * (rowCount - 1));

        s_commonalityCardHeight = CommonalityHeaderHeight + GenUI.GapTiny + rowStackHeight;
        return s_commonalityCardHeight;
    }
}
