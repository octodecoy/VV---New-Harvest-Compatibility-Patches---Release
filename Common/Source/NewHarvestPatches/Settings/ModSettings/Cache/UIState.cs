namespace NewHarvestPatches;  

/// <summary>
/// A utility struct that captures and restores UI state (font, color, text alignment, and enabled state) within a scoped context.
/// Base game TextBlock struct doesn't have support for GUI.enabled, hence this struct emulating it + adding GUI.enabled support.
/// </summary>
public readonly struct UIState : IDisposable
{
    private readonly GameFont _font;
    private readonly Color _color;
    private readonly TextAnchor _anchor;
    private readonly bool _enabled;

    public UIState(GameFont? font = null, Color? color = null, TextAnchor? anchor = null, bool? enabled = null)
    {
        _font = Text.Font;
        _color = GUI.color;
        _anchor = Text.Anchor;
        _enabled = GUI.enabled;
        if (font.HasValue) Text.Font = font.Value;
        if (anchor.HasValue) Text.Anchor = anchor.Value;
        if (color.HasValue) GUI.color = color.Value;
        if (enabled.HasValue) GUI.enabled = enabled.Value;
    }

    public void Dispose()
    {
        Text.Font = _font;
        Text.Anchor = _anchor;
        GUI.color = _color;
        GUI.enabled = _enabled;
    }
}