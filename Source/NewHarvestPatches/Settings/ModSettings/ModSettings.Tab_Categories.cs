using System.Text.RegularExpressions;

namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    private const float CardHeaderHeight = 34f;
    private const float CardDividerGap = 8f;
    private const float CardDividerThickness = 2f;
    private const float CardRowHeight = 34f;
    private const float CardRowGap = 8f;
    private const float CardInnerContentHeight = CardHeaderHeight + CardDividerGap + (CardRowHeight * 4f) + (CardRowGap * 3f);
    private const float CardHeight = CardInnerContentHeight;
    private const float CardSeparation = GenUI.GapLabel * 1.5f;
    private static readonly Regex s_disallowedLabelCharsRegex = new("\\[|\\]|\\{|\\}", RegexOptions.Compiled);

    // Fixed icon paths - const so the folder + name concat happens at compile time rather than per card
    // per frame. The per-kind label icon is built alongside the card's strings instead (it needs the key).
    private const string MergeIconPath = UITextureCache.IconFolder + "MergeIcon";
    private const string ReadoutIconPath = UITextureCache.IconFolder + "ResourceReadoutIcon";
    private const string RenameIconPath = UITextureCache.IconFolder + "EditTextIcon";
    private const string EditContentsIconPath = UITextureCache.IconFolder + "EditCategoriesIcon";

    // One card's translated strings, keyed by category kind. Wiped in ClearMenuSessionCaches().
    private static Dictionary<string, CategoryCardStrings> s_categoryCardStrings;

    /// <summary>
    /// Every translated string one category card draws. All of them carry runtime args (the category type
    /// name and the category's own label), so <see cref="GetSettingLabel"/> cannot cache them by field name
    /// - they were being rebuilt for all six cards on every OnGUI frame, tooltips included. The category
    /// label is user-editable mid-session via the rename field, so the entry snapshots the label it was
    /// built from and <see cref="GetCategoryCardStrings"/> rebuilds on mismatch; that keeps the rename
    /// visible immediately without any invalidation call at the rename site.
    /// </summary>
    private sealed class CategoryCardStrings
    {
        public string LabelSnapshot;
        public string CategoryKey;
        public string LabelIconPath;
        public string MergeLabel;
        public string MergeTooltip;
        public string ReadoutLabel;
        public string ReadoutTooltip;
        public string EditContentsLabel;
        public string EditContentsTooltip;
        public string RenameTooltip;
    }

    private static CategoryCardStrings GetCategoryCardStrings(string categoryTypeKey, ThingCategoryDef categoryDef)
    {
        s_categoryCardStrings ??= [];

        if (s_categoryCardStrings.TryGetValue(categoryTypeKey, out CategoryCardStrings cached)
            && cached.LabelSnapshot == categoryDef.label)
        {
            return cached;
        }

        string categoryKey = $"General_{categoryTypeKey}".TranslateKey();
        string categoryLabelCap = categoryDef.LabelCapSafe();

        cached = new CategoryCardStrings
        {
            LabelSnapshot = categoryDef.label,
            CategoryKey = categoryKey,
            LabelIconPath = UITextureCache.IconFolder + categoryTypeKey + "Icon",
            MergeLabel = "CheckboxLabel_MergeCategory".TranslateKey(args: categoryKey),
            MergeTooltip = "CheckboxTooltip_MergeCategory".TranslateKey(args: [categoryLabelCap, categoryKey]),
            ReadoutLabel = "CheckboxLabel_ResourceReadout".TranslateKey(),
            ReadoutTooltip = "CheckboxTooltip_ResourceReadout".TranslateKey(args: categoryLabelCap),
            EditContentsLabel = "Button_EditCategoryContents".TranslateKey(),
            EditContentsTooltip = "Tooltip_EditCategoryContents".TranslateKey(args: categoryLabelCap),
            RenameTooltip = "Tooltip_ChangeCategoryName".TranslateKey(args: categoryKey),
        };

        s_categoryCardStrings[categoryTypeKey] = cached;
        return cached;
    }

    private void DoCategoriesTab(Listing_Standard listing)
    {
        if (s_isResettingTab)
            ResetAllCategoryAssignments();

        DrawCategoryGroup(listing, Category.Kind.AnimalFoods,
            InternalThingCategoryDefOf.VV_NHCP_DummyCategory_AnimalFoods,
            ref MergeAnimalFoodsCategory, ref AnimalFoodsCategoryResourceReadout,
            v => MergeAnimalFoodsCategory = v, v => AnimalFoodsCategoryResourceReadout = v);

        DrawCategoryGroup(listing, Category.Kind.Fruit,
            InternalThingCategoryDefOf.VV_NHCP_DummyCategory_Fruit,
            ref MergeFruitCategory, ref FruitCategoryResourceReadout,
            v => MergeFruitCategory = v, v => FruitCategoryResourceReadout = v);

        DrawCategoryGroup(listing, Category.Kind.Vegetables,
            InternalThingCategoryDefOf.VV_NHCP_DummyCategory_Vegetables,
            ref MergeVegetablesCategory, ref VegetablesCategoryResourceReadout,
            v => MergeVegetablesCategory = v, v => VegetablesCategoryResourceReadout = v);

        DrawCategoryGroup(listing, Category.Kind.Nuts,
            InternalThingCategoryDefOf.VV_NHCP_DummyCategory_Nuts,
            ref MergeNutsCategory, ref NutsCategoryResourceReadout,
            v => MergeNutsCategory = v, v => NutsCategoryResourceReadout = v);

        DrawCategoryGroup(listing, Category.Kind.Grains,
            InternalThingCategoryDefOf.VV_NHCP_DummyCategory_Grains,
            ref MergeGrainsCategory, ref GrainsCategoryResourceReadout,
            v => MergeGrainsCategory = v, v => GrainsCategoryResourceReadout = v);

        DrawCategoryGroup(listing, Category.Kind.Fungus,
            InternalThingCategoryDefOf.VV_NHCP_DummyCategory_Fungus,
            ref MergeFungusCategory, ref FungusCategoryResourceReadout,
            v => MergeFungusCategory = v, v => FungusCategoryResourceReadout = v);
    }

    /// <summary>
    /// Whole-tab reset: every def in every dummy category returns to its default assignment; changed defs
    /// are moved back physically right away rather than waiting for the window to close, because the
    /// dialog that would otherwise apply them is not open.
    /// </summary>
    private void ResetAllCategoryAssignments()
    {
        List<(ThingDef def, string newCategoryDefName)> diffs = CategoryAssignments.ResetAllToOriginal();
        if (diffs.Count > 0)
            CategoryApplier.TryApplyLive(diffs);
    }

    /// <summary>
    /// One category's card: Merge (parent) over the rename field, readout toggle and edit-contents button
    /// (children). The nesting is a real dependency chain, not just visual - each child's VALUE is forced
    /// off here when Merge is off, since the draw library only grays a child out and never cascades state.
    /// Readout is passed as both a ref and a setter because the reset lambdas cannot capture a ref.
    /// </summary>
    /// <param name="categoryTypeKey">Category type name (see <see cref="Category.Kind"/>); also the
    /// per-entry id suffix that keeps the six cards' shared "Merge" setting names distinct.</param>
    private void DrawCategoryGroup(
        Listing_Standard listing,
        string categoryTypeKey,
        ThingCategoryDef categoryDef,
        ref bool merge,
        ref bool resourceReadout,
        Action<bool> mergeSetter,
        Action<bool> resourceReadoutSetter)
    {
        CategoryCardStrings text = GetCategoryCardStrings(categoryTypeKey, categoryDef);

        Rect cardRect = listing.GetRect(CardHeight);
        float curY = cardRect.y;

        curY = DrawCardHeader(cardRect, curY, text.LabelIconPath, text.CategoryKey);

        // Merge row (parent). Labels/tooltips carry runtime args (category names) - not cacheable
        // by field name, so they're passed as overrides from the card's string cache.
        Rect mergeRowRect = new(cardRect.x, curY, cardRect.width, CardRowHeight);
        curY += CardRowHeight + CardRowGap;

        SettingCheckbox(
            mergeRowRect,
            "Merge",
            ref merge,
            mergeSetter,
            labelOverride: text.MergeLabel,
            tooltipOverride: text.MergeTooltip,
            idSuffix: categoryTypeKey,
            iconTexPath: MergeIconPath,
            iconScale: 0.8f);

        // Enable-cascade: children's VALUES are forced off when their parent is off - the library
        // only grays children out, it never cascades state.
        if (!merge)
            resourceReadout = false;

        // Category label text field row (child, indent x1) - aligned with the readout row below.
        Rect labelFieldRowRect = IndentedRect(new Rect(cardRect.x, curY, cardRect.width, CardRowHeight));
        curY += CardRowHeight + CardRowGap;

        TextFieldWithIcon(labelFieldRowRect, text.RenameTooltip, categoryDef, merge, RenameIconPath);

        // ResourceReadout row (child, indent x1).
        Rect resourceRowRect = IndentedRect(new Rect(cardRect.x, curY, cardRect.width, CardRowHeight));

        SettingCheckbox(
            resourceRowRect,
            "ResourceReadout",
            ref resourceReadout,
            resourceReadoutSetter,
            enabled: merge,
            labelOverride: text.ReadoutLabel,
            tooltipOverride: text.ReadoutTooltip,
            idSuffix: categoryTypeKey,
            iconTexPath: ReadoutIconPath,
            iconScale: 0.8f);

        if (!merge)
            resourceReadout = false;

        // Edit-contents row (child, indent x1) - opens the category children editor.
        curY += CardRowHeight + CardRowGap;
        Rect editRowRect = IndentedRect(new Rect(cardRect.x, curY, cardRect.width, CardRowHeight));
        DrawEditCategoryChildrenButton(editRowRect, text, categoryDef, merge, EditContentsIconPath);

        listing.Gap(CardSeparation);
    }

    /// <summary>
    /// Button opening <see cref="Dialog_EditCategoryChildren"/> for this category; only enabled while the
    /// merge setting is on, since without a merge there is no category for defs to be assigned to.
    /// Skipped entirely during tab reset - a reset must not open a window.
    /// </summary>
    private void DrawEditCategoryChildrenButton(Rect rect, CategoryCardStrings text, ThingCategoryDef categoryDef, bool enabled, string texPath)
    {
        if (s_isResettingTab)
            return;

        Rect buttonRowRect;
        using (new UIState(color: enabled ? white : gray))
        {
            buttonRowRect = DrawIconAndGetRect(rect, scale: 0.8f, texPath: texPath);
            string label = text.EditContentsLabel;
            DoTooltip(buttonRowRect, text.EditContentsTooltip, !enabled);

            float buttonWidth = Mathf.Min(buttonRowRect.width, label.GetWidthCached() + 32f);
            Rect buttonRect = buttonRowRect.LeftPartPixels(buttonWidth);

            if (Widgets.ButtonText(buttonRect, label, active: enabled) && enabled)
            {
                Find.WindowStack.Add(new Dialog_EditCategoryChildren(categoryDef));
            }
        }
    }

    /// <summary>
    /// Resets a category's def label and the stored rename. Three places have to agree or the reset only
    /// half-lands: the cache entry, the live def, and the text-field buffer - plus focus must be dropped,
    /// since a focused TextField ignores an externally changed string. The cache entry is cleared to empty
    /// rather than written back with the original: empty is what "no rename" means, and storing the
    /// original instead would re-pin the label against future def or translation changes.
    /// </summary>
    private void ResetCategoryLabel(ThingCategoryDef categoryDef)
    {
        if (!Settings.CategoryLabelCache.TryGetValue(categoryDef.defName, out var cached) || cached == null)
            return;

        if (cached.CurrentCategoryLabel.NullOrEmpty())
            return;

        cached.CurrentCategoryLabel = "";
        categoryDef.label = cached.OriginalCategoryLabel;
        categoryDef.ClearCachedData();
        MarkSettingChanged();

        // TextField reads from this buffer, not categoryDef.label directly - must overwrite it too or the field shows stale edit.
        UIBufferCache.SetTextFieldBuffer(ref UIBufferCache.s_categoryLabelBuffers, GetCategoryLabelBufferKey(categoryDef), cached.OriginalCategoryLabel);
        
        // TextField ignores the new value while it still has focus - drop focus so the reset value actually shows.
        GUI.FocusControl(null);
    }

    private static string GetCategoryLabelBufferKey(ThingCategoryDef categoryDef) => $"CategoryLabel_{categoryDef.defName}";

    /// <summary>
    /// Category rename field. Writes straight to categoryDef.label (and clears the def's cached label) as
    /// the user types, so the rename is visible immediately; the value is mirrored into
    /// <see cref="CategoryLabelInfo"/> for persistence. Bracket characters are stripped because RimWorld
    /// treats them as markup in labels, and a blank field left unfocused snaps back to the live label
    /// rather than committing an empty name.
    /// </summary>
    private void TextFieldWithIcon(Rect rect, string tooltip, ThingCategoryDef categoryDef, bool enabled, string texPath)
    {
        if (s_isResettingTab)
        {
            ResetCategoryLabel(categoryDef);
            return;
        }

        Rect fieldRect;
        using (new UIState(color: enabled ? white : gray))
        {
            fieldRect = DrawIconAndGetRect(rect, scale: 0.8f, texPath: texPath);
        }

        string bufferKey = GetCategoryLabelBufferKey(categoryDef);
        if (!UIBufferCache.TryGetTextFieldBuffer(UIBufferCache.s_categoryLabelBuffers, bufferKey, out string buffer))
            buffer = categoryDef.label;

        DoTooltip(fieldRect, tooltip, !enabled);

        if (enabled)
        {
            DoResetFloatMenu(fieldRect, () => ResetCategoryLabel(categoryDef));
        }

        GUI.SetNextControlName(bufferKey);

        using (new UIState(enabled: enabled))
        {
            buffer = Widgets.TextField(fieldRect, buffer, maxLength: 30);
        }

        buffer = s_disallowedLabelCharsRegex.Replace(buffer, "");

        // Field was left blank and focus moved away - snap back to the live label instead of showing an empty box.
        if (string.IsNullOrWhiteSpace(buffer) && GUI.GetNameOfFocusedControl() != bufferKey)
            buffer = categoryDef.label;

        UIBufferCache.SetTextFieldBuffer(ref UIBufferCache.s_categoryLabelBuffers, bufferKey, buffer);

        if (!enabled)
            return;

        string trimmed = buffer.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed) && trimmed != categoryDef.label)
        {
            // Before the def is written, not after: UpdateCategoryLabelInfo's create branch captures
            // OriginalCategoryLabel off categoryDef.label, and that has to be the label being renamed AWAY
            // from. Writing the def first would make the rename its own "original" and leave reset a no-op.
            CategoryLabelInfo.UpdateCategoryLabelInfo(categoryDef, trimmed);

            categoryDef.label = trimmed;
            categoryDef.ClearCachedData();
            MarkSettingChanged();
        }
    }
}
