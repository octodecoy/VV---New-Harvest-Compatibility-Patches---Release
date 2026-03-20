using Verse.Sound;

namespace NewHarvestPatches
{
    internal abstract class ExtraControlInfo(
        string controlName,
        string labelKey,
        int indentSpaces = 0,
        string sublabelKey = null)
    {
        public string ControlName { get; } = controlName;
        public string LabelKey { get; } = labelKey;
        public int IndentSpaces { get; } = indentSpaces;
        public string SublabelKey { get; } = sublabelKey;

        /// <summary>
        /// Draw the control into the listing. Returns the height used.
        /// </summary>
        public abstract float Draw(Listing_Standard ls, bool enabled);

        /// <summary>
        /// Helper to call the convenience overload unambiguously.
        /// </summary>
        protected static Rect DrawLabel(Listing_Standard ls, string label, bool subLabel = false, int indentSpaces = 0, Color? color = null)
        {
            return DrawCustomLabel(ls, label, subLabel: subLabel, indentSpaces: indentSpaces, color: color);
        }

        /// <summary>
        /// Returns the disabled or enabled color based on state.
        /// </summary>
        protected static Color GetLabelColor(bool enabled) => enabled ? white : gray;

        /// <summary>
        /// Formats a float value as a percentage string (e.g. 0.05 → "5%").
        /// </summary>
        protected static string FormatPercent(float value) => $"{value * 100f:F0}%";
    }

    internal class FloatRangeControl(
        string controlName,
        string labelKey,
        Func<FloatRange> getter,
        Action<FloatRange> setter,
        float min,
        float max,
        FloatRange defaultValue,
        int indentSpaces = 0,
        string sublabelKey = null,
        ToStringStyle valueStyle = ToStringStyle.FloatTwo,
        float roundTo = 0.01f, 
        float gapAfterControl = 24f,
        bool drawSeparatorLine = false,
        bool displayAsPercent = false)
        : ExtraControlInfo(controlName, labelKey, indentSpaces, sublabelKey)
    {
        public Func<FloatRange> Getter { get; } = getter;
        public Action<FloatRange> Setter { get; } = setter;
        public float Min { get; } = min;
        public float Max { get; } = max;
        public FloatRange DefaultValue { get; } = defaultValue;
        public ToStringStyle ValueStyle { get; } = valueStyle;
        public float RoundTo { get; } = roundTo;
        public bool DisplayAsPercent { get; } = displayAsPercent;

        public override float Draw(Listing_Standard ls, bool enabled)
        {
            float startHeight = ls.CurHeight;
            float indent = ChildIndent * IndentSpaces;
            Color labelColor = GetLabelColor(enabled);

            // Label — append current range value
            string label = Translator.TranslateKey(TKey.Type.RangeLabel, LabelKey);
            FloatRange current = Getter();
            if (DisplayAsPercent)
            {
                label = $"{label}: {FormatPercent(current.min)} - {FormatPercent(current.max)}";
            }
            DrawLabel(ls, label, indentSpaces: IndentSpaces, color: labelColor);

            // Sublabel
            if (!string.IsNullOrWhiteSpace(SublabelKey))
            {
                string subLabel = Translator.TranslateKey(TKey.Type.RangeSubLabel, SublabelKey);
                DrawLabel(ls, subLabel, subLabel: true, indentSpaces: IndentSpaces, color: labelColor);
                ls.Gap(GenUI.GapTiny); 
            }

            // FloatRange slider
            const float rangeHeight = 32f;
            Rect rangeRect = ls.GetRect(rangeHeight);
            if (indent > 0)
            {
                rangeRect.x += indent;
                rangeRect.width -= indent;
            }

            bool prevEnabled = GUI.enabled;
            Color prevColor = GUI.color;
            GUI.enabled = enabled;
            GUI.color = labelColor;

            int id = Gen.HashCombineInt(ControlName.GetHashCode(), rangeRect.GetHashCode());
            if (DisplayAsPercent)
            {
                // Use PercentZero so the built-in range label shows "5% - 50%" style
                Widgets.FloatRange(rangeRect, id: id,
                    ref current, Min, Max, valueStyle: ToStringStyle.PercentZero, roundTo: RoundTo);
            }
            else
            {
                Widgets.FloatRange(rangeRect, id, ref current, Min, Max, valueStyle: ValueStyle, roundTo: RoundTo);
            }

            if (enabled && current != Getter())
            {
                Setter(current);
                SettingChanged = true;
            }

            GUI.enabled = prevEnabled;
            GUI.color = prevColor;

            if (drawSeparatorLine)
            {
                float lineY = ls.CurHeight + gapAfterControl / 2f;
                float lineHeight = 3f;
                Rect lineRect = new(0f, lineY - lineHeight / 2f, ls.ColumnWidth, lineHeight);
                GUI.color = MenuSectionBGBorderColor;
                GUI.DrawTexture(lineRect, BaseContent.WhiteTex);
                GUI.color = white;
            }

            ls.Gap(gapAfterControl);
            return ls.CurHeight - startHeight;
        }
    }

    internal class IntRangeControl(
        string controlName,
        string labelKey,
        Func<IntRange> getter,
        Action<IntRange> setter,
        int min,
        int max,
        IntRange defaultValue,
        int indentSpaces = 0,
        string sublabelKey = null,
        int minWidth = 0,
        float gapAfterControl = 24f,
        bool drawSeparatorLine = false)
        : ExtraControlInfo(controlName, labelKey, indentSpaces, sublabelKey)
    {
        public Func<IntRange> Getter { get; } = getter;
        public Action<IntRange> Setter { get; } = setter;
        public int Min { get; } = min;
        public int Max { get; } = max;
        public IntRange DefaultValue { get; } = defaultValue;
        public int MinWidth { get; } = minWidth;

        public override float Draw(Listing_Standard ls, bool enabled)
        {
            float startHeight = ls.CurHeight;
            float indent = ChildIndent * IndentSpaces;
            Color labelColor = GetLabelColor(enabled);

            // Label — append current range value
            string label = Translator.TranslateKey(TKey.Type.RangeLabel, LabelKey);
            IntRange current = Getter();
            label = $"{label}: {current.min} - {current.max}";
            DrawLabel(ls, label, indentSpaces: IndentSpaces, color: labelColor);

            // Sublabel
            if (!string.IsNullOrWhiteSpace(SublabelKey))
            {
                string subLabel = Translator.TranslateKey(TKey.Type.RangeSubLabel, SublabelKey);
                DrawLabel(ls, subLabel, subLabel: true, indentSpaces: IndentSpaces, color: labelColor);
                ls.Gap(GenUI.GapTiny);
            }

            // IntRange slider
            const float rangeHeight = 32f;
            Rect rangeRect = ls.GetRect(rangeHeight);
            if (indent > 0)
            {
                rangeRect.x += indent;
                rangeRect.width -= indent;
            }

            bool prevEnabled = GUI.enabled;
            Color prevColor = GUI.color;
            GUI.enabled = enabled;
            GUI.color = labelColor;

            int id = Gen.HashCombineInt(ControlName.GetHashCode(), rangeRect.GetHashCode());
            Widgets.IntRange(rangeRect, id, ref current, Min, Max, minWidth: MinWidth);

            if (enabled && current != Getter())
            {
                Setter(current);
                SettingChanged = true;
            }

            GUI.enabled = prevEnabled;
            GUI.color = prevColor;

            if (drawSeparatorLine)
            {
                float lineY = ls.CurHeight + gapAfterControl / 2f;
                float lineHeight = 3f;
                Rect lineRect = new(0f, lineY - lineHeight / 2f, ls.ColumnWidth, lineHeight);
                GUI.color = MenuSectionBGBorderColor;
                GUI.DrawTexture(lineRect, BaseContent.WhiteTex);
                GUI.color = white;
            }

            ls.Gap(gapAfterControl);
            return ls.CurHeight - startHeight;
        }
    }

    internal class FloatSliderControl(
        string controlName,
        string labelKey,
        Func<float> getter,
        Action<float> setter,
        float min,
        float max,
        float defaultValue,
        int indentSpaces = 0,
        string sublabelKey = null,
        float roundTo = 0.01f,
        string leftLabel = null,
        string rightLabel = null,
        float gapAfterControl = 24f,
        bool drawSeparatorLine = false,
        bool displayAsPercent = false)
        : ExtraControlInfo(controlName, labelKey, indentSpaces, sublabelKey)
    {
        public Func<float> Getter { get; } = getter;
        public Action<float> Setter { get; } = setter;
        public float Min { get; } = min;
        public float Max { get; } = max;
        public float DefaultValue { get; } = defaultValue;
        public float RoundTo { get; } = roundTo;
        public string LeftLabel { get; } = leftLabel;
        public string RightLabel { get; } = rightLabel;
        public bool DisplayAsPercent { get; } = displayAsPercent;

        public override float Draw(Listing_Standard ls, bool enabled)
        {
            float startHeight = ls.CurHeight;
            float indent = ChildIndent * IndentSpaces;
            Color labelColor = GetLabelColor(enabled);

            // Label with current value
            string label = Translator.TranslateKey(TKey.Type.SliderLabel, LabelKey);
            float currentVal = Getter();
            string displayLabel = DisplayAsPercent
                ? $"{label}: {FormatPercent(currentVal)}"
                : $"{label}: {currentVal:F2}";

            DrawLabel(ls, displayLabel, indentSpaces: IndentSpaces, color: labelColor);

            // Sublabel
            if (!string.IsNullOrWhiteSpace(SublabelKey))
            {
                string subLabel = Translator.TranslateKey(TKey.Type.SliderSubLabel, SublabelKey);
                DrawLabel(ls, subLabel, subLabel: true, indentSpaces: IndentSpaces, color: labelColor);
                ls.Gap(GenUI.GapTiny); 
            }

            // Slider
            const float sliderHeight = 22f;
            Rect sliderRect = ls.GetRect(sliderHeight);
            if (indent > 0)
            {
                sliderRect.x += indent;
                sliderRect.width -= indent;
            }

            if (enabled)
            {
                // Only call HorizontalSlider when enabled — it uses a static
                // sliderDraggingID that never self-resets, causing persistent
                // drag sounds if called while disabled
                string effectiveLeftLabel = LeftLabel ?? (DisplayAsPercent ? FormatPercent(Min) : null);
                string effectiveRightLabel = RightLabel ?? (DisplayAsPercent ? FormatPercent(Max) : null);

                float newVal = Widgets.HorizontalSlider(
                    sliderRect, currentVal, Min, Max,
                    middleAlignment: true,
                    leftAlignedLabel: effectiveLeftLabel,
                    rightAlignedLabel: effectiveRightLabel,
                    roundTo: RoundTo);

                if (Math.Abs(newVal - currentVal) > float.Epsilon)
                {
                    Setter(newVal);
                    SettingChanged = true;
                }
            }
            // When disabled: rect is reserved but no widget is drawn,
            // preventing sliderDraggingID from latching onto this position
            // and causing drag sounds to persist until parent toggle

            if (drawSeparatorLine)
            {
                float lineY = ls.CurHeight + gapAfterControl / 2f;
                float lineHeight = 3f;
                Rect lineRect = new(0f, lineY - lineHeight / 2f, ls.ColumnWidth, lineHeight);
                GUI.color = MenuSectionBGBorderColor;
                GUI.DrawTexture(lineRect, BaseContent.WhiteTex);
                GUI.color = white;
            }

            ls.Gap(gapAfterControl);
            return ls.CurHeight - startHeight;
        }
    }

    internal class ExtraCheckboxControl(
        string controlName,
        string labelKey,
        Func<bool> getter,
        Action<bool> setter,
        bool defaultValue = false,
        int indentSpaces = 0,
        string sublabelKey = null,
        float gapAfterControl = 24f,
        bool drawSeparatorLine = false)
        : ExtraControlInfo(controlName, labelKey, indentSpaces, sublabelKey)
    {
        public Func<bool> Getter { get; } = getter;
        public Action<bool> Setter { get; } = setter;
        public bool DefaultValue { get; } = defaultValue;

        public override float Draw(Listing_Standard ls, bool enabled)
        {
            float startHeight = ls.CurHeight;
            float indent = ChildIndent * IndentSpaces;
            Color labelColor = GetLabelColor(enabled);

            string label = Translator.TranslateKey(TKey.Type.CheckboxLabel, LabelKey);

            const float rowHeight = 34f;
            Rect rowRect = ls.GetRect(rowHeight);
            if (indent > 0)
            {
                rowRect.x += indent;
                rowRect.width -= indent;
            }

            GUI.color = labelColor;

            bool value = Getter();
            if (enabled)
            {

                if (Mouse.IsOver(rowRect))
                {
                    // Draw red highlight on checked, green on unchecked
                    Widgets.DrawTextHighlight(rowRect, expandBy: 0f, color: value ? RedHighlightColor : GreenHighlightColor);
                }
                
                bool oldValue = value;
                Widgets.CheckboxLabeled(rowRect, label, ref value);
                if (value != oldValue)
                {
                    Setter(value);
                    SettingChanged = true;
                }
            }
            else
            {
                bool temp = Getter();
                Widgets.CheckboxLabeled(rowRect, label, ref temp, disabled: true);
            }

            GUI.color = white;

            // Sublabel
            if (!string.IsNullOrWhiteSpace(SublabelKey))
            {
                string subLabel = Translator.TranslateKey(TKey.Type.CheckboxSubLabel, SublabelKey);
                DrawLabel(ls, subLabel, subLabel: true, indentSpaces: IndentSpaces, color: labelColor);
                ls.Gap(GenUI.GapTiny); 
            }

            if (drawSeparatorLine)
            {
                float lineY = ls.CurHeight + gapAfterControl / 2f;
                float lineHeight = 3f;
                Rect lineRect = new(0f, lineY - lineHeight / 2f, ls.ColumnWidth, lineHeight);
                GUI.color = MenuSectionBGBorderColor;
                GUI.DrawTexture(lineRect, BaseContent.WhiteTex);
                GUI.color = white;
            }

            ls.Gap(gapAfterControl);
            return ls.CurHeight - startHeight;
        }
    }

    internal class IntAdjusterControl(
        string controlName,
        string labelKey,
        Func<int> getter,
        Action<int> setter,
        int defaultValue,
        int countChange = 1,
        int min = 0,
        int max = int.MaxValue,
        int indentSpaces = 0,
        string sublabelKey = null,
        float gapAfterControl = 24f,
        bool drawSeparatorLine = false,
        Func<int, string> displayFormatter = null)
        : ExtraControlInfo(controlName, labelKey, indentSpaces, sublabelKey)
    {
        public Func<int> Getter { get; } = getter;
        public Action<int> Setter { get; } = setter;
        public int DefaultValue { get; } = defaultValue;
        public int CountChange { get; } = countChange;
        public int Min { get; } = min;
        public int Max { get; } = max;
        public Func<int, string> DisplayFormatter { get; } = displayFormatter;

        public override float Draw(Listing_Standard ls, bool enabled)
        {
            float startHeight = ls.CurHeight;
            float indent = ChildIndent * IndentSpaces;
            Color labelColor = GetLabelColor(enabled);

            // Label with current value
            string label = Translator.TranslateKey(TKey.Type.AdjusterLabel, LabelKey);
            int currentVal = Getter();
            string displayValue = DisplayFormatter != null ? DisplayFormatter(currentVal) : currentVal.ToString();
            string displayLabel = $"{label}: {displayValue}";

            DrawLabel(ls, displayLabel, indentSpaces: IndentSpaces, color: labelColor);

            // Sublabel
            if (!string.IsNullOrWhiteSpace(SublabelKey))
            {
                string subLabel = Translator.TranslateKey(TKey.Type.AdjusterSubLabel, SublabelKey);
                DrawLabel(ls, subLabel, subLabel: true, indentSpaces: IndentSpaces, color: labelColor);
                ls.Gap(GenUI.GapTiny);
            }

            // IntAdjuster buttons
            const float buttonHeight = 24f;
            const float buttonWidth = 80f;
            const float buttonSpacing = 2f;
            Rect adjusterRect = ls.GetRect(buttonHeight);
            if (indent > 0)
            {
                adjusterRect.x += indent;
                adjusterRect.width -= indent;
            }

            bool prevEnabled = GUI.enabled;
            Color prevColor = GUI.color;
            GUI.enabled = enabled;
            GUI.color = labelColor;

            int adjustedChange = CountChange * GenUI.CurrentAdjustmentMultiplier();

            // Minus button
            Rect minusRect = new(adjusterRect.x, adjusterRect.y, buttonWidth, buttonHeight);
            if (Widgets.ButtonText(minusRect, $"-{adjustedChange}"))
            {
                if (enabled)
                {
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    int newVal = Mathf.Clamp(currentVal - adjustedChange, Min, Max);
                    Setter(newVal);
                    SettingChanged = true;
                }
            }

            // Plus button
            Rect plusRect = new(minusRect.xMax + buttonSpacing, adjusterRect.y, buttonWidth, buttonHeight);
            if (Widgets.ButtonText(plusRect, $"+{adjustedChange}"))
            {
                if (enabled)
                {
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    int newVal = Mathf.Clamp(currentVal + adjustedChange, Min, Max);
                    Setter(newVal);
                    SettingChanged = true;
                }
            }

            GUI.enabled = prevEnabled;
            GUI.color = prevColor;

            if (drawSeparatorLine)
            {
                float lineY = ls.CurHeight + gapAfterControl / 2f;
                float lineHeight = 3f;
                Rect lineRect = new(0f, lineY - lineHeight / 2f, ls.ColumnWidth, lineHeight);
                GUI.color = MenuSectionBGBorderColor;
                GUI.DrawTexture(lineRect, BaseContent.WhiteTex);
                GUI.color = white;
            }

            ls.Gap(gapAfterControl);
            return ls.CurHeight - startHeight;
        }
    }
}