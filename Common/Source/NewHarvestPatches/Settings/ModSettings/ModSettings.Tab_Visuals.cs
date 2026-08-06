using Verse.Sound;

namespace NewHarvestPatches;

[StaticConstructorOnStartup] // Or else warning due to Texture2D field.
public partial class NewHarvestPatchesModSettings : ModSettings
{
    private const float ColumnRowHeight = 30f;
    private const float ColumnGap = 24f;

    // Inline material color picker layout — sizes kept together so the picker's
    // proportions can be retuned without hunting through the draw methods.
    private const float DropdownRowHeight = ColumnRowHeight;
    private const float SwitchRowHeight = 32f;
    private const float SwitchWidth = 240f;
    private const float RowSpacing = 4f;
    private const float ColorWheelSize = 128f;
    private const float ColorTextfieldsWidth = 125f;
    private const float ColorFieldRowHeight = 30f; // matches Widgets.ColorTextfields' internal row height
    private const float ColorFieldRowGap = 4f;
    private const float MaterialTileSize = 52f; // shared square side: swatch boxes + def icon
    private const float BrightnessColumnWidth = 24f; // vertical value bar, between wheel and swatches
    private const float BrightnessBarWheelGap = 4f; // small gap so the bar reads as attached to the wheel
    private const float SwatchLabelGap = 4f; // label -> box/icon gap
    private const float SwatchBlockGap = 8f; // gap between the two stacked swatch blocks
    private const float ColumnPad = 12f; // gap between major picker-row sections
    private const int MaterialColorFieldsContextHash = 848573201;
    private const string MaterialColorFieldsControlName = "materialColorTextfields";
    private const int MaterialTooltipIdBase = 848573210; // +0 default, +1 current, +2 icon
    private const float ResetButtonHeight = 38f;
    private static readonly Widgets.ColorComponents s_materialColorComponents = Widgets.ColorComponents.Red | Widgets.ColorComponents.Green | Widgets.ColorComponents.Blue;

    // Cached, translation-dependent widths for the material picker — measured once per menu
    // session (text metrics are stable until the menu is closed/reopened) and invalidated in
    // ClearMenuSessionCaches().
    private static float s_materialDropdownWidth = -1f;
    private static float s_materialSwitchWidth = -1f;
    private static float s_materialResetWidth = -1f;

    // The two swatch captions, needed twice per frame each (once to measure the column, once to draw).
    // Session-scoped like the widths above - wiped in ClearMenuSessionCaches() so a language change lands.
    private static string s_defaultColorLabel;
    private static string s_currentColorLabel;

    // Deciduous trees present in FallColorTrees, in DefUtility order - built lazily on first
    // draw, wiped in ClearMenuSessionCaches() (menu-session-only data).
    private static List<ThingDef> s_fallColorTreeEntries;

    private static List<ThingDef> s_materialEntries;

    public enum ColorType
    {
        Stuff,
        Thing,
    }

    /// <summary>
    /// Three sections, each gated on the availability of the setting it introduces rather than on a mod
    /// flag: a section header must not be drawn for content that hides itself.
    /// </summary>
    private void DoVisualsTab(Listing_Standard listing)
    {
        if (IsSettingAvailable(nameof(UseVanillaLogGraphic)))
        {
            SettingSection(listing, "GeneralSection");

            SettingCheckbox(
                listing,
                nameof(UseVanillaLogGraphic),
                ref UseVanillaLogGraphic,
                v => UseVanillaLogGraphic = v,
                iconTexPath: UITextureCache.IconFolder + "WoodLogsIcon");
        }

        if (IsSettingAvailable(nameof(FallColorTrees)) && !FallColorTrees.NullOrEmpty())
        {
            listing.Gap(GenUI.GapLabel);
            SettingSection(listing, "FallColorTreesSection", hasSubLabel: true);
            DoFallColorTrees(listing);
        }

        if (IsSettingAvailable(nameof(MaterialColors)) && !MaterialColors.NullOrEmpty())
        {
            listing.Gap(GenUI.GapLabel);
            SettingSection(listing, "MaterialColorsSection", hasSubLabel: true);
            DoColorChanger(listing);
        }
    }

    private void DoFallColorTrees(Listing_Standard listing)
    {
        if (FallColorTrees.NullOrEmpty())
            return;

        if (s_fallColorTreeEntries == null)
        {
            s_fallColorTreeEntries = [];
            foreach (var def in DefUtility.ThingDefs.DeciduousTreeDefs)
            {
                if (FallColorTrees.ContainsKey(def.defName))
                    s_fallColorTreeEntries.Add(def);
            }
        }

        DrawTwoColumnGrid(
            listing,
            s_fallColorTreeEntries,
            cellHeight: ColumnRowHeight,
            rowGap: listing.verticalSpacing,
            contextHash: FallColorTrees.GetHashCode(),
            drawCell: (rect, tree) => DrawFallColorTreeEntry(tree, rect));
    }

    /// <summary>
    /// One tree's fall-color checkbox. Checked means colors are ON, so the default is true and the
    /// "enabled setting" this produces is the unchecked case (see GetEnabledFallColorTrees). Applies live
    /// via FallColorDisabler - SettingCheckbox marks the settings dirty on toggle.
    /// </summary>
    private bool DrawFallColorTreeEntry(ThingDef tree, Rect rect)
    {
        bool value = FallColorTrees[tree.defName];
        bool before = value;

        SettingCheckbox(
            rect,
            nameof(FallColorTrees),
            ref value,
            v => FallColorTrees[tree.defName] = v,
            defaultValue: true,
            restartRequired: false,
            iconDef: tree,
            labelOverride: tree.LabelCapSafe(),
            idSuffix: tree.defName,
            paintable: true);

        return value != before;
    }

    public Rect GetOffsetRect(Rect currentRect, float xOffset = 0f, float rightPadding = 0f)
        => new(currentRect.x + xOffset, currentRect.y, currentRect.width - xOffset - rightPadding, currentRect.height);

    // Session-only picker state (not scribed) — which def/channel the inline material color
    // picker is currently editing, and the HSV-wheel/textfield working buffers for it.
    private ThingDef _selectedMaterialDef;
    private ColorType _materialColorType = ColorType.Stuff;
    private bool _materialHsvDragging;
    private bool _materialBrightnessDragging;
    // Hue/saturation the brightness bar operates on. Brightness must scale ONE anchored color rather
    // than re-derive hue from whatever the bar itself last wrote - RGB carries less hue/saturation
    // the lower value gets, and none at all at black, so chaining off the live color drifts the
    // gradient and then collapses it to grayscale. Only an outside control (wheel, textfields,
    // def/channel switch, revert) moves the anchor.
    private float _materialBarHue;
    private float _materialBarSaturation;
    private string[] _materialColorBuffers = new string[6];
    private Color _materialColorBuffer;
    private string _materialPrevFocus;

    /// <summary>
    /// Inline material color picker: a dropdown selecting one material, a Stuff/Thing channel switch, then
    /// the editor row (RGB fields, HSV wheel, brightness bar, swatches, revert). Only the selected material
    /// is drawn, which is why tab reset has to iterate every entry itself rather than relying on the
    /// per-row reset gate the other tabs use.
    /// </summary>
    private void DoColorChanger(Listing_Standard listing)
    {
        if (MaterialColors.NullOrEmpty())
            return;

        if (s_materialEntries == null)
        {
            s_materialEntries = [];
            foreach (var def in DefUtility.ThingDefs.IndustrialResourceDefs.Keys)
            {
                if (MaterialColors.ContainsKey(def.defName))
                    s_materialEntries.Add(def);
            }
        }

        int count = s_materialEntries.Count;
        if (count == 0)
            return;

        // Right-click tab reset replays this method with s_isResettingTab set; since only the
        // selected def is visible at a time, revert every entry here rather than just the shown one.
        if (s_isResettingTab)
        {
            foreach (ThingDef resetDef in s_materialEntries)
            {
                if (MaterialColors.TryGetValue(resetDef.defName, out ColorInfo resetInfo))
                {
                    resetInfo.NewStuffColor = resetInfo.DefaultStuffColor;
                    resetInfo.NewThingColor = resetInfo.DefaultThingColor;
                }
            }

            // Same rule as SelectMaterial/SetMaterialColorType: the color just changed from outside the
            // widgets, so the picker's working state has to follow it. ApplyTabReset's GUI.FocusControl(null)
            // clears the LIVE focus but not _materialPrevFocus - leaving that set is exactly the
            // "was focused, no longer is" case where Widgets.ColorTextfields commits its stale buffer over
            // the color, silently undoing the reset on the next frame.
            if (_selectedMaterialDef != null && MaterialColors.TryGetValue(_selectedMaterialDef.defName, out ColorInfo selectedInfo))
                ClearMaterialColorBuffers(GetSelectedColor(selectedInfo));

            MarkSettingChanged(); // Material colors apply live via MaterialColorChanger.
            return;
        }

        if (_selectedMaterialDef == null || !s_materialEntries.Contains(_selectedMaterialDef))
            SelectMaterial(s_materialEntries[0]);

        if (!MaterialColors.TryGetValue(_selectedMaterialDef.defName, out ColorInfo colorInfo))
            return;

        float headerRowHeight = Text.LineHeight;
        float mainRowHeight = GetMaterialMainRowHeight();
        float totalHeight =
            headerRowHeight + RowSpacing +
            DropdownRowHeight + RowSpacing +
            SwitchRowHeight + RowSpacing +
            mainRowHeight;

        Rect fullRect = listing.GetRect(totalHeight);
        // Zero margin: row gaps are explicit (RowSpacing) — RectDivider's default margin.y
        // (4px) would otherwise steal extra height on every NewRow call.
        RectDivider layout = new(fullRect, contextHash: MaterialColors.GetHashCode(), margin: Vector2.zero);

        RectDivider headerRow = layout.NewRow(headerRowHeight);
        using (new UIState(anchor: TextAnchor.MiddleCenter))
            Widgets.Label(headerRow.Rect, "Label_ChooseMaterial".TranslateKey());
        layout.NewRow(RowSpacing);

        RectDivider dropdownRow = layout.NewRow(DropdownRowHeight);
        float dropdownWidth = GetMaterialDropdownWidth(s_materialEntries);
        Rect dropdownRect = new(dropdownRow.Rect.center.x - dropdownWidth / 2f, dropdownRow.Rect.y, dropdownWidth, dropdownRow.Rect.height);
        if (Widgets.ButtonText(dropdownRect, _selectedMaterialDef.LabelCapSafe()))
        {
            List<FloatMenuOption> options = [.. s_materialEntries.Select(def => new FloatMenuOption(def.LabelCapSafe(), () => SelectMaterial(def), def))];
            Find.WindowStack.Add(new FloatMenu(options));
        }
        layout.NewRow(RowSpacing);

        // Mode toggle, centered on its own row.
        RectDivider switchRow = layout.NewRow(SwitchRowHeight);
        DrawMaterialSwitch(switchRow.Rect, colorInfo);
        layout.NewRow(RowSpacing);

        RectDivider mainRow = layout.NewRow(mainRowHeight);
        DrawMaterialPickerRow(mainRow.Rect, colorInfo);
    }

    /// <summary>
    /// Modern two-segment Stuff/Thing switch. Selected side is highlighted, the other grayed;
    /// clicking (or dragging then releasing over) either half selects it. Centered at a cached
    /// width sized to fit whichever ApplyTo label is longer, so translations don't clip.
    /// </summary>
    private void DrawMaterialSwitch(Rect rect, ColorInfo info)
    {
        float width = GetMaterialSwitchWidth();
        Rect pill = new(rect.center.x - width / 2f, rect.y, width, rect.height);
        Rect stuffHalf = new(pill.x, pill.y, pill.width / 2f, pill.height);
        Rect thingHalf = new(pill.center.x, pill.y, pill.width / 2f, pill.height);

        Color selectedFill = ColorLibrary.SkyBlue;
        Color unselectedFill = new(0.28f, 0.28f, 0.28f);

        Widgets.DrawBoxSolidWithOutline(stuffHalf, IsStuffSelected ? selectedFill : unselectedFill, MenuSectionBGBorderColor, 1);
        Widgets.DrawBoxSolidWithOutline(thingHalf, IsStuffSelected ? unselectedFill : selectedFill, MenuSectionBGBorderColor, 1);

        using (new UIState(anchor: TextAnchor.MiddleCenter, color: IsStuffSelected ? white : gray))
            Widgets.Label(stuffHalf, "Button_ApplyToStuff".TranslateKey());
        using (new UIState(anchor: TextAnchor.MiddleCenter, color: IsStuffSelected ? gray : white))
            Widgets.Label(thingHalf, "Button_ApplyToThing".TranslateKey());

        if (Widgets.ButtonInvisibleDraggable(pill) is Widgets.DraggableResult.Pressed or Widgets.DraggableResult.DraggedThenPressed)
        {
            bool clickedStuff = Event.current.mousePosition.x < pill.center.x;
            SetMaterialColorType(clickedStuff ? ColorType.Stuff : ColorType.Thing, info);
        }
    }

    // --- Main picker row --------------------------------------------------------------------

    /// <summary>
    /// RGB textfields, HSV wheel, brightness bar, Default/Current swatches, and the def icon + revert
    /// button, left to right, filling the full row width with ColumnPad gaps between sections; every
    /// column is vertically centered within the shared row height. The wheel column takes whatever width
    /// the fixed-size columns leave over, so the row adapts to the settings window being resized.
    /// </summary>
    private void DrawMaterialPickerRow(Rect rect, ColorInfo info)
    {
        float defaultLabelWidth = DefaultColorLabel.GetWidthCached();
        float currentLabelWidth = CurrentColorLabel.GetWidthCached();
        float swatchColumnWidth = Mathf.Max(MaterialTileSize, Mathf.Max(defaultLabelWidth, currentLabelWidth));
        float iconColumnWidth = Mathf.Max(MaterialTileSize, GetMaterialResetWidth());
        float wheelColumnWidth = rect.width - ColorTextfieldsWidth - BrightnessColumnWidth - swatchColumnWidth - iconColumnWidth - ColumnPad * 4f;

        Rect textfieldsRect = new(rect.x, rect.y, ColorTextfieldsWidth, rect.height);
        Rect wheelColumnRect = new(textfieldsRect.xMax + ColumnPad, rect.y, wheelColumnWidth, rect.height);
        Rect brightnessColRect = new(wheelColumnRect.xMax + ColumnPad, rect.y, BrightnessColumnWidth, rect.height);
        Rect swatchColumnRect = new(brightnessColRect.xMax + ColumnPad, rect.y, swatchColumnWidth, rect.height);
        Rect iconColumnRect = new(swatchColumnRect.xMax + ColumnPad, rect.y, iconColumnWidth, rect.height);

        float wheelRightEdge = DrawMaterialColorEditor(textfieldsRect, wheelColumnRect, info);

        // Center the bar's height on ColorWheelSize (same rule the wheel itself uses below) so its
        // top/bottom line up with the wheel regardless of row height. X is anchored to the wheel's
        // own right edge (not the column edge) since the wheel column is wider than the wheel and
        // centers it — anchoring to the column would leave a big gap instead of a small one.
        float barHeight = Mathf.Min(ColorWheelSize, brightnessColRect.height);
        Rect brightnessBarRect = new(
            wheelRightEdge + BrightnessBarWheelGap, brightnessColRect.y + (brightnessColRect.height - barHeight) / 2f,
            BrightnessColumnWidth, barHeight);
        DrawMaterialBrightnessBar(brightnessBarRect, info);

        DrawMaterialSwatchColumn(swatchColumnRect, info);
        DrawMaterialIconColumn(iconColumnRect, info);
    }

    /// <summary>
    /// RGB textfields (vertically centered) on the left, HSV wheel centered in its own column;
    /// writes back to the live setting (and marks it changed) as soon as either control produces
    /// a different color.
    /// </summary>
    /// <returns>The wheel's right edge, so the brightness bar can anchor to the wheel rather than to the
    /// wider column that contains it.</returns>
    private float DrawMaterialColorEditor(Rect textfieldsRect, Rect wheelColumnRect, ColorInfo info)
    {
        Color workingColor = GetSelectedColor(info);
        Color beforeEdit = workingColor;

        float textfieldsHeight = ColorFieldRowHeight * 3f + ColorFieldRowGap * 2f;
        float textfieldsY = textfieldsRect.y + (textfieldsRect.height - textfieldsHeight) / 2f;
        RectAggregator aggregator = new(
            new Rect(textfieldsRect.x, textfieldsY, textfieldsRect.width, 0f),
            MaterialColorFieldsContextHash,
            margin: new Vector2(0f, ColorFieldRowGap));

        Widgets.ColorTextfields(
            ref aggregator,
            ref workingColor,
            ref _materialColorBuffers,
            ref _materialColorBuffer,
            _materialPrevFocus,
            MaterialColorFieldsControlName,
            s_materialColorComponents,
            s_materialColorComponents);

        // While nothing is focused, ColorTextfields' buffers hold LAST frame's color at 8-bit
        // precision (it builds its strings from colorBuffer and syncs colorBuffer to the live color
        // only at the end of the call). Parsing them then would stomp whatever the wheel or the
        // brightness bar just set, once per frame. Read them only while a field is focused - the sole
        // case they carry something the delayed commit hasn't handled, since workingColor and
        // _materialColorBuffer only update on Enter/unfocus, not per keystroke. Skip an unparsable or
        // blank buffer (mid-edit) so the preview holds its last valid value instead of flickering.
        if (GUI.GetNameOfFocusedControl().StartsWith(MaterialColorFieldsControlName) &&
            int.TryParse(_materialColorBuffers[0], out int typedR) &&
            int.TryParse(_materialColorBuffers[1], out int typedG) &&
            int.TryParse(_materialColorBuffers[2], out int typedB))
        {
            workingColor = new ColorInt(
                Mathf.Clamp(typedR, 0, 255),
                Mathf.Clamp(typedG, 0, 255),
                Mathf.Clamp(typedB, 0, 255)).ToColor;
        }

        // Anchor first so the wheel below reads what was just typed, not last frame's value.
        if (workingColor != beforeEdit)
            SyncBrightnessAnchor(workingColor);

        float wheelSize = Mathf.Min(ColorWheelSize, wheelColumnRect.width, wheelColumnRect.height);
        Rect wheelRect = new(
            wheelColumnRect.x + (wheelColumnRect.width - wheelSize) / 2f,
            wheelColumnRect.y + (wheelColumnRect.height - wheelSize) / 2f,
            wheelSize, wheelSize);

        // The wheel is handed an anchored full-value color rather than the live one, for the same reason
        // the brightness bar is (see SyncBrightnessAnchor): at value 0 the live color carries no hue and no
        // saturation, so Widgets.HSVColorWheel would render solid black, park its selection circle at the
        // center and write black back on every drag - hue and saturation unreachable until brightness is
        // raised. colorValueOverride keeps it drawing and reporting at full value; brightness stays the
        // bar's job and is recombined below.
        RGBToHSV(workingColor, out _, out _, out float liveValue);
        Color wheelColor = HSVToRGB(_materialBarHue, _materialBarSaturation, 1f);
        Color beforeWheel = wheelColor;
        Widgets.HSVColorWheel(wheelRect, ref wheelColor, ref _materialHsvDragging, colorValueOverride: 1f);

        if (wheelColor != beforeWheel)
        {
            // Set the anchor directly rather than through SyncBrightnessAnchor: that method refuses to
            // anchor a zero-value color, which is precisely the case this branch exists to keep working.
            RGBToHSV(wheelColor, out float wheelHue, out float wheelSaturation, out _);
            _materialBarHue = wheelHue;
            _materialBarSaturation = wheelSaturation;
            workingColor = HSVToRGB(wheelHue, wheelSaturation, liveValue);
        }

        if (workingColor != beforeEdit)
        {
            SetSelectedColor(info, workingColor);
            MarkSettingChanged(); // Material colors apply live via MaterialColorChanger.
        }


        if (Event.current.type == EventType.Layout)
            _materialPrevFocus = GUI.GetNameOfFocusedControl();

        return wheelRect.xMax;
    }

    // Grayscale 1xN gradient the brightness bar tints; built once and kept for the process lifetime.
    // Do not wipe it in ClearMenuSessionCaches().  Would need to Destroy instead.
    private static Texture2D s_brightnessRampInt;
    private static Texture2D BrightnessRamp
    {
        get
        {
            if (s_brightnessRampInt != null)
                return s_brightnessRampInt;

            const int steps = 128;
            s_brightnessRampInt = new Texture2D(1, steps) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < steps; i++)
            {
                float t = i / (float)(steps - 1);
                s_brightnessRampInt.SetPixel(0, i, new Color(t, t, t));
            }
            s_brightnessRampInt.Apply();
            return s_brightnessRampInt;
        }
    }

    /// <summary>
    /// Vertical value (brightness) slider beside the wheel. Reads only V from the live color; hue and
    /// saturation come from the anchor fields, since RGB loses those components as V approaches 0 and a
    /// bar that re-derived them from its own output would drift and then collapse to grayscale.
    /// See <see cref="SyncBrightnessAnchor"/>.
    /// </summary>
    private void DrawMaterialBrightnessBar(Rect rect, ColorInfo info)
    {
        Color workingColor = GetSelectedColor(info);
        Color beforeEdit = workingColor;

        // Only value comes from the live color. Hue/saturation stay anchored.
        RGBToHSV(workingColor, out _, out _, out float value);
        float hue = _materialBarHue;
        float saturation = _materialBarSaturation;

        if (!_materialBrightnessDragging)
        {
            TooltipHandler.TipRegion(rect, "Tooltip_Brightness".TranslateKey());
            MouseoverSounds.DoRegion(rect);
        }

        if (Event.current.button == 0)
        {
            if (_materialBrightnessDragging && Event.current.type == EventType.MouseUp)
            {
                _materialBrightnessDragging = false;
            }
            else if ((Mouse.IsOver(rect) && Event.current.type == EventType.MouseDown) ||
                     (_materialBrightnessDragging && UnityGUIBugsFixer.MouseDrag()))
            {
                _materialBrightnessDragging = true;
                if (Event.current.type == EventType.MouseDrag)
                    Event.current.Use();

                float fraction = Mathf.Clamp01((Event.current.mousePosition.y - rect.yMin) / rect.height);
                value = 1f - fraction;
                workingColor = HSVToRGB(hue, saturation, value);
            }
        }

        if (Event.current.type == EventType.Repaint)
        {
            // Ramp is grayscale; GUI.color multiplies it. V scales RGB linearly, so tinting by the
            // full-value color yields the same black -> full-color gradient in a single draw call.
            using (new UIState(color: HSVToRGB(hue, saturation, 1f)))
            {
                GUI.DrawTexture(rect, BrightnessRamp);
            }
        }

        float markerY = rect.y + rect.height * (1f - value);
        Color markerColor = value < 0.5f ? white : black;
        Widgets.DrawLineHorizontal(rect.x, markerY - 2f, rect.width, markerColor);
        Widgets.DrawLineHorizontal(rect.x, markerY + 2f, rect.width, markerColor);

        if (workingColor != beforeEdit)
        {
            SetSelectedColor(info, workingColor);
            MarkSettingChanged(); // Material colors apply live via MaterialColorChanger.
        }
    }

    /// <summary>
    /// Re-anchors the brightness bar on a color set from outside the bar. Black carries no hue and no
    /// saturation, pure gray no hue - skip those components instead of zeroing the anchor, since the
    /// bar needs them to climb back out of black.
    /// </summary>
    private void SyncBrightnessAnchor(Color color)
    {
        RGBToHSV(color, out float hue, out float saturation, out float value);
        if (value <= 0f)
            return;

        _materialBarSaturation = saturation;
        if (saturation > 0f)
            _materialBarHue = hue;
    }


    /// <summary>
    /// Default (top) and Current (bottom) swatches, each with a full-width label above and a
    /// same-size tile below; the two-block stack is vertically centered in the column. Hovering
    /// a tile shows its RGB value as a tooltip. A Thing-channel default of pure white means the def
    /// simply has no tint, so that case draws a "no color" marker instead of a white square.
    /// </summary>
    private void DrawMaterialSwatchColumn(Rect rect, ColorInfo info)
    {
        float blockHeight = Text.LineHeight + SwatchLabelGap + MaterialTileSize;
        float stackHeight = blockHeight * 2f + SwatchBlockGap;
        float stackY = rect.y + (rect.height - stackHeight) / 2f;

        Rect defaultBlockRect = new(rect.x, stackY, rect.width, blockHeight);
        Rect currentBlockRect = new(rect.x, defaultBlockRect.yMax + SwatchBlockGap, rect.width, blockHeight);

        Color defaultColor = GetSelectedDefaultColor(info);
        bool noDefaultColor = !IsStuffSelected && defaultColor.IndistinguishableFromFast(white);
        DrawSwatchBlock(defaultBlockRect, DefaultColorLabel, defaultColor, noDefaultColor, MaterialTooltipIdBase);

        Color currentColor = GetSelectedColor(info);
        bool currentNoColor = noDefaultColor && currentColor.IndistinguishableFromFast(white);
        DrawSwatchBlock(currentBlockRect, CurrentColorLabel, currentColor, currentNoColor, MaterialTooltipIdBase + 1);
    }

    /// <summary>
    /// One label-above-tile block, shared by both swatch rows. Label spans the full column width
    /// (fixes the old clipped "Default color"/"Current" text) and is horizontally centered above
    /// a horizontally-centered square tile.
    /// </summary>
    /// <param name="tooltipId">Stable per-tile id; TipRegion needs distinct ids or the three lazily-built
    /// tooltips in this picker share one entry and show the wrong color.</param>
    private void DrawSwatchBlock(Rect blockRect, string label, Color color, bool showNoDefaultColor, int tooltipId)
    {
        Rect labelRect = new(blockRect.x, blockRect.y, blockRect.width, Text.LineHeight);
        Rect boxRect = new(blockRect.center.x - MaterialTileSize / 2f, labelRect.yMax + SwatchLabelGap, MaterialTileSize, MaterialTileSize);

        using (new UIState(anchor: TextAnchor.MiddleCenter))
            Widgets.Label(labelRect, label);

        if (showNoDefaultColor)
        {
            Widgets.DrawBoxSolidWithOutline(boxRect, white, white, 2);
            Texture2D icon = Widgets.CheckboxOffTex ?? BaseContent.BadTex;
            Widgets.DrawTextureFitted(boxRect, icon, 1f);
            TooltipHandler.TipRegion(boxRect, "General_ThingHasNoDefaultColor".TranslateKey());
        }
        else
        {
            Widgets.DrawBoxSolidWithOutline(boxRect, color, white, 2);
            TooltipHandler.TipRegion(boxRect, () => BuildRgbTooltip(color), tooltipId);
        }
    }

    /// <summary>
    /// Top Y of the default (top) swatch box for a picker column sharing the main row's rect (same
    /// y/height as the swatch column) — mirrors DrawMaterialSwatchColumn/DrawSwatchBlock's layout
    /// math so callers can align to the box itself rather than the label above it.
    /// </summary>
    private static float GetDefaultSwatchBoxY(Rect columnRect)
    {
        float blockHeight = Text.LineHeight + SwatchLabelGap + MaterialTileSize;
        float stackHeight = blockHeight * 2f + SwatchBlockGap;
        float stackY = columnRect.y + (columnRect.height - stackHeight) / 2f;
        return stackY + Text.LineHeight + SwatchLabelGap;
    }

    /// <summary>
    /// Def icon (no label — the dropdown header already names the material) vertically centered on
    /// the default color swatch box, with the Reset button anchored to the bottom; both horizontally
    /// centered. Hovering the icon shows its RGB value as a tooltip. Reset grays out when the current
    /// color's 0-255 RGB int values already match the default's (a raw float Color != would
    /// false-positive on the picker's quantization rounding).
    /// </summary>
    private void DrawMaterialIconColumn(Rect rect, ColorInfo info)
    {
        float resetWidth = GetMaterialResetWidth();

        Rect iconRect = new(rect.center.x - MaterialTileSize / 2f, GetDefaultSwatchBoxY(rect), MaterialTileSize, MaterialTileSize);
        Rect resetRect = new(rect.center.x - resetWidth / 2f, rect.yMax - ResetButtonHeight, resetWidth, ResetButtonHeight);

        Widgets.ThingIcon(iconRect, _selectedMaterialDef, color: info.NewThingColor);
        TooltipHandler.TipRegion(iconRect, () => BuildRgbTooltip(info.NewThingColor), MaterialTooltipIdBase + 2);

        bool canReset = !GetSelectedColor(info).IndistinguishableFromFast(GetSelectedDefaultColor(info));
        using (new UIState(color: canReset ? white : gray))
        {
            if (Widgets.ButtonText(resetRect, "Button_RevertToDefault".TranslateKey(), active: canReset) && canReset)
            {
                ResetSelectedMaterialColor(info);
            }
        }
    }

    /// <summary>
    /// Builds the "R: x / G: y / B: z" tooltip text for a swatch/icon on hover. Only called from
    /// within a TipRegion's lazy Func&lt;string&gt;, so it costs nothing on frames without a hover.
    /// </summary>
    private static string BuildRgbTooltip(Color color)
    {
        Color32 rgb = color;
        return "Tooltip_SwatchRGB".TranslateKey(args: [rgb.r.ToString(), rgb.g.ToString(), rgb.b.ToString()]);
    }

    // --- Cached label metrics ----------------------------------------------------------------

    /// <summary>Caption above the Default swatch; also measured to size the swatch column.</summary>
    private static string DefaultColorLabel => s_defaultColorLabel ??= "Label_DefaultColor".TranslateKey();

    /// <summary>Caption above the Current swatch. Vanilla key, so it needs the manual capitalization.</summary>
    private static string CurrentColorLabel => s_currentColorLabel ??= "CurrentColor".Translate().CapitalizeFirst();

    /// <summary>
    /// Widest def label among the currently-shown materials, so the dropdown button always fits
    /// its text regardless of translation. Computed once (the entries list is session-stable —
    /// see DoColorChanger) and invalidated in ClearMenuSessionCaches().
    /// </summary>
    private static float GetMaterialDropdownWidth(List<ThingDef> entries)
    {
        if (s_materialDropdownWidth >= 0f)
            return s_materialDropdownWidth;

        float maxLabelWidth = 0f;
        foreach (ThingDef def in entries)
            maxLabelWidth = Mathf.Max(maxLabelWidth, def.LabelCapSafe().GetWidthCached());


        s_materialDropdownWidth = maxLabelWidth + ColumnPad * 2f;
        return s_materialDropdownWidth;
    }

    /// <summary>
    /// Switch pill width sized to fit the longer of the two ApplyTo labels, so a longer
    /// translation never clips.
    /// </summary>
    private static float GetMaterialSwitchWidth()
    {
        if (s_materialSwitchWidth >= 0f)
            return s_materialSwitchWidth;

        float stuffWidth = "Button_ApplyToStuff".TranslateKey().GetWidthCached();
        float thingWidth = "Button_ApplyToThing".TranslateKey().GetWidthCached();
        float halfWidth = Mathf.Max(stuffWidth, thingWidth) + ColumnPad;
        s_materialSwitchWidth = Mathf.Max(SwitchWidth, halfWidth * 2f);
        return s_materialSwitchWidth;
    }

    /// <summary>Reset-button width sized to fit its (possibly translated) label.</summary>
    private static float GetMaterialResetWidth()
    {
        if (s_materialResetWidth >= 0f)
            return s_materialResetWidth;

        s_materialResetWidth = "Button_RevertToDefault".TranslateKey().GetWidthCached() + ColumnPad * 2f;
        return s_materialResetWidth;
    }

    /// <summary>
    /// Shared row height for the whole main picker row (textfields / wheel / swatches / icon+reset),
    /// sized to whichever content needs the most vertical space. The swatch stack (two labeled
    /// tiles) normally wins.
    /// </summary>
    private static float GetMaterialMainRowHeight()
    {
        float swatchStackHeight = (Text.LineHeight + SwatchLabelGap + MaterialTileSize) * 2f + SwatchBlockGap;
        float iconStackHeight = MaterialTileSize + SwatchLabelGap + ResetButtonHeight;
        float textfieldsHeight = ColorFieldRowHeight * 3f + ColorFieldRowGap * 2f;

        return Mathf.Max(ColorWheelSize, Mathf.Max(swatchStackHeight, Mathf.Max(iconStackHeight, textfieldsHeight)));
    }

    private bool IsStuffSelected => _materialColorType is ColorType.Stuff;

    private Color GetSelectedColor(ColorInfo info) =>
        IsStuffSelected ? info.NewStuffColor : info.NewThingColor;

    private void SetSelectedColor(ColorInfo info, Color value)
    {
        if (IsStuffSelected)
            info.NewStuffColor = value;
        else
            info.NewThingColor = value;
    }

    private Color GetSelectedDefaultColor(ColorInfo info) =>
        IsStuffSelected ? info.DefaultStuffColor : info.DefaultThingColor;

    /// <summary>
    /// Switches the picker to another material. Must go through here rather than assigning the field:
    /// the textfield buffers and brightness anchor still hold the previous material's color and would
    /// otherwise be committed over the newly selected one on the next frame.
    /// </summary>
    private void SelectMaterial(ThingDef def)
    {
        _selectedMaterialDef = def;
        if (MaterialColors.TryGetValue(def.defName, out ColorInfo info))
            ClearMaterialColorBuffers(GetSelectedColor(info));
    }

    /// <summary>Switches the edited channel; same buffer-staleness reason as <see cref="SelectMaterial"/>.</summary>
    private void SetMaterialColorType(ColorType type, ColorInfo info)
    {
        if (_materialColorType == type)
            return;

        _materialColorType = type;
        ClearMaterialColorBuffers(GetSelectedColor(info));
    }

    private void ResetSelectedMaterialColor(ColorInfo info)
    {
        Color defaultColor = GetSelectedDefaultColor(info);
        SetSelectedColor(info, defaultColor);
        ClearMaterialColorBuffers(defaultColor);
        MarkSettingChanged(); // Material colors apply live via MaterialColorChanger.
        Messages.Message(
            "General_RevertedColorToDefault".TranslateKey(args: _selectedMaterialDef.LabelCapSafe()),
            null,
            MessageTypeDefOf.NeutralEvent);
    }

    /// <summary>
    /// Re-points every piece of picker working state at <paramref name="current"/> - focus, the
    /// ColorTextfields buffers and the brightness anchor. Any code path that changes the edited color from
    /// outside the widgets (select, channel switch, revert button, tab reset) must call this or the next
    /// frame's widgets write their stale state back over it.
    /// </summary>
    private void ClearMaterialColorBuffers(Color current)
    {
        GUI.FocusControl(null);
        _materialPrevFocus = null;
        _materialColorBuffer = current;
        SyncMaterialColorBuffers(current);
        SyncBrightnessAnchor(current);
    }

    /// <summary>
    /// Drops the picker's per-menu-session working state so a reopened menu starts clean: no remembered
    /// material, no leftover focus, and no drag flag left set by a window closed mid-drag. Called from
    /// <see cref="ClearMenuSessionCaches"/>. Nulling <see cref="_selectedMaterialDef"/> is what forces
    /// <see cref="DoColorChanger"/> to re-run <see cref="SelectMaterial"/> on the next open, which is what
    /// resyncs the textfield buffers and brightness anchor - otherwise they hold the previous session's
    /// color and get committed over the live one. <see cref="_materialColorType"/> is deliberately kept:
    /// GetSelectedColor honors it, so the remembered channel stays consistent with the resynced buffers.
    /// </summary>
    private void ResetMaterialPickerSession()
    {
        _selectedMaterialDef = null;
        _materialPrevFocus = null;
        _materialHsvDragging = false;
        _materialBrightnessDragging = false;
    }

    /// <summary>
    /// Widgets.ColorTextfields only resyncs a field's buffer string from previousFocusedControlName
    /// on the NEXT draw where that field is no longer the exact focused control; writing the correct
    /// R/G/B strings here directly makes the display correct immediately after a selection/type/revert change.
    /// </summary>
    private void SyncMaterialColorBuffers(Color color)
    {
        _materialColorBuffers = new string[6];
        _materialColorBuffers[0] = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255).ToString();
        _materialColorBuffers[1] = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255).ToString();
        _materialColorBuffers[2] = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255).ToString();
    }
}
