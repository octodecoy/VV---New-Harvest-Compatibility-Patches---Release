namespace NewHarvestPatches
{
    /// <summary>
    /// A user-renamed category label plus the def label to revert to. Same split as
    /// <see cref="ColorInfo"/>: only the current label is scribed, the original is re-captured from the
    /// def each session so reverting follows def changes instead of restoring a stale name.
    /// <para>
    /// Empty <see cref="CurrentCategoryLabel"/> means "the user has not renamed this category", and only a
    /// real rename may ever put a value in it. Anything stored here outranks the def for the rest of time -
    /// which is correct for a name the user chose, and wrong for one merely copied off the def, because it
    /// also outranks corrected labels from mod updates and translated labels from the DefInjected files of
    /// a newly selected language. <c>NewHarvestPatchesModSettings.InitializeCategoryLabelInfo</c> is where
    /// that invariant is established each boot.
    /// </para>
    /// </summary>
    public class CategoryLabelInfo : IExposable
    {
        [Unsaved(false)]
        public string OriginalCategoryLabel = ""; // No need to scribe since it could change between sessions so just update it on load and use to revert

        /// <summary>The user's rename, or empty when they have not renamed this category.</summary>
        public string CurrentCategoryLabel = "";
        public void ExposeData()
        {
            Scribe_Values.Look(ref CurrentCategoryLabel, nameof(CurrentCategoryLabel), "", false);
        }

        /// <summary>
        /// Records a rename. Call BEFORE writing the new label onto the def: the create branch has no other
        /// source for OriginalCategoryLabel than <c>category.label</c>, so calling it afterwards would store
        /// the rename as its own original and make reset a no-op. That branch is a fallback -
        /// <c>NewHarvestPatchesModSettings.InitializeCategoryLabelInfo</c> pre-creates an entry for every
        /// category each boot - but it is the only place the invariant can be broken silently.
        /// </summary>
        internal static void UpdateCategoryLabelInfo(ThingCategoryDef category, string newLabel)
        {
            if (Settings.CategoryLabelCache.TryGetValue(category.defName, out var existingEntry))
            {
                existingEntry.CurrentCategoryLabel = newLabel;
            }
            else
            {
                // Create a new CategoryLabelInfo
                var newLabelInfo = new CategoryLabelInfo
                {
                    OriginalCategoryLabel = category.label,
                    CurrentCategoryLabel = newLabel
                };
                Settings.CategoryLabelCache[category.defName] = newLabelInfo;
            }
        }

        /// <summary>
        /// Pushes every stored rename onto its ThingCategoryDef. Live-appliable and idempotent: labels
        /// already matching are skipped, so repeated calls neither re-log nor thrash. Resetting is done by
        /// writing the original label into the cache entry, not by this method.
        /// </summary>
        public static void TrySetCategoryLabels()
        {
            ActionRunner.Run(nameof(CategoryLabelInfo), nameof(TrySetCategoryLabels), SetCategoryLabels);
        }

        private static void SetCategoryLabels()
        {
            if (Settings.CategoryLabelCache.NullOrEmpty())
                return;

            var categories = DefUtility.ThingCategoryDefs.InternalThingCategoryDefs;
            foreach (var category in categories)
            {
                if (!Settings.CategoryLabelCache.TryGetValue(category.defName, out var categoryLabelInfo))
                    continue;

                if (categoryLabelInfo.CurrentCategoryLabel.NullOrEmpty())
                    continue;

                if (category.label != categoryLabelInfo.CurrentCategoryLabel)
                {
                    category.label = categoryLabelInfo.CurrentCategoryLabel;
                    LogMessage(() => $"Set category label for [{category.defName}] to [{category.label}]");
                }
            }
        }
    }
}

