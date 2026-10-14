namespace NewHarvestPatches;

public partial class NewHarvestPatchesModSettings : ModSettings
{
    public readonly FloatRange NourishedHediffSeverityRange = new(0.01f, 24f);
    private const float BehaviorAdjusterRowHeight = 24f;
    private const float BehaviorRangeRowHeight = 32f;
    private const float BehaviorSliderRowHeight = 22f;
    private const float BehaviorButtonWidth = 80f;
    private const float BehaviorButtonSpacing = 2f;
    private static float s_nourishedCardHeight = -1f;
    private static float s_truffleCardHeight = -1f;

    private void DoBehaviorsTab(Listing_Standard listing)
    {
        DrawNourishedBehaviorCard(listing);
        listing.Gap(GenUI.GapLabel);
        DrawTruffleBehaviorCard(listing);
    }

    /// <summary>
    /// Card 1: nourished hediff from animal food (requires Forage module + Harmony, per the toggle's
    /// [RequiresMod] - the whole card is gated on that one setting's availability because the card owns
    /// the header and rects, which the individual rows cannot suppress).
    /// Layout: header (icon + title) -> divider -> toggle row -> severity adjuster -> duration range.
    /// Both value rows pass restartRequired: false purely to suppress a second restart marker - a restart IS
    /// required, and the card's own title already carries the marker.
    /// </summary>
    private void DrawNourishedBehaviorCard(Listing_Standard listing)
    {
        if (!IsSettingAvailable(nameof(AddNourishedHediffFromAnimalFood)))
            return;

        Rect cardRect = listing.GetRect(GetNourishedCardHeight());
        float curY = cardRect.y;

        string title = GetSettingLabel(SettingLabelKind.Checkbox, nameof(AddNourishedHediffFromAnimalFood), withRestartMarker: true);
        curY = DrawCardHeader(cardRect, curY, UITextureCache.IconFolder + "HeartIcon", title);

        Rect toggleRowRect = new(cardRect.x, curY, cardRect.width, CardRowHeight);
        curY += CardRowHeight + CardRowGap;

        SettingCheckbox(
            toggleRowRect,
            nameof(AddNourishedHediffFromAnimalFood),
            ref AddNourishedHediffFromAnimalFood,
            v => AddNourishedHediffFromAnimalFood = v,
            labelOverride: GetSettingLabel(SettingLabelKind.Raw, "General_Enabled"),
            showIcon: false,
            placeCheckboxNearText: true);

        bool enabled = AddNourishedHediffFromAnimalFood;
        Rect indented = IndentedRect(cardRect);
    
        // Restart IS required for these, but the section title already indicates it.
        Rect severityRect = new(indented.x, curY, indented.width, GetAdjusterBlockHeight());
        SettingFloatAdjuster(
            severityRect,
            "NourishedHediffSeverity",
            currentValue: NourishedHediffSeverity,
            defaultValue: 0.05f,
            min: NourishedHediffSeverityRange.min,
            max: NourishedHediffSeverityRange.max,
            countChange: 0.01f,
            enabled: enabled,
            setter: v => NourishedHediffSeverity = v, 
            restartRequired: false);
        curY += GetAdjusterBlockHeight() + CardRowGap;

        Rect durationRect = new(indented.x, curY, indented.width, GetRangeBlockHeight());
        SettingIntRange(
            durationRect,
            "NourishedHediffDuration",
            currentValue: NourishedHediffDuration,
            defaultValue: new IntRange(15000, 60000),
            min: GenDate.TicksPerHour,
            max: GenDate.TicksPerDay * 7,
            enabled: enabled,
            setter: v => NourishedHediffDuration = v,
            restartRequired: false);
    }

    /// <summary>
    /// Card 2: truffle digging behavior (requires Mushrooms module).
    /// Layout: header -> divider -> toggle row -> tick adjuster -> chance range -> chance reduction slider
    /// -> amount range -> two plain checkboxes. Rows write into the nested
    /// <see cref="TruffleDiggingSettings"/> object, which is why they use the setter-only helpers.
    /// </summary>
    private void DrawTruffleBehaviorCard(Listing_Standard listing)
    {
        if (!IsSettingAvailable(nameof(AddTruffleDiggingBehavior)))
            return;

        Rect cardRect = listing.GetRect(GetTruffleCardHeight());
        float curY = cardRect.y;

        string title = GetSettingLabel(SettingLabelKind.Checkbox, nameof(AddTruffleDiggingBehavior));
        curY = DrawCardHeader(cardRect, curY, UITextureCache.IconFolder + "PigIcon", title);

        Rect toggleRowRect = new(cardRect.x, curY, cardRect.width, CardRowHeight);
        curY += CardRowHeight + CardRowGap;

        SettingCheckbox(
            toggleRowRect,
            nameof(AddTruffleDiggingBehavior),
            ref AddTruffleDiggingBehavior,
            v => AddTruffleDiggingBehavior = v,
            labelOverride: GetSettingLabel(SettingLabelKind.Raw, "General_Enabled", withRestartMarker: true),
            showIcon: false,
            placeCheckboxNearText: true);

        bool enabled = AddTruffleDiggingBehavior;
        Rect indented = IndentedRect(cardRect);

        // Restart IS required for these, but the section title already indicates it.
        Rect ticksRect = new(indented.x, curY, indented.width, GetAdjusterBlockHeight());
        SettingIntAdjuster(
            ticksRect,
            "TicksBetweenTruffleDigAttempts",
            currentValue: TruffleSettings.TicksBetweenDigAttempts,
            defaultValue: TruffleDiggingSettings.Defaults.TicksBetweenDigAttempts,
            min: GenDate.TicksPerHour,
            max: GenDate.TicksPerHour * 1000,
            countChange: GenDate.TicksPerHour,
            enabled: enabled,
            setter: v => TruffleSettings.TicksBetweenDigAttempts = v,
            restartRequired: false);

        curY += GetAdjusterBlockHeight() + CardRowGap;

        Rect chanceRangeRect = new(indented.x, curY, indented.width, GetRangeBlockHeight());
        SettingFloatRangePercent(
            chanceRangeRect,
            "TruffleDiggingChanceRange",
            currentValue: TruffleSettings.DiggingChanceRange,
            defaultValue: TruffleDiggingSettings.Defaults.DiggingChanceRange,
            min: 0f,
            max: 1f,
            roundTo: 0.01f,
            enabled: enabled,
            setter: v => TruffleSettings.DiggingChanceRange = v,
            restartRequired: false);

        curY += GetRangeBlockHeight() + CardRowGap;

        Rect reductionRect = new(indented.x, curY, indented.width, GetSliderBlockHeight());
        SettingFloatSliderPercent(
            reductionRect,
            "TruffleDiggingChanceReduction",
            currentValue: TruffleSettings.DiggingChanceReduction,
            defaultValue: TruffleDiggingSettings.Defaults.DiggingChanceReduction,
            min: 0f,
            max: 1f,
            roundTo: 0.01f,
            enabled: enabled,
            setter: v => TruffleSettings.DiggingChanceReduction = v,
            restartRequired: false);

        curY += GetSliderBlockHeight() + CardRowGap;

        Rect amountRect = new(indented.x, curY, indented.width, GetRangeBlockHeight());
        SettingIntRange(
            amountRect,
            "TruffleAmountRange",
            currentValue: TruffleSettings.TrufflesPerDigRange,
            defaultValue: TruffleDiggingSettings.Defaults.TrufflesPerDigRange,
            min: 1,
            max: 10,
            enabled: enabled,
            setter: v => TruffleSettings.TrufflesPerDigRange = v,
            restartRequired: false);

        curY += GetRangeBlockHeight() + CardRowGap;

        Rect spawnsForbiddenRect = new(indented.x, curY, indented.width, CardRowHeight);
        SettingCheckboxBlock(
            spawnsForbiddenRect,
            "TruffleSpawnsForbidden",
            currentValue: TruffleSettings.SpawnsForbidden,
            defaultValue: TruffleDiggingSettings.Defaults.SpawnsForbidden,
            enabled: enabled,
            setter: v => TruffleSettings.SpawnsForbidden = v);

        curY += CardRowHeight + CardRowGap;

        Rect gizmoRequiresTrainingRect = new(indented.x, curY, indented.width, CardRowHeight);
        SettingCheckboxBlock(
            gizmoRequiresTrainingRect,
            "TruffleGizmoRequiresTraining",
            currentValue: TruffleSettings.GizmoRequiresTraining,
            defaultValue: TruffleDiggingSettings.Defaults.GizmoRequiresTraining,
            enabled: enabled,
            setter: v => TruffleSettings.GizmoRequiresTraining = v);
    }

    // Block heights are label + gap + widget row. Not constants: Text.LineHeight depends on the active
    // font/language, so these are recomputed rather than baked in.
    private static float GetAdjusterBlockHeight() => Text.LineHeight + GenUI.GapTiny + BehaviorAdjusterRowHeight;

    private static float GetRangeBlockHeight() => Text.LineHeight + GenUI.GapTiny + BehaviorRangeRowHeight;

    private static float GetSliderBlockHeight() => Text.LineHeight + GenUI.GapTiny + BehaviorSliderRowHeight;

    private static float GetNourishedCardHeight()
    {
        if (s_nourishedCardHeight >= 0f)
            return s_nourishedCardHeight;

        float inner = CardHeaderHeight + CardDividerGap
            + CardRowHeight + CardRowGap
            + GetAdjusterBlockHeight() + CardRowGap
            + GetRangeBlockHeight();

        s_nourishedCardHeight = inner;
        return s_nourishedCardHeight;
    }

    private static float GetTruffleCardHeight()
    {
        if (s_truffleCardHeight >= 0f)
            return s_truffleCardHeight;

        float inner = CardHeaderHeight + CardDividerGap
            + CardRowHeight + CardRowGap
            + GetAdjusterBlockHeight() + CardRowGap
            + GetRangeBlockHeight() + CardRowGap
            + GetSliderBlockHeight() + CardRowGap
            + GetRangeBlockHeight() + CardRowGap
            + CardRowHeight + CardRowGap
            + CardRowHeight;

        s_truffleCardHeight = inner;
        return s_truffleCardHeight;
    }
}
