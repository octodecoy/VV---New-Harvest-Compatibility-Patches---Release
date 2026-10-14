namespace NewHarvestPatches
{
    /// <summary>
    /// Per-stuff commonality settings.
    /// CoreCommonality is live in BOTH modes - it is the value written to <c>stuffProps.commonality</c>,
    /// so every reader that does not go through VEF's per-category patch (trader stock, quest/reward
    /// generation, VEF's own KCSG structure generator, <c>ThingStuffPair.AllWith</c>) always sees it.
    /// Standard mode has nothing else. VEF mode ALSO has the three category offsets, which
    /// MaterialCommonalityChanger turns into StuffExtension factors (figure / CoreCommonality) that scale
    /// CoreCommonality back out for the three patched paths (pawn weapon/apparel generation,
    /// GenStuff.TryRandomStuffByCommonalityFor, GenStuff.RandomStuffInexpensiveFor) - independent of
    /// CoreCommonality's own value, not derived from it.
    /// The three category fields hold <see cref="Unset"/> (-1) outside VEF mode so a mode switch can
    /// detect "never configured for this mode" and seed from the def's default commonality. Slider range
    /// is <see cref="Min"/>..<see cref="Max"/>, so -1 is always safely outside the valid range - both
    /// modes stay scribed, so installing or removing VEF never destroys the settings belonging to the
    /// other mode.
    /// </summary>
    public class CommonalityInfo : IExposable
    {
        public const float Unset = -1f;

        /// <summary>
        /// Slider bounds, and the single source of truth for them: the UI clamps typed input to this range
        /// (<c>ModSettings.Tab_Commonality</c>) and both apply paths clamp again on the way out, since a
        /// scribed value can reach the applier without ever passing through the UI.
        /// </summary>
        public const float Min = 0f;
        public const float Max = 100f;

        // Derived from the def each load (not scribed): label for UI, default for change detection.
        public string DefLabel = "";
        public float DefaultCommonality = 0f;

        // Scribed - field names/order unchanged, so existing saves stay valid. The three "Offset" names are
        // historical and kept only because they are the scribe keys: each holds an absolute commonality for
        // its category, not a delta. MaterialCommonalityChanger turns them into VEF factors relative to
        // CoreCommonality. CoreCommonality itself is live in both modes - see the class summary.
        public float CoreCommonality = Unset;
        public float StructureOffset = Unset;
        public float WeaponOffset = Unset;
        public float ApparelOffset = Unset;

        public void ExposeData()
        {
            Scribe_Values.Look(ref CoreCommonality, nameof(CoreCommonality), Unset);
            Scribe_Values.Look(ref StructureOffset, nameof(StructureOffset), Unset);
            Scribe_Values.Look(ref WeaponOffset, nameof(WeaponOffset), Unset);
            Scribe_Values.Look(ref ApparelOffset, nameof(ApparelOffset), Unset);
        }

        /// <summary>True when the user has moved any live-mode value off the def default.</summary>
        public bool DiffersFromDefault =>
            ShowVEFCommonalitySettings
                ? CoreCommonality != DefaultCommonality
                    || StructureOffset != DefaultCommonality
                    || WeaponOffset != DefaultCommonality
                    || ApparelOffset != DefaultCommonality
                : CoreCommonality != DefaultCommonality;

        /// <summary>
        /// True when every field the ACTIVE mode reads holds a real value rather than <see cref="Unset"/>.
        /// CoreCommonality is required in both modes - VEF mode ALSO requires the three category fields.
        /// <see cref="BuildCommonalityStats"/> only refreshes entries still present in
        /// <c>DefUtility.ThingDefs.IndustrialResourceDefs</c>, so an entry whose def still loads but has
        /// dropped out of that set keeps its Unset (-1) fields alongside a DefaultCommonality of 0 - and
        /// <see cref="DiffersFromDefault"/> then reports true. The appliers gate on this so that -1 can
        /// never be written into <c>stuffProps.commonality</c>.
        /// </summary>
        public bool IsConfiguredForCurrentMode =>
            ShowVEFCommonalitySettings
                ? CoreCommonality != Unset && StructureOffset != Unset && WeaponOffset != Unset && ApparelOffset != Unset
                : CoreCommonality != Unset;

        /// <summary>Clamps a raw commonality into the <see cref="Min"/>..<see cref="Max"/> slider range.</summary>
        public static float Clamp(float value) => Mathf.Clamp(value, Min, Max);

        /// <summary>
        /// Set the currently-active mode's field(s) to the given commonality and park the inactive
        /// mode's field(s) at Unset. Used for first-time seeding, mode switches, and reset-to-default.
        /// CoreCommonality is set in BOTH branches - it is live in both modes - only the three category
        /// fields are mode-specific.
        /// </summary>
        internal void SeedForCurrentMode(float commonality)
        {
            CoreCommonality = commonality;
            if (ShowVEFCommonalitySettings)
                StructureOffset = WeaponOffset = ApparelOffset = commonality;
            else
                StructureOffset = WeaponOffset = ApparelOffset = Unset;
        }

        /// <summary>
        /// Reconciles the commonality dictionary with the defs that loaded: drops dead keys, refreshes the
        /// unscribed label/default pair, and seeds only entries whose ACTIVE mode is still Unset. That
        /// last condition is what makes toggling VEF non-destructive - switching modes seeds the newly
        /// active fields from the def while the other mode's stored values sit untouched.
        /// </summary>
        internal static void BuildCommonalityStats(ref Dictionary<string, CommonalityInfo> commonalityInfo)
        {
            if (!IsSettingAvailable(nameof(Settings.StuffCommonality)))
                return;

            commonalityInfo ??= [];
            foreach (var kvp in commonalityInfo.ToList())
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || DefDatabase<ThingDef>.GetNamedSilentFail(kvp.Key) is null)
                    commonalityInfo.Remove(kvp.Key);  
            }

            var industrialDefs = DefUtility.ThingDefs.IndustrialResourceDefs;
            if (industrialDefs.NullOrEmpty())
            {
                LogMessage(() => "Could not get defs for commonality dictionary.", LogMessageType.Error);
                commonalityInfo.Clear();
                return;
            }

            bool vef = ShowVEFCommonalitySettings;

            foreach (var kvp in industrialDefs)
            {
                var def = kvp.Key;
                if (def.stuffProps?.commonality is not float commonality)
                    continue;

                if (!commonalityInfo.TryGetValue(def.defName, out var info))
                {
                    info = new CommonalityInfo();
                    commonalityInfo[def.defName] = info;
                }

                info.DefLabel = def.label;
                info.DefaultCommonality = commonality;

                // Seed only if the active mode's field(s) were never configured (still Unset):
                // brand-new entry, or one carried over from the other mode.
                bool activeModeUnset = vef ? info.ApparelOffset == Unset : info.CoreCommonality == Unset;
                if (activeModeUnset)
                {
                    info.SeedForCurrentMode(commonality);
                }
                else if (vef && info.CoreCommonality == Unset)
                {
                    // Migration: a VEF-mode entry saved before CoreCommonality became live in VEF mode,
                    // when the applier derived the base as the mean of the three category figures instead
                    // of reading a stored value. Seed from the def's true default rather than replaying
                    // that old mean, so trader/quest/KCSG rates return to vanilla instead of silently
                    // adopting a number this mod itself used to compute.
                    info.CoreCommonality = commonality;
                }
            }
        }
    }
}
