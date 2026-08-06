namespace NewHarvestPatches;

/// <summary>
/// Every texture the settings window draws, resolved through ContentFinder once and held for the menu
/// session. All lookups fall back to a placeholder rather than returning null, so a missing or renamed
/// texture file degrades to a visible placeholder instead of throwing mid-OnGUI.
/// </summary>
// [StaticConstructorOnStartup] or else texture warnings.
[StaticConstructorOnStartup]
internal static class UITextureCache
{
    private const string UIFolder = $"{ModPrefix}/UI";
    public const string IconFolder = $"{UIFolder}/Icons/";
    private static Texture2D _backgroundLogoInt;
    private static Texture2D _placeholderIconInt;
    private static Dictionary<TabCategoryDef, Texture2D> _tabIconsInt;
    private static Dictionary<string, Texture2D> _iconsByPathInt;

    public static Texture2D BackgroundLogo
    {
        get
        {
            // We use a sentinel (PlaceholderIcon) otherwise if someone deletes the background logo file, there would be a warning every frame.
            _backgroundLogoInt ??= ContentFinder<Texture2D>.GetAllInFolder($"{UIFolder}/MenuBackground")?.RandomElement() ?? PlaceholderIcon;
            return _backgroundLogoInt;
        }
    }

    public static Dictionary<TabCategoryDef, Texture2D> MenuTabIcons
    {
        get
        {
            if (_tabIconsInt == null) 
                BuildMenuTabIcons();

            return _tabIconsInt;
        }
    }

    /// <summary>
    /// Resolves one icon per tab. Goes through <see cref="GetIcon"/> rather than ContentFinder directly
    /// because texPath is optional on the Def: ContentFinder does not tolerate a null path (it reaches a
    /// Dictionary lookup on it and throws ArgumentNullException), and a tab def that omits texPath is a
    /// missing icon, not a reason to throw out of OnGUI.
    /// <para>
    /// Built into a local and published only once complete, so a throw here cannot leave a non-null but
    /// half-filled cache behind - the null check in <see cref="MenuTabIcons"/> would never fire again and
    /// the missing tabs would stay iconless for the whole session.
    /// </para>
    /// </summary>
    private static void BuildMenuTabIcons()
    {
        Dictionary<TabCategoryDef, Texture2D> icons = [];
        foreach (var tab in Tabs)
        {
            icons[tab] = GetIcon(tab.texPath);
        }
        _tabIconsInt = icons;
    }

    public static Texture2D PlaceholderIcon
    {
        get
        {
            _placeholderIconInt ??= Widgets.PlaceholderIconTex ?? BaseContent.BadTex;
            return _placeholderIconInt;
        }
    }

    /// <summary>
    /// Texture for an arbitrary UI path, resolved through ContentFinder once per menu session instead of
    /// on every OnGUI frame that draws the icon. A miss caches the placeholder too, so a bad path costs
    /// one failed lookup rather than one per frame.
    /// </summary>
    public static Texture2D GetIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return PlaceholderIcon;

        _iconsByPathInt ??= [];

        if (_iconsByPathInt.TryGetValue(path, out Texture2D cached))
            return cached;

        Texture2D icon = ContentFinder<Texture2D>.Get(path, false) ?? PlaceholderIcon;
        _iconsByPathInt[path] = icon;
        return icon;
    }

    internal static void ClearCache()
    {
        _backgroundLogoInt = null;
        _placeholderIconInt = null;
        _tabIconsInt = null;
        _iconsByPathInt = null;
    }

    public static void DrawBackgroundLogo(Rect rect)
    {     
        Widgets.DrawTextureFitted(rect, BackgroundLogo, scale: 0.8f, alpha: 0.08f);
    }
}