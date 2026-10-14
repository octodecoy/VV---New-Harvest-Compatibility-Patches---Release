namespace NewHarvestPatches;

/// <summary>
/// Resolves xpath ONCE and holds the matches under a name so sibling operations in caseTrue can read
/// them via cacheKey (PatchOperationPathedExtended) instead of each re-running the same document-wide
/// xpath. Lifetime is strictly lexical: the slot is written just before caseTrue runs and removed in a
/// finally right after, so it cannot outlive this block even if a child operation throws - RimWorld
/// gives XML patches no per-file or per-block lifecycle hook to key off instead (PatchOperation.Complete
/// only fires per-mod, after Defs are already built). Only this mod's own cacheKey-aware operations can
/// read the cache; vanilla PatchOperations resolve their own xpath against the document and have no seam for it.
/// XML: key (cache slot name), xpath (as PatchOperationPathed), caseTrue/caseFalse as usual. A key
/// already live (nested CacheNodes reusing the same key) is rejected rather than shadowed.
/// </summary>
internal class CacheNodes : PatchOperationPathedExtended
{
    private readonly string key = null;

    protected override bool ApplyWorker(XmlDocument xml)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                LogMessage(() => "CacheNodes requires a <key>.", LogMessageType.Error);
                return false;
            }

            if (XmlNodeCache == null)
            {
                LogMessage(() => $"Node cache already released; key [{key}] cannot be filled.", LogMessageType.Error);
                return false;
            }

            if (XmlNodeCache.ContainsKey(key))
            {
                LogMessage(() => $"Cache key [{key}] is already live; a nested block may not reuse a key.", LogMessageType.Error);
                return false;
            }

            if (!PreCheck(xpath, xml))
                return caseFalse == null || caseFalse.Apply(xml);

            XmlNodeCache[key] = nodes;
            try
            {
                return caseTrue == null || caseTrue.Apply(xml);
            }
            finally
            {
                XmlNodeCache.Remove(key); // Block-scoped: cannot leak past caseTrue.
            }
        }
        catch (Exception ex)
        {
            LogException(ex, ex.TargetSite, optMsg: $"{xpath}");
            return false;
        }
    }
}
