namespace NewHarvestPatches
{
    /// <summary>
    /// A material's user-chosen colors plus the def colors to revert to.
    /// Stuff color tints things built FROM the material; thing color tints the material item itself.
    /// <para>
    /// Both pairs are scribed, and the Default* pair carries the whole "did the user pick this?" signal:
    /// a New* color equal to the Default* it was seeded from is not a choice, it is a copy.
    /// <see cref="NewHarvestPatchesModSettings.InitializeColorInfo"/> settles that question against the
    /// SCRIBED Default* (the def color as of the last save) before overwriting Default* with the def's
    /// current color, so "revert" still lands on the def's CURRENT color while an uncustomized New*
    /// follows the def forward instead of pinning it to a stale value.
    /// </para>
    /// <para>
    /// Scribing Default* is what makes that possible. While only New* persisted, an untouched entry was
    /// indistinguishable from a deliberate choice, so a def that GAINED a <c>graphicData/color</c> in a mod
    /// update had the old seed (white, which the old scribe call also omitted from disk) treated as a user
    /// claim - <c>MaterialColorChanger</c> wrote white back over the new def color and the item plus its UI
    /// icon rendered colorless. An old config with no Default* node reads as white, which matches the seed
    /// those entries were written with, so they self-heal while genuine customizations survive.
    /// </para>
    /// </summary>
    public class ColorInfo : IExposable
    {
        public Color DefaultStuffColor = white;
        public Color DefaultThingColor = white;
        public Color NewStuffColor = white;
        public Color NewThingColor = white;

        // forceSave throughout: every one of these must round-trip even when it equals the field default,
        // because the New*/Default* COMPARISON is the signal - a silently omitted half would fabricate or
        // erase a customization on the next load.
        public void ExposeData()
        {
            Scribe_Values.Look(ref DefaultStuffColor, nameof(DefaultStuffColor), white, true);
            Scribe_Values.Look(ref DefaultThingColor, nameof(DefaultThingColor), white, true);
            Scribe_Values.Look(ref NewStuffColor, nameof(NewStuffColor), white, true);
            Scribe_Values.Look(ref NewThingColor, nameof(NewThingColor), white, true);
        }
    }
}
