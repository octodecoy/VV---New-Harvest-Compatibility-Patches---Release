namespace NewHarvestPatches;

/// <summary>
/// Changes colors of ThingDefs and TerrainDefs built out of them. Live-appliable: every call
/// diffs against the current MaterialColors settings, restoring any def no longer customized
/// back to its true default, then applying the current custom colors - each direction also
/// refreshes already-spawned Things/terrain so the change is visible without a restart.
/// </summary>
internal static class MaterialColorChanger
{
    private static readonly MethodInfo s_resolveThingDefIconMethod =
        typeof(ThingDef).GetMethod("ResolveIcon", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo s_resolveTerrainDefIconMethod =
        typeof(TerrainDef).GetMethod("ResolveIcon", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo s_cachedGraphicField =
        typeof(GraphicData).GetField("cachedGraphic", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo s_shaderPollutedProperty =
        typeof(TerrainDef).GetProperty("ShaderPolluted", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo s_terrainMatCacheField =
        typeof(TerrainGrid).GetField("terrainMatCache", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly Dictionary<string, Color> s_stuffBaseline = [];
    private static readonly Dictionary<string, Color> s_thingBaseline = [];

    // Terrain gets its own baselines, keyed by TERRAIN defName: a floor's <color> is an independent
    // def field from the stuff's, and the two routinely differ (vanilla WoodPlankFloor 108,78,55 vs
    // WoodLog 133,97,67). Reverting through the stuff baseline alone would leave every recolored
    // floor wearing the stuff's default instead of its own.
    private static readonly Dictionary<string, Color> s_terrainBaseline = [];
    private static readonly HashSet<string> s_stuffApplied = [];
    private static readonly HashSet<string> s_thingApplied = [];

    public static void TryChangeMaterialColors()
    {
        ActionRunner.Run(nameof(MaterialColorChanger), nameof(TryChangeMaterialColors), ChangeMaterialColors);
    }

    /// <summary>
    /// Body of <see cref="TryChangeMaterialColors"/>. Stuff color and thing color are tracked as two
    /// independent claims per def, since a user can customize one and leave the other at default.
    /// A def counts as "desired" only when its configured color is actually distinguishable from the
    /// recorded default - equal colors are left unclaimed so nothing is written or reverted for them.
    /// Refreshes are deferred to the end and driven off the set of defs that really changed, so an
    /// unchanged settings window close costs no map redraw.
    /// </summary>
    private static void ChangeMaterialColors()
    {
        var dict = Settings.MaterialColors;

        HashSet<string> desiredStuff = [];
        HashSet<string> desiredThing = [];
        if (!dict.NullOrEmpty())
        {
            foreach (var kvp in dict)
            {
                var info = kvp.Value;

                if (info == null || DefDatabase<ThingDef>.GetNamedSilentFail(kvp.Key) is not { })
                    continue;

                if (!info.NewStuffColor.IndistinguishableFromFast(info.DefaultStuffColor))
                    desiredStuff.Add(kvp.Key);
                if (!info.NewThingColor.IndistinguishableFromFast(info.DefaultThingColor))
                    desiredThing.Add(kvp.Key);
            }
        }

        HashSet<ThingDef> touchedDefs = [];
        bool terrainTouched = false;

        foreach (var defName in s_stuffApplied.Except(desiredStuff).ToList())
        {
            if (DefDatabase<ThingDef>.GetNamedSilentFail(defName) is { } def
                && s_stuffBaseline.TryGetValue(defName, out var baseline)
                && ResetStuffColor(def, baseline, ref terrainTouched))
            {
                touchedDefs.Add(def);
            }
            s_stuffApplied.Remove(defName);
            s_stuffBaseline.Remove(defName);
        }

        foreach (var defName in s_thingApplied.Except(desiredThing).ToList())
        {
            if (DefDatabase<ThingDef>.GetNamedSilentFail(defName) is { } def
                && s_thingBaseline.TryGetValue(defName, out var baseline)
                && ResetThingColor(def, baseline))
            {
                touchedDefs.Add(def);
            }
            s_thingApplied.Remove(defName);
            s_thingBaseline.Remove(defName);
        }

        if (!dict.NullOrEmpty())
        {
            foreach (var kvp in dict)
            {
                if (DefDatabase<ThingDef>.GetNamedSilentFail(kvp.Key) is not { } def)
                    continue;

                ColorInfo info = kvp.Value;
                if (info == null)
                    continue;

                // Baseline/applied bookkeeping stays unconditional - only the first call actually
                // writes a color, and dropping our claim on later no-op calls would lose the reset.
                if (desiredStuff.Contains(kvp.Key))
                {
                    if (!s_stuffBaseline.ContainsKey(kvp.Key))
                        s_stuffBaseline[kvp.Key] = info.DefaultStuffColor;
                    if (ChangeStuffColor(def, info.NewStuffColor, ref terrainTouched))
                        touchedDefs.Add(def);
                    s_stuffApplied.Add(kvp.Key);
                }

                if (desiredThing.Contains(kvp.Key))
                {
                    if (!s_thingBaseline.ContainsKey(kvp.Key))
                        s_thingBaseline[kvp.Key] = info.DefaultThingColor;
                    if (ChangeThingColor(def, info.NewThingColor))
                        touchedDefs.Add(def);
                    s_thingApplied.Add(kvp.Key);
                }
            }
        }

        RefreshSpawnedThings(touchedDefs);
        if (terrainTouched)
            RefreshTerrain();
    }

    /// <summary>
    /// Returns whether a color was actually written, so callers only pay for a refresh when something changed.
    /// The return value covers the STUFF color only - terrain is reported separately through
    /// <paramref name="terrainTouched"/>, and is handled ahead of the equality guard because terrain carries
    /// its own baselines: a reset still has floors to restore even when something else already put
    /// stuffProps.color back on its own.
    /// </summary>
    /// <param name="isReset">True when writing a recorded baseline back rather than a user color; makes
    /// terrain restore its own recorded color instead of taking <paramref name="newColor"/>.</param>
    private static bool ChangeStuffColor(ThingDef def, Color newColor, ref bool terrainTouched, bool isReset = false)
    {
        if (def.stuffProps == null)
            return false;

        if (ChangeTerrainDefColors(def.defName, newColor, isReset))
            terrainTouched = true;

        Color oldColor = def.stuffProps.color;
        if (oldColor.IndistinguishableFromFast(newColor))
            return false;

        def.stuffProps.color = newColor;

        LogMessage(() => $"[{def.defName}] - Stuff color change: old=[{oldColor}], new=[{newColor}].");
        return true;
    }

    /// <summary>
    /// Named alias for writing a recorded baseline back. Resetting deliberately reuses the apply path
    /// so a reset color propagates to terrain and icons exactly as a custom one does - except for the
    /// terrain COLOR itself, which comes from the terrain's own baseline (see <see cref="s_terrainBaseline"/>).
    /// </summary>
    private static bool ResetStuffColor(ThingDef def, Color originalColor, ref bool terrainTouched)
    {
        return ChangeStuffColor(def, originalColor, ref terrainTouched, isReset: true);
    }

    /// <summary>Returns whether a color was actually written, so callers only pay for a refresh when something changed.</summary>
    private static bool ChangeThingColor(ThingDef def, Color newColor)
    {
        if (def.graphicData == null)
            return false;

        var oldColor = def.graphicData.color;
        if (oldColor.IndistinguishableFromFast(newColor))
            return false;

        def.graphicData.color = newColor;
        // The cached graphic bakes the color in, and GraphicData.Graphic hands the cached one straight
        // back when it exists (ExplicitlyInitCachedGraphic is literally `cachedGraphic = Graphic`, so it
        // can not force a rebuild either). Nulling the field is the only way to make the next read
        // re-Init against the new color.
        s_cachedGraphicField?.SetValue(def.graphicData, null);
        def.graphic = def.graphicData.Graphic;
        s_resolveThingDefIconMethod?.Invoke(def, null);
        LogMessage(() => $"[{def.defName}] - Thing color change: old=[{oldColor}], new=[{newColor}].");
        return true;
    }

    /// <summary>Named alias for writing a recorded baseline back; see <see cref="ResetStuffColor"/>.</summary>
    private static bool ResetThingColor(ThingDef def, Color originalColor)
    {
        return ChangeThingColor(def, originalColor);
    }

    /// <summary>
    /// Propagates a stuff color change onto the floors built from that stuff. Only paintable,
    /// single-cost terrain qualifies: single-cost so the stuff is unambiguously the one being recolored,
    /// paintable because the shader for non-paintable terrain ignores the def color. Each match gets a
    /// freshly built graphic plus a rebuilt polluted variant and icon, since all three bake the color in -
    /// and the new graphic gets TerrainDef.PostLoad's edge/custom-shader setup re-applied, which does not
    /// survive the swap to a differently-colored Material.
    /// </summary>
    /// <param name="defName">The stuff def whose color changed.</param>
    /// <param name="newStuffColor">The color to push onto matching terrain. Ignored when <paramref name="isReset"/>
    /// is true - a reset restores each floor's own recorded baseline instead.</param>
    /// <param name="isReset">True when reverting. Only terrain this class actually recolored is touched, and
    /// each one goes back to the color it had before we first wrote it (see <see cref="s_terrainBaseline"/>).</param>
    /// <returns>True if any terrain was recolored, signalling the caller to refresh terrain meshes.</returns>
    private static bool ChangeTerrainDefColors(string defName, Color newStuffColor, bool isReset)
    {
        List<TerrainDef> terrainDefs = [.. DefDatabase<TerrainDef>.AllDefs
            .Where(td => td != null
                    && td.isPaintable
                    && td.costList != null
                    && td.costList.Count == 1
                    && td.costList[0]?.thingDef?.defName == defName)];

        if (terrainDefs.Count == 0)
            return false;

        bool anyChanged = false;
        foreach (var terrain in terrainDefs)
        {
            Color targetColor = newStuffColor;
            if (isReset)
            {
                // No baseline means we never recolored this floor - leave whatever owns it alone.
                if (!s_terrainBaseline.TryGetValue(terrain.defName, out targetColor))
                    continue;
                s_terrainBaseline.Remove(terrain.defName);
            }

            Color oldColor = terrain.color;
            if (oldColor.IndistinguishableFromFast(targetColor))
                continue;

            if (!isReset && !s_terrainBaseline.ContainsKey(terrain.defName))
                s_terrainBaseline[terrain.defName] = oldColor;

            terrain.color = targetColor;
            terrain.graphic = GraphicDatabase.Get<Graphic_Terrain>(
                terrain.texturePath,
                terrain.Shader,
                Vector2.one,
                terrain.DrawColor,
                2000 + terrain.renderPrecedence
            );

            // A new color means a new GraphicDatabase entry and therefore a new Material, so the
            // edge/custom-shader setup TerrainDef.PostLoad applied to the original is not on it.
            if (terrain.edgeType is TerrainDef.TerrainEdgeType.FadeRough or TerrainDef.TerrainEdgeType.Water)
                terrain.graphic.MatSingle.SetTexture(ShaderPropertyIDs.AlphaAddTex, TexGame.AlphaAddTex);

            if (terrain.customShader != null && terrain.customShaderParameters != null)
            {
                for (int i = 0; i < terrain.customShaderParameters.Count; i++)
                    terrain.customShaderParameters[i].Apply(terrain.graphic.MatSingle);
            }

            RebuildPollutedGraphic(terrain);
            s_resolveTerrainDefIconMethod?.Invoke(terrain, null);
            anyChanged = true;
            LogMessage(() => $"[{defName}] - Terrain color change: [{terrain.defName}] - old=[{oldColor}], new=[{terrain.color}].");
        }

        return anyChanged;
    }

    /// <summary>
    /// Rebuilds a terrain's Biotech polluted graphic against its new DrawColor, mirroring TerrainDef.PostLoad -
    /// that graphic is built separately from <see cref="TerrainDef.graphic"/>, so recoloring the terrain without
    /// this leaves polluted cells rendering the old color.
    /// </summary>
    private static void RebuildPollutedGraphic(TerrainDef terrain)
    {
        // Nothing to rebuild unless PostLoad built one in the first place.
        if (!ModsConfig.BiotechActive
            || terrain.graphicPolluted == null
            || terrain.graphicPolluted == BaseContent.BadGraphic
            || (terrain.pollutionOverlayTexturePath.NullOrEmpty() && terrain.pollutedTexturePath.NullOrEmpty()))
            return;

        if (s_shaderPollutedProperty?.GetValue(terrain) is not Shader shaderPolluted)
            return;

        terrain.graphicPolluted = GraphicDatabase.Get<Graphic_Terrain>(
            terrain.pollutedTexturePath ?? terrain.texturePath,
            shaderPolluted,
            Vector2.one,
            terrain.DrawColor,
            2000 + terrain.renderPrecedence
        );

        Material matSingle = terrain.graphicPolluted.MatSingle;

        if (!terrain.pollutionOverlayTexturePath.NullOrEmpty())
            matSingle.SetTexture(ShaderPropertyIDs.BurnTex, ContentFinder<Texture2D>.Get(terrain.pollutionOverlayTexturePath));

        matSingle.SetColor(ShaderPropertyIDs.BurnColor, terrain.pollutionColor);
        matSingle.SetVector(ShaderPropertyIDs.BurnScale, terrain.pollutionOverlayScale);
        matSingle.SetVector(ShaderPropertyIDs.ScrollSpeed, terrain.pollutionOverlayScrollSpeed);
        matSingle.SetColor(ShaderPropertyIDs.PollutionTintColor, terrain.pollutionTintColor);

        if (terrain.edgeType == TerrainDef.TerrainEdgeType.FadeRough)
            matSingle.SetTexture(ShaderPropertyIDs.AlphaAddTex, TexGame.AlphaAddTex);

        if (matSingle != terrain.graphic.MatSingle)
            matSingle.SetFloat(ShaderPropertyIDs.IsPolluted, 1f);

        if ((terrain.pollutionShaderType != null || terrain.customShader != null) && terrain.customShaderParameters != null)
        {
            for (int i = 0; i < terrain.customShaderParameters.Count; i++)
                terrain.customShaderParameters[i].Apply(matSingle);
        }
    }

    /// <summary>
    /// Clears each spawned Thing's cached graphic and dirties its map mesh so the new color renders without
    /// a restart. Pawn gear is walked separately: equipment and worn apparel are held, not spawned, so they
    /// are absent from listerThings and would otherwise keep the old stuff color until something else
    /// re-resolved them. Things stashed in containers or travelling with a caravan are still missed - they
    /// are not rendered either, and re-resolve on the next spawn.
    /// </summary>
    private static void RefreshSpawnedThings(HashSet<ThingDef> affectedDefs)
    {
        if (Current.ProgramState != ProgramState.Playing || affectedDefs.NullOrEmpty())
            return;

        foreach (Map map in Find.Maps)
        {
            List<Thing> things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (affectedDefs.Contains(thing.def) || (thing.Stuff != null && affectedDefs.Contains(thing.Stuff)))
                    thing.Notify_ColorChanged();
            }

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                RefreshPawnGear(pawns[i], affectedDefs);
            }
        }
    }

    /// <summary>
    /// Refreshes one pawn's held equipment and worn apparel. Apparel.Notify_ColorChanged already tells the
    /// wearer's tracker to rebuild the render tree, so no separate renderer invalidation is needed here.
    /// </summary>
    private static void RefreshPawnGear(Pawn pawn, HashSet<ThingDef> affectedDefs)
    {
        if (pawn.equipment != null)
        {
            List<ThingWithComps> equipment = pawn.equipment.AllEquipmentListForReading;
            for (int i = 0; i < equipment.Count; i++)
            {
                ThingWithComps eq = equipment[i];
                if (affectedDefs.Contains(eq.def) || (eq.Stuff != null && affectedDefs.Contains(eq.Stuff)))
                    eq.Notify_ColorChanged();
            }
        }

        if (pawn.apparel == null)
            return;

        List<Apparel> worn = pawn.apparel.WornApparel;
        for (int i = 0; i < worn.Count; i++)
        {
            Apparel apparel = worn[i];
            if (affectedDefs.Contains(apparel.def) || (apparel.Stuff != null && affectedDefs.Contains(apparel.Stuff)))
                apparel.Notify_ColorChanged();
        }
    }

    /// <summary>Forces every map's terrain mesh to redraw so changed terrain colors render without a restart.</summary>
    private static void RefreshTerrain()
    {
        if (Current.ProgramState != ProgramState.Playing)
            return;

        foreach (Map map in Find.Maps)
        {
            if (s_terrainMatCacheField?.GetValue(map.terrainGrid) is System.Collections.IDictionary cache)
                cache.Clear();
            map.mapDrawer.WholeMapChanged(MapMeshFlagDefOf.Terrain);
        }
    }
}
