using Verse.Sound;

namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    /// <summary>
    /// Which key prefix a name-derived translation uses. Label kinds pair with a *Tooltip kind
    /// whose key is the matching "*SubLabel_" prefix. Raw = no prefix (full key passed as name,
    /// e.g. "General_Enabled", "ON").
    /// </summary>
    private enum SettingLabelKind : byte
    {
        Checkbox,        // CheckboxLabel_{name}
        CheckboxTooltip, // CheckboxTooltip_{name}
        Section,         // SectionLabel_{name}
        SectionSub,      // SectionLabel_{name}Sub
        Adjuster,        // AdjusterLabel_{name}
        AdjusterTooltip, // AdjusterTooltip_{name}
        Range,           // RangeLabel_{name}
        RangeTooltip,    // RangeTooltip_{name}
        Slider,          // SliderLabel_{name}
        SliderTooltip,   // SliderTooltip_{name}
        Button,          // Button_{name}
        Raw,             // {name}
    }

    // Fully-translated display strings keyed by (kind, name, restartMarker). Avoids re-allocating the
    // interpolated key + prefix concat + TaggedString conversion every OnGUI frame. Session-scoped:
    // cleared in ClearMenuSessionCaches() so a language change can never serve stale text.
    private static readonly Dictionary<(SettingLabelKind kind, string name, bool restartMarker), string> s_settingLabelCache = [];

    /// <summary>Applies the Keyed-file prefix convention for a kind; see <see cref="SettingLabelKind"/>.</summary>
    private static string BuildSettingKey(SettingLabelKind kind, string name)
    {
        return kind switch
        {
            SettingLabelKind.Checkbox => "CheckboxLabel_" + name,
            SettingLabelKind.CheckboxTooltip => "CheckboxTooltip_" + name,
            SettingLabelKind.Section => "SectionLabel_" + name,
            SettingLabelKind.SectionSub => "SectionLabel_" + name + "Sub",
            SettingLabelKind.Adjuster => "AdjusterLabel_" + name,
            SettingLabelKind.AdjusterTooltip => "AdjusterTooltip_" + name,
            SettingLabelKind.Range => "RangeLabel_" + name,
            SettingLabelKind.RangeTooltip => "RangeTooltip_" + name,
            SettingLabelKind.Slider => "SliderLabel_" + name,
            SettingLabelKind.SliderTooltip => "SliderTooltip_" + name,
            SettingLabelKind.Button => "Button_" + name,
            _ => name,
        };
    }

    /// <summary>
    /// Translated UI string for a setting, derived from the field name via the mod's key
    /// convention (e.g. Checkbox + "AddWoodDryads" -> "CheckboxLabel_AddWoodDryads".TranslateKey()).
    /// Do NOT use for labels with args (category names, yields) - pass those through labelOverride/tooltipOverride
    /// parameters instead; args make the result uncacheable by name alone.
    /// </summary>
    private static string GetSettingLabel(SettingLabelKind kind, string name, bool withRestartMarker = false)
    {
        var cacheKey = (kind, name, withRestartMarker);
        if (s_settingLabelCache.TryGetValue(cacheKey, out string cached))
            return cached;

        string translated = BuildSettingKey(kind, name).TranslateKey(withRestartMarker);
        s_settingLabelCache[cacheKey] = translated;
        return translated;
    }

    /// <summary>
    /// Tooltip variant of <see cref="GetSettingLabel"/>: returns null (cached) when the Keyed
    /// entry does not exist, so settings without a tooltip key simply draw no tooltip instead of
    /// logging a missing-translation error every frame.
    /// </summary>
    private static string GetSettingTooltip(SettingLabelKind kind, string name)
    {
        var cacheKey = (kind, name, false);
        if (s_settingLabelCache.TryGetValue(cacheKey, out string cached))
            return cached;

        string key = BuildSettingKey(kind, name);
        string translated = $"{TKey.KeyPrefix}{key}".CanTranslate() ? key.TranslateKey() : null;
        s_settingLabelCache[cacheKey] = translated;
        return translated;
    }

    /// <summary>
    /// Tab-reset gate every setting-input helper must call FIRST. While ResetTab replays the tab
    /// (s_isResettingTab), applies the default via <paramref name="setter"/>, runs
    /// <paramref name="alsoOnReset"/> (e.g. clearing a text-field buffer), marks the settings dirty,
    /// and returns true - the caller must return immediately without drawing. A helper that skips
    /// this gate silently breaks right-click tab reset for its setting.
    /// </summary>
    /// <param name="name">Identity of the setting being reset. Currently unread - the dirty flag is
    /// global, not per setting - but kept so callers stay uniform with the id-based helpers.</param>
    private static bool TryConsumeTabReset<T>(string name, T defaultValue, Action<T> setter, Action alsoOnReset = null)
    {
        if (!s_isResettingTab)
            return false;

        setter(defaultValue);
        alsoOnReset?.Invoke();
        MarkSettingChanged();
        return true;
    }

    /// <summary>
    /// Shrinks a row rect from the left by ChildIndent per level: 1 for a child setting gated on
    /// a parent bool, 2 for a grandchild (see Tab_Categories). Level 0 returns the rect unchanged.
    /// </summary>
    private static Rect IndentedRect(Rect rect, int indentLevel = 1)
    {
        if (indentLevel <= 0)
            return rect;

        float indent = ChildIndent * indentLevel;
        return new(rect.x + indent, rect.y, rect.width - indent, rect.height);
    }

    /// <summary>
    /// One-call checkbox setting row inside a Listing_Standard. Derives label
    /// ("CheckboxLabel_{name}", restart-markered when <paramref name="restartRequired"/>) and tooltip
    /// ("CheckboxTooltip_{name}", omitted if the key doesn't exist) from <paramref name="name"/>
    /// (pass nameof(Field)); both cached for the menu session. Row height comes from the
    /// TRANSLATED label via Text.CalcHeight, so long languages wrap instead of clipping. Handles
    /// tab-reset replay, right-click single reset, disabled graying, mouseover highlight, and
    /// marks the settings dirty when the value changes.
    /// A setting whose [RequiresMod] is unsatisfied draws NOTHING and consumes no layout - no caller
    /// needs its own mod check (see <see cref="IsSettingAvailable"/>).
    /// NEW SETTING:   SettingCheckbox(listing, nameof(MyField), ref MyField, v => MyField = v);
    /// CHILD SETTING: add indentLevel: 1 and enabled: ParentField (force the child's value off
    ///                when the parent is off in the tab body - the helper only grays it out).
    /// DICT-BACKED:   pass labelOverride + idSuffix (unique per entry) + per-entry defaultValue,
    ///                with a setter writing the dictionary slot (see Tab_Fuel).
    /// <paramref name="setter"/> must write the same field <paramref name="value"/> reads - refs
    /// can't be captured by the reset lambdas, so both are required.
    /// </summary>
    private bool SettingCheckbox(
        Listing_Standard listing,
        string name,
        ref bool value,
        Action<bool> setter,
        bool defaultValue = false,
        bool restartRequired = true,
        int indentLevel = 0,
        bool enabled = true,
        Def iconDef = null,
        string iconTexPath = null,
        string labelOverride = null,
        string tooltipOverride = null,
        string idSuffix = null,
        bool paintable = false,
        bool showIcon = true,
        bool placeCheckboxNearText = false,
        float iconScale = 1f)
    {
        // Before the reset gate on purpose: an unavailable setting must keep its stored value, not be
        // rewritten to the default by a tab reset it is not even part of.
        if (!IsSettingAvailable(name))
            return value;

        string id = idSuffix == null ? name : name + "_" + idSuffix;

        if (TryConsumeTabReset(id, defaultValue, setter))
            return defaultValue;

        string label = labelOverride ?? GetSettingLabel(SettingLabelKind.Checkbox, name, withRestartMarker: restartRequired);

        float indent = indentLevel > 0 ? ChildIndent * indentLevel : 0f;
        float rowHeight = Text.CalcHeight(label, listing.ColumnWidth - indent) + GenUI.GapSmall;

        Rect rect = listing.GetRect(rowHeight);
        rect.width = Math.Min(rect.width + 24f, listing.ColumnWidth);
        rect = IndentedRect(rect, indentLevel);

        bool result = SettingCheckbox(
            rect, name, ref value, setter, defaultValue, restartRequired, enabled,
            iconDef, iconTexPath, labelOverride, tooltipOverride, idSuffix,
            paintable, showIcon, placeCheckboxNearText, iconScale);

        listing.Gap(listing.verticalSpacing + GenUI.GapSmall);

        return result;
    }

    /// <summary>
    /// Rect-based overload of <see cref="SettingCheckbox(Listing_Standard, string, ref bool, Action{bool}, bool, bool, int, bool, Def, string, string, string, string, bool, bool, bool, float)"/>
    /// for fixed-layout cards where the caller owns the rect (indent it via IndentedRect).
    /// Identical key derivation, reset handling, and change notification.
    /// </summary>
    private bool SettingCheckbox(
        Rect rowRect,
        string name,
        ref bool value,
        Action<bool> setter,
        bool defaultValue = false,
        bool restartRequired = true,
        bool enabled = true,
        Def iconDef = null,
        string iconTexPath = null,
        string labelOverride = null,
        string tooltipOverride = null,
        string idSuffix = null,
        bool paintable = false,
        bool showIcon = true,
        bool placeCheckboxNearText = false,
        float iconScale = 1f)
    {
        if (!IsSettingAvailable(name))
            return value;

        string id = idSuffix == null ? name : name + "_" + idSuffix;

        if (TryConsumeTabReset(id, defaultValue, setter))
            return defaultValue;

        string label = labelOverride ?? GetSettingLabel(SettingLabelKind.Checkbox, name, withRestartMarker: restartRequired);
        string tooltip = tooltipOverride ?? GetSettingTooltip(SettingLabelKind.CheckboxTooltip, name);

        bool before = value;

        bool result = CheckboxLabeledWithIcon(
            rowRect,
            id: id,
            label: label,
            checkOn: ref value,
            defaultValue: defaultValue,
            defaultValueSetter: setter,
            tooltip: tooltip,
            texPath: iconTexPath,
            defForIcon: iconDef,
            disabled: !enabled,
            paintable: paintable,
            showIcon: showIcon,
            placeCheckboxNearText: placeCheckboxNearText,
            iconScale: iconScale);

        if (value != before)
        {
            // Redundant for field-backed settings (the ref already wrote the field), REQUIRED for
            // dict-backed settings where "value" is a local temp and only the setter hits the store.
            setter(value);
            MarkSettingChanged();
        }

        return result;
    }

    /// <summary>
    /// Icon-left card header: icon + medium-font title row (truncated to fit; title strings are
    /// stable so Verse's truncation cache absorbs the cost), divider line beneath. Returns the Y
    /// to continue drawing card rows from; consumes CardHeaderHeight + CardDividerGap. Used by
    /// the Categories and Behaviors cards. The Commonality tab keeps its own stacked
    /// icon-over-label header - a genuinely different layout.
    /// </summary>
    private static float DrawCardHeader(Rect cardRect, float curY, string iconPath, string title)
    {
        Rect headerRect = new(cardRect.x, curY, cardRect.width, CardHeaderHeight);
        Rect headerIconRect = headerRect.LeftPartPixels(CardHeaderHeight);
        Widgets.DrawTextureFitted(headerIconRect, UITextureCache.GetIcon(iconPath), 1f);

        Rect headerLabelRect = headerRect.RightPartPixels(headerRect.width - CardHeaderHeight - GenUI.GapSmall);
        using (new UIState(font: GameFont.Medium, anchor: TextAnchor.MiddleLeft))
        {
            Widgets.Label(headerLabelRect, title.Truncate(headerLabelRect.width));
        }
        curY += CardHeaderHeight;

        Rect dividerRect = new(cardRect.x, curY + ((CardDividerGap - CardDividerThickness) / 2f), cardRect.width, CardDividerThickness);
        using (new UIState(color: MenuSectionBGBorderColor))
        {
            GUI.DrawTexture(dividerRect, BaseContent.WhiteTex);
        }
        curY += CardDividerGap;

        return curY;
    }

    /// <summary>
    /// Section header with divider derived from a section name: "SectionLabel_{name}" and, when
    /// <paramref name="hasSubLabel"/>, "SectionLabel_{name}Sub" - both cached for the session.
    /// Thin wrapper over DrawSectionLabel; call that directly only for custom colors/anchors.
    /// </summary>
    private static void SettingSection(Listing_Standard listing, string name, bool hasSubLabel = false)
    {
        DrawSectionLabel(
            listing,
            label: GetSettingLabel(SettingLabelKind.Section, name),
            subLabel: hasSubLabel ? GetSettingLabel(SettingLabelKind.SectionSub, name) : null);
    }

    /// <summary>
    /// Shared scaffold for a labeled value block ("Label: value" line above a widget row):
    /// tab-reset gate, cached name-derived label, tooltip (matching *SubLabel_ key + "Default: X"
    /// line, built only while hovered), right-click reset, disabled label graying. Returns false
    /// when tab reset consumed the call - the caller must return immediately. On true,
    /// <paramref name="widgetRowRect"/> is the row under the label sized to
    /// <paramref name="widgetRowHeight"/>; draw the widget there inside
    /// using (new UIState(color: enabled ? white : gray, enabled: enabled)) and call
    /// MarkSettingChanged when the widget changes the value.
    /// Only the ": value" concat allocates per frame - the label half is cached.
    /// </summary>
    private bool BeginSettingBlock<T>(
        Rect blockRect,
        float widgetRowHeight,
        SettingLabelKind labelKind,
        SettingLabelKind tooltipKind,
        string name,
        string valueDisplay,
        string defaultDisplay,
        bool enabled,
        T defaultValue,
        Action<T> setter,
        out Rect widgetRowRect,
        bool restartRequired = true)
    {
        widgetRowRect = default;

        // Shared availability/reset gate for every value widget (adjusters, ranges, sliders).
        if (!IsSettingAvailable(name))
            return false;

        if (TryConsumeTabReset(name, defaultValue, setter))
            return false;

        Rect labelRect = new(blockRect.x, blockRect.y, blockRect.width, Text.LineHeight);
        widgetRowRect = new(blockRect.x, labelRect.yMax + GenUI.GapTiny, blockRect.width, widgetRowHeight);

        using (new UIState(color: enabled ? white : gray))
        {
            Widgets.Label(labelRect, GetSettingLabel(labelKind, name, withRestartMarker: restartRequired) + ": " + valueDisplay);
        }

        // TipRegion ignores non-hovered rects anyway - skip building the tooltip string unless hovered.
        if (Mouse.IsOver(blockRect))
            DoTooltip(blockRect, BuildDefaultTooltip(tooltipKind, name, defaultDisplay), !enabled);

        DoResetFloatMenu(blockRect, () => { setter(defaultValue); MarkSettingChanged(); });

        return true;
    }

    /// <summary>
    /// Minus/plus button row nudging a float setting by <paramref name="countChange"/> (scaled by
    /// the keyboard-modifier multiplier), clamped to [min, max]. Derives "AdjusterLabel_{name}" /
    /// "AdjusterTooltip_{name}". Rect-based only: the caller must size <paramref name="blockRect"/> to
    /// GetAdjusterBlockHeight() itself, carving it out of the listing (see Tab_Misc's flour slider).
    /// </summary>
    private void SettingFloatAdjuster(
        Rect blockRect,
        string name,
        float currentValue,
        float defaultValue,
        float min,
        float max,
        float countChange,
        bool enabled,
        Action<float> setter,
        bool restartRequired = true)
    {
        if (!BeginSettingBlock(
                blockRect, BehaviorAdjusterRowHeight, SettingLabelKind.Adjuster, SettingLabelKind.AdjusterTooltip,
                name, currentValue.ToString("F2", CultureInfo.InvariantCulture),
                defaultValue.ToString("F2", CultureInfo.InvariantCulture),
                enabled, defaultValue, setter, out Rect rowRect, restartRequired))
            return;

        using (new UIState(color: enabled ? white : gray, enabled: enabled))
        {
            float adjustedChange = countChange * GenUI.CurrentAdjustmentMultiplier();
            string changeStr = adjustedChange.ToString("F2", CultureInfo.InvariantCulture);

            Rect minusRect = new(rowRect.x, rowRect.y, BehaviorButtonWidth, rowRect.height);
            Rect plusRect = new(minusRect.xMax + BehaviorButtonSpacing, rowRect.y, BehaviorButtonWidth, rowRect.height);

            if (Widgets.ButtonText(minusRect, "-" + changeStr) && enabled)
            {
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                setter(Mathf.Clamp(currentValue - adjustedChange, min, max));
                MarkSettingChanged();
            }

            if (Widgets.ButtonText(plusRect, "+" + changeStr) && enabled)
            {
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                setter(Mathf.Clamp(currentValue + adjustedChange, min, max));
                MarkSettingChanged();
            }
        }
    }

    /// <summary>Int counterpart of <see cref="SettingFloatAdjuster"/> (tick counts etc.).</summary>
    private void SettingIntAdjuster(
        Rect blockRect,
        string name,
        int currentValue,
        int defaultValue,
        int min,
        int max,
        int countChange,
        bool enabled,
        Action<int> setter,
        bool restartRequired = true)
    {
        if (!BeginSettingBlock(
                blockRect, BehaviorAdjusterRowHeight, SettingLabelKind.Adjuster, SettingLabelKind.AdjusterTooltip,
                name, currentValue.ToString(), defaultValue.ToString(),
                enabled, defaultValue, setter, out Rect rowRect, restartRequired))
            return;

        using (new UIState(color: enabled ? white : gray, enabled: enabled))
        {
            int adjustedChange = countChange * GenUI.CurrentAdjustmentMultiplier();

            Rect minusRect = new(rowRect.x, rowRect.y, BehaviorButtonWidth, rowRect.height);
            Rect plusRect = new(minusRect.xMax + BehaviorButtonSpacing, rowRect.y, BehaviorButtonWidth, rowRect.height);

            if (Widgets.ButtonText(minusRect, "-" + adjustedChange) && enabled)
            {
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                setter(Mathf.Clamp(currentValue - adjustedChange, min, max));
                MarkSettingChanged();
            }

            if (Widgets.ButtonText(plusRect, "+" + adjustedChange) && enabled)
            {
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                setter(Mathf.Clamp(currentValue + adjustedChange, min, max));
                MarkSettingChanged();
            }
        }
    }

    /// <summary>
    /// Vanilla IntRange slider under a "RangeLabel_{name}: min - max" line. Rect-based only - caller
    /// sizes <paramref name="blockRect"/> to GetRangeBlockHeight().
    /// </summary>
    private void SettingIntRange(
        Rect blockRect,
        string name,
        IntRange currentValue,
        IntRange defaultValue,
        int min,
        int max,
        bool enabled,
        Action<IntRange> setter,
        bool restartRequired = true)
    {
        if (!BeginSettingBlock(
                blockRect, BehaviorRangeRowHeight, SettingLabelKind.Range, SettingLabelKind.RangeTooltip,
                name, $"{currentValue.min} - {currentValue.max}", $"{defaultValue.min} - {defaultValue.max}",
                enabled, defaultValue, setter, out Rect rowRect, restartRequired))
            return;

        using (new UIState(color: enabled ? white : gray, enabled: enabled))
        {
            IntRange value = currentValue;
            int id = Gen.HashCombineInt(name.GetHashCode(), 0x4E484350);
            Widgets.IntRange(rowRect, id, ref value, min, max);

            if (enabled && value != currentValue)
            {
                setter(value);
                MarkSettingChanged();
            }
        }
    }

    /// <summary>
    /// Vanilla FloatRange slider displayed/rounded as a percentage, under a
    /// "RangeLabel_{name}: X% - Y%" line. Caller sizes to GetRangeBlockHeight().
    /// </summary>
    private void SettingFloatRangePercent(
        Rect blockRect,
        string name,
        FloatRange currentValue,
        FloatRange defaultValue,
        float min,
        float max,
        float roundTo,
        bool enabled,
        Action<FloatRange> setter,
        bool restartRequired = true)
    {
        if (!BeginSettingBlock(
                blockRect, BehaviorRangeRowHeight, SettingLabelKind.Range, SettingLabelKind.RangeTooltip,
                name, $"{GenText.ToStringPercent(currentValue.min)} - {GenText.ToStringPercent(currentValue.max)}",
                $"{GenText.ToStringPercent(defaultValue.min)} - {GenText.ToStringPercent(defaultValue.max)}",
                enabled, defaultValue, setter, out Rect rowRect, restartRequired))
            return;

        using (new UIState(color: enabled ? white : gray, enabled: enabled))
        {
            FloatRange value = currentValue;
            int id = Gen.HashCombineInt(name.GetHashCode(), 0x4E484350);
            Widgets.FloatRange(rowRect, id, ref value, min, max, valueStyle: ToStringStyle.PercentZero, roundTo: roundTo);

            if (enabled && value != currentValue)
            {
                setter(value);
                MarkSettingChanged();
            }
        }
    }

    /// <summary>
    /// Vanilla horizontal slider displayed/rounded as a percentage, under a
    /// "SliderLabel_{name}: X%" line. Caller sizes to GetSliderBlockHeight().
    /// </summary>
    private void SettingFloatSliderPercent(
        Rect blockRect,
        string name,
        float currentValue,
        float defaultValue,
        float min,
        float max,
        float roundTo,
        bool enabled,
        Action<float> setter,
        bool restartRequired = true)
    {
        if (!BeginSettingBlock(
                blockRect, BehaviorSliderRowHeight, SettingLabelKind.Slider, SettingLabelKind.SliderTooltip,
                name, GenText.ToStringPercent(currentValue), GenText.ToStringPercent(defaultValue),
                enabled, defaultValue, setter, out Rect rowRect, restartRequired))
            return;

        using (new UIState(color: enabled ? white : gray, enabled: enabled))
        {
            // roundTo is applied manually below instead of passed to the widget: the widget's own
            // internal rounding creates a float mismatch against currentValue that never resolves
            // while enabled == false (the setter branch below never runs to write it back), which
            // makes Widgets.CheckPlayDragSliderSound() fire every frame the slider is disabled.
            float newValue = Widgets.HorizontalSlider(
                rowRect,
                currentValue,
                min,
                max,
                middleAlignment: true,
                leftAlignedLabel: GenText.ToStringPercent(min),
                rightAlignedLabel: GenText.ToStringPercent(max),
                roundTo: -1f);

            if (roundTo > 0f)
            {
                newValue = Mathf.RoundToInt(newValue / roundTo) * roundTo;
            }

            if (enabled && Math.Abs(newValue - currentValue) > float.Epsilon)
            {
                setter(newValue);
                MarkSettingChanged();
            }
        }
    }

    /// <summary>
    /// Plain checkbox row for card sub-toggles (no icon, red/green hover highlight, tooltip from
    /// "CheckboxTooltip_{name}" + "Default: ON/OFF" line). Unlike SettingCheckbox this takes the
    /// value by copy + setter only - use it inside cards where the backing store is a nested
    /// object (see TruffleSettings). Caller sizes <paramref name="rowRect"/> to CardRowHeight.
    /// </summary>
    private void SettingCheckboxBlock(
        Rect rowRect,
        string name,
        bool currentValue,
        bool defaultValue,
        bool enabled,
        Action<bool> setter)
    {
        if (!IsSettingAvailable(name))
            return;

        if (TryConsumeTabReset(name, defaultValue, setter))
            return;

        string label = GetSettingLabel(SettingLabelKind.Checkbox, name);

        if (Mouse.IsOver(rowRect))
        {
            string defaultText = GetSettingLabel(SettingLabelKind.Raw, defaultValue ? "ON" : "OFF");
            DoTooltip(rowRect, BuildDefaultTooltip(SettingLabelKind.CheckboxTooltip, name, defaultText), !enabled);
        }

        DoResetFloatMenu(rowRect, () => { setter(defaultValue); MarkSettingChanged(); });

        using (new UIState(color: enabled ? white : gray))
        {
            if (enabled)
            {
                if (Mouse.IsOver(rowRect))
                {
                    Widgets.DrawTextHighlight(rowRect, expandBy: 0f, color: currentValue ? Colors.RedHighlightColor : Colors.GreenHighlightColor);
                }

                bool value = currentValue;
                Widgets.CheckboxLabeled(rowRect, label, ref value);
                if (value != currentValue)
                {
                    setter(value);
                    MarkSettingChanged();
                }
            }
            else
            {
                bool value = currentValue;
                Widgets.CheckboxLabeled(rowRect, label, ref value, disabled: true);
            }
        }
    }

    /// <summary>
    /// Tooltip body for value blocks: the matching *SubLabel_ description (omitted when the key
    /// doesn't exist) followed by a blank line and the localized "Default: X" tag. Build only
    /// while the block is hovered - it allocates.
    /// </summary>
    private static string BuildDefaultTooltip(SettingLabelKind tooltipKind, string name, string defaultValueText)
    {
        string description = GetSettingTooltip(tooltipKind, name);
        string defaultLine = "General_Default".TranslateKey(args: defaultValueText);
        return description == null ? defaultLine : description + "\n\n" + defaultLine;
    }

    /// <summary>
    /// Two-column card grid with center vertical divider - the shared layout of the Commonality
    /// and fall-color-tree grids. Consumes rows * (cellHeight + rowGap) from the listing, calls
    /// <paramref name="drawCell"/> once per item (right column pre-offset by 8px), and returns
    /// true if any cell reported a change. <paramref name="rowSeparators"/> draws half-width
    /// horizontal lines between rows. The drawCell lambda typically captures locals and allocates
    /// one delegate per frame - acceptable at this scale; don't hoist state into fields for it.
    /// </summary>
    private bool DrawTwoColumnGrid<T>(
        Listing_Standard listing,
        IList<T> items,
        float cellHeight,
        float rowGap,
        int contextHash,
        Func<Rect, T, bool> drawCell,
        bool rowSeparators = false)
    {
        int count = items.Count;
        if (count == 0)
            return false;

        int rows = Mathf.CeilToInt(count / 2f);

        // RectDivider.NewRow spaces rows by its own margin.y - pass rowGap there so allocated
        // height matches what NewRow actually consumes.
        Rect gridRect = listing.GetRect(rows * (cellHeight + rowGap));
        RectDivider grid = new(gridRect, contextHash, margin: new Vector2(17f, rowGap));

        using (new UIState(color: MenuSectionBGBorderColor))
        {
            Widgets.DrawLineVertical(gridRect.center.x, gridRect.y, gridRect.height);
        }

        bool changed = false;

        for (int i = 0; i < count; i += 2)
        {
            RectDivider row = grid.NewRow(cellHeight);
            float columnWidth = (row.Rect.width - ColumnGap) / 2f;
            RectDivider left = row.NewCol(columnWidth, marginOverride: ColumnGap / 2f);
            RectDivider right = row.NewCol(columnWidth);

            changed |= drawCell(left.Rect, items[i]);

            bool hasRight = i + 1 < count;
            if (hasRight)
                changed |= drawCell(GetOffsetRect(right.Rect, xOffset: 8f), items[i + 1]);

            if (rowSeparators && i + 2 < count)
            {
                using (new UIState(color: MenuSectionBGBorderColor))
                {
                    DrawGridRowSeparator(left.Rect, rowGap);
                    if (hasRight)
                        DrawGridRowSeparator(right.Rect, rowGap);
                }
            }
        }

        return changed;
    }

    // Half-width separator centered under a grid cell, drawn in the row gap. Caller wraps in a
    // UIState color scope (DrawTwoColumnGrid does).
    private static void DrawGridRowSeparator(Rect cellRect, float rowGap)
    {
        float lineWidth = cellRect.width / 2f;
        float lineX = cellRect.x + ((cellRect.width - lineWidth) / 2f);
        float lineY = cellRect.yMax + (rowGap / 2f);
        Widgets.DrawLineHorizontal(lineX, lineY, lineWidth);
    }

    // ---------------------------------------------------------------------------------------------
    // Low-level primitives. Prefer the Setting* helpers above for actual settings - these are the
    // building blocks they delegate to, kept for bespoke layouts (Categories text field, dialogs).
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Draws a left icon (texPath > Def > placeholder) sized to the row height and returns the
    /// remaining rect to the right of it (plus gap). showIcon: false returns the rect unchanged.
    /// </summary>
    private Rect DrawIconAndGetRect(Rect rowRect, bool showIcon = true, float gapAfterIcon = GenUI.GapTiny, float scale = 1f, float alpha = 1f, Def def = null, string texPath = null)
    {
        if (!showIcon)
            return rowRect;

        bool hasTexPath = !string.IsNullOrWhiteSpace(texPath);

        float iconSize = rowRect.height;

        Rect iconRect = rowRect.LeftPartPixels(iconSize);

        if (hasTexPath)
        {
            Widgets.DrawTextureFitted(iconRect, UITextureCache.GetIcon(texPath), scale, alpha);
        }
        else if (def != null)
        {
            Widgets.DefIcon(iconRect, def, drawPlaceholder : true);
        }
        else
        {
            var tex = UITextureCache.PlaceholderIcon;
            Widgets.DrawTextureFitted(iconRect, tex, 1f);
        }

        return rowRect.RightPartPixels(rowRect.width - iconSize - gapAfterIcon);
    }

    /// <summary>
    /// Raw checkbox row primitive: icon + Widgets.CheckboxLabeled + disabled graying + mouseover
    /// highlight + right-click single reset + tab-reset early-return. SettingCheckbox wraps this
    /// with key derivation and change notification - call this directly only when the label/tooltip
    /// don't follow the name convention AND SettingCheckbox's overrides don't fit.
    /// </summary>
    private bool CheckboxLabeledWithIcon(
        Rect rect,
        string id,
        string label,
        ref bool checkOn,
        bool defaultValue = false,
        Action<bool> defaultValueSetter = null,
        string tooltip = null,
        string texPath = null,
        Def defForIcon = null,
        bool disabled = false,
        bool paintable = false,
        bool showIcon = true,
        bool placeCheckboxNearText = false,
        float gapAfterIcon = GenUI.GapTiny,
        float iconScale = 1f,
        float iconAlpha = 1f)
    {
        if (s_isResettingTab)
        {
            defaultValueSetter?.Invoke(defaultValue);
            return defaultValue;
        }

        bool isDisabled = disabled;

        using (new UIState(color: isDisabled ? gray : white))
        {
            Rect checkboxRect = DrawIconAndGetRect(rect, showIcon, gapAfterIcon, iconScale, iconAlpha, !isDisabled ? defForIcon : null, texPath);

            DoTooltip(checkboxRect, tooltip, isDisabled);

            bool check = !isDisabled && checkOn;

            Widgets.CheckboxLabeled(checkboxRect, label, ref check, isDisabled, placeCheckboxNearText: placeCheckboxNearText,paintable: paintable);

            if (!isDisabled)
            {
                Widgets.DrawHighlightIfMouseover(rect);
                checkOn = check;

                if (defaultValueSetter != null)
                    DoResetFloatMenu(rect, () => defaultValueSetter.Invoke(defaultValue));
            }
        }

        return checkOn;
    }

    /// <summary>Tooltip region; gray-colorized while disabled. Null/empty tooltip draws nothing.</summary>
    private void DoTooltip(Rect rect, string tooltip, bool isDisabled)
    {
        if (string.IsNullOrEmpty(tooltip))
            return;

        if (isDisabled)
            tooltip = tooltip.Colorize(gray);

        TooltipHandler.TipRegion(rect, tooltip);
    }

    /// <summary>
    /// Section header primitive: centered label, optional tiny sub-label, divider line. Heights
    /// come from Text.CalcHeight on the passed (translated) strings, so long languages wrap.
    /// Prefer SettingSection for name-derived, cached labels.
    /// </summary>
    public static void DrawSectionLabel(
        Listing_Standard listing,
        string label,
        string subLabel = null,
        Color? labelColor = null,
        Color? subLabelColor = null,
        Color? lineColor = null,
        TextAnchor labelAnchor = TextAnchor.MiddleCenter,
        TextAnchor subLabelAnchor = TextAnchor.MiddleCenter,
        bool drawLine = true,
        bool lineMatchesText = false,
        float linePct = 1f)
    {
        if (label == null)
            return;

        float labelHeight = Text.CalcHeight(label, listing.ColumnWidth);
        float subLabelHeight = subLabel == null ? 0f : Text.CalcHeight(subLabel, listing.ColumnWidth);

        float totalHeight = labelHeight + subLabelHeight + SectionLineThickness;

        Rect rect = listing.GetRect(totalHeight);

        // Top portion for the label
        Rect labelRect = rect.TopPartPixels(labelHeight);

        using (new UIState(color: labelColor ?? white, anchor: labelAnchor))
        {
            Widgets.Label(labelRect, label);

            if (drawLine)
            {
                float lineWidth = lineMatchesText ? label.GetWidthCached() : (rect.width * linePct);
                float lineX = rect.x + (rect.width - lineWidth) / 2f;
                GUI.color = lineColor ?? MenuSectionBGBorderColor;
                GUI.DrawTexture(new(lineX, labelRect.yMax, lineWidth, SectionLineThickness), BaseContent.WhiteTex);
            }
        }

        listing.Gap(GenUI.GapTiny);

        if (!string.IsNullOrWhiteSpace(subLabel))
        {
            Rect subLabelRect = new(labelRect.x, labelRect.yMax, labelRect.width, subLabelHeight);
            using (new UIState(font: GameFont.Tiny, color: subLabelColor ?? gray, anchor: subLabelAnchor))
            {
                Widgets.Label(subLabelRect, subLabel);
            }
        }

        listing.Gap(GenUI.Pad);
    }

    /// <summary>
    /// Tiny free-form sub-label row (e.g. tab descriptions), height from the translated string,
    /// optional ChildIndent-based indentation via indentSpaces.
    /// </summary>
    private static void DrawCustomSubLabel(Listing_Standard listing, string label, TextAnchor anchor = TextAnchor.MiddleCenter, Color? color = null, int indentSpaces = 0)
    {
        using (new UIState(font: GameFont.Tiny, color: color ?? gray, anchor: anchor))
        {
            float indent = indentSpaces > 0 ? ChildIndent * indentSpaces : 0f;
            float availableWidth = listing.ColumnWidth - indent;

            float labelHeight = Text.CalcHeight(label, availableWidth);
            Rect labelRect = listing.GetRect(labelHeight);

            if (indentSpaces > 0)
            {
                labelRect.x += indent;
                labelRect.width -= indent;
            }

            Widgets.Label(labelRect, label);
        }
    }
}
