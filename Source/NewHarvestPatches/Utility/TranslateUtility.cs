namespace NewHarvestPatches;

/// <summary>
/// Wraps RimWorld's Translate so callers pass a bare key and the mod's key prefix is applied here. Keeps
/// the prefix in one place and stops a stray unprefixed key from silently resolving to another mod's string.
/// </summary>
public static class TranslateUtility
{
    /// <summary>Marker prepended to labels whose setting only takes effect after a restart.</summary>
    public static readonly string RestartMarker = "《".Colorize(ColorLibrary.Gold) + "✱".Colorize(GenColor.FromHex("#64B5F6")) + "》".Colorize(ColorLibrary.Gold);
    /// <param name="keepTags">
    /// Skip the tag strip so rich-text markup in <paramref name="args"/> (e.g. <see cref="RestartMarker"/>) survives.
    /// Off by default: the implicit TaggedString-to-string conversion strips every &lt;...&gt; tag.
    /// </param>
    public static string TranslateKey(this string key, bool withRestartMarker = false, bool keepTags = false, params NamedArgument[] args)
    {
        TaggedString translated = $"{TKey.KeyPrefix}{key}".Translate(args);
        string translation = keepTags ? translated.RawText : translated.RawText.StripTags();
        return withRestartMarker ? RestartMarker + translation : translation;
    }

    public static string TranslateKey(this string key, Color color, bool withRestartMarker = false, bool keepTags = false, params NamedArgument[] args)
    {
        return key.TranslateKey(withRestartMarker: withRestartMarker, keepTags: keepTags, args: args).Colorize(color);
    }
}