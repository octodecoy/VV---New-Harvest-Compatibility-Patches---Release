namespace NewHarvestPatches;

/// <summary>
/// The declared type of a mod-setting value being written into a def by a PatchOperation. Despite the
/// Compare* name it is unrelated to the condition enums beside it - it drives the type-checked conversion
/// in PatchOperationPathedExtended, where an XML author naming the wrong type gets a logged error instead
/// of a coerced value.
/// </summary>
internal enum CompareType
{
    Bool,
    String,
    Int,
    Float,
    IntRange
}
