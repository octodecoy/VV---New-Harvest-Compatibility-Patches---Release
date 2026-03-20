namespace NewHarvestPatches
{
    internal static class Translator
    {
        public const string KeyPrefix = "NHCP.";

        public static string TranslateKey(string keyType, string key)
        {
            return $"{KeyPrefix}{keyType}_{key}".Translate();
        }

        public static TaggedString TranslateComposite(
            string mainKey,
            (string value, bool translate)[] args = null,
            bool? boolArg = null,
            (string value, bool translate)? trueArg = null,
            (string value, bool translate)? falseArg = null)
        {
            var namedArgs = new List<NamedArgument>();
            int argCount = 0;

            if (args != null)
            {
                foreach (var (value, translate) in args)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        string argValue = translate ? $"{KeyPrefix}{value}".Translate() : value;
                        namedArgs.Add(new NamedArgument(argValue, argCount.ToString()));
                        argCount++;
                    }
                }
            }

            if (boolArg.HasValue && trueArg.HasValue && falseArg.HasValue)
            {
                string lastValue = boolArg.Value
                    ? (trueArg.Value.translate ? $"{KeyPrefix}{trueArg.Value.value}".Translate() : trueArg.Value.value)
                    : (falseArg.Value.translate ? $"{KeyPrefix}{falseArg.Value.value}".Translate() : falseArg.Value.value);
                namedArgs.Add(new NamedArgument(lastValue, argCount.ToString()));
            }

            string mainFullKey = $"{KeyPrefix}{mainKey}";
            return mainFullKey.Translate(namedArgs.ToArray());
        }
    }
}
