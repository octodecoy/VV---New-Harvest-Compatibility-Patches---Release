namespace NewHarvestPatches
{
    public static class Constants
    {
        public static class Category
        {
            public static class Prefix
            {
                public const string VV_NHCP_DummyCategory_ = nameof(VV_NHCP_DummyCategory_);
            }
            public static class Suffix
            {
                public const string AddToCategory = nameof(AddToCategory);
                public const string CreateCategory = nameof(CreateCategory);
                public const string MergeCategory = nameof(MergeCategory);
                public const string ResourceReadout = nameof(ResourceReadout);
            }

            public static class Type
            {
                public const string AnimalFoods = nameof(AnimalFoods);
                public const string Grains = nameof(Grains);
                public const string Fruit = nameof(Fruit);
                public const string Nuts = nameof(Nuts);
                public const string Vegetables = nameof(Vegetables);
                public const string Fungus = nameof(Fungus);
                public const string None_Base = nameof(None_Base); // None of our categories are assigned via xml
            }
        }
        public static class ModName
        {
            public static class Prefix
            {
                public const string VV_ = nameof(VV_);
                public const string VV_NHCP_ = nameof(VV_NHCP_);
            }
        }
        public static class Setting
        {
            public static class Prefix
            {
                public const string DisabledFuel_ = nameof(DisabledFuel_); // Disabled fuel types
                public const string NoFallColors_ = nameof(NoFallColors_); // Disabled fall colors on trees
                public const string SetCommonality_ = nameof(SetCommonality_); // Changed commonality
                public const string ColorChange_ = nameof(ColorChange_); // Material color change
            }

            public static class Suffix
            {
                public const string Category = nameof(Category);
                public const string ToDropdowns = nameof(ToDropdowns);
            }
        }

        public static class TKey
        {
            public static class Type
            {
                public const string General = nameof(General);
                public const string HeaderLabel = nameof(HeaderLabel);
                public const string Tab = nameof(Tab);
                public const string TabSubLabel = nameof(TabSubLabel);
                public const string CheckboxLabel = nameof(CheckboxLabel);
                public const string CheckboxSubLabel = nameof(CheckboxSubLabel);
                public const string SliderLabel = nameof(SliderLabel);
                public const string SliderSubLabel = nameof(SliderSubLabel);
                public const string Button = nameof(Button);
                public const string SectionLabel = nameof(SectionLabel);
                public const string RadioLabel = nameof(RadioLabel);
                public const string Tooltip = nameof(Tooltip);
                public const string AdjusterLabel = nameof(AdjusterLabel);
                public const string AdjusterSubLabel = nameof(AdjusterSubLabel);
                public const string RangeLabel = nameof(RangeLabel);
                public const string RangeSubLabel = nameof(RangeSubLabel);
            }
        }

        public static class Safety
        {
            public const int CategoryTraversalLimit = 128;
        }
    }
}