namespace NewHarvestPatches
{
    internal static class Checkboxes
    {
        private static readonly HashSet<string> _activatedSettingsThisSession = [];
        public static readonly Dictionary<string, string> _categoryLabelBuffers = [];

        public static float DoCheckboxes(Listing_Standard ls, List<CheckboxInfo> checkboxInfos)
        {
            // Get checkboxes for current tab
            var checkboxesToShow = checkboxInfos.Where(c => c.Tab == _currentTab).ToList();
            if (checkboxesToShow.Count == 0)
                return ls.CurHeight;

            if (_currentTab == SettingsTab.Fuel)
            {
                DrawCustomLabel(ls, Translator.TranslateKey(TKey.Type.TabSubLabel, "FuelDescription"),
                    font: GameFont.Tiny, anchor: TextAnchor.MiddleCenter);
            }
            ls.Gap(GenUI.Gap);

            bool mergingAnyCategories = Settings.MergingAnyCategories;
            foreach (var info in checkboxesToShow)
            {
                if (info.Parent == null)
                {
                    DrawCheckboxRecursive(ls, info, true);
                }
            }

            // If we were merging categories but are no longer, clear the caches
            if (mergingAnyCategories && !Settings.MergingAnyCategories)
            {
                Settings.ClearCategoryCache();
            }

            return ls.CurHeight;
        }


        private static void DrawCheckboxRecursive(Listing_Standard ls, CheckboxInfo info, bool parentEnabled)
        {
            bool checkboxEnabled = parentEnabled;
            bool value = info.Getter?.Invoke() ?? false;

            // Section headers, rects, icons, etc.
            DrawSectionHeaderIfNeeded(ls, info);

            const float rowHeight = 34f;
            Rect rowRect = ls.GetRect(rowHeight);
            if (info.IndentSpaces > 0)
            {
                float indent = ChildIndent * info.IndentSpaces;
                rowRect.x += indent;
                rowRect.width -= indent;
            }
            Rect checkboxRect = DrawIconIfPresent(rowRect, info);

            if (checkboxEnabled)
            {
                if (Mouse.IsOver(rowRect))
                {
                    // Draw red highlight on checked, green on unchecked
                    Widgets.DrawTextHighlight(rowRect, expandBy: 0f, color: value ? RedHighlightColor : GreenHighlightColor);
                }
            }

            GUI.color = checkboxEnabled ? white : gray;

            // Draw checkbox
            DrawCheckbox(checkboxRect, GetCheckboxLabel(info), ref value, info, checkboxEnabled);

            GUI.color = white;

            // Sublabel and category button
            DrawSubLabel(ls, info, checkboxEnabled, false, GetCheckboxLabel(info));
            DrawCategoryButtonIfNeeded(ls, info, value, checkboxEnabled);

            // Draw extra controls (sliders, ranges, etc.) tied to this checkbox
            DrawExtraControls(ls, info, checkboxEnabled && value);

            // Force children to false if parent is unchecked
            if (!value && info.Children != null)
            {
                foreach (var child in info.Children)
                {
                    child.Setter?.Invoke(false);

                    // recursively reset deeper descendants
                    if (child.Children != null)
                    {
                        ResetChildren(child);
                    }
                }
            }

            // Draw children (always visible)
            if (info.Children != null)
            {
                foreach (var child in info.Children)
                {
                    DrawCheckboxRecursive(ls, child, checkboxEnabled && value);
                }
            }

            float startY = ls.CurHeight;
            ls.Gap(info.GapAfterCheckbox);

            if (info.DrawSeparatorLine)
            {
                float lineY = startY + info.GapAfterCheckbox / 2f;
                float lineHeight = 3f;
                Rect lineRect = new(0f, lineY - lineHeight / 2f, ls.ColumnWidth, lineHeight);
                GUI.color = MenuSectionBGBorderColor;
                GUI.DrawTexture(lineRect, BaseContent.WhiteTex);
                GUI.color = white;
            }
        }

        private static void DrawExtraControls(Listing_Standard ls, CheckboxInfo info, bool enabled)
        {
            if (info.ExtraControls.NullOrEmpty())
                return;

            if (info.GapBeforeExtraControls > 0f)
                ls.Gap(info.GapBeforeExtraControls);

            foreach (var control in info.ExtraControls)
            {
                control.Draw(ls, enabled);
            }
        }

        private static bool IsMergeCategorySetting(CheckboxInfo info)
        {
            return info.CategoryDefForButton != null && info.SettingName.StartsWith("Merge") && info.SettingName.EndsWith("Category");
        }

        private static void ResetChildren(CheckboxInfo parent)
        {
            // Turn off all children recursively
            foreach (var child in parent.Children)
            {
                // If turning off a merge category checkbox through a parent, clean up the category
                bool isMergeCategory = IsMergeCategorySetting(child);
                bool currentValue = child.Getter?.Invoke() ?? false;
                if (isMergeCategory && currentValue)
                {
                    DefToCategoryInfo.RevertCategoryInCache(child.CategoryDefForButton.defName);
                }

                child.Setter?.Invoke(false);

                if (child.Children != null)
                    ResetChildren(child);
            }
        }

        private static string GetCheckboxLabel(CheckboxInfo info)
        {
            bool hasNoTranslateLabel = !string.IsNullOrWhiteSpace(info.LabelNoTranslate);

            // Prioritize LabelOverride if it exists
            if (info.LabelOverride.HasValue)
            {
                var (key, values) = info.LabelOverride.Value;
                var translationArgs = values?.Select(v => (v, true)).ToArray();
                return Translator.TranslateComposite(key, translationArgs);
            }

            // Fallbacks
            if (hasNoTranslateLabel)
                return info.LabelNoTranslate;

            return Translator.TranslateKey(TKey.Type.CheckboxLabel, info.SettingName);
        }

        private static void DrawCategoryButtonIfNeeded(Listing_Standard ls, CheckboxInfo info, bool value, bool checkboxEnabled)
        {
            var category = info.CategoryDefForButton;
            if (category == null)
                return;

            string labelCap = category.LabelCap.ToString() ?? category.defName ?? "??";
            string buttonLabel = Translator.TranslateKey(TKey.Type.Button, "Select") + " " + labelCap;

            const float buttonHeight = 28f;
            Rect buttonRect = ls.GetRect(buttonHeight);

            if (info.IndentSpaces > 0)
            {
                float indent = ChildIndent * info.IndentSpaces;
                buttonRect.x += indent;
                buttonRect.width -= indent;
            }

            // Only allow button if not the first time this session
            bool justActivatedThisSession = _activatedSettingsThisSession.Contains(info.SettingName);

            bool buttonActive = value && checkboxEnabled && !justActivatedThisSession && Settings.CategoryData.Count > 0;

            // Calculate button width based on text size
            string applyText = Translator.TranslateKey(TKey.Type.Button, "ChangeCategoryLabel");
            float applyButtonWidth = Text.CalcSize(applyText).x + 16f; // Add padding

            GUI.color = buttonActive ? white : gray;

            // Draw the category select button
            buttonRect.width = Text.CalcSize(buttonLabel).x + 32f; // Add padding for the select button
            if (Widgets.ButtonText(buttonRect, buttonLabel, active: buttonActive))
            {
                if (!Find.WindowStack.IsOpen<SettingWindows.ThingDefSelectorWindow>())
                {
                    Find.WindowStack.Add(new SettingWindows.ThingDefSelectorWindow(category.defName, buttonLabel));
                }
            }

            GUI.color = white;

            ls.Gap(GenUI.Pad); // Gap before label customization controls

            // Add label customization controls under the button
            const float textFieldHeight = 28f;
            float textFieldWidth = 200f;

            // Initialize or update text buffer for this category
            string bufferKey = $"CategoryLabel_{category.defName}";
            if (!_categoryLabelBuffers.ContainsKey(bufferKey))
            {
                _categoryLabelBuffers[bufferKey] = category.label;
            }

            // Place the text field under the button, aligned with the button's left edge
            float y = ls.CurHeight;
            float x = buttonRect.x;
            Rect textFieldRect = new(x, y, textFieldWidth, textFieldHeight);

            string buffer = _categoryLabelBuffers[bufferKey];
            GUI.SetNextControlName(bufferKey);

            // Disable the text field if !value
            bool prevEnabled = GUI.enabled;
            GUI.enabled = value;
            buffer = Widgets.TextField(textFieldRect, buffer, maxLength: 30);
            GUI.enabled = prevEnabled;

            // If the mouse is not over the text field area (textFieldWidth for both width and height), reset buffer to current label
            if (!Mouse.IsOver(textFieldRect.ExpandedBy(textFieldWidth)))
            {
                if (buffer != category.label)
                {
                    buffer = category.label;
                    GUI.FocusControl(null); // Remove focus to avoid immediate re-editing
                }
            }

            if (_categoryLabelBuffers[bufferKey] != buffer)
            {
                _categoryLabelBuffers[bufferKey] = buffer;
            }

            // Place the apply button to the right of the text field
            Rect applyButtonRect = new(textFieldRect.xMax + GenUI.Pad, y, applyButtonWidth, textFieldHeight);
            string trimmedBuffer = buffer.Trim();
            bool canApply = trimmedBuffer != category.label && !string.IsNullOrWhiteSpace(trimmedBuffer) && value;

            GUI.color = canApply ? white : gray;
            if (Widgets.ButtonText(applyButtonRect, applyText, active: canApply))
            {
                category.label = trimmedBuffer;
                category.ClearCachedData();
                SettingChanged = true;
                CategoryLabelInfo.UpdateCategoryLabelInfo(category, trimmedBuffer);
                Messages.Message(Translator.TranslateKey(TKey.Type.Button, "LabelChangedTo") + " " + category.label, MessageTypeDefOf.PositiveEvent, false);
            }
            GUI.color = white;

            ls.Gap(textFieldHeight + 4f);
        }


        private static void DrawSectionHeaderIfNeeded(Listing_Standard ls, CheckboxInfo info)
        {
            if (!string.IsNullOrWhiteSpace(info.SectionStartLabel))
            {
                DrawSectionLabel(ls, info.SectionStartLabel);
                if (!string.IsNullOrWhiteSpace(info.SectionStartSubLabel))
                {
                    ls.Gap(GenUI.GapTiny);
                    DrawCustomLabel(ls, Translator.TranslateKey(TKey.Type.SectionLabel, info.SectionStartSubLabel),
                        anchor: TextAnchor.MiddleCenter, subLabel: true);
                    ls.Gap(GenUI.Pad);
                }
            }
        }

        private static Rect DrawIconIfPresent(Rect rowRect, CheckboxInfo info)
        {
            if (info.DefForIcon == null)
                return rowRect;

            float iconSize = rowRect.height;
            float iconOffset = GenUI.GapTiny;
            Rect iconRect = rowRect.LeftPartPixels(iconSize);
            Widgets.DefIcon(iconRect, info.DefForIcon);

            return rowRect.RightPartPixels(rowRect.width - iconSize - iconOffset);
        }

        private static void DrawCheckbox(Rect checkboxRect, string label, ref bool value, CheckboxInfo info, bool checkboxEnabled)
        {
            if (checkboxEnabled && info.Getter != null && info.Setter != null)
            {
                bool oldValue = info.Getter(); // Get the old value before changing it
                Widgets.CheckboxLabeled(checkboxRect, label, ref value, paintable: info.Paintable);

                if (value != oldValue)
                {
                    // If turning off a merge category checkbox, remove its category from the map
                    if (oldValue && !value && IsMergeCategorySetting(info))
                    {
                        DefToCategoryInfo.RevertCategoryInCache(info.CategoryDefForButton.defName);
                    }

                    info.Setter(value); 
                    SettingChanged = true;

                    // Track first-time activation (so category button can be disabled until restart)
                    if (value)
                    {
                        _activatedSettingsThisSession.Add(info.SettingName);
                    }
                    else
                    {
                        _activatedSettingsThisSession.Remove(info.SettingName);
                    }
                }
            }
            else
            {
                // Draw disabled checkbox
                bool tempValue = false;
                Widgets.CheckboxLabeled(checkboxRect, label, ref tempValue, disabled: true);
            }
        }

        private static void DrawSubLabel(Listing_Standard ls, CheckboxInfo info, bool checkboxEnabled, bool hasNoTranslateLabel, string label)
        {
            // Check if we have a SubLabelOverride and use TranslateComposite if we do
            if (info.SubLabelOverride.HasValue)
            {
                var (key, values) = info.SubLabelOverride.Value;
                // Convert string[] values to (string value, bool translate)[] format
                var translationArgs = values?.Select(v => (v, true)).ToArray();
                string translatedText = Translator.TranslateComposite(key, translationArgs);
                DrawCustomLabel(ls, translatedText, subLabel: true, indentSpaces: info.IndentSpaces);
                return;
            }

            // Otherwise use the standard logic
            //if (info.HasSubLabel && checkboxEnabled)
            //{
            //    string subLabel = GetSubLabelText(info, hasNoTranslateLabel, label);
            //    DrawCustomLabel(ls, subLabel, subLabel: true, indentSpaces: info.IndentSpaces);
            //}

            if (info.HasSubLabel)
            {
                string subLabel = GetSubLabelText(info, hasNoTranslateLabel, label);
                DrawCustomLabel(ls, subLabel, subLabel: true, indentSpaces: info.IndentSpaces);
            }
        }

        private static string GetSubLabelText(CheckboxInfo info, bool hasNoTranslateLabel, string label)
        {
            // For OnlyOnOffSublabel, we only show the default value indicator
            if (info.OnlyOnOffSublabel)
            {
                return GetDefaultValueText(info.DefaultValue);
            }

            // Otherwise, show sublabel with extra info followed by ON/OFF default value
            string name = info.SettingName;
            int index = name.IndexOf('='); // Remove the setting indentifier prefix if applicable
            string key = index >= 0 ? name.Substring(0, index) : name;
            string noTranslateLabel = hasNoTranslateLabel ? label : "";

            return Translator.TranslateKey(TKey.Type.CheckboxSubLabel, key) + "\n" + GetDefaultValueText(info.DefaultValue, noTranslateLabel);
        }

        private static string GetDefaultValueText(bool defaultValue, string labelText = "")
        {
            return Translator.TranslateComposite(
                $"{TKey.Type.General}_Default",
                string.IsNullOrEmpty(labelText) ? null : [(labelText, false)],
                defaultValue,
                ("ON", true),
                ("OFF", true)
            );
        }
    }
}
