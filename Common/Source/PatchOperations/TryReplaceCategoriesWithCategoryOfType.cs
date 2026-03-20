
namespace NewHarvestPatches
{
    internal class TryReplaceCategoriesWithCategoryOfType : PatchOperationPathedExtended
    {
        private readonly string categoryType = null;
        private readonly string requiredCategoryDefName = null;

        protected override bool ApplyWorker(XmlDocument xml)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(categoryType))
                    return false;

                string newCategoryDefName = GetCategoryName(xml, categoryType);
                if (string.IsNullOrEmpty(newCategoryDefName))
                    return false;

                if (!PreCheck(xpath, xml))
                    return false;

                foreach (XmlNode thingDef in nodes)
                {
                    if (thingDef == null)
                        continue;

                    var ownerDoc = thingDef.OwnerDocument;
                    if (ownerDoc == null)
                        continue;

                    if (requiredCategoryDefName != null && !HasCategoryInSelfOrParent(xml, thingDef, requiredCategoryDefName))
                        continue;

                    string thingDefName = GetThingDefName(thingDef);
                    if (!ResolveCategory(xml, thingDefName, newCategoryDefName, out string resolvedCategory))
                    {
                        continue;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                ExToLog(ex, MethodBase.GetCurrentMethod(), optMsg: $"{xpath}");
                return false;
            }
        }
    }
}
