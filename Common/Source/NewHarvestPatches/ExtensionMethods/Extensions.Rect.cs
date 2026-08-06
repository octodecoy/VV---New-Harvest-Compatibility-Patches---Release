namespace NewHarvestPatches;

public static partial class Extensions
{
    public static bool IsBeingRightClicked(this Rect rect)
        => Mouse.IsOver(rect) && Event.current.type is EventType.MouseDown && Event.current.button == 1;
}