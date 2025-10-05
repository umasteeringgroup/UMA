#if UMA_ADDRESSABLES
//#define UMA_VES
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;


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

        public void DebugLog(string message)
        {
            // File.AppendAllText("D:\\DebugGenerator.txt", message + Environment.NewLine);
        }


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
			if (Index == null)
			{
				Index = UMAAssetIndexer.Instance;
				return;
			}
            // if the preference is turned on, then force the build flag to clear materials
            bool stripUmaMaterials = UMAEditorUtilities.StripUMAMaterials();
            bool stripTextures = UMAEditorUtilities.StripTextures();

            if (stripUmaMaterials)
                ClearMaterials = true;

            try
            {
                LogText("");
                LogText("****************************************************");
                LogText("Generating from recipes: " + DateTime.Now.ToString());
                LogText("****************************************************");
                LogText("");
                bool IncludeRecipes = UMASettings.AddrIncludeRecipes;
                bool IncludeOthers = UMASettings.AddrIncludeOther;
                string DefaultAddressableLabel = UMASettings.AddrDefaultLabel;

                RecipeExtraLabels = new Dictionary<string, List<string>>();
                
                    var WardrobeCollections = Index.GetAllAssets<UMAWardrobeCollection>();
                    foreach (var wc in WardrobeCollections)
                    {
                        if (wc == null) continue;
                        string label = wc.AssignedLabel;
                        List<string> recipes = wc.wardrobeCollection.GetAllRecipeNamesInCollection();
                        foreach (string recipe in recipes)
                        {
#if UMA_VES
                        if (VesUmaLabelMaker.DO_NOT_INCLUDE_LABELS.Contains(label))
                        {   //VES added
                            continue;
                        }
#endif
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
#if UMA_VES
                    if (VesUmaLabelMaker.DO_NOT_INCLUDE_LABELS.Contains(uwr.AssignedLabel))
                    { //VES added
                        continue;
                    }
#endif
                    List<string> ExtraLabels = new List<string>();

                    if (RecipeExtraLabels.ContainsKey(uwr.name))
                    {
                        ExtraLabels = RecipeExtraLabels[uwr.name];
                    }

                    LogText("");
                    LogText("Processing recipe: " + uwr.name + " Label: " + uwr.AssignedLabel);

                    EditorUtility.DisplayProgressBar("Generating", "processing recipe: " + uwr.name , pos);


                    IUMAIndexOptions options = uwr as IUMAIndexOptions;
                    if (options != null && options.LabelLocalFiles)
                    {
                        // Get the asset items for the recipe from the local directory, not the index
                        // if it doesn't exist in the local directory, then get it from the index
                        List<AssetItem> items = Index.GetAssetItems(uwr, false);
                        foreach (AssetItem ai in items)
                        {
                            // Local items do not get default labels.
                            // instead, they only get the label of the wardrobe recipe they are in.
                            string label = uwr.AssignedLabel;
                            // get the recipe path.
                            // search for the asset in the recipe path, including children.
                            // if it exists, then add the label to the asset.

                            string path = AssetDatabase.GetAssetPath(ai.Item.GetInstanceID());
                            string filename = System.IO.Path.GetFileName(path);
                            string basePath = System.IO.Path.GetDirectoryName(path);

                            Debug.Log("Looking for asset: " + filename + " in path: " + basePath);
                            AssetItem ai2 = GetLocalAssetItemIfExist(basePath, filename, ai._Type.Name, ai);

                            if (ai._SerializedItem.GetInstanceID() != ai2._SerializedItem.GetInstanceID())
                            {
                                AddressableItems[ai].Add(uwr.AssignedLabel);
                            }
                        }
                    }
                    else
                    {
                        // Get the asset items for the recipe from the index
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
                    }

                    if (IncludeRecipes)
                    {
                        AssetItem RecipeItem = Index.GetRecipeItem(uwr);
						if(RecipeItem == null) { //VES added
							Debug.LogError("UMA RecipeItem is null! " + uwr);
						}
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

                // if we are stripping textures, then we need to ensure the index is indexing Texture type objects.
                if (stripTextures)
                {
                    Index.AddType(typeof(Texture));
                } 



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

                    if (ai._Type == typeof(SlotDataAsset) && ClearMaterials)
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
                        if (stripTextures)
                        {
                            // Ensure textures get the same labels and are added to the index as addressable items before stripping
                            var overlayLabels = AddressableItems[ai];
                            for (int i = 0; i < od.textureList.Length; i++)
                            {
                                var tex = od.textureList[i];
                                if (od.textureNames == null || od.textureNames.Length != od.textureList.Length)
                                {
                                    od.textureNames = new string[od.textureList.Length];
                                }
                                if (tex != null)
                                {
                                    // 1) Add texture to UMA index (if not already)
                                    Index.AddIfIndexed(tex);
                                    var texAI = Index.GetAssetItemForObject(tex);

                                    // 2) Create or move the texture entry to the shared group with the same labels
                                    string texPath = AssetDatabase.GetAssetPath(tex.GetInstanceID());
                                    string address = AssetItem.AddressableFolder + "Texture2D-" + tex.name + "-" + texPath.GetHashCode();
                                    bool texFound = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tex.GetInstanceID(), out string texGUID, out long texLocalId);
                                    if (texFound)
                                    {
                                        UMAAddressablesSupport.Instance.AddItemToSharedGroup(texGUID, address, overlayLabels, sharedGroup);
                                    }

                                    // 3) Mark the texture AssetItem as addressable, with the same labels
                                    if (texAI != null)
                                    {
                                        texAI.IsAddressable = true;
                                        texAI.AddressableGroup = sharedGroup.name;
                                        texAI.AddressableAddress = address;
                                        // Build labels string
                                        sb.Clear();
                                        for (int li = 0; li < overlayLabels.Count; li++)
                                        {
                                            sb.Append(overlayLabels[li]);
                                            sb.Append(';');
                                        }
                                        texAI.AddressableLabels = sb.ToString();
                                    }

                                    // 4) Store texture name then strip reference
                                    od.textureNames[i] = tex.name;
                                    od.textureList[i] = null;
                                    EditorUtility.SetDirty(od);
                                }
                                else
                                {
                                    od.textureNames[i] = "";
                                }
                            }

                            // Also handle alphaMask if present
                            if (od.alphaMask != null)
                            {
                                var tex = od.alphaMask;
                                Index.AddIfIndexed(tex);
                                var texAI = Index.GetAssetItemForObject(tex);
                                string texPath = AssetDatabase.GetAssetPath(tex.GetInstanceID());
                                string address = AssetItem.AddressableFolder + "Texture2D-" + tex.name + "-" + texPath.GetHashCode();
                                bool texFound = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tex.GetInstanceID(), out string texGUID, out long texLocalId);
                                if (texFound)
                                {
                                    UMAAddressablesSupport.Instance.AddItemToSharedGroup(texGUID, address, AddressableItems[ai], sharedGroup);
                                }
                                if (texAI != null)
                                {
                                    texAI.IsAddressable = true;
                                    texAI.AddressableGroup = sharedGroup.name;
                                    texAI.AddressableAddress = address;
                                    sb.Clear();
                                    foreach (var lbl in AddressableItems[ai])
                                    {
                                        sb.Append(lbl);
                                        sb.Append(';');
                                    }
                                    texAI.AddressableLabels = sb.ToString();
                                }

                                // do not null alphaMask reference here; only stripping overlay.textureList[]
                            }
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
                        // Clear out the shaders on the UMAMaterial
                        if (ai.Item is UMAMaterial um)
                        {
                            if (um.material != null)
                            {
                                um.ShaderName = um.material.shader.name;
                                um.MaterialName = um.material.name;
                                um.material.shader = null;
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
				Index.ForceSave();
            }
        }

        AssetItem GetLocalAssetItemIfExist(string path, string name, string typename, AssetItem defaultItem)
        {
            // We need to use the EvilName of the asset item to get the correct asset item.
            string evilName = defaultItem.EvilName;

            var guids = AssetDatabase.FindAssets("t:" + typename, new string[] { path });
            foreach (string guid in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var o = AssetDatabase.LoadAssetAtPath(p, defaultItem._Type);
                if (o != null)
                {
                    var thisEvilName = AssetItem.GetEvilName(o);
                    if (thisEvilName == evilName)
                    {
                        Debug.Log("found local asset: " + p);
                        AssetItem localItem = new AssetItem(defaultItem._Type, o.name, p, o);
                        return localItem;
                    }
                }
            }
            return defaultItem;
        }

        public bool Prepare()
        {
			if(Index == null) {
            Index = UMAAssetIndexer.Instance;
			}
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
            List<AssetItem> Items = Index.GetAssetItems(t);
            foreach (AssetItem item in Items)
            {
                if (AddressableItems.ContainsKey(item))
                {
                    Debug.LogWarning("Duplicate Addressable item found: " + item._Name + " of type " + item._Type.Name);
                    continue;
                }
                AddressableItems.Add(item, new List<string>());
                AddressableItems[item].Add(DefaultLabel);
            }
        }
    }
}
#endif
