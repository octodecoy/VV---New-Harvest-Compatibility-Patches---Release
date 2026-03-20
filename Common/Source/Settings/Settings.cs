using Verse.Sound;

namespace NewHarvestPatches
{
    public class NewHarvestPatchesModSettings : ModSettings
    {
        public string ModVersion;
        public bool Logging = false;
        public bool AddToAnimalFoodsCategory = false;
        public bool MergeAnimalFoodsCategory = false;
        public bool AnimalFoodsCategoryResourceReadout = false;
        public bool AddToFruitCategory = false;
        public bool MergeFruitCategory = false;
        public bool FruitCategoryResourceReadout = false;
        public bool AddToGrainsCategory = false;
        public bool MergeGrainsCategory = false;
        public bool GrainsCategoryResourceReadout = false;
        public bool AddToNutsCategory = false;
        public bool MergeNutsCategory = false;
        public bool NutsCategoryResourceReadout = false;
        public bool AddToVegetablesCategory = false;
        public bool MergeVegetablesCategory = false;
        public bool VegetablesCategoryResourceReadout = false;
        public bool AddToFungusCategory = false;
        public bool MergeFungusCategory = false;
        public bool FungusCategoryResourceReadout = false;
        public bool MoveDrinksToVBECategory = false;
        public bool AddAquaticReedsToBiomes = false;
        public bool AddMoreWoodFloors = false;
        public bool NewHarvestWoodFloorsToDropdowns = false;
        public bool BaseWoodFloorsToDropdowns = false;
        public bool ModWoodFloorsToDropdowns = false;
        public bool NewHarvestNonWoodFloorsToDropdown = false;
        public bool AddHayConversionRecipe = false;
        public bool AddWoodConversionRecipe = false;
        public bool UseVanillaLogGraphic = false;
        public bool AddWoodDryads = false;

        [IgnoreEnabled]
        public bool HayNeedsCooling = true;
        public bool GrainsProduceVCEFlourSecondary = false;
        public bool AddTruffleDiggingBehavior = false;
        public TruffleDiggingSettings TruffleSettings = new();
        public Dictionary<string, bool> FuelTypes = [];
        public Dictionary<string, CommonalityInfo> StuffCommonality = [];
        public Dictionary<string, bool> FallColorTrees = [];
        public Dictionary<string, ColorInfo> MaterialColors = [];
        public List<DefToCategoryInfo> CategoryData = [];
        public Dictionary<string, CategoryLabelInfo> CategoryLabelCache = [];

        public static Dictionary<string, HashSet<string>> ModAddedCategoryDictionary = [];
        public static HashSet<string> ModAddedCategoryTypeCache = [];

        // All the checkbox data (plus some other data) for building the UI
        private static List<CheckboxInfo> _checkboxInfos = [];

        public bool MergingAnyCategories =>
            MergeAnimalFoodsCategory ||
            MergeFruitCategory ||
            MergeGrainsCategory ||
            MergeNutsCategory ||
            MergeVegetablesCategory ||
            MergeFungusCategory;

        public NewHarvestPatchesModSettings()
        {
            // Have to have access to Def data so need to build after loaded in
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                StartStopwatch(nameof(ModSettings), nameof(NewHarvestPatchesModSettings));
                try
                {
                    var (oldVersion, newVersion) = Utils.VersionChecker.UpdateModVersion();
                    CommonalityInfo.BuildCommonalityStats(ref StuffCommonality);
                    CheckboxInfo.BuildCheckboxInfo(ref _checkboxInfos);
                    ColorInfo.BuildColorInfo(ref MaterialColors);
                    DefToCategoryInfo.CacheDefToCategoryInfo(ref CategoryData);
                    CategoryLabelInfo.CacheDefaultCategoryLabelInfo(ref CategoryLabelCache);
                    BuildTabs();

                    WriteSettingsToFile(); // Write in case null stuff was removed from scribed data while building
                }
                finally
                {
                    LogStopwatch(nameof(ModSettings), nameof(NewHarvestPatchesModSettings));
                }
            });
        }

        internal void WriteSettingsToFile()
        {
            Write();
            SettingChanged = false;
        }

        internal void UpdateScribedVersion(Version newVersion)
        {
            // Build proper version string with Build component if it exists
            if (newVersion.Build >= 0)
            {
                ModVersion = $"{newVersion.Major}.{newVersion.Minor}.{newVersion.Build}";
            }
            else
            {
                ModVersion = $"{newVersion.Major}.{newVersion.Minor}";
            }

            WriteSettingsToFile();
        }

        internal static void ClearBuffers()
        {
            CommonalityTab._commonalityBuffers?.Clear();
            ChangeColorSection._colorChannelBuffers?.Clear();
            Checkboxes._categoryLabelBuffers?.Clear();
        }

        internal static void ClearCaches()
        {
            ModAddedCategoryDictionary?.Clear();
            ModAddedCategoryDictionary = null;

            ModAddedCategoryTypeCache?.Clear();
            ModAddedCategoryTypeCache = null;
        }

        internal void ClearCategoryCache()
        {
            CategoryData?.Clear();
            WriteSettingsToFile();
        }

        //public Dictionary<ThingDef, WeedSpawnerInfo> test = [];

        public override void ExposeData()
        {
            base.ExposeData();
            if (HasAnyModule)
            {
                Scribe_Values.Look(ref ModVersion, nameof(ModVersion), "", false);
                Scribe_Values.Look(ref Logging, nameof(Logging), false, false);
                Scribe_Values.Look(ref AddToAnimalFoodsCategory, nameof(AddToAnimalFoodsCategory), false, false);
                Scribe_Values.Look(ref MergeAnimalFoodsCategory, nameof(MergeAnimalFoodsCategory), false, false);
                Scribe_Values.Look(ref AnimalFoodsCategoryResourceReadout, nameof(AnimalFoodsCategoryResourceReadout), false, false);
                Scribe_Values.Look(ref AddToFruitCategory, nameof(AddToFruitCategory), false, false);
                Scribe_Values.Look(ref MergeFruitCategory, nameof(MergeFruitCategory), false, false);
                Scribe_Values.Look(ref FruitCategoryResourceReadout, nameof(FruitCategoryResourceReadout), false, false);
                Scribe_Values.Look(ref AddToGrainsCategory, nameof(AddToGrainsCategory), false, false);
                Scribe_Values.Look(ref MergeGrainsCategory, nameof(MergeGrainsCategory), false, false);
                Scribe_Values.Look(ref GrainsCategoryResourceReadout, nameof(GrainsCategoryResourceReadout), false, false);
                Scribe_Values.Look(ref AddToNutsCategory, nameof(AddToNutsCategory), false, false);
                Scribe_Values.Look(ref MergeNutsCategory, nameof(MergeNutsCategory), false, false);
                Scribe_Values.Look(ref NutsCategoryResourceReadout, nameof(NutsCategoryResourceReadout), false, false);
                Scribe_Values.Look(ref AddToVegetablesCategory, nameof(AddToVegetablesCategory), false, false);
                Scribe_Values.Look(ref MergeVegetablesCategory, nameof(MergeVegetablesCategory), false, false);
                Scribe_Values.Look(ref VegetablesCategoryResourceReadout, nameof(VegetablesCategoryResourceReadout), false, false);
                Scribe_Values.Look(ref AddToFungusCategory, nameof(AddToFungusCategory), false, false);
                Scribe_Values.Look(ref MergeFungusCategory, nameof(MergeFungusCategory), false, false);
                Scribe_Values.Look(ref FungusCategoryResourceReadout, nameof(FungusCategoryResourceReadout), false, false);
                Scribe_Values.Look(ref MoveDrinksToVBECategory, nameof(MoveDrinksToVBECategory), false, false);
                Scribe_Values.Look(ref AddAquaticReedsToBiomes, nameof(AddAquaticReedsToBiomes), false, false);
                Scribe_Values.Look(ref AddMoreWoodFloors, nameof(AddMoreWoodFloors), false, false);
                Scribe_Values.Look(ref NewHarvestWoodFloorsToDropdowns, nameof(NewHarvestWoodFloorsToDropdowns), false, false);
                Scribe_Values.Look(ref BaseWoodFloorsToDropdowns, nameof(BaseWoodFloorsToDropdowns), false, false);
                Scribe_Values.Look(ref ModWoodFloorsToDropdowns, nameof(ModWoodFloorsToDropdowns), false, false);
                Scribe_Values.Look(ref NewHarvestNonWoodFloorsToDropdown, nameof(NewHarvestNonWoodFloorsToDropdown), false, false);
                Scribe_Values.Look(ref AddHayConversionRecipe, nameof(AddHayConversionRecipe), false, false);
                Scribe_Values.Look(ref AddWoodConversionRecipe, nameof(AddWoodConversionRecipe), false, false);
                Scribe_Values.Look(ref UseVanillaLogGraphic, nameof(UseVanillaLogGraphic), false, false);
                Scribe_Values.Look(ref AddWoodDryads, nameof(AddWoodDryads), false, false);
                Scribe_Values.Look(ref HayNeedsCooling, nameof(HayNeedsCooling), true, false);
                Scribe_Values.Look(ref GrainsProduceVCEFlourSecondary, nameof(GrainsProduceVCEFlourSecondary), false, false);
                Scribe_Values.Look(ref AddTruffleDiggingBehavior, nameof(AddTruffleDiggingBehavior), false, false);
                Scribe_Deep.Look(ref TruffleSettings, nameof(TruffleSettings));
                TruffleSettings ??= new TruffleDiggingSettings();
                // Scribe_Values.Look(ref TicksBetweenTruffleDigAttempts, nameof(TicksBetweenTruffleDigAttempts), GenDate.TicksPerDay, false);
                // Scribe_Values.Look(ref TruffleDiggingChanceRange, nameof(TruffleDiggingChanceRange), new FloatRange(0.05f, 0.5f), false);
                // Scribe_Values.Look(ref TruffleDiggingChanceReduction, nameof(TruffleDiggingChanceReduction), 0.05f, false);
                // Scribe_Values.Look(ref TruffleAmountRange, nameof(TruffleAmountRange), IntRange.One, false);
                // Scribe_Values.Look(ref TruffleSpawnsForbidden, nameof(TruffleSpawnsForbidden), false, false);
                // Scribe_Values.Look(ref TruffleGizmoRequiresTraining, nameof(TruffleGizmoRequiresTraining), true, false);

                if (HasIndustrialModule)
                {
                    Scribe_Collections.Look(ref StuffCommonality, nameof(StuffCommonality), LookMode.Value, LookMode.Deep);
                    StuffCommonality ??= [];

                    Scribe_Collections.Look(ref MaterialColors, nameof(MaterialColors), LookMode.Value, LookMode.Deep);
                    MaterialColors ??= [];

                    if (ShowFuelSettings)
                    {
                        Scribe_Collections.Look(ref FuelTypes, nameof(FuelTypes), LookMode.Value, LookMode.Value);
                        FuelTypes ??= [];
                    }
                }

                if (HasAnyTrees)
                {
                    Scribe_Collections.Look(ref FallColorTrees, nameof(FallColorTrees), LookMode.Value, LookMode.Value);
                    FallColorTrees ??= [];
                }

                Scribe_Collections.Look(ref CategoryData, nameof(CategoryData), LookMode.Deep);
                CategoryData ??= [];

                Scribe_Collections.Look(ref CategoryLabelCache, nameof(CategoryLabelCache), LookMode.Value, LookMode.Deep);
                CategoryLabelCache ??= [];
            }
        }

        // For getting the values of all public bool fields to check for enabled settings
        private static readonly FieldInfo[] _boolFields =
            [.. typeof(NewHarvestPatchesModSettings).GetFields().Where(f => f.FieldType == typeof(bool))];


        public IEnumerable<string> EnabledSettings => GetEnabledSettings;
        private IEnumerable<string> GetEnabledSettings
        {
            get
            {
                foreach (var @field in _boolFields)
                {
                    if ((bool)@field.GetValue(this))
                    {
                        var attr = @field.GetCustomAttribute<IgnoreEnabledAttribute>();
                        if (attr == null)
                        {
                            yield return @field.Name;
                        }
                    }
                }

                if (HasIndustrialModule)
                {
                    if (!FuelTypes.NullOrEmpty())
                    {
                        foreach (var kvp in FuelTypes)
                        {
                            if (!kvp.Value) // Enabled if the value is false - to disallow fuel type
                                yield return Setting.Prefix.DisabledFuel_ + kvp.Key;
                        }
                    }
                    if (!StuffCommonality.NullOrEmpty())
                    {
                        foreach (var kvp in StuffCommonality)
                        {
                            float defaultCommonality = kvp.Value.DefaultCommonality;
                            if (!ShowVEFCommonalitySettings)
                            {
                                if (kvp.Value.CoreCommonality != defaultCommonality)
                                {
                                    yield return Setting.Prefix.SetCommonality_ + kvp.Key;
                                }
                            }
                            else if ((kvp.Value.StructureOffset != defaultCommonality) ||
                                    (kvp.Value.ApparelOffset != defaultCommonality) || 
                                    (kvp.Value.WeaponOffset != defaultCommonality))
                                {
                                    yield return Setting.Prefix.SetCommonality_ + kvp.Key;
                                }

                        }
                    }

                    if (!MaterialColors.NullOrEmpty())
                    {
                        foreach (var kvp in MaterialColors)
                        {
                            if ((kvp.Value.DoStuff && !kvp.Value.DefaultStuffColor.EqualsColor(kvp.Value.NewStuffColor, 0.001f)) ||
                                (kvp.Value.DoThing && !kvp.Value.DefaultThingColor.EqualsColor(kvp.Value.NewThingColor, 0.001f)))
                            {
                                yield return Setting.Prefix.ColorChange_ + kvp.Key;
                            }
                        }
                    }
                }

                if (HasAnyTrees)
                {
                    if (!FallColorTrees.NullOrEmpty())
                    {
                        foreach (var kvp in FallColorTrees)
                        {
                            // Enabled if the value is false since all are on by default - to turn off fall colors
                            if (!kvp.Value)
                                yield return Setting.Prefix.NoFallColors_ + kvp.Key;
                        }
                    }
                }
            }
        }

        public static bool SettingChanged = false;

        public static readonly Color BackgroundGrayWithAlpha = new(0.5f, 0.5f, 0.5f, 0.2f);
        public static readonly Color DisabledRowColor = new(0f, 0f, 0f, 0.5f);
        public static readonly Color GreenHighlightColor = new(0f, 1f, 0f, 0.2f);
        public static readonly Color RedHighlightColor = new(1f, 0f, 0f, 0.2f);

        internal static SettingsTab _currentTab = SettingsTab.General;
        internal static readonly List<TabRecord> _tabs = [];
        private struct TabCheckboxGroup(List<CheckboxInfo> checkboxes, float height = -1f)
        {
            public List<CheckboxInfo> Checkboxes = checkboxes;
            public float Height = height;
        }

        public readonly struct UIState(Color color, TextAnchor anchor, GameFont font)
        {
            public Color Color { get; } = color;
            public TextAnchor Anchor { get; } = anchor;
            public GameFont Font { get; } = font;

            public readonly void Restore()
            {
                GUI.color = Color;
                Text.Anchor = Anchor;
                Text.Font = Font;
            }
        }

        public const float ChildIndent = 48f;
        private static readonly Vector2 _resetButtonSize = new(200f, 40f);
        private static readonly Color _activeTabHighlightGreen = new(0.3f, 0.6f, 0.3f, 0.5f);
        public static readonly Color MenuSectionBGBorderColor = new ColorInt(97, 108, 122).ToColor;

        private static readonly Dictionary<SettingsTab, TabCheckboxGroup> _tabCheckboxes = [];
        private static readonly Dictionary<SettingsTab, (Vector2 position, float height)> _tabContentSizeInfo = [];

        private const float ResetButtonLeftMargin = 24f;
        private const float BorderThickness = 3f;

        // Used to avoid escessive Write().  Probably a better way, or not even needed.
        private static double _lastWriteTime = 0d;
        private const double WriteDebounceSeconds = 2.5d;

        private void BuildTabs()
        {
            foreach (SettingsTab tab in GetSettingsTabList())
            {
                if (tab == SettingsTab.AllTabs)
                {
                    continue; // Not a real tab
                }

                if (tab == SettingsTab.Commonality)
                {
                    if (!HasIndustrialModule || StuffCommonality.NullOrEmpty())
                    {
                        continue;
                    }
                }

                if (tab == SettingsTab.Fuel)
                {
                    if (!ShowFuelSettings || FuelTypes.NullOrEmpty())
                    {
                        continue;
                    }
                }

                string tabLabel = $"{tab}";
                _tabs.Add(new TabRecord(tabLabel, () => { _currentTab = tab; }, () => _currentTab == tab)); // () => _currentTab == tab keeps selection in sync!
                UpdateScrollInfoForTab(tab, Vector2.zero, -1f);
                _tabCheckboxes[tab] = new TabCheckboxGroup([]);
            }

            // Remove empty tabs
            _tabs.RemoveAll(tab =>
            {
                // Try to parse the tab label to SettingsTab
                if (Enum.TryParse<SettingsTab>(tab.label, out var settingsTab))
                {
                    if (settingsTab == SettingsTab.Commonality) // Has no checkboxes or not _checkboxInfos checkboxes
                        return false;
                    return !_checkboxInfos.Any(c => c.Tab == settingsTab);
                }
                return true;
            });
        }

        private static List<SettingsTab> GetSettingsTabList()
        {
            return [.. Enum.GetValues(typeof(SettingsTab)).Cast<SettingsTab>()];
        }

        internal void DoSettingsWindowContents(Rect inRect)
        {
            const float resetButtonAreaHeight = 80f;
            const float tabWidth = 160f;

            // Header
            string headerLabel = Translator.TranslateKey(TKey.Type.HeaderLabel, "RestartRequired");
            string leftHeaderLabel = $"v{ModVersion}";
            float headerHeight = headerLabel.GetHeightCached() + BorderThickness;
            Rect headerRect = inRect.TopPartPixels(headerHeight);

            // Split headerRect horizontally
            Rect leftHeaderRect = headerRect.LeftPartPixels(tabWidth);
            Rect rightHeaderRect = headerRect.RightPartPixels(headerRect.width - tabWidth);
            DrawHeader(leftHeaderRect, rightHeaderRect, leftHeaderLabel, headerLabel);

            // Tabs (left side)
            Rect tabRect = new(inRect.x, headerRect.yMax, tabWidth, inRect.height - headerHeight);

            // Content (right side, below header, right of tabs)
            Rect contentRect = new(tabRect.xMax - BorderThickness, headerRect.yMax, (inRect.width - tabWidth) + BorderThickness, inRect.height - headerHeight - resetButtonAreaHeight);

            UITextureBank.DrawBackgroundLogo(contentRect);
            // Reset area (bottom, right of tabs)
            Rect footerRect = new(tabRect.xMax - BorderThickness, contentRect.yMax - BorderThickness, contentRect.width, resetButtonAreaHeight + BorderThickness);

            // Pass rects to draw methods
            DrawTabs(tabRect);
            DrawCurrentTabContent(contentRect);
            DrawFooter(footerRect);

            // Debounced settings write
            if (SettingChanged)
            {
                double now = Time.realtimeSinceStartup;
                if (now - _lastWriteTime > WriteDebounceSeconds)
                {
                    SettingChanged = false;
                    _lastWriteTime = now;
                    WriteSettingsToFile();
                }
            }
        }


        // New header drawing method
        private void DrawHeader(Rect leftHeaderRect, Rect rightHeaderRect, string leftLabel, string headerLabel)
        {
            var originalUIState = new UIState(GUI.color, Text.Anchor, Text.Font);

            // Left label
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = cyan;
            Widgets.Label(leftHeaderRect, leftLabel);

            // Header label
            GUI.color = green;
            Widgets.Label(rightHeaderRect, headerLabel);

            originalUIState.Restore();
        }

        private Rect DrawFooter(Rect footerRect)
        {
            DrawMenuSection(footerRect, clearTex: true);

            Rect tabResetButtonRect = ShowResetOptions(footerRect);

            Rect detectedModulesRect = ShowDetectedModules(footerRect, tabResetButtonRect);

            return footerRect;
        }
        internal void DoResetSettingDialogue(SettingsTab tab)
        {
            if (_checkboxInfos.NullOrEmpty()) // If none for some unknown reason, nothing to reset regardless of the other dictionaries
                return;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                Translator.TranslateKey(TKey.Type.Button, "AreYouSure"),
                delegate
                {
                    bool resetAll = tab == SettingsTab.AllTabs;
                    var checkboxesToReset = resetAll ? _checkboxInfos : _checkboxInfos.Where(c => c.Tab == tab);
                    foreach (var info in checkboxesToReset)
                    {
                        info.Setter?.Invoke(info.DefaultValue);

                        if (info.ExtraControls != null)
                        {
                            foreach (var control in info.ExtraControls)
                            {
                                switch (control)
                                {
                                    case FloatRangeControl frc:
                                        frc.Setter(frc.DefaultValue);
                                        break;
                                    case FloatSliderControl fsc:
                                        fsc.Setter(fsc.DefaultValue);
                                        break;
                                    case ExtraCheckboxControl ecc:
                                        ecc.Setter(ecc.DefaultValue);
                                        break;
                                    case IntRangeControl irc:
                                        irc.Setter(irc.DefaultValue);
                                        break;
                                    case IntAdjusterControl iac:
                                        iac.Setter(iac.DefaultValue);
                                        break;
                                }
                            }
                        }
                    }

                    if (resetAll || tab == SettingsTab.Commonality)
                    {
                        if (!StuffCommonality.NullOrEmpty())
                        {
                            foreach (var kvp in StuffCommonality)
                            {
                                float initial = -1;
                                if (!ShowVEFCommonalitySettings)
                                {
                                    kvp.Value.CoreCommonality = kvp.Value.DefaultCommonality;
                                }
                                else
                                {
                                    kvp.Value.CoreCommonality = initial;
                                    initial = kvp.Value.DefaultCommonality;
                                }
                                kvp.Value.ApparelOffset = initial;
                                kvp.Value.StructureOffset = initial;
                                kvp.Value.WeaponOffset = initial;
                            }
                            CommonalityTab._commonalityBuffers?.Clear(); // Reset text field values
                        }
                    }
                    if (resetAll || tab == SettingsTab.Visuals)
                    {
                        if (!MaterialColors.NullOrEmpty())
                        {
                            foreach (var kvp in MaterialColors)
                            {
                                kvp.Value.DoStuff = true;
                                kvp.Value.DoThing = false;
                                kvp.Value.NewStuffColor = kvp.Value.DefaultStuffColor;
                                kvp.Value.NewThingColor = kvp.Value.DefaultThingColor;
                            }
                            ChangeColorSection._selectedMaterialDefName = null;
                            ChangeColorSection._colorChannelBuffers?.Clear(); // Reset text field values
                        }
                    }
                    if (resetAll || tab == SettingsTab.Categories)
                    {
                        ClearCategoryCache();
                        if (!CategoryLabelCache.NullOrEmpty())
                        {
                            foreach (var labelInfo in CategoryLabelCache.Values)
                            {
                                labelInfo.CurrentCategoryLabel = labelInfo.OriginalCategoryLabel;
                            }
                            CategoryLabelInfo.SetCategoryLabels();
                            Checkboxes._categoryLabelBuffers?.Clear(); // Reset text field values
                        }
                    }

                    SettingChanged = true;
                    Messages.Message(Translator.TranslateKey(TKey.Type.Tab, $"{tab}") + " " + Translator.TranslateKey(TKey.Type.Button, "SettingsReset"), MessageTypeDefOf.PositiveEvent, false);
                    
                    // Really don't think this is needed
                    var openWindow = Find.WindowStack.Windows
                        .OfType<SettingWindows.ResizableWindow>()
                        .FirstOrDefault();
                    Find.WindowStack.TryRemove(openWindow);
                }
            ));
        }

        private Rect ShowResetOptions(Rect footerRect)
        {
            const float resetWindowWidth = 300f;

            // Position button on the left side, centered vertically
            float buttonX = footerRect.x + ResetButtonLeftMargin;
            float buttonY = footerRect.y + (footerRect.height - _resetButtonSize.y) / 2f;
            Rect buttonRect = new(buttonX, buttonY, _resetButtonSize.x, _resetButtonSize.y);
            string type = "Reset";
            string label = Translator.TranslateKey(TKey.Type.Button, type);
            if (Widgets.ButtonText(buttonRect, label))
            {
                if (!Find.WindowStack.IsOpen<SettingWindows.ResizableWindow>())
                {
                    float windowHeight = (_tabs.Count + 1) * 55f + 85f; // Lines + Title * Button Height + Padding (accounting for cancel button)
                    Find.WindowStack.Add(new SettingWindows.ResizableWindow(type, new Vector2(resetWindowWidth, windowHeight), label));
                }
            }
            return buttonRect;
        }

        private Rect ShowDetectedModules(Rect footerRect, Rect resetButtonRect)
        {
            // Position button on the left side, centered vertically, to the right of the reset button
            float buttonX = footerRect.x + resetButtonRect.x + (ResetButtonLeftMargin * 2);
            float buttonY = footerRect.y + (footerRect.height - _resetButtonSize.y) / 2f;
            Rect buttonRect = new(buttonX, buttonY, _resetButtonSize.x, _resetButtonSize.y);
            string type = "Modules";
            string label = Translator.TranslateKey(TKey.Type.Button, type);
            if (Widgets.ButtonText(buttonRect, label))
            {
                if (!Find.WindowStack.IsOpen<SettingWindows.ResizableWindow>())
                {
                    var installedModules = Utils.VersionChecker.NewHarvestVersions;
                    if (installedModules.NullOrEmpty())
                        return buttonRect;

                    float windowWidth = installedModules.Values.Max(v => v.translationKey.Translate().GetWidthCached()) + 150f;
                    float cancelButtonHeight = 55f;
                    float rowHeight = 50f;
                    float windowHeight = Mathf.Max(150f, (installedModules.Count + 1) * rowHeight + cancelButtonHeight);

                    Find.WindowStack.Add(new SettingWindows.ResizableWindow(type, new Vector2(windowWidth, windowHeight), label));
                }
            }
            return buttonRect;
        }

        internal static void DrawMenuSection(Rect rect, Color? bgColor = null, Color? borderColor = null, bool clearTex = false, int thickness = 3)
        {
            var originalUIState = new UIState(GUI.color, Text.Anchor, Text.Font);
            GUI.color = clearTex ? clear : bgColor ?? Widgets.MenuSectionBGFillColor;
            GUI.DrawTexture(rect, clearTex ? BaseContent.ClearTex : BaseContent.WhiteTex);
            GUI.color = borderColor ?? MenuSectionBGBorderColor;
            Widgets.DrawBox(rect, thickness);
            originalUIState.Restore();
        }

        private Rect DrawTabs(Rect tabRect)
        {
            DrawMenuSection(tabRect);

            const float tabButtonHeight = 40f;
            const float pad = GenUI.Pad;
            float tabButtonWidth = tabRect.width - pad;
            float currentTabY = tabRect.y + pad;

            var originalUIState = new UIState(GUI.color, Text.Anchor, Text.Font);

            foreach (TabRecord tab in _tabs)
            {
                Rect tabButtonRect = new(tabRect.x + 5f, currentTabY, tabButtonWidth, tabButtonHeight);

                if (tab.Selected)
                {
                    GUI.color = _activeTabHighlightGreen;
                    GUI.DrawTexture(tabButtonRect, BaseContent.WhiteTex);
                    GUI.color = originalUIState.Color;

                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(tabButtonRect, Translator.TranslateKey(TKey.Type.Tab, tab.label));
                }
                else if (Widgets.ButtonText(tabButtonRect, Translator.TranslateKey(TKey.Type.Tab, tab.label)))
                {
                    tab.clickedAction();
                    SoundDefOf.TabOpen.PlayOneShotOnCamera();
                    ResetScrollPositionForTab(_currentTab);
                }

                currentTabY += tabButtonHeight + 2f;
            }

            originalUIState.Restore();
            return tabRect;
        }

        private static void UpdateScrollInfoForTab(SettingsTab tab, Vector2 position, float height)
        {
            _tabContentSizeInfo[tab] = (position, height);
        }

        private static void ResetScrollPositionForTab(SettingsTab tab)
        {
            var (_, height) = _tabContentSizeInfo[tab];
            _tabContentSizeInfo[tab] = (Vector2.zero, height);
        }

        private (Vector2 position, float height) GetContentSizeInfoForTab(SettingsTab tab)
        {
            if (_tabContentSizeInfo.TryGetValue(tab, out var info))
            {
                return info;
            }
            return (Vector2.zero, -1f);
        }

        private (Rect contentAreaRect, Rect viewRect, Rect innerContentRect) DrawCurrentTabContent(Rect contentRect)
        {
            DrawMenuSection(contentRect, clearTex: true);

            // The rect for the scroll view, contracted by line thickness
            Rect contentAreaRect = contentRect.ContractedBy(BorderThickness);

            float scrollBarWidth = GenUI.ScrollBarWidth;
            float contentWidth = contentAreaRect.width - scrollBarWidth;

            var (position, height) = GetContentSizeInfoForTab(_currentTab);

            // Measurement pass
            if (height < 0)
            {
                Rect tempRect = new(0, 0, contentWidth, 9999f);
                Rect innerTempRect = tempRect.MiddlePart(0.95f, 1.0f);
                innerTempRect.x += scrollBarWidth / 2f;
                innerTempRect.width -= scrollBarWidth - BorderThickness;

                var tempListing = new Listing_Standard();
                tempListing.Begin(innerTempRect);
                DoCheckboxes(tempListing);
                DoCommonalityTab(tempListing);
                DoMaterialColorSection(tempListing);
                height = (Mathf.Ceil(tempListing.CurHeight) / 0.95f) + GenUI.Gap; // In case of run off
                tempListing.End();
            }

            // The rect for the content inside the scroll view
            Rect viewRect = new(0, 0, contentWidth, height);

            Widgets.BeginScrollView(contentAreaRect, ref position, viewRect);

            // The rect for the actual content, inset slightly from viewRect
            Rect innerContentRect = viewRect.MiddlePart(0.95f, 1.0f);

            // Even out space on both sides
            innerContentRect.x += scrollBarWidth / 2f;
            innerContentRect.width -= scrollBarWidth - BorderThickness;

            var settingListing = new Listing_Standard();
            settingListing.Begin(innerContentRect);

            DoCheckboxes(settingListing);
            DoCommonalityTab(settingListing);
            DoMaterialColorSection(settingListing);

            settingListing.End();
            Widgets.EndScrollView();

            UpdateScrollInfoForTab(_currentTab, position, height);

            return (contentAreaRect, viewRect, innerContentRect);
        }

        private float DoCheckboxes(Listing_Standard ls)
        {
            if (!_checkboxInfos.NullOrEmpty())
            {
                Checkboxes.DoCheckboxes(ls, _checkboxInfos);
            }

            return ls.CurHeight;
        }

        private float DoCommonalityTab(Listing_Standard ls)
        {
            if (_currentTab == SettingsTab.Commonality)
            {
                if (!StuffCommonality.NullOrEmpty())
                {
                    CommonalityTab.DoCommonalitySliders(ls, StuffCommonality);
                }
            }
            return ls.CurHeight;
        }

        private float DoMaterialColorSection(Listing_Standard ls)
        {
            if (_currentTab == SettingsTab.Visuals)
            {
                if (!MaterialColors.NullOrEmpty())
                {
                    DrawSectionLabel(ls, "MaterialColorsSection");
                    ls.Gap(GenUI.GapTiny);
                    DrawCustomLabel(ls, Translator.TranslateKey(TKey.Type.SectionLabel, "MaterialColorsSectionSub"), anchor: TextAnchor.MiddleCenter, subLabel: true);
                    ls.Gap(GenUI.Pad);
                    ChangeColorSection.DoColorDropdowns(ls, MaterialColors);
                }
            }
            return ls.CurHeight;
        }

        internal static Rect DrawSectionLabel(Listing_Standard ls, string sectionStartLabel, Color? textColor = null, Color? lineColor = null, bool center = true, bool drawLine = true, bool lineMatchesText = false, float linePct = 0.8f)
        {
            const float sectionLineThickness = 2f;

            ls.Gap(GenUI.Pad);

            string labelText = Translator.TranslateKey(TKey.Type.SectionLabel, sectionStartLabel);
            float labelHeight = Text.CalcHeight(labelText, ls.ColumnWidth);

            float totalHeight = labelHeight + sectionLineThickness;
            Rect blockRect = ls.GetRect(totalHeight);

            var originalUIState = new UIState(GUI.color, Text.Anchor, Text.Font);

            // Top portion for the label
            Rect labelRect = blockRect.TopPartPixels(labelHeight);
            Text.Anchor = center ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            GUI.color = textColor ?? white;
            Widgets.Label(labelRect, labelText);

            if (drawLine)
            {
                float lineWidth = lineMatchesText ? labelText.GetWidthCached() : (blockRect.width * linePct);
                float lineX = blockRect.x + (blockRect.width - lineWidth) / 2f;
                Rect lineRect = new(lineX, labelRect.yMax, lineWidth, sectionLineThickness);
                GUI.color = lineColor ?? MenuSectionBGBorderColor;
                GUI.DrawTexture(lineRect, BaseContent.WhiteTex);
            }

            originalUIState.Restore();

            return blockRect;
        }

        internal static Rect DrawCustomLabel(Listing_Standard listing, string label, bool category = false, bool subLabel = false, TextAnchor anchor = TextAnchor.UpperLeft, Color? color = null, int indentSpaces = 0)
        {
            Rect labelRect;
            if (category)
            {
                labelRect = DrawCustomLabel(listing, label, GameFont.Medium, TextAnchor.MiddleCenter, green, indentSpaces);
            }
            else if (subLabel)
            {
                labelRect = DrawCustomLabel(listing, label, GameFont.Tiny, anchor, gray, indentSpaces);
            }
            else
            {
                labelRect = DrawCustomLabel(listing, label, GameFont.Small, anchor, color ?? white, indentSpaces);
            }
            return labelRect;
        }

        internal static Rect DrawCustomLabel(
            Listing_Standard listing,
            string label,
            GameFont font = GameFont.Small,
            TextAnchor anchor = TextAnchor.UpperLeft,
            Color? color = null,
            int indentSpaces = 0)
        {
            var originalUIState = new UIState(GUI.color, Text.Anchor, Text.Font);

            Text.Font = font;
            Text.Anchor = anchor;
            GUI.color = color ?? white;

            // Get a rect for the label
            float labelHeight = Text.CalcHeight(label, listing.ColumnWidth);
            Rect labelRect = listing.GetRect(labelHeight);

            if (indentSpaces > 0)
            {
                float indent = ChildIndent * indentSpaces;
                labelRect.x += indent;
                labelRect.width -= indent;
            }

            // Draw the label
            Widgets.Label(labelRect, label);

            originalUIState.Restore();

            return labelRect;
        }

        [Conditional("DEBUG")]
        private void VisualizeRect(Rect rect, Color? color = null)
        {
            Color fillColor = color ?? green;
            Widgets.DrawBoxSolid(rect, fillColor);
        }
    }
}