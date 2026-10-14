namespace NewHarvestPatches;

/// <summary>
/// In-progress text-field contents, keyed per field. Text fields cannot read straight from the setting:
/// mid-edit text ("0.0", "") is not yet a valid value, so the raw typed string lives here while the
/// parsed value goes to the setting only once it makes sense. Cleared with the rest of the menu session
/// state, which is also what makes a reset show up in the field.
/// </summary>
internal static class UIBufferCache
{
    internal static Dictionary<string, string> s_categoryLabelBuffers;
    internal static Dictionary<string, string> s_commonalityBuffers;

    /// <summary>
    /// Stores the in-progress text for a field. A null <paramref name="value"/> REMOVES the entry rather
    /// than storing null - that is how a reset makes the field fall back to the (just reset) setting value.
    /// </summary>
    public static void SetTextFieldBuffer(ref Dictionary<string, string> cache, string key, string value)
    {
        if (value == null)
        {
            // Resetting to default
            cache?.Remove(key);
            return;
        }
        cache ??= [];
        cache[key] = value;
    }


    public static bool TryGetTextFieldBuffer(Dictionary<string, string> cache, string key, out string value)
    {
        if (cache == null) 
        { 
            value = null; 
            return false; 
        }

        return cache.TryGetValue(key, out value);
    }

    internal static void ClearCache()
    {
        s_categoryLabelBuffers = null;
        s_commonalityBuffers = null;
    }
}