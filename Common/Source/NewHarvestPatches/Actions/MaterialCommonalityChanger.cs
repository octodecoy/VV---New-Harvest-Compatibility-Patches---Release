using System.Collections;

namespace NewHarvestPatches;

/// <summary>
/// Controls how often each stuff is picked for generated items and buildings. Two mutually exclusive
/// implementations of the same user-facing setting: the standard path writes
/// <c>stuffProps.commonality</c> directly from <see cref="CommonalityInfo.CoreCommonality"/>, while the
/// VEF path writes that SAME field there as an independent base and attaches a StuffExtension whose
/// factors (category figure / base) scale it back out to each category's own figure for the three paths
/// VEF patches - readers VEF does not patch (trader stock, quest/reward generation, VEF's own KCSG
/// structure generator) see the base, unaffected by the three category sliders. Which path runs is
/// fixed for the whole session: <see cref="ModChecker.ShowVEFCommonalitySettings"/> is a static
/// readonly flag and the active mod list cannot change without a restart. Both modes stay scribed (see
/// <see cref="CommonalityInfo"/>), so installing or removing VEF never destroys the other mode's values -
/// the mod simply applies the other path on the next launch.
/// Every VEF type is confined to method bodies - see <see cref="GetVEFCache"/> for why.
/// </summary>
internal static class MaterialCommonalityChanger
{
    /// <summary>
    /// Change commonality of stuff.  If VEF is installed, we insert our defs that will be using VEF.Things.StuffExtension into VEF's cache.
    /// Live-appliable: every call resets whatever this mod previously applied to defs that have dropped
    /// out of the enabled set back to their true default, then walks the enabled set - a per-def value can
    /// change (slider drag) without the enabled-defName set changing, so the whole set is walked, not just
    /// the difference. Within that walk each path skips defs whose LIVE state already matches what it would
    /// write (see <see cref="ChangeStandard"/> and <see cref="IsVEFStateCurrent"/>); a def something else
    /// overwrote therefore still gets repaired, while an untouched one costs nothing.
    /// </summary>
    public static void TryChangeMaterialCommonality()
    {
        ActionRunner.Run(nameof(MaterialCommonalityChanger), nameof(TryChangeMaterialCommonality), () =>
        {
            if (ShowVEFCommonalitySettings)
                ChangeVEF();
            else
                ChangeStandard();
        });
    }

    /// <summary>The stuff defs the user has switched commonality control on for; empty when the commonality setting is unavailable.</summary>
    private static ThingDef[] GetEnabledDefs()
    {
        if (!IsSettingAvailable(nameof(Settings.StuffCommonality)))
            return [];

        var enabledDefNames = ExtractNamesFromEnabledSettings(Setting.Prefix.SetCommonality_);

        return !enabledDefNames.NullOrEmpty() ? [.. DefUtility.GetDefsOfTypeByDefNames<ThingDef>(order: false, defNames: [.. enabledDefNames])] : [];
    }

    /// <summary>
    /// Joins the enabled defs to their stored commonality values, shared by both paths so they always
    /// agree on what "currently desired" means.
    /// </summary>
    /// <returns>
    /// One pair per enabled def that still has a settings entry holding real values for the active mode.
    /// Defs without an entry, or whose entry is still <see cref="CommonalityInfo.Unset"/> for this mode,
    /// are dropped - which also takes them out of the desired set, so anything already applied to them is
    /// RESET rather than overwritten with -1.
    /// </returns>
    private static List<(ThingDef Def, CommonalityInfo Info)> GetEnabledInfoPairs()
    {
        List<(ThingDef, CommonalityInfo)> pairs = [];

        var enabledDefs = GetEnabledDefs();
        if (enabledDefs.NullOrEmpty())
            return pairs;

        var stuffDefDictionary = Settings.StuffCommonality;
        if (stuffDefDictionary.NullOrEmpty())
            return pairs;

        foreach (var def in enabledDefs)
        {
            if (stuffDefDictionary.TryGetValue(def.defName, out var info) && info.IsConfiguredForCurrentMode)
                pairs.Add((def, info));
        }

        return pairs;
    }

    // ---------------------------------------------------------------- standard path

    // Original (true, pre-mod) commonality per defName this mod has touched via the standard
    // path this session - kept separately from CommonalityInfo.DefaultCommonality so reset never
    // depends on that dictionary entry still existing.
    private static readonly Dictionary<string, float> s_standardBaseline = [];

    /// <summary>
    /// Non-VEF path: writes <c>stuffProps.commonality</c> directly, capturing each def's live value the
    /// first time it is seen so the true pre-mod default survives later re-applies. The stored value is
    /// clamped on the way out (see <see cref="CommonalityInfo.Clamp"/>) - it is scribed, so it reaches
    /// here without necessarily having passed through the settings UI's own clamp.
    /// The write is skipped when the value the def already carries equals the one about to be written -
    /// a LIVE comparison, so a commonality something else overwrote is still corrected.
    /// </summary>
    private static void ChangeStandard()
    {
        var pairs = GetEnabledInfoPairs();
        var desiredDefNames = pairs.Select(p => p.Def.defName).ToHashSet();

        // Reset defs no longer in the enabled set back to their true default.
        foreach (var defName in s_standardBaseline.Keys.Except(desiredDefNames).ToList())
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def?.stuffProps != null)
            {
                var originalCommonality = s_standardBaseline[defName];
                def.stuffProps.commonality = originalCommonality;
                LogMessage(() => $"Reset commonality for {defName} -> {originalCommonality}");
            }
            s_standardBaseline.Remove(defName);
        }

        if (pairs.NullOrEmpty())
            return;

        foreach (var (def, info) in pairs)
        {
            if (def.stuffProps == null)
                continue;

            // capture the live value, which is still untouched on first sight of this def.
            if (!s_standardBaseline.ContainsKey(def.defName))
                s_standardBaseline[def.defName] = def.stuffProps.commonality;

            // Clamped, not trusted: the value is scribed, so a hand-edited config - or an entry the
            // settings pass no longer refreshes - can carry a figure outside the slider's range.
            var newCommonality = CommonalityInfo.Clamp(info.CoreCommonality);

            var oldCommonality = def.stuffProps.commonality;
            if (oldCommonality == newCommonality)
                continue; // No change

            def.stuffProps.commonality = newCommonality;
            LogMessage(() =>
                $"Set commonality for {def.defName} -> " +
                $"Default Commonality={info.DefaultCommonality}, " +
                $"Old Commonality={oldCommonality}, " +
                $"New Commonality={newCommonality}");
        }
    }

    // ---------------------------------------------------------------- VEF path

    /// <summary>
    /// Everything needed to put one def back exactly as it was before the VEF path touched it.
    /// VEF types kept out of every field signature so this record - and its owning class - load
    /// without Vanilla Expanded Framework present (class-load resolves field types eagerly, and a
    /// missing-assembly field type throws TypeLoadException even on the non-VEF path). StuffExtension
    /// is stored as its base-game base type (DefModExtension); the cache entry is a plain object,
    /// cast back to the VEF type only inside VEF-path method bodies (which JIT lazily).
    /// </summary>
    private class VEFCommonalityRecord
    {
        public DefModExtension AddedExtension;
        public List<DefModExtension> RemovedOriginalExtensions = [];
        public bool HadCacheEntry;
        public object OriginalCacheEntry;
        public float OriginalCommonality;
    }

    private static readonly Dictionary<string, VEFCommonalityRecord> s_vefRecords = [];

    private const string VEFPatchTypeName = "VanillaExpandedFramework_ThingStuffPair_Commonality_Patch";
    private const string VEFPatchTypeFullName = "VEF.Things." + VEFPatchTypeName;
    private const string VEFCacheFieldName = "cachedExtension";

    // Reflection handles for VEF's patch type and its cache FIELD, resolved once per session. Neither can
    // change while the process lives - the loaded assembly set is fixed - and AccessTools.TypeByName is
    // expensive enough that repeating it per settings-window close showed up as this mod's single slowest
    // action in profiling (see GetVEFCache for why). The field's VALUE is deliberately NOT cached: VEF can
    // replace the dictionary instance, so it is re-read on every call.
    // Type/FieldInfo are BCL types, so these fields carry no VEF type and the class still loads without
    // Vanilla Expanded Framework present (see VEFCommonalityRecord for the rule).
    private static bool s_vefReflectionResolved;
    private static Type s_vefPatchType;
    private static FieldInfo s_vefCacheField;

    /// <summary>
    /// Resolves VEF's private static cachedExtension dict via reflection. Null (and logged) if VEF's
    /// internals changed shape - the warning still fires on EVERY call, but the lookup behind it runs
    /// only once (see <see cref="s_vefReflectionResolved"/>).
    /// </summary>
    /// <param name="patchType">
    /// VEF's patch type, or null when it could not be resolved. Handed back so
    /// <see cref="IsVEFCommonalityPatchLive"/> can match against it without resolving the type twice.
    /// </param>
    // Returns the non-generic IDictionary base type, NOT Dictionary&lt;ThingDef, VEF.Things.StuffExtension&gt;:
    // RimWorld's debug menu reflects over every method in the assembly (GenAttribute.HasAttribute), which
    // forces resolution of each method's SIGNATURE types even when the method is never called. A VEF type
    // in the return/parameter would throw TypeLoadException on that scan when VEF is absent. VEF types are
    // therefore confined to method BODIES (JIT-lazy, only compiled on the VEF path). System.Type is a BCL
    // type, so the out parameter is safe.
    private static IDictionary GetVEFCache(out Type patchType)
    {
        if (!s_vefReflectionResolved)
        {
            s_vefReflectionResolved = true;

            // Full name FIRST, simple name only as a fallback. AccessTools.TypeByName tries
            // Type.GetType, then assembly.GetType per loaded assembly - both of which need the FULL
            // name - and only then falls through to materializing AllTypes() into an array and running
            // two full LINQ scans over it. A simple name always takes that bottom path, which with a
            // large mod list means walking every type in every loaded assembly twice, per call.
            // The fallback keeps working if VEF ever moves the class to another namespace; it costs the
            // slow path once, not once per settings-window close.
            s_vefPatchType = HarmonyLib.AccessTools.TypeByName(VEFPatchTypeFullName)
                ?? HarmonyLib.AccessTools.TypeByName(VEFPatchTypeName);

            s_vefCacheField = s_vefPatchType?.GetField(VEFCacheFieldName, BindingFlags.Static | BindingFlags.NonPublic);
        }

        patchType = s_vefPatchType;
        if (patchType == null)
        {
            LogMessage(() => $"Patch type [{VEFPatchTypeName}] not found. Stuff commonality changes aborted.", LogMessageType.Warning);
            return null;
        }

        if (s_vefCacheField == null)
        {
            LogMessage(() => $"Field [{VEFCacheFieldName}] not found in patch type [{VEFPatchTypeName}]. Stuff commonality changes aborted.", LogMessageType.Warning);
            return null;
        }

        // Value re-read every call, never cached: VEF is free to replace the dictionary instance.
        var cacheObj = s_vefCacheField.GetValue(null);
        if (cacheObj is not Dictionary<ThingDef, VEF.Things.StuffExtension> cache)
        {
            if (cacheObj == null)
                LogMessage(() => $"Cache field [{VEFCacheFieldName}] in patch type [{VEFPatchTypeName}] is null. Stuff commonality changes aborted.", LogMessageType.Warning);
            else
                LogMessage(() => $"Cache field [{VEFCacheFieldName}] in patch type [{VEFPatchTypeName}] is not a Dictionary<ThingDef, VEF.Things.StuffExtension>. Stuff commonality changes aborted.", LogMessageType.Warning);
            return null;
        }

        return cache;
    }

    /// <summary>
    /// True only when VEF's patch is actually attached to <c>ThingStuffPair.Commonality</c>. The patch type
    /// and its cache field resolving is NOT proof the patch applied - VEF's own Harmony pass can fail, and
    /// another mod can unpatch it. That distinction matters because the StuffExtension <see cref="ChangeVEF"/>
    /// attaches is what separates the three per-category figures: with the patch dead nothing reads it, and
    /// all three would silently sit at whatever CoreCommonality alone provides instead of their own figure.
    /// Nothing is applied in that case - see <see cref="ChangeVEF"/>, which resets instead.
    /// Re-evaluated per call rather than cached, so a patch that lands after this mod's boot pass is picked
    /// up on the next settings-window close.
    /// </summary>
    /// <param name="patchType">VEF's patch type as resolved by <see cref="GetVEFCache"/>.</param>
    // Harmony types stay in the method BODY for the same reason VEF types do (see GetVEFCache). Safe here
    // because the whole VEF path only runs while VEF is installed, and VEF itself requires Harmony.
    private static bool IsVEFCommonalityPatchLive(Type patchType)
    {
        if (patchType == null)
            return false;

        var target = HarmonyLib.AccessTools.PropertyGetter(typeof(ThingStuffPair), nameof(ThingStuffPair.Commonality));
        if (target == null)
        {
            LogMessage(() => $"{nameof(ThingStuffPair)}.{nameof(ThingStuffPair.Commonality)} getter not found - cannot confirm VEF's patch. Stuff commonality changes aborted.", LogMessageType.Warning);
            return false;
        }

        var patches = HarmonyLib.Harmony.GetPatchInfo(target);
        if (patches != null)
        {
            // Matched on type IDENTITY, not name: the point is that THIS patch class is what is running.
            foreach (var patch in patches.Prefixes.Concat(patches.Postfixes).Concat(patches.Transpilers))
            {
                if (patch.PatchMethod?.DeclaringType == patchType)
                    return true;
            }
        }

        LogMessage(() => $"Patch type [{VEFPatchTypeName}] is present but is not patching {nameof(ThingStuffPair)}.{nameof(ThingStuffPair.Commonality)}. Stuff commonality changes aborted.", LogMessageType.Warning);
        return false;
    }

    /// <summary>
    /// VEF path: writes <see cref="CommonalityInfo.CoreCommonality"/> to <c>stuffProps.commonality</c> as an
    /// INDEPENDENT base - not derived from the three category figures - and gives the def a StuffExtension
    /// whose per-category FACTORS (figure / base) scale that base back out to each category's own figure for
    /// the three paths VEF patches. The extension is also written into VEF's own lookup cache - VEF populates
    /// that cache once and never rechecks the def, so an extension added without the cache write would be
    /// ignored for the rest of the session (and VEF caches a null result too, so the write is required, not
    /// merely faster).
    /// Factors rather than offsets, because VEF MULTIPLIES whatever value it was handed: inside
    /// <c>ThingStuffPair.Commonality</c> that value already carries the pair's commonalityMultiplier, the
    /// thing's own generateCommonality and the derp-weapon/apparel penalty, so scaling by figure/base
    /// substitutes the user's number for the stuff's commonality and leaves the rest of vanilla's formula
    /// intact - whereas an additive offset would be scaled along with the base and no longer land on the
    /// user's figure.
    /// CoreCommonality being independent (not the mean of the three) is what lets "common as a weapon,
    /// never as anything else" be expressed at all: readers VEF does NOT patch - trader stock
    /// (<c>StockGeneratorUtility</c>), quest and reward generation (<c>ThingSetMakerUtility</c>, which skips
    /// any stuff whose commonality is not above 0), VEF's own KCSG structure generator (which reads raw
    /// commonality, not the structure factor) and the average in <c>ThingStuffPair.AllWith</c> - all see
    /// CoreCommonality alone, unaffected by how the three category sliders are split.
    /// Figures are clamped on the way out (see <see cref="CommonalityInfo.Clamp"/>) - they are scribed, so
    /// they reach here without necessarily having passed through the settings UI's own clamp. A base of 0 is
    /// reachable independently of the three figures now, and is a deliberate "off everywhere" - VEF
    /// multiplies by the factor, so 0 times any factor is still 0. The factors are left null rather than
    /// computed in that case purely to avoid a division by zero; VEF treats a null factor as "use the base
    /// unscaled", which base 0 makes moot either way.
    /// Applies only while VEF's patch is confirmed live (<see cref="IsVEFCommonalityPatchLive"/>); when it
    /// is not, every def this mod touched is reset instead, since an unread extension would leave all three
    /// categories following CoreCommonality alone instead of their own figure.
    /// Skips a def only when the LIVE def state already matches what this pass would write - never when
    /// merely the stored settings are unchanged. The stored values record what this mod INTENDED, not what
    /// is currently on the def, so a stored-value diff could not repair a commonality, extension or cache
    /// entry that something else overwrote since the last pass; a live diff can, because any such overwrite
    /// fails one of <see cref="IsVEFStateCurrent"/>'s four checks and the def is re-applied in full.
    /// </summary>
    private static void ChangeVEF()
    {
        // Can safely use Harmony methods here since VEF requires it.
        var cacheObj = GetVEFCache(out var patchType);
        bool patchLive = cacheObj != null && IsVEFCommonalityPatchLive(patchType);

        var pairs = GetEnabledInfoPairs();
        var desiredDefNames = pairs.Select(p => p.Def.defName).ToHashSet();

        // Reset defs no longer in the enabled set - or every def this mod touched when the patch is not
        // live, since leaving those zeroed would silently remove them from stuff selection.
        var defNamesToReset = patchLive ? s_vefRecords.Keys.Except(desiredDefNames) : s_vefRecords.Keys;
        foreach (var defName in defNamesToReset.ToList())
            ResetVEFForDef(cacheObj, defName);

        if (!patchLive || pairs.NullOrEmpty())
            return;

        // Concrete VEF-typed cast lives in the method body (JIT-lazy) - this method only runs on the VEF path.
        var cache = (Dictionary<ThingDef, VEF.Things.StuffExtension>)cacheObj;

        foreach (var (def, info) in pairs)
        {
            if (def.stuffProps == null)
                continue;

            // Clamped, not trusted: the figures are scribed, so a hand-edited config - or an entry the
            // settings pass no longer refreshes - can carry a figure outside the slider's range.
            float baseCommonality = CommonalityInfo.Clamp(info.CoreCommonality);
            float structureCommonality = CommonalityInfo.Clamp(info.StructureOffset);
            float weaponCommonality = CommonalityInfo.Clamp(info.WeaponOffset);
            float apparelCommonality = CommonalityInfo.Clamp(info.ApparelOffset);

            // Scale the base back out to each category's own figure. Left null when the base is 0 - not
            // "all three figures are 0" anymore, since the base is independent - purely to avoid dividing
            // by zero. VEF treats a null factor as no scaling, and base 0 makes that moot anyway: VEF
            // multiplies by the factor, so 0 times anything is still 0.
            float? structureFactor = null;
            float? weaponFactor = null;
            float? apparelFactor = null;
            if (baseCommonality > 0f)
            {
                structureFactor = structureCommonality / baseCommonality;
                weaponFactor = weaponCommonality / baseCommonality;
                apparelFactor = apparelCommonality / baseCommonality;
            }

            s_vefRecords.TryGetValue(def.defName, out var existingRecord);

            // Nothing to do when the def already carries exactly this state - the common case, since the
            // window closing is what triggers this pass whether or not this particular def was touched.
            // Checked against the DEF, not against the stored settings, so an overwrite by another mod
            // still gets repaired below (see the method summary).
            if (existingRecord != null
                && IsVEFStateCurrent(def, cacheObj, existingRecord, baseCommonality, structureFactor, weaponFactor, apparelFactor))
            {
                continue;
            }

            // Offsets deliberately left null: VEF adds them BEFORE applying the factor, so any non-null
            // offset would be scaled too and the arithmetic above would no longer land on the user's figure.
            var stuffExt = new VEF.Things.StuffExtension
            {
                structureGenerationCommonalityFactor = structureFactor,
                weaponGenerationCommonalityFactor = weaponFactor,
                apparelGenerationCommonalityFactor = apparelFactor
            };

            var record = new VEFCommonalityRecord
            {
                AddedExtension = stuffExt,
                OriginalCommonality = def.stuffProps.commonality,
            };

            def.modExtensions ??= []; // Add modExtension if it doesn't exist, which it shouldn't

            if (existingRecord != null)
            {
                record.RemovedOriginalExtensions = existingRecord.RemovedOriginalExtensions;
                record.HadCacheEntry = existingRecord.HadCacheEntry;
                record.OriginalCacheEntry = existingRecord.OriginalCacheEntry;

                // MUST carry the first-seen baseline over. On a re-apply the live commonality is
                // already this mod's base value from the previous pass, so reading it again would
                // bake that in and the def could never be restored to its true default.
                record.OriginalCommonality = existingRecord.OriginalCommonality;
            }
            else
            {
                record.RemovedOriginalExtensions = [.. def.modExtensions.Where(x => x is VEF.Things.StuffExtension)];
                record.HadCacheEntry = cache.TryGetValue(def, out var originalCacheEntry);
                record.OriginalCacheEntry = originalCacheEntry;
            }

            // Stored BEFORE anything below mutates the def. ResolveReferences runs VEF code that can throw
            // and ActionRunner swallows it, so without the record already in place the extensions stripped
            // just below would be unrecoverable and no later reset could run at all. ResetVEFForDef treats
            // every step as a no-op when its change never landed, so an early store is always safe.
            s_vefRecords[def.defName] = record;

            if (existingRecord != null)
                def.modExtensions.Remove(existingRecord.AddedExtension); // Ours from a previous call - swap it, don't treat it as a new original.
            else
                def.modExtensions.RemoveAll(x => x is VEF.Things.StuffExtension); // Remove any StuffExtension already on the def, which there shouldn't be

            def.modExtensions.Add(stuffExt);
            stuffExt.ResolveReferences(def); // Resolve the new StuffExtension

            def.stuffProps.commonality = baseCommonality; // The value the factors below are relative to.
            cache[def] = stuffExt; // Update VEF cache with the new StuffExtension

            // Can't put stuffExt directly into lambda, or else type error is thrown if VEF not installed.
            string logMessage =
                $"Set commonality for {def.defName} -> " +
                $"Default Commonality={info.DefaultCommonality}, " +
                $"Base Commonality={baseCommonality}, " +
                $"Structure={structureCommonality} (factor {stuffExt.structureGenerationCommonalityFactor}), " +
                $"Weapon={weaponCommonality} (factor {stuffExt.weaponGenerationCommonalityFactor}), " +
                $"Apparel={apparelCommonality} (factor {stuffExt.apparelGenerationCommonalityFactor})";

            LogMessage(() => logMessage);
        }
    }

    /// <summary>
    /// True when the def is ALREADY in exactly the state <see cref="ChangeVEF"/> would put it in, so that
    /// pass can skip it. Deliberately reads the def and VEF's cache rather than comparing stored settings:
    /// all four of the things the apply writes are checked, so anything overwritten since the last pass
    /// fails here and gets re-applied instead of being assumed intact.
    /// </summary>
    /// <param name="cacheObj">
    /// VEF's cache from <see cref="GetVEFCache"/>. Takes the non-generic IDictionary base type so this
    /// method's signature carries no VEF type (see <see cref="GetVEFCache"/>).
    /// </param>
    /// <param name="record">This mod's record of what it last applied to <paramref name="def"/>.</param>
    private static bool IsVEFStateCurrent(
        ThingDef def,
        IDictionary cacheObj,
        VEFCommonalityRecord record,
        float baseCommonality,
        float? structureFactor,
        float? weaponFactor,
        float? apparelFactor)
    {
        // Concrete VEF-typed casts live in the method body (JIT-lazy) - only reached on the VEF path.
        if (record.AddedExtension is not VEF.Things.StuffExtension applied)
            return false;

        if (def.stuffProps.commonality != baseCommonality)
            return false;

        if (applied.structureGenerationCommonalityFactor != structureFactor
            || applied.weaponGenerationCommonalityFactor != weaponFactor
            || applied.apparelGenerationCommonalityFactor != apparelFactor)
        {
            return false;
        }

        // Reference identity throughout: the whole point is that OUR instance is still the one in place.
        if (def.modExtensions == null || !def.modExtensions.Contains(applied))
            return false;

        return cacheObj is Dictionary<ThingDef, VEF.Things.StuffExtension> cache
            && cache.TryGetValue(def, out var cachedExtension)
            && ReferenceEquals(cachedExtension, applied);
    }

    /// <summary>
    /// Undoes one def's VEF-path changes: removes the added extension, restores any extension that was
    /// displaced, writes the baseline commonality back, and returns VEF's cache entry to what it was
    /// (removing it entirely if there was none). Tolerates a HALF-applied def - <see cref="ChangeVEF"/>
    /// stores its record before it mutates anything, so a throw part-way through leaves a record whose
    /// changes only partly landed; every step below is therefore written to be a no-op when its change
    /// was never made. If the cache cannot be reached the record is kept rather than dropped, so the
    /// reset can be retried later instead of leaking the extension.
    /// </summary>
    /// <param name="cacheObj">
    /// VEF's cache from <see cref="GetVEFCache"/>. Takes the non-generic IDictionary base type so this
    /// method's signature carries no VEF type (see <see cref="GetVEFCache"/>).
    /// </param>
    /// <param name="defName">Def to reset; a no-op when this mod holds no record for it.</param>
    private static void ResetVEFForDef(IDictionary cacheObj, string defName)
    {
        if (!s_vefRecords.TryGetValue(defName, out var record))
            return;

        var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        if (def == null)
        {
            s_vefRecords.Remove(defName); // Def is gone; nothing left to restore.
            return;
        }

        // Concrete VEF-typed cast lives in the method body (JIT-lazy) - this method only runs on the VEF path.
        if (cacheObj is not Dictionary<ThingDef, VEF.Things.StuffExtension> cache)
        {
            // Keep the record because VEF's cache still holds our extension, so a later reset must be able to retry.
            LogMessage(() => $"VEF cache unavailable; deferring reset for {defName}", LogMessageType.Warning);
            return;
        }

        def.modExtensions?.Remove(record.AddedExtension);
        if (record.RemovedOriginalExtensions.Count > 0)
        {
            def.modExtensions ??= [];

            // Contains-guarded rather than AddRange: the record is stored before the strip that removed
            // these, so a throw in between can leave a recorded "original" still on the def.
            foreach (var extension in record.RemovedOriginalExtensions)
            {
                if (!def.modExtensions.Contains(extension))
                    def.modExtensions.Add(extension);
            }
        }

        def.stuffProps?.commonality = record.OriginalCommonality;

        if (record.HadCacheEntry)
            cache[def] = (VEF.Things.StuffExtension)record.OriginalCacheEntry;
        else
            cache.Remove(def);

        LogMessage(() => $"Reset VEF commonality extension for {defName}");
        s_vefRecords.Remove(defName);
    }
}
