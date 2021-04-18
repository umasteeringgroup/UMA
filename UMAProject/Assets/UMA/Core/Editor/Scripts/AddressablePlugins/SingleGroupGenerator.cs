using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UMA;
using UnityEditor;
using UnityEngine;
using UMA.CharacterSystem;

#if UMA_ADDRESSABLES
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;


namespace UMA
{
    public class SingleGroupGenerator : IUMAAddressablePlugin
    {
        public bool ClearMaterials = false; // should be set when generating during the build process, so the materials are cleared in the bundles
        public UMAAssetIndexer Index;
        List<UMAPackedRecipeBase> Recipes;
        Dictionary<AssetItem, List<string>> AddressableItems = new Dictionary<AssetItem, List<string>>();
        Dictionary<string, List<string>> RecipeExtraLabels = new Dictionary<string, List<string>>();

        const string SharedGroupName = "UMA_SharedItems";

        public string Menu
        {
            get
            {
                return "Generate Single group (fast)";
            }
        }

        public void LogText(string text)
        {
#if SUPER_LOGGING
            string filePath = System.IO.Path.Combine(Application.dataPath, "Generatelog.txt");
            System.IO.File.AppendAllText(filePath, text+Environment.NewLine);
#endif
        }

        public void Complete()
        {
            bool stripUmaMaterials = UMAEditorUtilities.StripUMAMaterials();
            try
            {
                LogText("");
                LogText("****************************************************");
                LogText("Generating from recipes: " + DateTime.Now.ToString());
                LogText("****************************************************");
                LogText("");
                bool IncludeRecipes = UMAEditorUtilities.GetConfigValue(UMAEditorUtilities.ConfigToggle_IncludeRecipes, false);
                bool IncludeOthers = UMAEditorUtilities.GetConfigValue(UMAEditorUtilities.ConfigToggle_IncludeOther, false);
                string DefaultAddressableLabel = UMAEditorUtilities.GetDefaultAddressableLabel();

                RecipeExtraLabels = new Dictionary<string, List<string>>();
                
                    var WardrobeCollections = UMAAssetIndexer.Instance.GetAllAssets<UMAWardrobeCollection>();
                    foreach (var wc in WardrobeCollections)
                    {
                        if (wc == null) continue;
                        string label = wc.AssignedLabel;
                        List<string> recipes = wc.wardrobeCollection.GetAllRecipeNamesInCollection();
                        foreach (string recipe in recipes)
                        {
                            if (RecipeExtraLabels.ContainsKey(recipe) == false)
                            {
                                RecipeExtraLabels.Add(recipe, new List<string>());
                            }
                            RecipeExtraLabels[recipe].Add(label);
                    }
                }

                float pos = 0.0f;
                float inc = 1.0f / Recipes.Count;
                foreach (UMAPackedRecipeBase uwr in Recipes)
                {
                    List<string> ExtraLabels = new List<string>();

                    if (RecipeExtraLabels.ContainsKey(uwr.name))
                    {
                        ExtraLabels = RecipeExtraLabels[uwr.name];
                    }

                    LogText("");
                    LogText("Processing recipe: " + uwr.name + " Label: " + uwr.AssignedLabel);

                    EditorUtility.DisplayProgressBar("Generating", "processing recipe: " + uwr.name , pos);
                    List<AssetItem> items = Index.GetAssetItems(uwr, true);
                    foreach (AssetItem ai in items)
                    {
                        if (AddressableItems.ContainsKey(ai) == false)
                        {
                            AddressableItems.Add(ai, new List<string>());
                            AddressableItems[ai].Add(DefaultAddressableLabel);
                        }
                        AddressableItems[ai].Add(uwr.AssignedLabel);
                        AddressableItems[ai].AddRange(ExtraLabels);
                    }
                    if (IncludeRecipes)
                    {
                        AssetItem RecipeItem = UMAAssetIndexer.Instance.GetRecipeItem(uwr);
                        if (AddressableItems.ContainsKey(RecipeItem) == false)
                        {
                            AddressableItems.Add(RecipeItem, new List<string>());
                            AddressableItems[RecipeItem].Add(DefaultAddressableLabel);
                        }
                        AddressableItems[RecipeItem].Add(uwr.AssignedLabel);
                        AddressableItems[RecipeItem].Add("UMA_Recipes");
                        AddressableItems[RecipeItem].AddRange(ExtraLabels);
                    }
                    pos += inc;
                }


                if (IncludeOthers)
                {
                    AddAssetItems(typeof(RaceData), DefaultAddressableLabel);
                    AddAssetItems(typeof(RuntimeAnimatorController), DefaultAddressableLabel);
                    AddAssetItems(typeof(TextAsset), DefaultAddressableLabel);
                    AddAssetItems(typeof(DynamicUMADnaAsset), DefaultAddressableLabel);
                }


                // Create the shared group that has each item packed separately.
                AddressableAssetGroup sharedGroup = AddressableUtility.AddressableSettings.FindGroup(SharedGroupName);
                if (sharedGroup == null)
                {
                    sharedGroup = AddressableUtility.AddressableSettings.CreateGroup(SharedGroupName, false, false, true,AddressableUtility.AddressableSettings.DefaultGroup.Schemas);
                    sharedGroup.GetSchema<BundledAssetGroupSchema>().BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
                }

                pos = 0.0f;
                inc = 1.0f / AddressableItems.Count;

                StringBuilder sb = new StringBuilder();
                foreach (AssetItem ai in AddressableItems.Keys)
                {
                    if (!ai.Item)
                    {
                        Debug.LogError($"Asset \"{ai._Name}\" of type \"{ai._Type}\" doesn't exist anymore - did it get deleted?");
                        continue;
                    }
                    ai.IsAddressable = true;
                    ai.AddressableAddress = ""; // let the system assign it if we are generating.
                    ai.AddressableGroup = sharedGroup.name;
                    EditorUtility.DisplayProgressBar("Generating", "Processing Asset: " + ai.Item.name, pos);

                    sb.Clear();
                    foreach (string s in AddressableItems[ai])
                    {
                        sb.Append(s);
                        sb.Append(';');
                    }
                    ai.AddressableLabels = sb.ToString();

                    bool found = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ai.Item.GetInstanceID(), out string itemGUID, out long localID);

                    UMAAddressablesSupport.Instance.AddItemToSharedGroup(itemGUID, ai.AddressableAddress, AddressableItems[ai], sharedGroup);

                    if (ai._Type == typeof(SlotDataAsset) && stripUmaMaterials)
                    {
                        SlotDataAsset sda = ai.Item as SlotDataAsset;
                        if (sda == null)
                        {
                            Debug.Log("Invalid Slotdata in recipe: " + ai._Name + ". Skipping.");
                            continue;
                        }
                        if (sda.material != null)
                        {
                            if (ClearMaterials)
                            {
                                sda.materialName = sda.material.name;
                                sda.material = null;
                                EditorUtility.SetDirty(sda);
                            }
                            else
                            {
                                if (sda.material == null)
                                {
                                    sda.material = Index.GetAsset<UMAMaterial>(sda.materialName);
                                    EditorUtility.SetDirty(sda);
                                }
                            }
                        }
                    }
                    if (ai._Type == typeof(OverlayDataAsset))
                    {
                        OverlayDataAsset od = ai.Item as OverlayDataAsset;
                        if (od == null)
                        {
                            Debug.Log("Invalid overlay in recipe: " + ai._Name + ". Skipping.");
                            continue;
                        }
                        if (od.material != null)
                        {
                            if (ClearMaterials)
                            {
                                od.materialName = od.material.name;
                                od.material = null;
                                EditorUtility.SetDirty(od);
                            }
                            else
                            {
                                if (od.material == null)
                                {
                                    od.material = Index.GetAsset<UMAMaterial>(od.materialName);
                                    EditorUtility.SetDirty(od);
                                }
                            }
                        }
#if INCL_TEXTURE2D
                        foreach (Texture tex in od.textureList)
                        {
                            if (tex == null) continue;
                            if (tex as Texture2D == null)
                            {
                                Debug.Log("Texture is not Texture2D!!! " + tex.name);
                                continue;
                            }
                            string path = AssetDatabase.GetAssetPath(tex.GetInstanceID());
                            string Address = "Texture2D-" + tex.name + "-" + path.GetHashCode();

                            found = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tex.GetInstanceID(), out string texGUID, out long texlocalID);
                            if (found)
                            {
                                UMAAddressablesSupport.Instance.AddItemToSharedGroup(texGUID, AssetItem.AddressableFolder + Address, AddressableItems[ai], sharedGroup);
                            }
                        }
#endif
                    }
                    pos += inc;
                }

                UMAAddressablesSupport.Instance.AssignAddressableInformation();

                Type[] types = Index.GetTypes();

                foreach (Type t in types)
                {
                    UMAAddressablesSupport.Instance.ReleaseReferences(t);
                }

                UMAAddressablesSupport.Instance.CleanupAddressables(true);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                UMAAssetIndexer.Instance.ForceSave();
            }
        }

        public bool Prepare()
        {

            Index = UMAAssetIndexer.Instance;
            UMAAddressablesSupport.Instance.CleanupAddressables(false, true);
            foreach (Type t in Index.GetTypes())
            {
                UMAAddressablesSupport.Instance.ClearAddressableFlags(t);
            }

            Recipes = new List<UMAPackedRecipeBase>();
            return true;
        }

        public List<string> ProcessItem(AssetItem ai)
        {
            // This generator does not process single items.
            return null;
        }

        /// <summary>
        /// Process the recipes. This generator simply accumulates the recipes for processing
        /// once all recipes are in the list.
        /// </summary>
        /// <param name="recipe"></param>
        public void ProcessRecipe(UMAPackedRecipeBase recipe)
        {
            if (recipe.resourcesOnly)
                return;
            Recipes.Add(recipe);
        }

        private void AddAssetItems(Type t, string DefaultLabel)
        {
            List<AssetItem> Items = UMAAssetIndexer.Instance.GetAssetItems(t);
            foreach (AssetItem item in Items)
            {
                AddressableItems.Add(item, new List<string>());
                AddressableItems[item].Add(DefaultLabel);
            }
        }
    }
}
#endif