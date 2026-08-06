using System.Collections;

namespace NewHarvestPatches;

/// <summary>
/// Turn off the fall color shader parameter for trees in settings using Reflection.
/// Live-appliable: re-diffs against the FallColorTrees setting every call, restoring the
/// original shader value for trees no longer targeted.
/// Plants are drawn from a TEXTURE-ATLAS REPLACEMENT material
/// (Graphic.TryGetTextureAtlasReplacementInfo, called from Plant.Print), not from the graphic's
/// own Material - once a static atlas tile exists for a plant, every future print reuses a cached
/// atlas Material built once at first-print time. Mutating the def's shaderParameters or the
/// graphic's own Material (the previous approach) changes a Material that is no longer the one on
/// screen, so the toggle never visibly lands until something rebuilds the atlas from scratch
/// (game restart). The fix: also reach into MaterialPool.matDictionaryReverse and directly
/// SetFloat/Apply the fall parameters onto every atlas Material whose stored MaterialRequest
/// carries the SAME shaderParameters list reference as this tree def - see
/// ApplyShaderParametersToAtlasMaterials. That list-reference identity is what atlas mats keep
/// from their source material's request even after the atlas overwrites mainTex, so it uniquely
/// finds our tree's atlas mats without any texture/path matching. This requires the def's
/// shaderParameters list to stay the SAME list instance for the tree's lifetime (mutate the
/// ShaderParameter values in place, never assign a new list) - otherwise atlas mats built against
/// the old list become unreachable.
/// </summary>
internal static class FallColorDisabler
{
    private const string TargetShaderParamName = "_FallBehaviorEnabled";

    private static readonly FieldInfo s_nameField = 
        typeof(ShaderParameter).GetField("name", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo s_valueField = 
        typeof(ShaderParameter).GetField("value", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo s_cachedGraphicField = 
        typeof(GraphicData).GetField("cachedGraphic", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo s_subGraphicsField = 
        typeof(Graphic_Collection).GetField("subGraphics", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo s_matDictionaryReverseField = 
        typeof(MaterialPool).GetField("matDictionaryReverse", BindingFlags.NonPublic | BindingFlags.Static);

    // Original shader value per tree defName, captured only for trees this mod actually flipped
    // off (never for trees another mod/XML already had disabled) so revert never overrides state
    // that wasn't ours to begin with.
    private static readonly Dictionary<string, Vector4> s_baseline = [];
    private static readonly HashSet<string> s_applied = [];

    public static void TryDisableFallColors()
    {
        ActionRunner.Run(nameof(FallColorDisabler), nameof(TryDisableFallColors), DisableFallColors);
    }

    /// <summary>
    /// Body of <see cref="TryDisableFallColors"/>. Resets trees this mod flipped that are no longer
    /// selected, then flips the currently selected ones. Both halves are idempotent, so calling this
    /// on an unchanged setting writes nothing.
    /// </summary>
    private static void DisableFallColors()
    {
        if (s_nameField == null || s_valueField == null)
        {
            LogMessage(() => "Could not access ShaderParameter name/value fields via reflection.", LogMessageType.Error);
            return;
        }

        HashSet<string> desired = IsSettingAvailable(nameof(Settings.FallColorTrees))
            ? [.. ExtractNamesFromEnabledSettings(Setting.Prefix.NoFallColors_)]
            : [];

        foreach (var treeDefName in s_applied.Except(desired).ToList())
        {
            ResetForDef(treeDefName);
        }

        foreach (var treeDefName in desired)
        {
            DisableForDef(treeDefName);
        }
    }

    /// <summary>
    /// The tree's fall-behavior shader parameter, or null if the def has none. <c>ShaderParameter.name</c>
    /// is private, hence the reflected field read.
    /// </summary>
    private static ShaderParameter FindParam(ThingDef treeDef)
    {
        return treeDef?.graphicData?.shaderParameters?.FirstOrDefault(p => (string)s_nameField.GetValue(p) == TargetShaderParamName);
    }

    /// <summary>
    /// Zeroes one tree's fall parameter and records its baseline. A tree already disabled by someone
    /// else is skipped without being tracked - claiming it would make a later revert re-enable fall
    /// colors that another mod or the XML deliberately turned off.
    /// </summary>
    private static void DisableForDef(string treeDefName)
    {
        if (s_applied.Contains(treeDefName))
            return; // Already disabled by us this session - nothing changed.

        var treeDef = DefDatabase<ThingDef>.GetNamedSilentFail(treeDefName);
        if (treeDef == null)
        {
            LogMessage(() => $"Couldn't find ThingDef named {treeDefName}. Skipping.", LogMessageType.Warning);
            return;
        }

        var param = FindParam(treeDef);
        if (param == null)
        {
            LogMessage(() => $"Couldn't find shader parameter named {TargetShaderParamName} for tree {treeDefName}. Skipping.", LogMessageType.Warning);
            return;
        }

        Vector4 value = (Vector4)s_valueField.GetValue(param);
        if (value.x != 1f)
        {
            LogMessage(() => $"Shader parameter {TargetShaderParamName} for tree {treeDefName} is already disabled (value.x = {value.x}). Skipping.", LogMessageType.Warning);
            return; // Not our doing - never track it, so we never revert someone else's state.
        }

        s_baseline[treeDefName] = value;
        s_valueField.SetValue(param, Vector4.zero);
        RefreshLiveTrees(treeDef);
        s_applied.Add(treeDefName);
        LogMessage(() => $"Disabling fall color shader parameter {TargetShaderParamName} for tree {treeDefName}");
    }

    /// <summary>
    /// Writes one tree's recorded baseline back and drops the claim. The claim is dropped even when the
    /// def or parameter has since vanished, so a def removed by another mod cannot pin the entry forever.
    /// </summary>
    private static void ResetForDef(string treeDefName)
    {
        var treeDef = DefDatabase<ThingDef>.GetNamedSilentFail(treeDefName);
        var param = FindParam(treeDef);
        if (param != null && s_baseline.TryGetValue(treeDefName, out var original))
        {
            s_valueField.SetValue(param, original);
            RefreshLiveTrees(treeDef);
            LogMessage(() => $"Reset fall color shader parameter {TargetShaderParamName} for tree {treeDefName}");
        }
        s_applied.Remove(treeDefName);
        s_baseline.Remove(treeDefName);
    }

    /// <summary>
    /// Resets the def-level graphic cache (so future-spawned trees build correctly from
    /// shaderParameters) then re-Applies the FULL def shaderParameters list onto every Material
    /// already built for the def graphic, every live tree's own (possibly separately-colored, see
    /// class doc) Graphic, AND every texture-atlas replacement Material already on screen (see
    /// <see cref="ApplyShaderParametersToAtlasMaterials"/>) - that last step is what makes the toggle
    /// visible immediately instead of only after the atlas gets rebuilt (game restart).
    /// Plant overrides Thing.Graphic and can return def.plant.immatureGraphic / leaflessGraphic /
    /// leaflessImmatureGraphic / pollutedGraphic / GraphicSowing instead of the def graphic, and
    /// PlantProperties.PostLoadSpecial builds all of those without passing shaderParameters - so
    /// their materials never receive _FallBehaviorEnabled and fall back to the shader's own default,
    /// which is off (every plant shares CutoutPlant, and DeciduousTreeBase is the only def in the
    /// game that sets the parameter at all). Those alt graphics therefore never show fall colors in
    /// the first place, so there is nothing for this method to turn off on them - not a gap.
    /// </summary>
    private static void RefreshLiveTrees(ThingDef treeDef)
    {
        if (treeDef?.graphicData?.shaderParameters == null)
            return;

        List<ShaderParameter> parameters = treeDef.graphicData.shaderParameters;

        s_cachedGraphicField?.SetValue(treeDef.graphicData, null);
        ApplyShaderParametersToGraphic(treeDef.graphicData.Graphic, parameters);
        ApplyShaderParametersToAtlasMaterials(parameters);

        List<Map> maps = Find.Maps;
        if (maps == null)
            return; // No game loaded yet (e.g. main menu at StaticConstructorOnStartup time) - nothing to refresh.

        foreach (Map map in maps)
        {
            foreach (Thing thing in map.listerThings.ThingsOfDef(treeDef))
            {
                thing.Notify_ColorChanged(); // Force re-resolve first, so a per-thing-colored tree
                                             // rebuilds through GetColoredVersion now (dropping its
                                             // params), and we then patch that exact resolved Graphic
                                             // below. Trees are never made from stuff, but Plant is a
                                             // ThingWithComps, so a CompColorable added by another mod
                                             // still makes DrawColor differ from graphicData.color and
                                             // gives the thing its own Graphic.
                ApplyShaderParametersToGraphic(thing.Graphic, parameters);
            }
        }
    }

    /// <summary>
    /// Walks every Material a Graphic can resolve to (collection sub-graphics, random-rotated
    /// wrapper) and re-Applies each ShaderParameter in the def's full list directly onto it, so the
    /// change is visible regardless of which stale cache (GraphicDatabase/MaterialPool) still points
    /// at these same Material instances, and regardless of whether that Material was ever given any
    /// fall parameters in the first place (see class doc - GetColoredVersion drops them all).
    /// The Graphic_Collection and Graphic_Multi cases are defensive, not currently live: every
    /// targeted tree overrides DeciduousTreeBase's Graphic_Random with Graphic_Single, and the
    /// target set is restricted to New Harvest trees. They stay because a Graphic_Collection tree
    /// would silently go unpatched otherwise, and because Graphic_Collection.Init synthesises a
    /// Graphic_Multi SUB-graphic for any texture group carrying _north/_east/_south/_west variants,
    /// which is the only way a Graphic_Multi can appear here (no tree sets that graphicClass).
    /// </summary>
    /// <param name="graphic">Graphic to walk; null and unrecognized types are no-ops.</param>
    /// <param name="parameters">The def's full parameter list, applied wholesale rather than just the fall parameter.</param>
    private static void ApplyShaderParametersToGraphic(Graphic graphic, List<ShaderParameter> parameters)
    {
        switch (graphic)
        {
            case null:
                return;
            case Graphic_RandomRotated randomRotated:
                ApplyShaderParametersToGraphic(randomRotated.SubGraphic, parameters);
                return;
            case Graphic_Collection collection:
                if (s_subGraphicsField?.GetValue(collection) is Graphic[] subGraphics)
                {
                    foreach (Graphic subGraphic in subGraphics)
                    {
                        ApplyShaderParametersToGraphic(subGraphic, parameters);
                    }
                }
                return;
            case Graphic_Multi multi:
                ApplyShaderParametersToMaterials([multi.MatNorth, multi.MatEast, multi.MatSouth, multi.MatWest], parameters);
                return;
            case Graphic_Single single:
                ApplyShaderParametersToMaterials([single.MatSingle], parameters);
                return;
        }
    }

    private static void ApplyShaderParametersToMaterials(Material[] materials, List<ShaderParameter> parameters)
    {
        if (materials == null)
            return;

        foreach (Material material in materials)
        {
            if (material == null)
                continue;

            foreach (ShaderParameter parameter in parameters)
            {
                parameter.Apply(material);
            }
        }
    }

    /// <summary>
    /// Plants print through Graphic.TryGetTextureAtlasReplacementInfo, which - once a static atlas
    /// tile exists - returns a cached atlas Material built once at first print and reused forever
    /// after (see class doc). That atlas Material is what's actually on screen, so the toggle must
    /// reach it directly: walk every built Material via MaterialPool.matDictionaryReverse, and for
    /// any whose stored MaterialRequest.shaderParameters is the SAME list instance as this tree
    /// def's shaderParameters (atlas requests are copied from the source material's request, so the
    /// list reference survives even though mainTex gets overwritten to the shared atlas texture),
    /// re-Apply the full parameter set onto it. Reference-equality against the def's own list is
    /// what makes this match unique to this tree without any texture/path lookup.
    /// </summary>
    /// <param name="parameters">The tree def's own shaderParameters list - the reference is the match key, so a copy will not work.</param>
    private static void ApplyShaderParametersToAtlasMaterials(List<ShaderParameter> parameters)
    {
        if (s_matDictionaryReverseField?.GetValue(null) is not IDictionary matDictionaryReverse)
        {
            LogMessage(() => "matDictionaryReverse reflection field returned null - cannot reach atlas materials.", LogMessageType.Error);
            return;
        }

        foreach (DictionaryEntry entry in matDictionaryReverse)
        {
            if (entry.Key is not Material material)
                continue;

            var request = (MaterialRequest)entry.Value;
            if (!ReferenceEquals(request.shaderParameters, parameters))
                continue;

            foreach (ShaderParameter parameter in parameters)
            {
                parameter.Apply(material);
            }
        }
    }
}
