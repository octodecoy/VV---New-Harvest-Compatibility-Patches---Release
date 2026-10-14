namespace NewHarvestPatches;

/// <summary>
/// Category-scoped editor listing every food def the mod tracks. Defs owned by this
/// category show checked; unowned defs can be claimed; defs owned by another category
/// are disabled and show their owner. Right-click resets one entry, shift + right-click
/// offers resetting the whole category.
/// </summary>
public class Dialog_EditCategoryChildren : Window, ISettingsMenuChildWindow
{
    /// <summary>Per-def draw data resolved once on open so drawing does no lookups or allocations.</summary>
    private class Row
    {
        public ThingDef Def;
        public DefCategoryAssignment Assignment;
        public string Label;
        public string SearchText;
        public string Tooltip;
    }

    private static readonly Color s_disabledRowColor = new(0f, 0f, 0f, 0.5f);

    private readonly string _categoryDefName;
    private List<Row> _allRows;
    private List<Row> _filteredRows;
    private Dictionary<string, string> _categoryLabels;
    // Assignment state when the dialog opened, so PostClose can live-apply only what changed.
    private Dictionary<string, string> _openSnapshot;
    private Vector2 _scrollPosition = Vector2.zero;
    private readonly QuickSearchWidget _searchWidget = new();

    private const float RowHeight = 50f;
    private const float RowGap = GenUI.GapTiny;
    private const float RowStep = RowHeight + RowGap;
    private const float CloseButtonRowHeight = 55f;
    private const float SearchBarGap = GenUI.Pad;

    public override Vector2 InitialSize => new(650f, 600f);
    public override QuickSearchWidget CommonSearchWidget => _searchWidget;

    public Dialog_EditCategoryChildren(ThingCategoryDef category)
    {
        _categoryDefName = category.defName;
        forcePause = true;
        doCloseButton = true;
        doCloseX = true;
        absorbInputAroundWindow = true;
        resizeable = true;
        optionalTitle = "Title_EditCategoryChildren".TranslateKey(args: category.LabelCapSafe());
    }

    protected override void LateWindowOnGUI(Rect inRect)
    {
        base.LateWindowOnGUI(inRect);
        Rect searchRect = QuickSearchWidgetRect(windowRect.AtZero(), inRect);
        TooltipHandler.TipRegion(searchRect, "EditCategory_SearchTypes".TranslateKey());
    }

    public override void PreOpen()
    {
        base.PreOpen();
        CategoryApplier.EnsureClassified();
        CategoryAssignments.EnsureEditorData();
        BuildRows();
        SnapshotAssignments();
    }

    public override void PostClose()
    {
        base.PostClose();
        var diffs = CollectAssignmentDiffs(); // Before the flush prunes transient entries.
        CategoryAssignments.FlushEditorData();
        if (s_settingChanged)
            Settings.WriteSettingsKeepFlag();

        if (diffs.Count > 0)
            CategoryApplier.TryApplyLive(diffs);
    }

    private void SnapshotAssignments()
    {
        _openSnapshot = [];
        foreach (var assignment in Settings.CategoryData)
        {
            if (!assignment.ThingDefName.NullOrEmpty())
                _openSnapshot[assignment.ThingDefName] = assignment.CurrentCategory;
        }
    }

    /// <summary>Defs whose assignment changed while the dialog was open, resolved for the live engine.</summary>
    private List<(ThingDef def, string newCategoryDefName)> CollectAssignmentDiffs()
    {
        List<(ThingDef, string)> diffs = [];
        if (_openSnapshot == null)
            return diffs;

        foreach (var assignment in Settings.CategoryData)
        {
            if (assignment.ThingDefName.NullOrEmpty())
                continue;

            _openSnapshot.TryGetValue(assignment.ThingDefName, out string oldCategory);
            if ((oldCategory ?? "") == assignment.CurrentCategory)
                continue;

            var def = DefDatabase<ThingDef>.GetNamedSilentFail(assignment.ThingDefName);
            if (def != null)
                diffs.Add((def, assignment.CurrentCategory));
        }
        return diffs;
    }

    public override void Notify_CommonSearchChanged()
    {
        FilterRows();
    }

    /// <summary>Resolves defs, labels and tooltips once; drawing only reads these.</summary>
    private void BuildRows()
    {
        _allRows = [];
        _categoryLabels = [];

        foreach (var assignment in Settings.CategoryData)
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(assignment.ThingDefName);
            if (def == null)
                continue;

            string label = def.LabelCapSafe();
            string modName = def.modContentPack?.Name ?? "??";
            CacheCategoryLabel(assignment.OriginalCategory);
            CacheCategoryLabel(assignment.CurrentCategory);

            _allRows.Add(new Row
            {
                Def = def,
                Assignment = assignment,
                Label = label,
                SearchText = $"{label}\n{def.defName}\n{modName}".ToLowerInvariant(),
                Tooltip = modName + "\n\n" + "defName: " + def.defName + "\n\n" + (def.description ?? ""),
            });
        }

        _allRows.Sort((a, b) => CurrentCultureComparer.Compare(a.Label, b.Label));
        FilterRows();
    }

    private void CacheCategoryLabel(string categoryDefName)
    {
        if (categoryDefName.NullOrEmpty() || _categoryLabels.ContainsKey(categoryDefName))
            return;

        var categoryDef = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(categoryDefName);
        _categoryLabels[categoryDefName] = categoryDef.LabelCapSafe();
    }

    private string GetCategoryLabel(string categoryDefName)
    {
        return _categoryLabels.TryGetValue(categoryDefName, out string label) ? label : categoryDefName;
    }

    private void FilterRows()
    {
        if (_allRows == null)
        {
            _filteredRows = null;
            return;
        }

        string search = _searchWidget.filter.Text;
        if (string.IsNullOrWhiteSpace(search))
        {
            _filteredRows = _allRows;
            return;
        }

        search = search.Trim().ToLowerInvariant();
        _filteredRows = [.. _allRows.Where(row =>
            row.SearchText.Contains(search)
            || (row.Assignment.HasCategory && GetCategoryLabel(row.Assignment.CurrentCategory).ToLowerInvariant().Contains(search)))];
    }

    public override void DoWindowContents(Rect inRect)
    {
        if (_filteredRows == null)
            return;

        float reservedHeight = CloseButtonRowHeight + QuickSearchSize.y + SearchBarGap;
        Rect scrollableRect = new(inRect.x, inRect.y, inRect.width, inRect.height - reservedHeight);

        int rowCount = _filteredRows.Count;
        float contentHeight = rowCount > 0 ? rowCount * RowStep - RowGap : 0f;
        Rect viewRect = new(0f, 0f, scrollableRect.width - GenUI.ScrollBarWidth * 2f, contentHeight);
        Widgets.BeginScrollView(scrollableRect, ref _scrollPosition, viewRect);

        // Fixed row height - only draw the rows inside the visible scroll window.
        int firstVisible = Mathf.Max(0, Mathf.FloorToInt(_scrollPosition.y / RowStep));
        int lastVisible = Mathf.Min(rowCount - 1, Mathf.CeilToInt((_scrollPosition.y + scrollableRect.height) / RowStep));

        for (int i = firstVisible; i <= lastVisible; i++)
        {
            DrawRow(_filteredRows[i], new Rect(0f, i * RowStep, viewRect.width, RowHeight));
        }

        Widgets.EndScrollView();
    }

    private void DrawRow(Row row, Rect rowRect)
    {
        var assignment = row.Assignment;
        bool isChecked = assignment.IsAssignedTo(_categoryDefName);
        bool isEditable = assignment.CanBeEditedIn(_categoryDefName);

        if (isEditable && Mouse.IsOver(rowRect))
        {
            Widgets.DrawTextHighlight(rowRect, expandBy: 0f, color: isChecked ? Colors.RedHighlightColor : Colors.GreenHighlightColor);
        }

        Rect iconRect = rowRect.LeftPartPixels(rowRect.height);
        Widgets.DefIcon(iconRect, row.Def, drawPlaceholder: true);

        Rect checkboxRect = rowRect.RightPartPixels(rowRect.width - rowRect.height - GenUI.GapTiny);

        bool value = isChecked;
        Widgets.CheckboxLabeled(checkboxRect, row.Label, ref value, disabled: !isEditable);

        if (isEditable && value != isChecked)
        {
            if (value)
                assignment.AssignTo(_categoryDefName);
            else
                assignment.Unassign(byUser: true);
            MarkSettingChanged();
        }

        // Lazy tooltip - only built when actually hovered.
        TooltipHandler.TipRegion(checkboxRect, new TipSignal(() => GetRowTooltip(row, isEditable), row.Def.shortHash ^ 0x2E9C0A5));

        if (!isEditable)
            DrawOwnerOverlay(checkboxRect, assignment);

        DoRowResetFloatMenu(rowRect, row, isEditable);
    }

    /// <summary>Dims a row owned by another category and shows that category's name on it.</summary>
    private void DrawOwnerOverlay(Rect checkboxRect, DefCategoryAssignment assignment)
    {
        string ownerLabel = "EditCategory_OwnedBy".TranslateKey(args: GetCategoryLabel(assignment.CurrentCategory));

        using (new UIState(color: s_disabledRowColor))
        {
            GUI.DrawTexture(checkboxRect, BaseContent.WhiteTex);
        }

        using (new UIState(color: yellow, anchor: TextAnchor.MiddleCenter))
        {
            // Vector2 labelSize = Text.CalcSize(ownerLabel);
            Vector2 labelSize = ownerLabel.GetSizeCached();
            Rect overlayRect = new(checkboxRect.x + (checkboxRect.width - labelSize.x) / 2f,
                                   checkboxRect.y + (checkboxRect.height - labelSize.y) / 2f,
                                   labelSize.x, labelSize.y);
            Widgets.Label(overlayRect, ownerLabel);
        }
    }

    private string GetRowTooltip(Row row, bool isEditable)
    {
        if (isEditable)
            return row.Tooltip + "\n\n" + "EditCategory_RightClickHint".TranslateKey();

        string ownerLabel = GetCategoryLabel(row.Assignment.CurrentCategory);
        string moveHint = "EditCategory_MoveHint".TranslateKey(args: ownerLabel);
        return ("EditCategory_OwnedBy".TranslateKey(args: ownerLabel) + "\n\n" + moveHint + "\n\n" + row.Tooltip).Colorize(gray);
    }

    /// <summary>Right-click resets one entry; shift + right-click offers resetting the whole category.</summary>
    private void DoRowResetFloatMenu(Rect rowRect, Row row, bool isEditable)
    {
        if (!rowRect.IsBeingRightClicked())
            return;

        if (Event.current.shift)
        {
            string label = "General_Reset".TranslateKey(ColorLibrary.Peach, args: "EditCategory_AllEntries".TranslateKey());
            Find.WindowStack.Add(new FloatMenu([new(label, ConfirmResetAll)]));
            Event.current.Use();
            return;
        }

        if (!isEditable)
        {
            // Owned by another category: offer moving it here (single owner field, so AssignTo just overwrites).
            if (row.Assignment.HasCategory)
            {
                string moveLabel = "EditCategory_MoveHere".TranslateKey(
                    ColorLibrary.Gold, args: GetCategoryLabel(row.Assignment.CurrentCategory));
                Find.WindowStack.Add(new FloatMenu(
                [
                    new(moveLabel, () =>
                    {
                        row.Assignment.AssignTo(_categoryDefName);
                        MarkSettingChanged();
                    }),
                ]));
                Event.current.Use();
            }
            return;
        }

        string resetLabel = "General_Reset".TranslateKey(ColorLibrary.Gold, args: row.Label);
        Find.WindowStack.Add(new FloatMenu(
        [
            new(resetLabel, () =>
            {
                row.Assignment.ResetToOriginal();
                MarkSettingChanged();
            }),
        ]));
        Event.current.Use();
    }

    private void ConfirmResetAll()
    {
        string text = "Button_CategoryResetInfo".TranslateKey() + "\n\n" + "Button_AreYouSure".TranslateKey();
        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
            text,
            () =>
            {
                CategoryAssignments.ResetCategory(_categoryDefName);
                MarkSettingChanged();
            },
            destructive: true));
    }
}
